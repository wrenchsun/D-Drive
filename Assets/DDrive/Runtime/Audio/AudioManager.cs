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
                PlaySeDataLocal(data, item.Position);
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

        public Handle<SeMarker> PlaySeData(SeData data, Vector3? explicitPosition = null, Transform contextRoot = null)
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
                    Position = ResolveWorldPositionForBroadcast(data, explicitPosition, contextRoot),
                });
                return Handle<SeMarker>.Invalid;
            }

            return PlaySeDataLocal(data, explicitPosition, contextRoot);
        }

        private static Vector3 ResolveWorldPositionForBroadcast(SeData data, Vector3? explicitPosition, Transform contextRoot)
        {
            if (explicitPosition.HasValue)
            {
                return explicitPosition.Value;
            }

            var resolved = AnchorResolver.Resolve(data.Anchor, contextRoot);
            return resolved != null ? resolved.TransformPoint(data.Anchor.LocalOffset) : data.Anchor.LocalOffset;
        }

        private Handle<SeMarker> PlaySeDataLocal(SeData data, Vector3? explicitPosition = null, Transform contextRoot = null)
        {
            if (IsOnCooldown(data))
            {
                return Handle<SeMarker>.Invalid;
            }

            EnforceMaxConcurrent(data);

            var pooled = _pool.Rent(_seSourcePrefab);
            pooled.Priority = data.Flags.Priority;

            var source = pooled.GameObject.GetComponent<AudioSource>();
            if (source == null)
            {
                source = pooled.GameObject.AddComponent<AudioSource>();
            }

            ConfigureSource(source, data);

            Transform followTarget = null;
            var hasFollowTarget = false;
            var followLocalOffset = data.Anchor.LocalOffset;
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
                var resolved = AnchorResolver.Resolve(data.Anchor, contextRoot);
                if (resolved != null)
                {
                    followTarget = resolved;
                    hasFollowTarget = true;

                    // 解決先が AnchorPoint(シーン配置型アンカー)なら、アンカー固有のオフセット+
                    // 位置のランダム散らばりを追加適用する(回転/スケールは音に無関係なので位置のみ)。
                    if (resolved.TryGetComponent<AnchorPoint>(out var point))
                    {
                        followLocalOffset = point.SampleLocalOffset(data.Anchor.LocalOffset);
                    }

                    sourceTransform.position = resolved.TransformPoint(followLocalOffset);
                    if (data.Anchor.FollowRotation)
                    {
                        sourceTransform.rotation = resolved.rotation;
                    }
                }
                else
                {
                    sourceTransform.position = data.Anchor.LocalOffset;
                }
            }

            if (data.StartOffsetSec > 0f && source.clip != null)
            {
                source.time = Mathf.Min(data.StartOffsetSec, Mathf.Max(0f, source.clip.length - 0.001f));
            }

            source.Play();

            var instance = new SeInstance
            {
                Data = data,
                Source = source,
                Pooled = pooled,
                FollowTarget = followTarget,
                HasFollowTarget = hasFollowTarget,
                FollowLocalOffset = followLocalOffset,
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

        public bool IsPlaying(Handle<SeMarker> handle)
            => _instances.TryGet(handle, out var instance) && instance.Source.isPlaying;

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
                        if (instance.Data.Anchor.FollowRotation)
                        {
                            instance.Source.transform.rotation = instance.FollowTarget.rotation;
                        }
                    }
                    else if (!instance.Data.Anchor.DetachOnStop)
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

        public void OnPause(PauseChannel channel, bool paused)
        {
            for (var i = 0; i < _allActive.Count; i++)
            {
                if (!_instances.TryGet(_allActive[i], out var instance))
                {
                    continue;
                }

                if (instance.Data.Flags.Pause != PauseMode.PauseWithGame)
                {
                    continue;
                }

                if (paused)
                {
                    instance.Paused = true;
                    instance.Source.Pause();
                }
                else
                {
                    instance.Paused = false;
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

        private AudioClip SelectClip(SeData data)
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
                    return data.Clips[Random.Range(0, data.Clips.Length)];
            }
        }

        private void ConfigureSource(AudioSource source, SeData data)
        {
            source.clip = SelectClip(data);
            source.outputAudioMixerGroup = data.Mixer;
            source.volume = data.Volume;
            source.pitch = Random.Range(data.PitchRange.x, data.PitchRange.y);
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
