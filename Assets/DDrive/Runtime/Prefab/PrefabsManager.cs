using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Model;
using DDrive.Runtime.Vfx;
using UnityEngine;
using PrefabAssetId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Prefab.PrefabMarker>;

namespace DDrive.Runtime.Prefab
{
    // [07_canvas_prefab.md] Part B-3 — 汎用 Prefab の Spawn/Despawn/Tag/Layer/Pool([05] ModelsManager と同じ設計)。
    // ModelData と違い Slot/Avatar/Anim の差し替えは持たない。OnSpawn/OnDestroy は EventBus 経由で
    // AssetEventDispatcher に配線できるよう Events を公開する(着地煙 VFX・出現 SE 等をデータ側で設定可能にする)。
    public sealed class PrefabsManager : IAssetManager
    {
        private sealed class PrefabInstance
        {
            public PrefabData Data;
            public GameObject Root;
            public PooledObject Pooled; // Placeholder のときは null(Pool を経由しない)
            public InstanceContext Context;
            public bool IsPlaceholder;
            public bool IsPooled; // Flags.Pool.Kind == Pooled のとき true。false(None)は Despawn で Discard する
        }

        private readonly IPoolService _pool;
        private readonly IAssetRegistry _registry;
        private readonly EventBus _events;
        private readonly InstanceStore<PrefabMarker, PrefabInstance> _instances = new();
        private readonly List<Handle<PrefabMarker>> _allActive = new();
        private readonly HashSet<PrefabData> _placeholderWarned = new();

        public AssetType Type => AssetType.Prefab;

        public EventBus Events => _events;

        public PrefabsManager(IPoolService pool, IAssetRegistry registry, EventBus events = null)
        {
            _pool = pool;
            _registry = registry;
            _events = events ?? new EventBus();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterPlaceholder()
        {
            PlaceholderProvider.Register(CreatePlaceholder);
        }

        // FR-1.4: 未登録 ID は Prefab が null のまま Placeholder になる。SpawnData 側が Prefab==null を
        // 「空の GameObject を生成する」扱いにするので、モック段階でも Spawn/Despawn が普通に動く。
        private static PrefabData CreatePlaceholder()
        {
            var data = ScriptableObject.CreateInstance<PrefabData>();
            data.DisplayName = "<Placeholder:PREFAB>";
            data.Prefab = null;
            return data;
        }

        // ── Spawn / Despawn ──

        public Handle<PrefabMarker> Spawn(PrefabAssetId id, Vector3 pos, Quaternion rot)
            => SpawnData(_registry.ResolveOrPlaceholder<PrefabData>(id.Value), pos, rot);

        public Handle<PrefabMarker> Spawn(PrefabAssetId id, Transform parent)
        {
            var data = _registry.ResolveOrPlaceholder<PrefabData>(id.Value);
            var pos = parent != null ? parent.position : Vector3.zero;
            var rot = parent != null ? parent.rotation : Quaternion.identity;
            return SpawnData(data, pos, rot, parent);
        }

        public Handle<PrefabMarker> SpawnData(PrefabData data, Vector3 pos, Quaternion rot, Transform parent = null)
        {
            if (data == null)
            {
                return Handle<PrefabMarker>.Invalid;
            }

            GameObject root;
            PooledObject pooled = null;
            var isPlaceholder = data.Prefab == null;

            if (isPlaceholder)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (_placeholderWarned.Add(data))
                {
                    Debug.LogWarning($"[DDrive] PrefabData '{data.DisplayName}' has no Prefab assigned; spawning an empty placeholder GameObject instead.");
                }
#endif
                root = new GameObject("<Placeholder:PREFAB>");
            }
            else
            {
                // Kind == None(既定)は「プールしない」の意味。Instance 生成/親付け/上限は Pool 経由で統一しつつ、
                // Despawn では Return せず Discard して破棄する(Codex レビュー 2026-09-10。[07] 実装メモ参照)。
                if (data.Flags.Pool.Kind == PoolPolicyKind.Pooled && _pool is PoolService concrete)
                {
                    concrete.SetLimit(data.Prefab, data.Flags.Pool.MaxCount);
                }

                pooled = _pool.Rent(data.Prefab);
                pooled.Priority = data.Flags.Priority;
                root = pooled.GameObject;
            }

            root.transform.SetParent(null);
            root.transform.SetPositionAndRotation(pos, rot);
            if (parent != null)
            {
                root.transform.SetParent(parent, worldPositionStays: true);
            }

            if (data.CollisionLayer >= 0)
            {
                VfxManager.SetLayerRecursively(root, data.CollisionLayer);
            }

            ApplyLodProfile(root, data.Lod);

            var instance = new PrefabInstance
            {
                Data = data,
                Root = root,
                Pooled = pooled,
                IsPlaceholder = isPlaceholder,
                IsPooled = !isPlaceholder && data.Flags.Pool.Kind == PoolPolicyKind.Pooled,
            };

            var handle = _instances.Add(instance);
            instance.Context = new InstanceContext(handle.Index, handle.Generation);
            _allActive.Add(handle);

            _events.Begin(instance.Context, data.Events);
            _events.Fire(instance.Context, EventTrigger.OnSpawn);

            return handle;
        }

        public void Despawn(Handle<PrefabMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return;
            }

            _events.Fire(instance.Context, EventTrigger.OnDestroy);
            _events.End(instance.Context);

            _allActive.Remove(handle);
            _instances.Remove(handle);

            if (instance.IsPlaceholder)
            {
                if (instance.Root != null)
                {
                    Object.Destroy(instance.Root);
                }
            }
            else if (instance.IsPooled)
            {
                _pool.Return(instance.Pooled);
            }
            else
            {
                // Kind == None: プールに戻さず破棄する(待機中インスタンスが無限に残るのを防ぐ)。
                _pool.Discard(instance.Pooled);
            }
        }

        // ── Handle 操作 ──

        public GameObject GetGameObject(Handle<PrefabMarker> handle)
            => _instances.TryGet(handle, out var instance) ? instance.Root : null;

        // 終了済み Handle の問い合わせは正常系なので警告を出さない。
        public bool IsValid(Handle<PrefabMarker> handle) => _instances.IsValidSilent(handle);

        public bool HasTag(Handle<PrefabMarker> handle, string tag)
            => _instances.TryGet(handle, out var instance) && instance.Data != null && instance.Data.HasTag(tag);

        public T GetComponent<T>(Handle<PrefabMarker> handle) where T : Component
            => _instances.TryGet(handle, out var instance) && instance.Root != null ? instance.Root.GetComponent<T>() : null;

        public void Move(Handle<PrefabMarker> handle, Vector3 pos, Quaternion rot)
        {
            if (_instances.TryGet(handle, out var instance) && instance.Root != null)
            {
                instance.Root.transform.SetPositionAndRotation(pos, rot);
            }
        }

        public void SetLayer(Handle<PrefabMarker> handle, int layer)
        {
            if (_instances.TryGet(handle, out var instance) && instance.Root != null)
            {
                VfxManager.SetLayerRecursively(instance.Root, layer);
            }
        }

        // 同期 Preload。ResolveOrPlaceholder は「既にロード済みのものだけ」返すため、Addressables から
        // まだ解決されていない(lazy な)カタログ登録は Prewarm できずに素通りする。その場合は開発ビルドで
        // 警告を出し、PreloadAsync を使うよう促す(Codex レビュー 2026-09-10。[07] 実装メモ参照)。
        public void Preload(params PrefabAssetId[] ids)
        {
            foreach (var id in ids)
            {
                if (!_registry.TryResolveSync<PrefabData>(id.Value, out var data) || data == null)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogWarning($"[DDrive] Prefabs.Preload: AssetId {id.Value} is not resolvable synchronously yet (lazy Addressables entry?). Use PreloadAsync instead to actually Prewarm it.");
#endif
                    continue;
                }

                if (data.Prefab == null || data.Flags.Pool.Kind != PoolPolicyKind.Pooled)
                {
                    continue;
                }

                _pool.Prewarm(data.Prefab, data.Flags.Pool.InitialCount);
            }
        }

        // 非同期 Preload。カタログがまだ実データを解決していない(lazy)場合でも ResolveAsync で確実に
        // ロードしてから Prewarm する。Kind == Pooled かつ InitialCount > 0 のときのみ Prewarm する
        // (Kind == None は「プールしない」ため事前確保は不要)。
        public async Cysharp.Threading.Tasks.UniTask PreloadAsync(params PrefabAssetId[] ids)
        {
            foreach (var id in ids)
            {
                var data = await _registry.ResolveAsync<PrefabData>(id.Value);
                if (data == null || data.Prefab == null)
                {
                    continue;
                }

                if (data.Flags.Pool.Kind == PoolPolicyKind.Pooled && data.Flags.Pool.InitialCount > 0)
                {
                    _pool.Prewarm(data.Prefab, data.Flags.Pool.InitialCount);
                }
            }
        }

        public int ActiveCount => _allActive.Count;

        // 発火元 Instance(InstanceContext)の Transform。AssetEventDispatcher が SE/VFX の contextRoot に使う([05] AnimManager と同じ配線)。
        public Transform GetContextTransform(InstanceContext ctx)
        {
            for (var i = 0; i < _allActive.Count; i++)
            {
                if (_instances.TryGet(_allActive[i], out var instance) && instance.Context.Equals(ctx))
                {
                    return instance.Root != null ? instance.Root.transform : null;
                }
            }

            return null;
        }

        public void Tick(float dt)
        {
            // 現時点で時間経過での自動処理は無い(ゲーム固有ロジックは Prefab 上のコンポーネントに書く。[07] B-3)。
        }

        public void OnPause(PauseChannel channel, bool paused)
        {
            // 汎用 Prefab 自体はポーズで特別な処理をしない(必要ならゲーム固有コンポーネント側で対応する)。
        }

        public void StopAll(StopReason reason)
        {
            for (var i = _allActive.Count - 1; i >= 0; i--)
            {
                Despawn(_allActive[i]);
            }
        }

        public void OnSceneUnload() => StopAll(StopReason.SceneUnload);

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
