using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Net;
using UnityEngine;
using SeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Audio.SeMarker>;
using AnchorId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anchoring.AnchorMarker>;

namespace DDrive.Runtime.Audio
{
    // SE 再生の中核。BGM(クロスフェード等)は 1-2 で拡張する。
    // Instance/Handle は外部非公開、ゲームコードは Handle<SeMarker> のみを介して操作する。
    public sealed class AudioManager : IAssetManager
    {
        private sealed class SeInstance
        {
            public SeData Data;
            public AudioSource Source;
            public PooledObject Pooled;
            public Transform FollowTarget;
            public bool HasFollowTarget;

            // 有効な Anchor 定義(埋め込み Data.Anchor か AnchorId の連鎖を合成したもの。[21] §3.3)。
            public AnchorDef Anchor;

            // 生成ディレイ待ち(Source は借りて位置も決めてあるが、まだ Play していない)。
            public bool Pending;
            public float PendingRemaining;

            // 追従時に使うローカルオフセット。AnchorPoint のランダム散らばりは Play 時に1回だけ
            // サンプリングされるため、Tick では Data 側から再計算せずこちらを使う。
            public Vector3 FollowLocalOffset;

            // Pause() 中は isPlaying=false になるため、Tick の再生完了判定(自動返却)から除外する目印。
            public bool Paused;

            // Stop(fade) 用。0 より大きい間はフェード中で、経過に応じて音量を落とし、完了時に返却する。
            public float FadeOutRemaining;
            public float FadeOutDuration;
            public float FadeOutStartVolume;
        }

        private readonly IPoolService _pool;
        private readonly IAssetRegistry _registry;
        private readonly GameObject _seSourcePrefab;
        private readonly InstanceStore<SeMarker, SeInstance> _instances = new();
        private readonly List<Handle<SeMarker>> _allActive = new();
        private readonly Dictionary<ulong, List<Handle<SeMarker>>> _activeBySeId = new();
        private readonly Dictionary<ulong, int> _roundRobinIndex = new();
        private readonly Dictionary<ulong, float> _lastPlayedAt = new();

        // [14_networking.md] §3/§4/§8 — NetMode=Cosmetic の SE は直接再生せず、いったんこのバッチに
        // 積んで Tick で 1 パケットにまとめて Broadcast する(欠落許容の Unreliable)。
        // 実際の再生は受信ハンドラ(OnReceiveCosmeticBatch)からのみ行う(送信元も loopback 経由で
        // 自分の Broadcast を受け取って初めて鳴る。二重再生を避けるため直接は鳴らさない)。
        private readonly INetBridge _netBridge;
        private readonly List<SeNetMsg> _pendingCosmeticBatch = new();

        private float _clock;

        public AssetType Type => AssetType.Se;

        public AudioManager(IPoolService pool, IAssetRegistry registry, GameObject seSourcePrefab, INetBridge netBridge = null)
        {
            _pool = pool;
            _registry = registry;
            _seSourcePrefab = seSourcePrefab;
            _netBridge = netBridge ?? new LocalLoopbackBridge();
            _netBridge.Subscribe<SeNetBatchMsg>(OnReceiveCosmeticBatch);
        }

        private void OnReceiveCosmeticBatch(ulong senderId, SeNetBatchMsg batch)
        {
            if (batch.Items == null)
            {
                return;
            }

            foreach (var item in batch.Items)
            {
                var data = _registry.ResolveOrPlaceholder<SeData>(item.SeId);

                // [14_networking.md] §4(6-0, C) — AnchorNetId が解決できればそこへ追従再生する
                // (解決できなければ既存のとおり送信時点の Position 固定)。
                var anchorRoot = item.AnchorNetId != 0 ? _netBridge.ResolveNetObject(item.AnchorNetId) : null;
                if (anchorRoot != null)
                {
                    PlaySeDataLocal(data, contextRoot: anchorRoot);
                }
                else
                {
                    PlaySeDataLocal(data, item.Position);
                }
            }
        }

        // FR-1.4: 未登録/未ロードの SE は無音 0.5s のプレースホルダで代替し、警告は 1 回だけ出す
        // (実際の警告一元化は AssetRegistry 側、ここは種別ごとの Placeholder 実体を用意するだけ)。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterPlaceholder()
        {
            PlaceholderProvider.Register(CreateSilentPlaceholder);
        }

        private static SeData CreateSilentPlaceholder()
        {
            var data = ScriptableObject.CreateInstance<SeData>();
            data.DisplayName = "<Placeholder:SE>";
            data.SelectMode = ClipSelectMode.First;
            data.Volume = 1f;
            data.MaxConcurrent = 1;
            data.Clips = new[] { CreateSilentClip() };
            return data;
        }

        private static AudioClip CreateSilentClip()
        {
            const int sampleRate = 44100;
            const float durationSec = 0.5f;
            return AudioClip.Create("PlaceholderSilence", (int)(sampleRate * durationSec), 1, sampleRate, false);
        }

        public void SetGlobalSeLimit(int maxConcurrentSources)
        {
            if (_pool is PoolService concrete)
            {
                concrete.SetLimit(_seSourcePrefab, maxConcurrentSources);
            }
        }

        // 引数なし: Data.Anchor をそのまま解決する(コンテキストが無いので BoneName/NamedObject は
        // 解決できず World 扱いにフォールバックする。実際の解決には下の contextRoot 版を使う)。
        public Handle<SeMarker> PlaySe(SeId id)
            => PlaySeData(_registry.ResolveOrPlaceholder<SeData>(id.Value));

        // ルール1: 明示座標は Data.Anchor を完全に上書きする。
        public Handle<SeMarker> PlaySe(SeId id, Vector3 pos)
            => PlaySeData(_registry.ResolveOrPlaceholder<SeData>(id.Value), explicitPosition: pos);

        // Data.Anchor(ボーン名等)を contextRoot 配下で解決する。「誰に付けるか」を呼び出し側が渡し、
        // 「どう付けるか」は Data.Anchor が持つ、という役割分担(AnchorResolver 参照)。
        public Handle<SeMarker> PlaySe(SeId id, Transform contextRoot)
            => PlaySeData(_registry.ResolveOrPlaceholder<SeData>(id.Value), contextRoot: contextRoot);

        // Anchor アセットを明示して再生する。Data.AnchorId / 埋め込み Anchor より優先([21_anchor_spec.md] §3.3)。
        public Handle<SeMarker> PlaySe(SeId id, AnchorId anchor, Transform contextRoot = null)
            => PlaySeData(_registry.ResolveOrPlaceholder<SeData>(id.Value), contextRoot: contextRoot, anchorOverride: anchor);

        public Handle<SeMarker> PlaySeData(SeData data, Vector3? explicitPosition = null, Transform contextRoot = null, AnchorId anchorOverride = default)
        {
            if (data == null)
            {
                return Handle<SeMarker>.Invalid;
            }

            // Cosmetic は直接再生せず、Tick でまとめて Broadcast する([14_networking.md] §3/§4/§8)。
            // 実際の再生は自分を含む全員が受信ハンドラ経由で行うため、ここでは Invalid を返す
            // (呼び出し側は「必ず今フレーム中にハンドルを得られる」ことを前提にしないこと)。
            if (data.Flags.Net == NetMode.Cosmetic)
            {
                _pendingCosmeticBatch.Add(new SeNetMsg
                {
                    SeId = data.Id,
                    AnchorNetId = _netBridge.ResolveNetId(contextRoot),
                    Position = ResolveWorldPositionForBroadcast(data, explicitPosition, contextRoot, anchorOverride),
                });
                return Handle<SeMarker>.Invalid;
            }

            return PlaySeDataLocal(data, explicitPosition, contextRoot, anchorOverride);
        }

        // 配置セット(AnchorGroup)など、呼び出し側が合成済みの姿勢(spec)を持っている場合の経路([22] §3.5)。
        // [14_networking.md] §6/§5 実装メモ(6-0、Seed の実消費) — seed が指定されると、Clip 選択(SelectMode
        // が Random のとき)とピッチのランダム幅を UnityEngine.Random ではなく seed から決定的に引く
        // (PresentationManager が networked な Se トラックでのみ instance.Seed を渡す。既存の呼び出し元は
        // seed を渡さないため挙動は変わらない)。
        public Handle<SeMarker> PlaySeData(SeData data, in AnchorSpawnSpec spec, Transform contextRoot = null, ushort? seed = null)
        {
            if (data == null)
            {
                return Handle<SeMarker>.Invalid;
            }

            if (data.Flags.Net == NetMode.Cosmetic)
            {
                var resolved = AnchorResolver.Resolve(spec.Def, contextRoot);
                _pendingCosmeticBatch.Add(new SeNetMsg
                {
                    SeId = data.Id,
                    AnchorNetId = _netBridge.ResolveNetId(contextRoot),
                    Position = resolved != null ? resolved.TransformPoint(spec.Def.LocalOffset + spec.ExtraOffset) : spec.Def.LocalOffset + spec.ExtraOffset,
                });
                return Handle<SeMarker>.Invalid;
            }

            return PlaySeDataLocal(data, null, contextRoot, default, spec, seed);
        }

        // 優先順位: 引数 anchorOverride > Data.AnchorId > Data.Anchor(埋め込み)([21_anchor_spec.md] §3.3)。
        private AnchorSpawnSpec ResolveAnchorSpec(SeData data, AnchorId anchorOverride, bool sampleRandom)
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

        private Vector3 ResolveWorldPositionForBroadcast(SeData data, Vector3? explicitPosition, Transform contextRoot, AnchorId anchorOverride)
        {
            if (explicitPosition.HasValue)
            {
                return explicitPosition.Value;
            }

            var def = ResolveAnchorSpec(data, anchorOverride, sampleRandom: false).Def;
            var resolved = AnchorResolver.Resolve(def, contextRoot);
            return resolved != null ? resolved.TransformPoint(def.LocalOffset) : def.LocalOffset;
        }

        private Handle<SeMarker> PlaySeDataLocal(SeData data, Vector3? explicitPosition = null, Transform contextRoot = null, AnchorId anchorOverride = default, AnchorSpawnSpec? presolved = null, ushort? seed = null)
        {
            if (IsOnCooldown(data))
            {
                return Handle<SeMarker>.Invalid;
            }

            // AnchorId の連鎖(またはそのまま埋め込み)を合成し、ランダム分をここで 1 回だけサンプリングする。
            // 明示座標のときは Anchor を使わないので合成しない(ディレイ/確率も効かない)。
            var anchor = data.Anchor;
            var anchorExtraOffset = Vector3.zero;
            var delay = 0f;
            if (!explicitPosition.HasValue)
            {
                var spec = presolved ?? ResolveAnchorSpec(data, anchorOverride, sampleRandom: true);
                if (spec.SpawnChance < 1f && UnityEngine.Random.value >= spec.SpawnChance)
                {
                    return Handle<SeMarker>.Invalid;
                }

                anchor = spec.Def;
                anchorExtraOffset = spec.ExtraOffset;
                delay = spec.DelaySec;
            }

            EnforceMaxConcurrent(data);

            var pooled = _pool.Rent(_seSourcePrefab);
            pooled.Priority = data.Flags.Priority;

            var source = pooled.GameObject.GetComponent<AudioSource>();
            if (source == null)
            {
                source = pooled.GameObject.AddComponent<AudioSource>();
            }

            ConfigureSource(source, data, seed);

            Transform followTarget = null;
            var hasFollowTarget = false;
            var followLocalOffset = anchor.LocalOffset + anchorExtraOffset;
            var sourceTransform = pooled.GameObject.transform;
            sourceTransform.SetParent(null);

            if (explicitPosition.HasValue)
            {
                if (data.Spatial == SpatialMode.None)
                {
                    Debug.LogWarning($"[DDrive] SE '{data.DisplayName}' is Spatial=None; explicit position argument is ignored.");
                }
                else
                {
                    sourceTransform.position = explicitPosition.Value;
                }
            }
            else
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (data.Spatial == SpatialMode.AtPosition)
                {
                    Debug.LogWarning($"[DDrive] SE '{data.DisplayName}' is Spatial=AtPosition but no position was passed; falling back to Anchor/World.");
                }
#endif
                var resolved = AnchorResolver.Resolve(anchor, contextRoot);
                if (resolved != null)
                {
                    followTarget = resolved;
                    hasFollowTarget = true;

                    // 解決先が AnchorPoint(シーン配置型アンカー)なら、アンカー固有のオフセット+
                    // 位置のランダム散らばりを追加適用する(回転/スケールは音に無関係なので位置のみ)。
                    if (resolved.TryGetComponent<AnchorPoint>(out var point))
                    {
                        followLocalOffset = point.SampleLocalOffset(followLocalOffset);
                    }

                    sourceTransform.position = resolved.TransformPoint(followLocalOffset);
                    if (anchor.FollowRotation)
                    {
                        sourceTransform.rotation = resolved.rotation;
                    }
                }
                else
                {
                    sourceTransform.position = followLocalOffset;
                }
            }

            if (data.StartOffsetSec > 0f && source.clip != null)
            {
                source.time = Mathf.Min(data.StartOffsetSec, Mathf.Max(0f, source.clip.length - 0.001f));
            }

            // 生成ディレイ: Source は借りて位置も決めた状態で待ち、Tick のカウントダウン後に Play する([21] §3.5)。
            if (delay <= 0f)
            {
                source.Play();
            }

            var instance = new SeInstance
            {
                Data = data,
                Source = source,
                Pooled = pooled,
                FollowTarget = followTarget,
                HasFollowTarget = hasFollowTarget,
                FollowLocalOffset = followLocalOffset,
                Anchor = anchor,
                Pending = delay > 0f,
                PendingRemaining = delay,
            };
            var handle = _instances.Add(instance);

            var poolable = pooled.GameObject.GetComponent<SeSourcePoolable>();
            if (poolable == null)
            {
                poolable = pooled.GameObject.AddComponent<SeSourcePoolable>();
            }

            poolable.OnReturnedToPool = () => CleanupBookkeeping(handle);

            _allActive.Add(handle);
            AddToActiveList(data.Id, handle);
            _lastPlayedAt[data.Id] = _clock;

            return handle;
        }

        public void Stop(Handle<SeMarker> handle, float fade = 0f)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return;
            }

            if (fade > 0f && instance.Source.isPlaying)
            {
                // 即時停止せず、Tick でフェードアウトしてから返却する。
                instance.FadeOutRemaining = fade;
                instance.FadeOutDuration = fade;
                instance.FadeOutStartVolume = instance.Source.volume;
                return;
            }

            instance.Source.Stop();
            _pool.Return(instance.Pooled);
        }

        // 終了済み Handle の問い合わせは正常系(ポーリング / Dispatcher の後始末)なので警告を出さない。
        public bool IsPlaying(Handle<SeMarker> handle)
            => _instances.IsValidSilent(handle) && _instances.TryGet(handle, out var instance) && (instance.Pending || instance.Source.isPlaying);

        // 生成ディレイ待ち(Handle は有効だが、まだ鳴っていない)か。
        public bool IsPending(Handle<SeMarker> handle)
            => _instances.TryGet(handle, out var instance) && instance.Pending;

        // 現在アクティブな SE 再生数。Cosmetic 配送はネットワーク経由で受信ハンドラ側が Spawn するため、
        // 送信元は具体的な Handle を得られない。「何か再生された」を確認する用途にも使える。
        public int ActiveCount => _allActive.Count;

        public Vector3? GetPosition(Handle<SeMarker> handle)
            => _instances.TryGet(handle, out var instance) ? instance.Source.transform.position : (Vector3?)null;

        public float? GetTime(Handle<SeMarker> handle)
            => _instances.TryGet(handle, out var instance) ? instance.Source.time : (float?)null;

        public float? GetPitch(Handle<SeMarker> handle)
            => _instances.TryGet(handle, out var instance) ? instance.Source.pitch : (float?)null;

        public bool? GetLoop(Handle<SeMarker> handle)
            => _instances.TryGet(handle, out var instance) ? instance.Source.loop : (bool?)null;

        public void SetVolume(Handle<SeMarker> handle, float volume)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                instance.Source.volume = volume;
            }
        }

        public void SetPitch(Handle<SeMarker> handle, float pitch)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                instance.Source.pitch = pitch;
            }
        }

        public void Move(Handle<SeMarker> handle, Vector3 position)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                instance.Source.transform.position = position;
            }
        }

        public void Tick(float dt)
        {
            _clock += dt;
            FlushCosmeticBatch();

            for (var i = _allActive.Count - 1; i >= 0; i--)
            {
                var handle = _allActive[i];
                if (!_instances.TryGet(handle, out var instance))
                {
                    _allActive.RemoveAt(i);
                    continue;
                }

                // 生成ディレイ待ち: カウントダウンして時間が来たら Play する(Pause 中は進めない)。
                if (instance.Pending)
                {
                    if (!instance.Paused)
                    {
                        instance.PendingRemaining -= dt;
                        if (instance.PendingRemaining <= 0f)
                        {
                            instance.Pending = false;
                            instance.Source.Play();
                        }
                    }

                    continue;
                }

                if (instance.FadeOutRemaining > 0f)
                {
                    instance.FadeOutRemaining -= dt;
                    if (instance.FadeOutRemaining <= 0f)
                    {
                        Stop(handle);
                        continue;
                    }

                    instance.Source.volume = instance.FadeOutStartVolume * (instance.FadeOutRemaining / instance.FadeOutDuration);
                }

                if (instance.HasFollowTarget)
                {
                    if (instance.FollowTarget != null)
                    {
                        instance.Source.transform.position = instance.FollowTarget.TransformPoint(instance.FollowLocalOffset);
                        if (instance.Anchor.FollowRotation)
                        {
                            instance.Source.transform.rotation = instance.FollowTarget.rotation;
                        }
                    }
                    else if (!instance.Anchor.DetachOnStop)
                    {
                        Stop(handle);
                        continue;
                    }
                    else
                    {
                        // DetachOnStop: 発生源が消えても最後の位置で鳴り終わるまで残す。
                        instance.HasFollowTarget = false;
                    }
                }

                // Pause() 中は isPlaying=false になるため、ポーズ中は「鳴り終わり」と誤判定しない。
                if (!instance.Paused && !instance.Source.loop && !instance.Source.isPlaying)
                {
                    Stop(handle);
                }
            }
        }

        public void OnPause(PauseChannel channel, bool paused) => ApplyPause(paused, respectFlags: true);

        // OnPause / SetPausedAll の共通実装。respectFlags=true なら Flags.Pause=PauseWithGame のものだけ。
        private void ApplyPause(bool paused, bool respectFlags)
        {
            for (var i = 0; i < _allActive.Count; i++)
            {
                if (!_instances.TryGet(_allActive[i], out var instance) || instance.Source == null)
                {
                    continue;
                }

                if (respectFlags && instance.Data.Flags.Pause != PauseMode.PauseWithGame)
                {
                    continue;
                }

                instance.Paused = paused;
                if (paused)
                {
                    instance.Source.Pause();
                }
                else
                {
                    instance.Source.UnPause();
                }
            }
        }

        public void StopAll(StopReason reason)
        {
            for (var i = _allActive.Count - 1; i >= 0; i--)
            {
                Stop(_allActive[i]);
            }
        }

        // 再生中の SE を Flags.Pause に関係なく全部一時停止 / 再開する(エディタのプレビュー一時停止用。ゲーム側は OnPause)。
        public void SetPausedAll(bool paused) => ApplyPause(paused, respectFlags: false);

        public void OnSceneUnload() => StopAll(StopReason.SceneUnload);

        private void FlushCosmeticBatch()
        {
            if (_pendingCosmeticBatch.Count == 0)
            {
                return;
            }

            _netBridge.Broadcast(new SeNetBatchMsg { Items = _pendingCosmeticBatch.ToArray() }, NetChannel.Unreliable);
            _pendingCosmeticBatch.Clear();
        }

        private void CleanupBookkeeping(Handle<SeMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return;
            }

            RemoveFromActiveList(instance.Data.Id, handle);
            _allActive.Remove(handle);
            _instances.Remove(handle);
        }

        private void EnforceMaxConcurrent(SeData data)
        {
            if (!_activeBySeId.TryGetValue(data.Id, out var list) || list.Count < data.MaxConcurrent)
            {
                return;
            }

            Stop(list[0]);
        }

        private bool IsOnCooldown(SeData data)
        {
            if (data.CooldownSec <= 0f)
            {
                return false;
            }

            return _lastPlayedAt.TryGetValue(data.Id, out var last) && _clock - last < data.CooldownSec;
        }

        private void AddToActiveList(ulong seId, Handle<SeMarker> handle)
        {
            if (!_activeBySeId.TryGetValue(seId, out var list))
            {
                list = new List<Handle<SeMarker>>();
                _activeBySeId[seId] = list;
            }

            list.Add(handle);
        }

        private void RemoveFromActiveList(ulong seId, Handle<SeMarker> handle)
        {
            if (_activeBySeId.TryGetValue(seId, out var list))
            {
                list.Remove(handle);
            }
        }

        private AudioClip SelectClip(SeData data, ushort? seed)
        {
            if (data.Clips == null || data.Clips.Length == 0)
            {
                return null;
            }

            switch (data.SelectMode)
            {
                case ClipSelectMode.First:
                    return data.Clips[0];

                case ClipSelectMode.RoundRobin:
                {
                    _roundRobinIndex.TryGetValue(data.Id, out var idx);
                    var clip = data.Clips[idx % data.Clips.Length];
                    _roundRobinIndex[data.Id] = idx + 1;
                    return clip;
                }

                default:
                    // [14_networking.md] §6(6-0) — seed が指定されているとき(networked な Se トラック)は
                    // 全クライアントで同じ Clip が選ばれるよう、UnityEngine.Random ではなく seed から
                    // 決定的に選ぶ。未指定(通常のローカル再生)は既存どおり UnityEngine.Random を使う。
                    return seed.HasValue
                        ? data.Clips[new System.Random(seed.Value).Next(0, data.Clips.Length)]
                        : data.Clips[Random.Range(0, data.Clips.Length)];
            }
        }

        private void ConfigureSource(AudioSource source, SeData data, ushort? seed = null)
        {
            source.clip = SelectClip(data, seed);
            source.outputAudioMixerGroup = data.Mixer;
            source.volume = data.Volume;
            // ピッチも同じ seed から決定的に引く(clip 選択とは異なる値になるよう +1 で分ける)。
            source.pitch = seed.HasValue
                ? Mathf.Lerp(data.PitchRange.x, data.PitchRange.y, (float)new System.Random(seed.Value + 1).NextDouble())
                : Random.Range(data.PitchRange.x, data.PitchRange.y);
            source.loop = data.Loop;
            source.spatialBlend = data.Spatial == SpatialMode.None ? 0f : 1f;
            source.minDistance = data.MinDistance;
            source.maxDistance = data.MaxDistance;
            source.spread = data.Spread;
            source.dopplerLevel = data.DopplerEnabled ? 1f : 0f;

            if (data.Rolloff != null && data.Rolloff.length > 0)
            {
                source.rolloffMode = AudioRolloffMode.Custom;
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, data.Rolloff);
            }
            else
            {
                // プール再利用時、前の SE のカスタム減衰カーブが残らないよう既定に戻す。
                source.rolloffMode = AudioRolloffMode.Logarithmic;
            }
        }

    }
}
