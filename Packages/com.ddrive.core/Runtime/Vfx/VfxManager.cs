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
using AnchorId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anchoring.AnchorMarker>;

namespace DDrive.Runtime.Vfx
{
    // VFX 再生の中核([04_vfx.md] §3)。Instance/Handle は外部非公開、呼び出し側は
    // Handle<VfxMarker> のみを介して操作する(Audio の AudioManager と同じ設計)。
    //
    // 対応範囲: ParticleSystem。VFX Graph(com.unity.visualeffectgraph)はこのプロジェクトに
    // パッケージが未導入のためハード依存を避けている。導入後は VisualEffect コンポーネントの
    // 検出・SetFloat 等の反映を Instance 生成時に追加すれば同じ Handle API で扱える。
    //
    // 姿勢(位置/回転/スケール)の式は AnchorPose に集約してあり、Spawn・Tick(追従)・ReapplyAnchor
    // (エディタでの Anchor ライブ編集)の全てが同じ式を通る([04_vfx.md] §2 / 2026-09-08 改定)。
    public sealed class VfxManager : IAssetManager
    {
        private sealed class VfxInstance
        {
            public VfxData Data;
            public GameObject Root;
            public PooledObject Pooled;
            public Transform FollowTarget;
            public bool HasFollowTarget;

            // 有効な Anchor 定義。埋め込み Data.Anchor か、AnchorId の連鎖を合成したもの([21] §3.3)。
            // Tick / ReapplyAnchor は Data.Anchor ではなくこちらを見る。
            public AnchorDef Anchor;

            // AnchorId 経由で解決した場合の元 ID(ReapplyAnchor で再合成するため。0 = 埋め込み)。
            public AnchorId AnchorSource;

            // 生成ディレイ待ち(実体未生成)。Root/Pooled は null、ParticleSystems/Renderers は空配列。
            public bool Pending;
            public float PendingRemaining;
            public Transform PendingContextRoot;

            // AnchorPoint / AnchorData 由来の追加オフセット/回転/スケール(SpawnOffset + ランダム散らばり)。
            // Spawn 時に 1 回だけサンプリングし、以後 Tick/ReapplyAnchor では再抽選しない
            // (毎 Tick 再計算するとランダム分が消えて位置が吸い付く/震える)。
            public Vector3 AnchorExtraOffset;
            public Quaternion AnchorJitterRotation = Quaternion.identity;
            public float AnchorScaleMultiplier = 1f;

            // 追従時に使うローカルオフセット(= Data.Anchor.LocalOffset + AnchorExtraOffset のキャッシュ)。
            public Vector3 FollowLocalOffset;

            // 追従時の相対回転(= Euler(LocalEuler) * JitterRotation のキャッシュ)。
            public Quaternion FollowLocalRotation = Quaternion.identity;

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

                // [14_networking.md] §4(6-0, C) — AnchorNetId が解決できればそこへ追従再生する。
                // 解決できない(0、または NGO 未接続/対象が既に Despawn 済み)場合は既存のとおり
                // 送信時点のワールド座標(Position)固定で再生する。
                var anchorRoot = item.AnchorNetId != 0 ? _netBridge.ResolveNetObject(item.AnchorNetId) : null;
                if (anchorRoot != null)
                {
                    SpawnDataLocal(data, contextRoot: anchorRoot);
                }
                else
                {
                    SpawnDataLocal(data, explicitPose: (item.Position, Quaternion.identity));
                }
            }
        }

        // ── Spawn ──

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

        // Anchor アセットを明示して Spawn する。Data.AnchorId / 埋め込み Anchor より優先([21_anchor_spec.md] §3.3)。
        public Handle<VfxMarker> Spawn(VfxId id, AnchorId anchor, Transform attach = null)
            => SpawnData(_registry.ResolveOrPlaceholder<VfxData>(id.Value), contextRoot: attach, anchorOverride: anchor);

        public Handle<VfxMarker> SpawnData(VfxData data, (Vector3 pos, Quaternion rot)? explicitPose = null, Transform contextRoot = null, AnchorId anchorOverride = default)
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
                    // [14_networking.md] §4(6-0, C) — contextRoot に NetworkObject が付いていて解決できる
                    // 場合は実値、それ以外は 0(受信側は既存のとおり Position にフォールバック)。
                    AnchorNetId = _netBridge.ResolveNetId(contextRoot),
                    Position = ResolveWorldPositionForBroadcast(data, explicitPose, contextRoot, anchorOverride),
                });
                return Handle<VfxMarker>.Invalid;
            }

            return SpawnDataLocal(data, explicitPose, contextRoot, anchorOverride);
        }

        // 配置セット(AnchorGroup)など、呼び出し側が合成済みの姿勢(spec)を持っている場合の経路([22] §3.5)。
        public Handle<VfxMarker> SpawnData(VfxData data, in AnchorSpawnSpec spec, Transform contextRoot = null)
        {
            if (data == null || data.Prefab == null)
            {
                return Handle<VfxMarker>.Invalid;
            }

            if (data.Flags.Net == NetMode.Cosmetic)
            {
                var resolved = AnchorResolver.Resolve(spec.Def, contextRoot);
                _pendingCosmeticBatch.Add(new VfxNetMsg
                {
                    VfxId = data.Id,
                    AnchorNetId = _netBridge.ResolveNetId(contextRoot),
                    Position = AnchorPose.WorldPosition(spec.Def, resolved, spec.ExtraOffset),
                });
                return Handle<VfxMarker>.Invalid;
            }

            return SpawnDataLocal(data, null, contextRoot, default, spec);
        }

        // 優先順位: 引数 anchorOverride > Data.AnchorId > Data.Anchor(埋め込み)([21_anchor_spec.md] §3.3)。
        // sampleRandom=false は静的な合成のみ(ネット送信位置・エディタの再適用)。
        private AnchorSpawnSpec ResolveAnchorSpec(VfxData data, AnchorId anchorOverride, bool sampleRandom)
        {
            if (anchorOverride.IsValid)
            {
                return AnchorChain.Resolve(_registry, anchorOverride, sampleRandom);
            }

            if (data.AnchorId.IsValid)
            {
                return AnchorChain.Resolve(_registry, data.AnchorId, sampleRandom);
            }

            return AnchorSpawnSpec.FromDef(data.Anchor);
        }

        private Vector3 ResolveWorldPositionForBroadcast(VfxData data, (Vector3 pos, Quaternion rot)? explicitPose, Transform contextRoot, AnchorId anchorOverride)
        {
            if (explicitPose.HasValue)
            {
                return explicitPose.Value.pos;
            }

            var def = ResolveAnchorSpec(data, anchorOverride, sampleRandom: false).Def;
            var resolved = AnchorResolver.Resolve(def, contextRoot);
            return AnchorPose.WorldPosition(def, resolved, Vector3.zero);
        }

        private Handle<VfxMarker> SpawnDataLocal(VfxData data, (Vector3 pos, Quaternion rot)? explicitPose = null, Transform contextRoot = null, AnchorId anchorOverride = default, AnchorSpawnSpec? presolved = null)
        {
            // OnReceiveCosmeticBatch はここへ直接来るため(SpawnData の null/Prefab ガードを通らない)、
            // Placeholder 解決に失敗した場合(Prefab 未設定)にも安全に無視できるようにする。
            if (data == null || data.Prefab == null)
            {
                return Handle<VfxMarker>.Invalid;
            }

            var instance = new VfxInstance
            {
                Data = data,
                Anchor = data.Anchor,
                ParticleSystems = System.Array.Empty<ParticleSystem>(),
                Renderers = System.Array.Empty<Renderer>(),
            };

            if (!explicitPose.HasValue)
            {
                // AnchorId の連鎖(またはそのまま埋め込み)を合成し、ランダム分をここで 1 回だけサンプリングする。
                // 配置セット経由なら合成済み(presolved)をそのまま使う。
                var spec = presolved ?? ResolveAnchorSpec(data, anchorOverride, sampleRandom: true);

                // 確率生成に外れた場合は何も出さない(設計上の期待動作なので警告なし)。
                if (spec.SpawnChance < 1f && UnityEngine.Random.value >= spec.SpawnChance)
                {
                    return Handle<VfxMarker>.Invalid;
                }

                instance.Anchor = spec.Def;
                instance.AnchorSource = presolved.HasValue ? default : (anchorOverride.IsValid ? anchorOverride : data.AnchorId);
                instance.AnchorExtraOffset = spec.ExtraOffset;
                instance.AnchorJitterRotation = spec.JitterRotation;
                instance.AnchorScaleMultiplier = spec.ScaleMultiplier;

                // 生成ディレイ: 実体を作らず Pending として台帳に載せ、Tick のカウントダウン後に生成する([21] §3.5)。
                if (spec.DelaySec > 0f)
                {
                    instance.Pending = true;
                    instance.PendingRemaining = spec.DelaySec;
                    instance.PendingContextRoot = contextRoot;
                    var pendingHandle = _instances.Add(instance);
                    _allActive.Add(pendingHandle);
                    return pendingHandle;
                }
            }

            var handle = _instances.Add(instance);
            Materialize(handle, instance, explicitPose, contextRoot);
            _allActive.Add(handle);
            return handle;
        }

        // Pool から実体を借りて Instance に結び付ける(即時 Spawn と Pending 解除の共通経路)。
        private void Materialize(Handle<VfxMarker> handle, VfxInstance instance, (Vector3 pos, Quaternion rot)? explicitPose, Transform contextRoot)
        {
            var data = instance.Data;
            if (data.Flags.Pool.Kind == PoolPolicyKind.Pooled && _pool is PoolService concrete)
            {
                concrete.SetLimit(data.Prefab, data.Flags.Pool.MaxCount);
            }

            var pooled = _pool.Rent(data.Prefab);
            pooled.Priority = data.Flags.Priority;

            var root = pooled.GameObject;
            root.transform.SetParent(null);

            instance.Root = root;
            instance.Pooled = pooled;
            instance.PropertyBlock = new MaterialPropertyBlock();
            instance.Pending = false;
            instance.PendingContextRoot = null;

            if (explicitPose.HasValue)
            {
                root.transform.SetPositionAndRotation(explicitPose.Value.pos, explicitPose.Value.rot);
                root.transform.localScale = AnchorPose.BaseScale(instance.Anchor);
            }
            else
            {
                var resolved = AnchorResolver.Resolve(instance.Anchor, contextRoot);
                instance.FollowTarget = resolved;
                instance.HasFollowTarget = resolved != null;

                // 解決先が AnchorPoint(シーン配置型アンカー)なら、アンカー固有のオフセット+
                // ランダム散らばり(位置/回転/スケール)を追加で 1 回だけサンプリングする([04_vfx.md] §2.5)。
                if (resolved != null && resolved.TryGetComponent<AnchorPoint>(out var point))
                {
                    instance.AnchorExtraOffset += point.SampleLocalOffset(Vector3.zero);
                    instance.AnchorJitterRotation = point.SampleRotation(instance.AnchorJitterRotation);
                    instance.AnchorScaleMultiplier *= point.SampleScaleMultiplier();
                }

                ApplyAnchorPose(instance);
            }

            var poolable = root.GetComponent<VfxInstancePoolable>();
            if (poolable == null)
            {
                poolable = root.AddComponent<VfxInstancePoolable>();
            }

            instance.ParticleSystems = root.GetComponentsInChildren<ParticleSystem>(true);
            instance.Renderers = root.GetComponentsInChildren<Renderer>(true);
            poolable.ParticleSystems = instance.ParticleSystems;

            SetLayerRecursively(root, data.RenderLayer);
            if (data.LightLayerMask != VfxData.LightLayerKeepPrefab)
            {
                foreach (var renderer in instance.Renderers)
                {
                    if (renderer != null)
                    {
                        renderer.renderingLayerMask = data.LightLayerMask;
                    }
                }
            }

            ApplyDefaultParams(instance);
            RestartParticles(instance);

            poolable.OnReturnedToPool = () => CleanupBookkeeping(handle);
        }

        // Data.Anchor + AnchorPoint 由来の追加分から Root の姿勢を組み立てる(Spawn / ReapplyAnchor 共通)。
        private static void ApplyAnchorPose(VfxInstance instance)
        {
            var anchor = instance.Anchor;
            var target = instance.HasFollowTarget ? instance.FollowTarget : null;

            instance.FollowLocalOffset = AnchorPose.LocalOffsetWithExtra(anchor, instance.AnchorExtraOffset);
            instance.FollowLocalRotation = Quaternion.Euler(anchor.LocalEuler) * instance.AnchorJitterRotation;

            var t = instance.Root.transform;
            t.SetPositionAndRotation(
                AnchorPose.WorldPosition(anchor, target, instance.AnchorExtraOffset),
                AnchorPose.WorldRotation(anchor, target, instance.AnchorJitterRotation));
            t.localScale = AnchorPose.WorldScale(anchor, instance.AnchorScaleMultiplier);
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

        // ── Stop / Kill ──

        // FadeOutSec 分の放出停止待ちを経てから Pool へ返却する。
        public void Stop(Handle<VfxMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance) || instance.Stopping)
            {
                return;
            }

            // 生成待ちは実体が無いので生成をキャンセルして台帳から外すだけ。
            if (instance.Pending)
            {
                ReturnToPool(handle, instance);
                return;
            }

            StopEmission(instance);

            if (instance.Data.FadeOutSec > 0f && instance.Root != null)
            {
                instance.Stopping = true;
                instance.FadeOutRemaining = instance.Data.FadeOutSec;
                return;
            }

            ReturnToPool(handle, instance);
        }

        // 即時返却(残留パーティクルの消失を待たない)。
        public void Kill(Handle<VfxMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return;
            }

            ReturnToPool(handle, instance);
        }

        // Pool へ返す。Root がシーン破棄等で既に消えている場合、Pool は IPoolable.OnReturn を呼ばない
        // (= 台帳掃除のコールバックが来ない)ため、ここで必ず台帳から外す(CleanupBookkeeping は冪等)。
        private void ReturnToPool(Handle<VfxMarker> handle, VfxInstance instance)
        {
            _pool.Return(instance.Pooled);
            CleanupBookkeeping(handle);
        }

        // ── 問い合わせ ──

        // 終了済み Handle の問い合わせは正常系(エディタのポーリング / Dispatcher の後始末)なので警告を出さない。
        public bool IsPlaying(Handle<VfxMarker> handle) => _instances.IsValidSilent(handle);

        // Cosmetic 配送は受信ハンドラ側が Spawn するため、送信元は具体的な Handle を得られない。
        // 「何か Spawn された」を確認する用途にも使える。
        public int ActiveCount => _allActive.Count;

        public GameObject GetGameObject(Handle<VfxMarker> handle)
            => _instances.TryGet(handle, out var instance) ? instance.Root : null;

        // 生成ディレイ待ち(Handle は有効だが実体はまだ無い)か。
        public bool IsPending(Handle<VfxMarker> handle)
            => _instances.TryGet(handle, out var instance) && instance.Pending;

        // 実際に使われている Anchor 定義(AnchorId の連鎖を合成済み)。エディタの表示・逆変換用。
        // Handle → Data(エディタのプレビュー台帳が OneShot の終了判定などに使う)。無効なら false。
        public bool TryGetData(Handle<VfxMarker> handle, out VfxData data)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                data = instance.Data;
                return true;
            }

            data = null;
            return false;
        }

        public bool TryGetEffectiveAnchor(Handle<VfxMarker> handle, out AnchorDef anchor)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                anchor = instance.Anchor;
                return true;
            }

            anchor = default;
            return false;
        }

        // 追従先(解決済み Anchor)。World 固定や explicitPose で生成した場合は null。
        public bool TryGetAnchorTarget(Handle<VfxMarker> handle, out Transform target)
        {
            if (_instances.TryGet(handle, out var instance) && instance.HasFollowTarget)
            {
                target = instance.FollowTarget;
                return target != null;
            }

            target = null;
            return false;
        }

        // AnchorPoint 由来の追加オフセット(SpawnOffset + ランダム)。エディタが「ワールド座標 → LocalOffset」
        // の逆変換をするときに差し引くために公開する。
        public Vector3 GetAnchorExtraOffset(Handle<VfxMarker> handle)
            => _instances.TryGet(handle, out var instance) ? instance.AnchorExtraOffset : Vector3.zero;

        // ── Handle 操作 ──

        public void Move(Handle<VfxMarker> handle, Vector3 position)
        {
            if (_instances.TryGet(handle, out var instance) && instance.Root != null)
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

        // Data.Anchor が(エディタ等で)変更された後に、再生中 Instance の姿勢を再計算する。
        // 追従先の再解決はしない(Path/Space の変更は Spawn し直す)。AnchorPoint のランダム分は保持する。
        public void ReapplyAnchor(Handle<VfxMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return;
            }

            // AnchorId 経由なら連鎖を再合成(ランダム分は Instance が保持したまま)、埋め込みなら Data から取り直す。
            instance.Anchor = instance.AnchorSource.IsValid
                ? AnchorChain.Resolve(_registry, instance.AnchorSource, sampleRandom: false).Def
                : instance.Data.Anchor;

            if (instance.Root != null)
            {
                ApplyAnchorPose(instance);
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

        // ── Tick / Pause / StopAll ──

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

                // 生成ディレイ待ち: カウントダウンして時間が来たら実体を作る(Pause 中は進めない)。
                if (instance.Pending)
                {
                    if (!instance.Paused)
                    {
                        instance.PendingRemaining -= dt;
                        if (instance.PendingRemaining <= 0f)
                        {
                            Materialize(handle, instance, null, instance.PendingContextRoot);
                        }
                    }

                    continue;
                }

                // シーン破棄等で Root が先に消えた Instance は台帳から外す(NRE で Tick を止めない)。
                if (instance.Root == null)
                {
                    ReturnToPool(handle, instance);
                    continue;
                }

                // 一時停止中はフェードアウト(Stopping)の残り時間も進めない。
                if (instance.Paused)
                {
                    continue;
                }

                if (instance.Stopping)
                {
                    instance.FadeOutRemaining -= dt;
                    if (instance.FadeOutRemaining <= 0f)
                    {
                        ReturnToPool(handle, instance);
                    }

                    continue;
                }

                instance.ElapsedSeconds += dt;

                if (instance.HasFollowTarget)
                {
                    if (instance.FollowTarget != null)
                    {
                        var t = instance.Root.transform;
                        t.position = instance.FollowTarget.TransformPoint(instance.FollowLocalOffset);
                        if (instance.Anchor.FollowRotation)
                        {
                            t.rotation = instance.FollowTarget.rotation * instance.FollowLocalRotation;
                        }
                    }
                    else if (!instance.Anchor.DetachOnStop)
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

        public void OnPause(PauseChannel channel, bool paused) => ApplyPause(paused, respectFlags: true);

        public void StopAll(StopReason reason)
        {
            for (var i = _allActive.Count - 1; i >= 0; i--)
            {
                Kill(_allActive[i]);
            }
        }

        // 再生中の VFX を Flags.Pause に関係なく全部一時停止 / 再開する(エディタのプレビュー一時停止用。ゲーム側は OnPause)。
        public void SetPausedAll(bool paused) => ApplyPause(paused, respectFlags: false);

        // OnPause / SetPausedAll の共通実装。respectFlags=true なら Flags.Pause=PauseWithGame のものだけ。
        // Stopping(フェードアウト中)は放出を再開させない: 再開時に Play(true) すると放出が戻ってしまうため、
        // Play で残留パーティクルの再生だけ戻したあと再度 StopEmitting にする。
        private void ApplyPause(bool paused, bool respectFlags)
        {
            for (var i = 0; i < _allActive.Count; i++)
            {
                if (!_instances.TryGet(_allActive[i], out var instance) || instance.ParticleSystems == null)
                {
                    continue;
                }

                if (respectFlags && instance.Data.Flags.Pause != PauseMode.PauseWithGame)
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
                    else if (instance.Stopping)
                    {
                        ps.Play(true);
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                    else
                    {
                        ps.Play(true);
                    }
                }
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
