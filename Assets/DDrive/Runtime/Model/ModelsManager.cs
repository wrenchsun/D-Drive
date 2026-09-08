using System.Collections.Generic;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Material;
using DDrive.Runtime.Vfx;
using UnityEngine;
using ModelId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Model.ModelMarker>;

namespace DDrive.Runtime.Model
{
    // モデル(Prefabメイン管理)の Spawn/Despawn/Slot 差し替え([05_model_animation.md] A-3)。
    //
    // 既知の制約(2026-07-27時点): SetMaterial は AssetId<MaterialMarker> を受け取り ID 参照の
    // 契約(直参照しない)を満たすが、MaterialData/MaterialManager 自体は Phase 3(3-5)で実装される。
    // そのため現時点では ID の妥当性チェックのみ行い、実際のマテリアル差し替えは
    // MaterialData 実装後に完成する(Phase 3 側で AssetRegistry.ResolveAsync<MaterialData> を
    // 呼ぶ実処理を追加する想定)。[11_tasks.md] 2-5 の AC 「SetMaterial がデータだけで動く」は
    // この制約を踏まえて Phase 3 完了後に再検証すること。
    public sealed class ModelsManager : IAssetManager
    {
        private sealed class ModelInstance
        {
            public ModelData Data;
            public GameObject Root;
            public PooledObject Pooled;
            public Renderer[] SlotRenderers; // Data.Slots と同じ並び順(未解決は null)
        }

        private readonly IPoolService _pool;
        private readonly IAssetRegistry _registry;
        private readonly InstanceStore<ModelMarker, ModelInstance> _instances = new();
        private readonly List<Handle<ModelMarker>> _allActive = new();

        public AssetType Type => AssetType.Model;

        public ModelsManager(IPoolService pool, IAssetRegistry registry)
        {
            _pool = pool;
            _registry = registry;
        }

        public Handle<ModelMarker> Spawn(ModelId id, Vector3 pos, Quaternion rot)
            => SpawnData(_registry.ResolveOrPlaceholder<ModelData>(id.Value), pos, rot);

        public Handle<ModelMarker> Spawn(ModelId id, Transform parent)
        {
            var data = _registry.ResolveOrPlaceholder<ModelData>(id.Value);
            var pos = parent != null ? parent.position : Vector3.zero;
            var rot = parent != null ? parent.rotation : Quaternion.identity;
            var handle = SpawnData(data, pos, rot);

            if (parent != null && _instances.TryGet(handle, out var instance))
            {
                instance.Root.transform.SetParent(parent, worldPositionStays: true);
            }

            return handle;
        }

        public Handle<ModelMarker> SpawnData(ModelData data, Vector3 pos, Quaternion rot)
        {
            if (data == null || data.Prefab == null)
            {
                return Handle<ModelMarker>.Invalid;
            }

            if (data.Flags.Pool.Kind == DDrive.Foundation.Data.PoolPolicyKind.Pooled && _pool is PoolService concrete)
            {
                concrete.SetLimit(data.Prefab, data.Flags.Pool.MaxCount);
            }

            var pooled = _pool.Rent(data.Prefab);
            pooled.Priority = data.Flags.Priority;

            var root = pooled.GameObject;
            root.transform.SetParent(null);
            root.transform.SetPositionAndRotation(pos, rot);

            VfxManager.SetLayerRecursively(root, data.RenderLayer);
            var allRenderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in allRenderers)
            {
                renderer.renderingLayerMask = data.LightLayerMask;
            }

            ApplyLodProfile(root, data.Lod);

            var slotRenderers = ResolveSlotRenderers(root, data.Slots);

            var instance = new ModelInstance
            {
                Data = data,
                Root = root,
                Pooled = pooled,
                SlotRenderers = slotRenderers,
            };

            var handle = _instances.Add(instance);
            _allActive.Add(handle);
            return handle;
        }

        public void Despawn(Handle<ModelMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return;
            }

            _allActive.Remove(handle);
            _instances.Remove(handle);
            _pool.Return(instance.Pooled);
        }

        public void SetLayer(Handle<ModelMarker> handle, int layer)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                VfxManager.SetLayerRecursively(instance.Root, layer);
            }
        }

        public GameObject GetGameObject(Handle<ModelMarker> handle)
            => _instances.TryGet(handle, out var instance) ? instance.Root : null;

        public bool IsValid(Handle<ModelMarker> handle) => _instances.IsValid(handle);

        // slotIndex は Data.Slots 配列内のインデックス(RendererPath+SlotIndex の組を label 相当として
        // 使う設計書の pseudocode を、実際のスキーマに合わせて明示引数化したもの)。
        public void SetMaterial(Handle<ModelMarker> handle, int slotIndex, AssetId<MaterialMarker> materialId)
        {
            if (!_instances.TryGet(handle, out var instance) ||
                instance.Data.Slots == null || slotIndex < 0 || slotIndex >= instance.Data.Slots.Length)
            {
                return;
            }

            instance.Data.Slots[slotIndex].Material = materialId;

            var renderer = instance.SlotRenderers[slotIndex];
            if (renderer == null || !materialId.IsValid)
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // MaterialData/MaterialManager(Phase 3, ticket 3-5)が未実装のため、ID の保存はできても
            // 実際のマテリアル差し替えはまだ行えない。ここは Phase 3 完了後に実処理へ差し替える。
            Debug.LogWarning("[DDrive] Models.SetMaterial: MaterialData is not implemented yet (Phase 3, ticket 3-5). ID stored on Slots but not yet applied to the renderer.");
#endif
        }

        public void Tick(float dt)
        {
            // 現時点でモデル自体に時間経過での自動処理は無い(アニメーションは AnimManager 側、Phase 3)。
        }

        public void OnPause(PauseChannel channel, bool paused)
        {
            // モデル自体はポーズで特別な処理をしない(Animator の一時停止は AnimManager 側の責務)。
        }

        public void StopAll(StopReason reason)
        {
            for (var i = _allActive.Count - 1; i >= 0; i--)
            {
                Despawn(_allActive[i]);
            }
        }

        public void OnSceneUnload() => StopAll(StopReason.SceneUnload);

        private static Renderer[] ResolveSlotRenderers(GameObject root, MaterialSlot[] slots)
        {
            if (slots == null || slots.Length == 0)
            {
                return System.Array.Empty<Renderer>();
            }

            var result = new Renderer[slots.Length];
            for (var i = 0; i < slots.Length; i++)
            {
                result[i] = ResolveRendererByPath(root.transform, slots[i].RendererPath);
            }

            return result;
        }

        private static Renderer ResolveRendererByPath(Transform root, string rendererPath)
        {
            if (string.IsNullOrEmpty(rendererPath))
            {
                return root.GetComponent<Renderer>();
            }

            var target = root.Find(rendererPath);
            return target != null ? target.GetComponent<Renderer>() : null;
        }

        private static void ApplyLodProfile(GameObject root, LodProfile lod)
        {
            if (!lod.Enabled || lod.ScreenRelativeTransitionHeights == null)
            {
                return;
            }

            var group = root.GetComponentInChildren<LODGroup>(true);
            if (group == null)
            {
                return;
            }

            var lods = group.GetLODs();
            var count = Mathf.Min(lods.Length, lod.ScreenRelativeTransitionHeights.Length);
            for (var i = 0; i < count; i++)
            {
                lods[i].screenRelativeTransitionHeight = lod.ScreenRelativeTransitionHeights[i];
            }

            group.SetLODs(lods);
        }
    }
}
