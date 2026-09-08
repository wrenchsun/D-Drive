using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Net;
using UnityEngine;
using VfxId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Vfx.VfxMarker>;

namespace DDrive.Runtime.Vfx
{
    // VFX 再生の中核([04_vfx.md] §3)。Instance/Handle は外部非公開、呼び出し側は
    // Handle<VfxMarker> のみを介して操作する(Audio の AudioManager と同じ設計)。
    //
    // 対応範囲: ParticleSystem。VFX Graph(com.unity.visualeffectgraph)はこのプロジェクトに
    // パッケージが未導入のためハード依存を避けている。導入後は VisualEffect コンポーネントの
    // 検出・SetFloat 等の反映を Instance 生成時に追加すれば同じ Handle API で扱える。
    public sealed class VfxManager : IAssetManager
    {
        private sealed class VfxInstance
        {
            public VfxData Data;
            public GameObject Root;
            public PooledObject Pooled;
            public Transform FollowTarget;
            public bool HasFollowTarget;

            // 追従時に使うローカルオフセット。AnchorPoint のランダム散らばりは Spawn 時に1回だけ
            // サンプリングされるため、Tick で Data.Anchor.LocalOffset から再計算せずこちらを使う
            // (毎 Tick 再計算するとランダム分が消えて位置が吸い付いてしまう)。
            public Vector3 FollowLocalOffset;

            public bool Paused;
            public float ElapsedSeconds;
            public float Speed = 1f;

            public ParticleSystem[] ParticleSystems;
            public Renderer[] Renderers;
            public MaterialPropertyBlock PropertyBlock;

            // Stop() 呼び出し後、FadeOutSec が経過するまでの間フェード中(放出済み分の消失待ち)。
            public bool Stopping;
            public float FadeOutRemaining;
        }

        // TargetProperty の Shader.PropertyToID は初回のみ解決し、以後キャッシュする([04] §3)。
        private static readonly Dictionary<string, int> PropertyIdCache = new();

        private readonly IPoolService _pool;
        private readonly IAssetRegistry _registry;
        private readonly InstanceStore<VfxMarker, VfxInstance> _instances = new();
        private readonly List<Handle<VfxMarker>> _allActive = new();

        // [14_networking.md] §3/§4/§8 — AudioManager と同じ考え方(Cosmetic はバッチして Broadcast、
        // 実際の Spawn は受信ハンドラのみが行う)。
        private readonly INetBridge _netBridge;
        private readonly List<VfxNetMsg> _pendingCosmeticBatch = new();

        public AssetType Type => AssetType.Vfx;

        public VfxManager(IPoolService pool, IAssetRegistry registry, INetBridge netBridge = null)
        {
            _pool = pool;
            _registry = registry;
            _netBridge = netBridge ?? new LocalLoopbackBridge();
            _netBridge.Subscribe<VfxNetBatchMsg>(OnReceiveCosmeticBatch);
        }

        // FR-1.4: 未登録/未ロードの VFX は「何も見えない」プレースホルダで代替する(AudioManager の
        // 無音プレースホルダと同じ考え方)。Prefab が null のままだと Cosmetic 受信側で Spawn できない。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterPlaceholder()
        {
            PlaceholderProvider.Register(CreatePlaceholder);
        }

        private static VfxData CreatePlaceholder()
        {
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.DisplayName = "<Placeholder:VFX>";
            data.Prefab = new GameObject("PlaceholderVfx");
            data.Prefab.SetActive(false);
            data.LifeMode = VfxLifeMode.Duration;
            data.Duration = 0.1f;
            return data;
        }

        private void OnReceiveCosmeticBatch(ulong senderId, VfxNetBatchMsg batch)
        {
            if (batch.Items == null)
            {
                return;
            }

            foreach (var item in batch.Items)
            {
                var data = _registry.ResolveOrPlaceholder<VfxData>(item.VfxId);
                SpawnDataLocal(data, explicitPose: (item.Position, Quaternion.identity));
            }
        }

        // 引数なし: Data.Anchor をそのまま解決する(コンテキストが無いので BoneName/NamedObject は
        // World 固定扱いにフォールバックする。Audio の PlaySe(id) と同じ規則)。
        public Handle<VfxMarker> Spawn(VfxId id)
            => SpawnData(_registry.ResolveOrPlaceholder<VfxData>(id.Value));

        public Handle<VfxMarker> Spawn(VfxId id, Vector3 pos, Quaternion rot)
            => SpawnData(_registry.ResolveOrPlaceholder<VfxData>(id.Value), explicitPose: (pos, rot));

        // Data.Anchor(ボーン名等)を attach 配下で解決し、追従させる。Anchor を明示的に上書きしたい
        // 場合はこちら(Audio の contextRoot 版に相当)。
        public Handle<VfxMarker> Spawn(VfxId id, Transform attach)
            => SpawnData(_registry.ResolveOrPlaceholder<VfxData>(id.Value), contextRoot: attach);

        public Handle<VfxMarker> SpawnData(VfxData data, (Vector3 pos, Quaternion rot)? explicitPose = null, Transform contextRoot = null)
        {
            if (data == null || data.Prefab == null)
            {
                return Handle<VfxMarker>.Invalid;
            }

            // Cosmetic は直接 Spawn せず、Tick でまとめて Broadcast する([14_networking.md] §3/§4/§8)。
            // 実際の Spawn は自分を含む全員が受信ハンドラ経由で行うため、ここでは Invalid を返す。
            if (data.Flags.Net == NetMode.Cosmetic)
            {
                _pendingCosmeticBatch.Add(new VfxNetMsg
                {
                    VfxId = data.Id,
                    Position = ResolveWorldPositionForBroadcast(data, explicitPose, contextRoot),
                });
                return Handle<VfxMarker>.Invalid;
            }

            return SpawnDataLocal(data, explicitPose, contextRoot);
        }

        private static Vector3 ResolveWorldPositionForBroadcast(VfxData data, (Vector3 pos, Quaternion rot)? explicitPose, Transform contextRoot)
        {
            if (explicitPose.HasValue)
            {
                return explicitPose.Value.pos;
            }

            var resolved = AnchorResolver.Resolve(data.Anchor, contextRoot);
            return resolved != null ? resolved.TransformPoint(data.Anchor.LocalOffset) : data.Anchor.LocalOffset;
        }

        private Handle<VfxMarker> SpawnDataLocal(VfxData data, (Vector3 pos, Quaternion rot)? explicitPose = null, Transform contextRoot = null)
        {
            // OnReceiveCosmeticBatch はここへ直接来るため(SpawnData の null/Prefab ガードを通らない)、
            // Placeholder 解決に失敗した場合(Prefab 未設定)にも安全に無視できるようにする。
            if (data == null || data.Prefab == null)
            {
                return Handle<VfxMarker>.Invalid;
            }

            if (data.Flags.Pool.Kind == PoolPolicyKind.Pooled && _pool is PoolService concrete)
            {
                concrete.SetLimit(data.Prefab, data.Flags.Pool.MaxCount);
            }

            var pooled = _pool.Rent(data.Prefab);
            pooled.Priority = data.Flags.Priority;

            var root = pooled.GameObject;
            var rootTransform = root.transform;
            rootTransform.SetParent(null);
            rootTransform.localScale = data.Anchor.LocalScale == Vector3.zero ? Vector3.one : data.Anchor.LocalScale;

            Transform followTarget = null;
            var hasFollowTarget = false;
            var followLocalOffset = data.Anchor.LocalOffset;

            if (explicitPose.HasValue)
            {
                rootTransform.SetPositionAndRotation(explicitPose.Value.pos, explicitPose.Value.rot);
            }
            else
            {
                var resolved = AnchorResolver.Resolve(data.Anchor, contextRoot);
                if (resolved != null)
                {
                    followTarget = resolved;
                    hasFollowTarget = true;

                    var baseRotation = data.Anchor.FollowRotation ? resolved.rotation : Quaternion.Euler(data.Anchor.LocalEuler);

                    // 解決先が AnchorPoint(シーン配置型アンカー)なら、アンカー固有のオフセット+
                    // ランダム散らばり(位置/回転/スケール)を追加適用する([04_vfx.md] §2.5)。
                    if (resolved.TryGetComponent<AnchorPoint>(out var point))
                    {
                        followLocalOffset = point.SampleLocalOffset(data.Anchor.LocalOffset);
                        rootTransform.rotation = point.SampleRotation(baseRotation);
                        rootTransform.localScale *= point.SampleScaleMultiplier();
                    }
                    else
                    {
                        rootTransform.rotation = baseRotation;
                    }

                    rootTransform.position = resolved.TransformPoint(followLocalOffset);
                }
                else
                {
                    rootTransform.position = data.Anchor.LocalOffset;
                    rootTransform.rotation = Quaternion.Euler(data.Anchor.LocalEuler);
                }
            }

            var poolable = root.GetComponent<VfxInstancePoolable>();
            if (poolable == null)
            {
                poolable = root.AddComponent<VfxInstancePoolable>();
            }

            var particleSystems = root.GetComponentsInChildren<ParticleSystem>(true);
            poolable.ParticleSystems = particleSystems;

            var instance = new VfxInstance
            {
                Data = data,
                Root = root,
                Pooled = pooled,
                FollowTarget = followTarget,
                HasFollowTarget = hasFollowTarget,
                FollowLocalOffset = followLocalOffset,
                ParticleSystems = particleSystems,
                Renderers = root.GetComponentsInChildren<Renderer>(true),
                PropertyBlock = new MaterialPropertyBlock(),
            };

            SetLayerRecursively(root, data.RenderLayer);
            foreach (var renderer in instance.Renderers)
            {
                if (renderer != null)
                {
                    renderer.renderingLayerMask = data.LightLayerMask;
                }
            }

            ApplyDefaultParams(instance);
            RestartParticles(instance);

            var handle = _instances.Add(instance);
            poolable.OnReturnedToPool = () => CleanupBookkeeping(handle);
            _allActive.Add(handle);

            return handle;
        }

        public void Preload(params VfxId[] ids)
        {
            foreach (var id in ids)
            {
                var data = _registry.ResolveOrPlaceholder<VfxData>(id.Value);
                if (data == null || data.Prefab == null || data.Flags.Pool.Kind != PoolPolicyKind.Pooled)
                {
                    continue;
                }

                _pool.Prewarm(data.Prefab, data.Flags.Pool.InitialCount);
            }
        }

        // FadeOutSec 分の放出停止待ちを経てから Pool へ返却する。
        public void Stop(Handle<VfxMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance) || instance.Stopping)
            {
                return;
            }

            StopEmission(instance);

            if (instance.Data.FadeOutSec > 0f)
            {
                instance.Stopping = true;
                instance.FadeOutRemaining = instance.Data.FadeOutSec;
                return;
            }

            _pool.Return(instance.Pooled);
        }

        // 即時返却(残留パーティクルの消失を待たない)。
        public void Kill(Handle<VfxMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return;
            }

            _pool.Return(instance.Pooled);
        }

        public bool IsPlaying(Handle<VfxMarker> handle) => _instances.IsValid(handle);

        // Cosmetic 配送は受信ハンドラ側が Spawn するため、送信元は具体的な Handle を得られない。
        // 「何か Spawn された」を確認する用途にも使える。
        public int ActiveCount => _allActive.Count;

        public GameObject GetGameObject(Handle<VfxMarker> handle)
            => _instances.TryGet(handle, out var instance) ? instance.Root : null;

        public void Move(Handle<VfxMarker> handle, Vector3 position)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                instance.Root.transform.position = position;
            }
        }

        public void Attach(Handle<VfxMarker> handle, Transform target)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                instance.FollowTarget = target;
                instance.HasFollowTarget = target != null;
            }
        }

        public void Detach(Handle<VfxMarker> handle)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                instance.HasFollowTarget = false;
                instance.FollowTarget = null;
            }
        }

        public void SetSpeed(Handle<VfxMarker> handle, float speed)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return;
            }

            instance.Speed = Mathf.Max(0f, speed);
            foreach (var ps in instance.ParticleSystems)
            {
                if (ps == null)
                {
                    continue;
                }

                var main = ps.main;
                main.simulationSpeed = instance.Speed;
            }
        }

        public void SetParam(Handle<VfxMarker> handle, string label, ParamValue value)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return;
            }

            ApplyParam(instance, label, value);
        }

        public void Tick(float dt)
        {
            FlushCosmeticBatch();

            for (var i = _allActive.Count - 1; i >= 0; i--)
            {
                var handle = _allActive[i];
                if (!_instances.TryGet(handle, out var instance))
                {
                    _allActive.RemoveAt(i);
                    continue;
                }

                if (instance.Stopping)
                {
                    instance.FadeOutRemaining -= dt;
                    if (instance.FadeOutRemaining <= 0f)
                    {
                        _pool.Return(instance.Pooled);
                    }

                    continue;
                }

                if (instance.Paused)
                {
                    continue;
                }

                instance.ElapsedSeconds += dt;

                if (instance.HasFollowTarget)
                {
                    if (instance.FollowTarget != null)
                    {
                        instance.Root.transform.position = instance.FollowTarget.TransformPoint(instance.FollowLocalOffset);
                        if (instance.Data.Anchor.FollowRotation)
                        {
                            instance.Root.transform.rotation = instance.FollowTarget.rotation;
                        }
                    }
                    else if (!instance.Data.Anchor.DetachOnStop)
                    {
                        Stop(handle);
                        continue;
                    }
                    else
                    {
                        instance.HasFollowTarget = false;
                    }
                }

                TickParamAnimations(instance);

                if (IsLifetimeExpired(instance))
                {
                    Stop(handle);
                }
            }
        }

        public void OnPause(PauseChannel channel, bool paused)
        {
            for (var i = 0; i < _allActive.Count; i++)
            {
                if (!_instances.TryGet(_allActive[i], out var instance) ||
                    instance.Data.Flags.Pause != PauseMode.PauseWithGame)
                {
                    continue;
                }

                instance.Paused = paused;
                foreach (var ps in instance.ParticleSystems)
                {
                    if (ps == null)
                    {
                        continue;
                    }

                    if (paused)
                    {
                        ps.Pause(true);
                    }
                    else
                    {
                        ps.Play(true);
                    }
                }
            }
        }

        public void StopAll(StopReason reason)
        {
            for (var i = _allActive.Count - 1; i >= 0; i--)
            {
                Kill(_allActive[i]);
            }
        }

        public void OnSceneUnload() => StopAll(StopReason.SceneUnload);

        private void FlushCosmeticBatch()
        {
            if (_pendingCosmeticBatch.Count == 0)
            {
                return;
            }

            _netBridge.Broadcast(new VfxNetBatchMsg { Items = _pendingCosmeticBatch.ToArray() }, NetChannel.Unreliable);
            _pendingCosmeticBatch.Clear();
        }

        private void CleanupBookkeeping(Handle<VfxMarker> handle)
        {
            _allActive.Remove(handle);
            _instances.Remove(handle);
        }

        private static bool IsLifetimeExpired(VfxInstance instance)
        {
            switch (instance.Data.LifeMode)
            {
                case VfxLifeMode.Loop:
                    return false;

                case VfxLifeMode.Duration:
                    return instance.ElapsedSeconds >= instance.Data.Duration;

                case VfxLifeMode.OneShot:
                default:
                    if (instance.ParticleSystems.Length == 0)
                    {
                        // ParticleSystem を持たない Prefab(将来の VFX Graph 等)は Duration を
                        // 終了判定のフォールバックとして使う。
                        return instance.ElapsedSeconds >= instance.Data.Duration;
                    }

                    foreach (var ps in instance.ParticleSystems)
                    {
                        if (ps != null && ps.IsAlive(true))
                        {
                            return false;
                        }
                    }

                    return true;
            }
        }

        // RenderLayer は GameObject.layer(カリングマスク用)として扱う。子オブジェクトにも再帰適用する。
        internal static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            var t = root.transform;
            for (var i = 0; i < t.childCount; i++)
            {
                SetLayerRecursively(t.GetChild(i).gameObject, layer);
            }
        }

        private static void RestartParticles(VfxInstance instance)
        {
            foreach (var ps in instance.ParticleSystems)
            {
                if (ps == null)
                {
                    continue;
                }

                ps.Clear(true);
                ps.Simulate(0f, true, true);
                ps.Play(true);
            }
        }

        private static void StopEmission(VfxInstance instance)
        {
            foreach (var ps in instance.ParticleSystems)
            {
                if (ps != null)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        private static void ApplyDefaultParams(VfxInstance instance)
        {
            if (instance.Data.Params == null)
            {
                return;
            }

            foreach (var param in instance.Data.Params)
            {
                ApplyParam(instance, param.Label, param.Default);
            }
        }

        // Anim(ValueDef)が Parametric/Curve で設定されている Float パラメータのみ、経過時間で自動反映する。
        // Mode=Constant のまま(未設定時のデフォルト = Constant 0)は「アニメーションなし」を意味するので、
        // ここで毎フレーム 0 を上書きしてしまわないようにモードで判定する。
        private static void TickParamAnimations(VfxInstance instance)
        {
            if (instance.Data.Params == null)
            {
                return;
            }

            foreach (var param in instance.Data.Params)
            {
                if (param.Type != VfxParamType.Float || param.Anim.Mode == ValueMode.Constant)
                {
                    continue;
                }

                var value = param.Anim.EvaluateAt(instance.ElapsedSeconds * instance.Speed);
                ApplyParam(instance, param.Label, ParamValue.Of(value));
            }
        }

        private static void ApplyParam(VfxInstance instance, string label, ParamValue value)
        {
            var param = FindParam(instance.Data, label);
            if (param == null || string.IsNullOrEmpty(param.Value.TargetProperty))
            {
                return;
            }

            var id = ResolvePropertyId(param.Value.TargetProperty);

            switch (param.Value.Type)
            {
                case VfxParamType.Float:
                    instance.PropertyBlock.SetFloat(id, value.FloatValue);
                    break;

                case VfxParamType.Int:
                    // MaterialPropertyBlock の int 系 setter は Unity バージョン間で名前が揺れるため、
                    // shader 側では float uniform として受ける前提で統一する。
                    instance.PropertyBlock.SetFloat(id, value.IntValue);
                    break;

                case VfxParamType.Color:
                    instance.PropertyBlock.SetColor(id, value.ColorValue);
                    break;

                case VfxParamType.Vector:
                    instance.PropertyBlock.SetVector(id, value.VectorValue);
                    break;

                case VfxParamType.Texture:
                    if (value.ObjectValue is Texture tex)
                    {
                        instance.PropertyBlock.SetTexture(id, tex);
                    }

                    break;

                case VfxParamType.Curve:
                case VfxParamType.Gradient:
                default:
                    // MaterialPropertyBlock は Curve/Gradient を直接サポートしない。
                    // これらはエディタでの参照値・将来のベイク処理向けのメタデータとして保持するのみ。
                    return;
            }

            foreach (var renderer in instance.Renderers)
            {
                if (renderer != null)
                {
                    renderer.SetPropertyBlock(instance.PropertyBlock);
                }
            }
        }

        private static VfxParam? FindParam(VfxData data, string label)
        {
            if (data.Params == null || string.IsNullOrEmpty(label))
            {
                return null;
            }

            foreach (var param in data.Params)
            {
                if (param.Label == label)
                {
                    return param;
                }
            }

            return null;
        }

        private static int ResolvePropertyId(string propertyName)
        {
            if (!PropertyIdCache.TryGetValue(propertyName, out var id))
            {
                id = Shader.PropertyToID(propertyName);
                PropertyIdCache[propertyName] = id;
            }

            return id;
        }
    }
}
