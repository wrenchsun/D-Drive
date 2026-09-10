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
    //
    // 2026-09-09: (1) Material の差し替えは共有 Data(ModelData.Slots)を書き換えず、Instance 側の
    // Materials 配列に保持する(Data は読み取り専用 — [02] §2)。(2) Instance は自分の Animator で再生した
    // Anim Handle を所有し、Despawn 時に必ず停止する(プール再利用時に旧アニメが残らない)。
    public sealed class ModelsManager : IAssetManager
    {
        private sealed class ModelInstance
        {
            public ModelData Data;
            public GameObject Root;
            public PooledObject Pooled;
            public Renderer[] SlotRenderers; // Data.Slots と同じ並び順(未解決は null)
            public AssetId<MaterialMarker>[] Materials; // Instance ごとの現在値(初期値は Data.Slots[i].Material)
            public Animator Animator; // Spawn 時に解決(無ければ null)
            public readonly List<Handle<DDrive.Runtime.Anim.AnimMarker>> Anims = new(); // この Instance が所有する再生
        }

        private readonly IPoolService _pool;
        private readonly IAssetRegistry _registry;
        private readonly InstanceStore<ModelMarker, ModelInstance> _instances = new();
        private readonly List<Handle<ModelMarker>> _allActive = new();

        public AssetType Type => AssetType.Model;

        private DDrive.Runtime.Anim.AnimManager _anim;

        public ModelsManager(IPoolService pool, IAssetRegistry registry, DDrive.Runtime.Anim.AnimManager anim = null)
        {
            _pool = pool;
            _registry = registry;
            _anim = anim;
        }

        // [05_model_animation.md] A-3 — h.PlayAnim(AnimId) の委譲先(Phase 3-3 で接続)。未設定なら PlayAnim は no-op。
        public void SetAnimManager(DDrive.Runtime.Anim.AnimManager anim) => _anim = anim;

        public Animator GetAnimator(Handle<ModelMarker> handle)
            => _instances.TryGet(handle, out var instance) ? instance.Animator : null;

        public Handle<DDrive.Runtime.Anim.AnimMarker> PlayAnim(Handle<ModelMarker> handle, AssetId<DDrive.Runtime.Anim.AnimMarker> animId, float fade = -1f)
        {
            if (_anim == null || !_instances.TryGet(handle, out var instance) || instance.Animator == null)
            {
                return Handle<DDrive.Runtime.Anim.AnimMarker>.Invalid;
            }

            var animHandle = fade < 0f ? _anim.Play(animId, instance.Animator) : _anim.Play(animId, instance.Animator, fade);
            if (_anim.IsPlaying(animHandle))
            {
                // 終わった分を落としてから所有リストに載せる(長寿命の Instance で伸び続けないように)。
                for (var i = instance.Anims.Count - 1; i >= 0; i--)
                {
                    if (!_anim.IsPlaying(instance.Anims[i]))
                    {
                        instance.Anims.RemoveAt(i);
                    }
                }

                instance.Anims.Add(animHandle);
            }

            return animHandle;
        }

        // Instance が所有する再生中の Anim 数(テスト / デバッグ表示用)。
        public int GetOwnedAnimCount(Handle<ModelMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance) || _anim == null)
            {
                return 0;
            }

            var count = 0;
            for (var i = 0; i < instance.Anims.Count; i++)
            {
                if (_anim.IsPlaying(instance.Anims[i]))
                {
                    count++;
                }
            }

            return count;
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
                Materials = CopySlotMaterials(data.Slots),
                Animator = root.GetComponentInChildren<Animator>(true),
            };

            var handle = _instances.Add(instance);
            _allActive.Add(handle);

            // DefaultAnimation: AnimManager が接続されていれば Spawn 直後に再生する([05] A-2)。
            if (data.DefaultAnimation.IsValid && _anim != null)
            {
                PlayAnim(handle, data.DefaultAnimation);
            }

            return handle;
        }

        public void Despawn(Handle<ModelMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return;
            }

            // この Instance の Animator で動いているアニメーションを全て止めてからプールへ返す
            // (所有リスト + 外部が Anim.Play した分も含めて Animator 単位で中断)。
            if (_anim != null)
            {
                for (var i = 0; i < instance.Anims.Count; i++)
                {
                    // 自然終了した所有 Handle は台帳から消えているので、無効 Handle 警告を出さないよう先に確認する。
                    if (_anim.IsPlaying(instance.Anims[i]))
                    {
                        _anim.Stop(instance.Anims[i]);
                    }
                }

                _anim.StopAllFor(instance.Animator);
            }

            instance.Anims.Clear();
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

        // 終了済み Handle の問い合わせは正常系なので警告を出さない。
        public bool IsValid(Handle<ModelMarker> handle) => _instances.IsValidSilent(handle);

        // slotIndex は Data.Slots 配列内のインデックス(RendererPath+SlotIndex の組を label 相当として
        // 使う設計書の pseudocode を、実際のスキーマに合わせて明示引数化したもの)。
        public void SetMaterial(Handle<ModelMarker> handle, int slotIndex, AssetId<MaterialMarker> materialId)
        {
            if (!_instances.TryGet(handle, out var instance) ||
                instance.Materials == null || slotIndex < 0 || slotIndex >= instance.Materials.Length)
            {
                return;
            }

            // 共有 Data(ModelData.Slots)は書き換えない。この Instance の現在値としてだけ持つ。
            instance.Materials[slotIndex] = materialId;

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

        // この Instance のスロットに現在割り当てられている Material ID(Data の初期値 + SetMaterial の上書き)。
        public bool TryGetMaterial(Handle<ModelMarker> handle, int slotIndex, out AssetId<MaterialMarker> materialId)
        {
            if (_instances.TryGet(handle, out var instance) && instance.Materials != null && slotIndex >= 0 && slotIndex < instance.Materials.Length)
            {
                materialId = instance.Materials[slotIndex];
                return true;
            }

            materialId = AssetId<MaterialMarker>.Invalid;
            return false;
        }

        private static AssetId<MaterialMarker>[] CopySlotMaterials(MaterialSlot[] slots)
        {
            if (slots == null || slots.Length == 0)
            {
                return System.Array.Empty<AssetId<MaterialMarker>>();
            }

            var result = new AssetId<MaterialMarker>[slots.Length];
            for (var i = 0; i < slots.Length; i++)
            {
                result[i] = slots[i].Material;
            }

            return result;
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
