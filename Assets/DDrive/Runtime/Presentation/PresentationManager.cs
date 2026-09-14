using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Net;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using R3;
using UnityEngine;
using PresentationId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Presentation.PresentationMarker>;

namespace DDrive.Runtime.Presentation
{
    // [08_presentation.md] §3 / [01_architecture.md] §8 — 「剣攻撃」等の演出データを 1 API で再生する
    // オーケストレータ(5-1)。自身は何も再生せず、Tracks を各 Manager(Audio/Vfx/Anim/Anim2D/Canvas/UiTween/
    // CameraFx/Haptics)へ委譲するだけ。Timeline のみ 6-10 待ちのため警告 1 回 + no-op。
    //
    // Tick は GameLoop 経由で TimeService.ScaledDeltaTime(unscaledDt) を受け取るため、HitStop 中は
    // (他の全 Manager 同様)AtTime の進行も自動的に止まる([16_camera_haptics.md] 参照。特別な配線は不要)。
    // CameraFxManager 自体は(このトラックの発火とは別に)Unscaled dt で駆動されるため、HitStop 中も
    // 揺れ自体は止まらない([16] Part A 実装メモ / DDriveRuntimeBootstrap の UnscaledCameraFxAdapter 参照)。
    public sealed class PresentationManager : IAssetManager
    {
        private sealed class PresentationInstance
        {
            public PresentationData Data;
            public PlayContext Ctx;
            public float Elapsed;
            public float Speed = 1f;
            public bool Paused;
            public bool Done;
            public bool[] Fired;

            public Subject<Unit> CompletedSubject;
            public Subject<Unit> CancelledSubject;
            public Subject<string> MarkerSubject;
            public Subject<PresentationTrack> TrackFiredSubject;

            // await Presentation.Play(...).WaitAsync() 用(UiTweenManager と同じ設計。UniTask.WaitUntil の
            // ポーリングに頼らず、Complete/Cancel 時に同期的に TrySetResult する)。
            public UniTaskCompletionSource Waiter;

            // StopOnCancel=true で発火した実体(Cancel 時にまとめて停止する。AssetEventDispatcher の
            // _keptVfx 等と同じ設計。型ごとに Handle の型が違うため個別リストに分ける)。
            public List<(int track, Handle<VfxMarker> handle)> FiredVfx;
            public List<(int track, Handle<SeMarker> handle)> FiredSe;
            public List<(int track, Handle<AnimMarker> handle)> FiredAnim;
            public List<(int track, Handle<UiTweenMarker> handle)> FiredUiTween;
            public List<(int track, Handle<CanvasMarker> handle)> FiredCanvas;
            public List<(int track, Handle<ShakeMarker> handle)> FiredShake;
            public List<(int track, Handle<HapticMarker> handle)> FiredHaptic;

            // ── [14_networking.md] §5(5-8/5-9) ネット関連の付帯情報 ──
            // HandleNetKey!=0 のとき「ネットワーク経路(Cosmetic)を通った Instance」であることを示す
            // (予測再生・確定受信・単純な自分の Broadcast 待ちのいずれも含む)。0 は完全ローカル。
            public uint HandleNetKey;
            public bool IsNetworked;
            public ushort Seed;

            // true は「PresentationPlayMsg を受信して生成した(=予測再生によるローカル直接生成ではない)」
            // Instance であることを示す。SelfNetId/TargetNetId を常に 0 で送る既知の制約(§4 実装メモ)により、
            // 受信側は「この事象が自分に起きたことか」を判定できない。HapticsData.LocalPlayerOnly=true な
            // Haptic トラックは誤発火(自分に関係ない振動)を避けるため、この Instance では安全側に倒して
            // 再生しない(オーケストレーターの追加指示、2026-09-14。要判断は docs/28 参照)。
            public bool PlayedViaNetworkReceive;
        }

        // Host のみが保持する「アクティブな Cosmetic Presentation」台帳(5-9, Late Join 用)。
        // ワンショット演出は尺が短いため Cleanup() で即座にここから外れ、自然に復元対象から漏れる
        // (専用の判定フィールドを増やさず、既存の Elapsed/Duration の仕組みに委ねた設計)。
        private struct ActiveNetworkedEntry
        {
            public PresentationData Data;
            public PlayContext Ctx;
            public double StartNetTime;
            public ushort Seed;
        }

        private readonly IAssetRegistry _registry;
        private readonly TimeService _time;
        private readonly AudioManager _audio;
        private readonly BgmManager _bgm;
        private readonly VfxManager _vfx;
        private readonly AnimManager _anim;
        private readonly UiManager _ui;
        private readonly UiTweenManager _uiTween;
        private readonly CameraFxManager _cameraFx;
        private readonly HapticsManager _haptics;

        private readonly InstanceStore<PresentationMarker, PresentationInstance> _instances = new();
        private readonly List<Handle<PresentationMarker>> _active = new();
        private readonly HashSet<PresentationData> _nonInterruptibleWarned = new();
        private readonly HashSet<TrackKind> _unimplementedWarned = new();
        private readonly HashSet<TrackKind> _missingManagerWarned = new();

        // [14_networking.md] §5(5-8/5-9) — null(既定)ならシングルプレイ相当で今までどおり完全ローカル
        // (Audio/Vfx/Prefabs と同じ「netBridge==null は通信の有無で挙動を変えない」原則、[14] §1)。
        private readonly INetBridge _netBridge;
        private readonly Dictionary<uint, Handle<PresentationMarker>> _networkedHandles = new();
        private readonly Dictionary<uint, ActiveNetworkedEntry> _activeNetworked = new();
        private readonly uint _instanceSalt;
        private uint _nextLocalSeq;

        public AssetType Type => AssetType.Presentation;

        // audio/bgm/vfx/anim/ui/uiTween は null 許容(未配線の種別トラックは警告 1 回 + no-op で継続する。
        // テストが必要な Manager だけを差し替えて構成できるようにするため)。
        public PresentationManager(
            IAssetRegistry registry,
            TimeService timeService,
            AudioManager audio = null,
            BgmManager bgm = null,
            VfxManager vfx = null,
            AnimManager anim = null,
            UiManager ui = null,
            UiTweenManager uiTween = null,
            CameraFxManager cameraFx = null,
            HapticsManager haptics = null,
            INetBridge netBridge = null)
        {
            _registry = registry;
            _time = timeService;
            _audio = audio;
            _bgm = bgm;
            _vfx = vfx;
            _anim = anim;
            _ui = ui;
            _uiTween = uiTween;
            _cameraFx = cameraFx;
            _haptics = haptics;
            _netBridge = netBridge;
            _instanceSalt = (uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue);

            if (_netBridge != null)
            {
                _netBridge.Subscribe<PresentationPlayMsg>(OnReceivePlayMsg);
                _netBridge.Subscribe<PresentationSignalMsg>(OnReceiveSignalMsg);
                _netBridge.Subscribe<PresentationCancelMsg>(OnReceiveCancelMsg);
                _netBridge.ClientConnected += OnClientConnected;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterPlaceholder()
        {
            PlaceholderProvider.Register(CreatePlaceholder);
        }

        // FR-1.4: 未登録 ID はトラック 0 個・尺 0 秒の Presentation(Play 直後の Tick で即完了)。
        private static PresentationData CreatePlaceholder()
        {
            var data = ScriptableObject.CreateInstance<PresentationData>();
            data.DisplayName = "<Placeholder:PRESENTATION>";
            data.Tracks = System.Array.Empty<PresentationTrack>();
            data.TotalDuration = 0f;
            data.Interruptible = true;
            return data;
        }

        // ── Play ──

        public Handle<PresentationMarker> Play(PresentationId id, in PlayContext ctx)
            => PlayData(_registry.ResolveOrPlaceholder<PresentationData>(id.Value), in ctx);

        public Handle<PresentationMarker> PlayData(PresentationData data, in PlayContext ctx)
        {
            if (data == null)
            {
                return Handle<PresentationMarker>.Invalid;
            }

            // [14_networking.md] §5 — Flags.Net=Cosmetic かつ netBridge が居るときだけネット経路に乗る
            // (null は今までどおり常にローカル、[14] §1 の原則)。Local/Simulated はここでは通常再生する
            // (Presentation に Simulated の意味付けは無い。Validator で Info 警告する。実装メモ参照)。
            if (_netBridge != null && data.Flags.Net == NetMode.Cosmetic)
            {
                return PlayCosmeticNetworked(data, in ctx);
            }

            return PlayLocalInternal(data, in ctx, elapsedSeek: 0f, seed: 0, handleNetKey: 0, isNetworked: false, playedViaNetworkReceive: false);
        }

        private Handle<PresentationMarker> PlayLocalInternal(
            PresentationData data,
            in PlayContext ctx,
            float elapsedSeek,
            ushort seed,
            uint handleNetKey,
            bool isNetworked,
            bool playedViaNetworkReceive)
        {
            var instance = new PresentationInstance
            {
                Data = data,
                Ctx = ctx,
                Elapsed = Mathf.Max(0f, elapsedSeek),
                Fired = data.Tracks != null ? new bool[data.Tracks.Length] : System.Array.Empty<bool>(),
                CompletedSubject = new Subject<Unit>(),
                CancelledSubject = new Subject<Unit>(),
                MarkerSubject = new Subject<string>(),
                TrackFiredSubject = new Subject<PresentationTrack>(),
                FiredVfx = new List<(int, Handle<VfxMarker>)>(),
                FiredSe = new List<(int, Handle<SeMarker>)>(),
                FiredAnim = new List<(int, Handle<AnimMarker>)>(),
                FiredUiTween = new List<(int, Handle<UiTweenMarker>)>(),
                FiredCanvas = new List<(int, Handle<CanvasMarker>)>(),
                FiredShake = new List<(int, Handle<ShakeMarker>)>(),
                FiredHaptic = new List<(int, Handle<HapticMarker>)>(),
                HandleNetKey = handleNetKey,
                IsNetworked = isNetworked,
                PlayedViaNetworkReceive = playedViaNetworkReceive,
                Seed = seed,
            };

            var handle = _instances.Add(instance);

            if (handleNetKey != 0)
            {
                _networkedHandles[handleNetKey] = handle;
            }

            // AtTime(0.00) は Play() 呼び出し時に即時委譲する([08] §3)。elapsedSeek==0 のときは従来どおり
            // 全トラックを普通に発火する。elapsedSeek>0(ネット受信でのシーク開始)のときだけ、既に過ぎた
            // ワンショットトラックを鳴らさずスキップする([14] §5 実装メモ)。
            SeekInitialTracks(handle, instance);

            _active.Add(handle);
            return handle;
        }

        // ── ネットワーク再生(5-8) ──

        private Handle<PresentationMarker> PlayCosmeticNetworked(PresentationData data, in PlayContext ctx)
        {
            var handleNetKey = NextHandleNetKey();
            // [14_networking.md] §6: 乱数は「行為者が 1 回だけ引いて結果(Seed)を送る」。ホスト・クライアントの
            // どちらが行為者でも、受け取った側は同じ Seed から決定的に選ぶ想定であれば各自で Random を呼ばない
            // (実際の SE 選択への接続は 5-8 のスコープ外。要判断は docs/28 参照)。
            var seed = (ushort)UnityEngine.Random.Range(0, ushort.MaxValue + 1);
            var startNetTime = _netBridge.NetworkTime;

            var predicted = Handle<PresentationMarker>.Invalid;
            if (data.PredictLocal)
            {
                predicted = PlayLocalInternal(data, in ctx, elapsedSeek: 0f, seed: seed, handleNetKey: handleNetKey, isNetworked: true, playedViaNetworkReceive: false);
            }

            _netBridge.Broadcast(new PresentationPlayMsg
            {
                PresId = data.Id,
                SelfNetId = 0,
                TargetNetId = 0,
                Position = ctx.Position,
                StartNetTime = startNetTime,
                Seed = seed,
                HandleNetKey = handleNetKey,
            }, NetChannel.ReliableOrdered);

            return predicted;
        }

        private void OnReceivePlayMsg(ulong senderId, PresentationPlayMsg msg)
        {
            // 予測再生済み(または既にこの受信ハンドラで生成済み)の確定通知。二重生成しない([14] §5)。
            if (_networkedHandles.TryGetValue(msg.HandleNetKey, out var existingHandle) && _instances.TryGet(existingHandle, out var existingInstance))
            {
                RegisterActiveIfServer(msg.HandleNetKey, existingInstance.Data, existingInstance.Ctx, msg.StartNetTime, msg.Seed);
                return;
            }

            var data = _registry.ResolveOrPlaceholder<PresentationData>(msg.PresId);
            var duration = PresentationTiming.EffectiveDuration(data);
            var elapsed = (float)System.Math.Max(0d, _netBridge.NetworkTime - msg.StartNetTime);

            // 到着時点で既に尺を超えている演出は復元しない(ワンショットを復元しないのと同じ考え方。[14] §5)。
            if (duration > 0f && elapsed >= duration)
            {
                return;
            }

            var ctx = new PlayContext { Position = msg.Position };
            if (msg.SelfNetId != 0)
            {
                var self = _netBridge.ResolveNetObject(msg.SelfNetId);
                if (self != null)
                {
                    ctx.Self = self;
                }
            }

            if (msg.TargetNetId != 0)
            {
                var target = _netBridge.ResolveNetObject(msg.TargetNetId);
                if (target != null)
                {
                    ctx.Target = target;
                }
            }

            var handle = PlayLocalInternal(data, in ctx, elapsedSeek: elapsed, seed: msg.Seed, handleNetKey: msg.HandleNetKey, isNetworked: true, playedViaNetworkReceive: true);

            if (_instances.TryGet(handle, out var instance))
            {
                RegisterActiveIfServer(msg.HandleNetKey, instance.Data, instance.Ctx, msg.StartNetTime, msg.Seed);
            }
        }

        private void RegisterActiveIfServer(uint handleNetKey, PresentationData data, PlayContext ctx, double startNetTime, ushort seed)
        {
            if (_netBridge == null || !_netBridge.IsServer || handleNetKey == 0)
            {
                return;
            }

            _activeNetworked[handleNetKey] = new ActiveNetworkedEntry
            {
                Data = data,
                Ctx = ctx,
                StartNetTime = startNetTime,
                Seed = seed,
            };
        }

        // [14_networking.md] §5(5-9) — 新規接続をホストだけが処理する。アクティブな Cosmetic Presentation を
        // それぞれ元の StartNetTime のまま SendTo する(OnReceivePlayMsg が既存のシーク/ワンショットスキップ
        // ロジックを再利用して復元する。専用の Late Join メッセージは用意しない)。
        private void OnClientConnected(ulong clientId)
        {
            if (_netBridge == null || !_netBridge.IsServer)
            {
                return;
            }

            foreach (var kv in _activeNetworked)
            {
                var entry = kv.Value;
                _netBridge.SendTo(clientId, new PresentationPlayMsg
                {
                    PresId = entry.Data.Id,
                    SelfNetId = 0,
                    TargetNetId = 0,
                    Position = entry.Ctx.Position,
                    StartNetTime = entry.StartNetTime,
                    Seed = entry.Seed,
                    HandleNetKey = kv.Key,
                }, NetChannel.ReliableOrdered);
            }
        }

        private uint NextHandleNetKey()
        {
            unchecked
            {
                _nextLocalSeq++;
                var timeBits = _netBridge != null ? System.BitConverter.DoubleToInt64Bits(_netBridge.NetworkTime) : 0L;
                var mixed = (uint)(timeBits ^ (timeBits >> 32));
                var key = (mixed ^ _instanceSalt) + _nextLocalSeq;
                return key == 0 ? 1u : key;
            }
        }

        // SignalKey を毎回文字列で送らないための 16bit FNV-1a(帯域節約。[14] §8)。0 alloc・純関数。
        private static ushort HashSignalKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return 0;
            }

            unchecked
            {
                const uint fnvPrime = 16777619u;
                var hash = 2166136261u;
                for (var i = 0; i < key.Length; i++)
                {
                    hash ^= key[i];
                    hash *= fnvPrime;
                }

                return (ushort)((hash ^ (hash >> 16)) & 0xFFFFu);
            }
        }

        // ── Signal / Cancel / Pause など ──

        public void Signal(Handle<PresentationMarker> handle, string key)
        {
            if (string.IsNullOrEmpty(key) || !_instances.TryGet(handle, out var instance))
            {
                return;
            }

            // [14_networking.md] §5/§9 — Signal は Host 権威。ネットワーク経路の Instance はローカルで
            // 即座に発火せず Broadcast する(Client 発は NgoNetBridge が Host へ中継 → Host がレート制限を
            // 検証してから全員へ配る。既存の Cosmetic 中継と同じ経路、無条件中継はしない)。自分の Broadcast を
            // 受信して初めて発火するため、ここで直接発火すると二重発火になる。
            if (instance.IsNetworked && _netBridge != null)
            {
                _netBridge.Broadcast(new PresentationSignalMsg
                {
                    HandleNetKey = instance.HandleNetKey,
                    SignalKeyHash = HashSignalKey(key),
                }, NetChannel.ReliableOrdered);
                return;
            }

            SignalLocal(handle, instance, key);
        }

        private void SignalLocal(Handle<PresentationMarker> handle, PresentationInstance instance, string key)
        {
            var tracks = instance.Data.Tracks;
            if (tracks == null)
            {
                return;
            }

            for (var t = 0; t < tracks.Length; t++)
            {
                if (instance.Fired[t] || tracks[t].Trigger != TrackTrigger.OnSignal || tracks[t].SignalKey != key)
                {
                    continue;
                }

                FireTrack(handle, instance, t, in tracks[t]);
            }
        }

        private void OnReceiveSignalMsg(ulong senderId, PresentationSignalMsg msg)
        {
            if (!_networkedHandles.TryGetValue(msg.HandleNetKey, out var handle) || !_instances.TryGet(handle, out var instance))
            {
                return;
            }

            var tracks = instance.Data.Tracks;
            if (tracks == null)
            {
                return;
            }

            for (var t = 0; t < tracks.Length; t++)
            {
                if (instance.Fired[t] || tracks[t].Trigger != TrackTrigger.OnSignal || HashSignalKey(tracks[t].SignalKey) != msg.SignalKeyHash)
                {
                    continue;
                }

                FireTrack(handle, instance, t, in tracks[t]);
            }
        }

        private void OnReceiveCancelMsg(ulong senderId, PresentationCancelMsg msg)
        {
            if (!_networkedHandles.TryGetValue(msg.HandleNetKey, out var handle) || !_instances.TryGet(handle, out var instance) || instance.Done)
            {
                return;
            }

            CancelInternal(handle, instance);
        }

        public void Cancel(Handle<PresentationMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance) || instance.Done)
            {
                return;
            }

            if (!instance.Data.Interruptible)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (_nonInterruptibleWarned.Add(instance.Data))
                {
                    Debug.LogWarning($"[DDrive] Presentation '{instance.Data.DisplayName}' は Interruptible=false のため Cancel() を無視しました。");
                }
#endif
                return;
            }

            // [14_networking.md] §5 — ネットワーク経路の Instance は Broadcast 経由で全員(自分含む)を
            // 揃えて止める(直接 CancelInternal を呼ぶと自分だけ先に止まってしまう)。
            if (instance.IsNetworked && _netBridge != null)
            {
                _netBridge.Broadcast(new PresentationCancelMsg { HandleNetKey = instance.HandleNetKey }, NetChannel.ReliableOrdered);
                return;
            }

            CancelInternal(handle, instance);
        }

        private void CancelInternal(Handle<PresentationMarker> handle, PresentationInstance instance)
        {
            instance.Done = true;
            StopFiredForCancel(instance);
            instance.CancelledSubject.OnNext(Unit.Default);
            instance.Waiter?.TrySetResult();
            Cleanup(handle, instance);
        }

        private void StopFiredForCancel(PresentationInstance instance)
        {
            for (var i = 0; i < instance.FiredVfx.Count; i++)
            {
                var h = instance.FiredVfx[i].handle;
                if (_vfx != null && _vfx.IsPlaying(h))
                {
                    _vfx.Stop(h);
                }
            }

            for (var i = 0; i < instance.FiredSe.Count; i++)
            {
                var h = instance.FiredSe[i].handle;
                if (_audio != null && _audio.IsPlaying(h))
                {
                    _audio.Stop(h);
                }
            }

            for (var i = 0; i < instance.FiredAnim.Count; i++)
            {
                var h = instance.FiredAnim[i].handle;
                if (_anim != null && _anim.IsPlaying(h))
                {
                    _anim.Stop(h);
                }
            }

            for (var i = 0; i < instance.FiredUiTween.Count; i++)
            {
                var h = instance.FiredUiTween[i].handle;
                if (_uiTween != null && _uiTween.IsPlaying(h))
                {
                    _uiTween.Stop(h);
                }
            }

            for (var i = 0; i < instance.FiredCanvas.Count; i++)
            {
                var h = instance.FiredCanvas[i].handle;
                if (_ui != null && _ui.IsOpen(h))
                {
                    _ui.Close(h);
                }
            }

            for (var i = 0; i < instance.FiredShake.Count; i++)
            {
                var h = instance.FiredShake[i].handle;
                if (_cameraFx != null && _cameraFx.IsPlaying(h))
                {
                    _cameraFx.Stop(h, 0f);
                }
            }

            for (var i = 0; i < instance.FiredHaptic.Count; i++)
            {
                var h = instance.FiredHaptic[i].handle;
                if (_haptics != null && _haptics.IsPlaying(h))
                {
                    _haptics.Stop(h);
                }
            }
        }

        public void SetPaused(Handle<PresentationMarker> handle, bool paused)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                instance.Paused = paused;
            }
        }

        public void SetSpeed(Handle<PresentationMarker> handle, float speed)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                instance.Speed = Mathf.Max(0f, speed);
            }
        }

        // デバッグ/スキップ用。通過したトラックはまとめて発火する。巻き戻し(過去への Seek)は
        // 既発火のトラックを再発火しない(Fired は保持したまま)。
        public void Seek(Handle<PresentationMarker> handle, float time)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return;
            }

            instance.Elapsed = Mathf.Max(0f, time);
            FireDueTracks(handle, instance);
        }

        // テスト/デバッグ専用: 現在再生中の Handle を列挙する。ネットワーク受信で生成された Instance
        // (PresentationPlayMsg 受信側)は呼び出し元に Handle を返さないため、5-8/5-9 のテストが
        // 「受信側で何が再生中か」を観測する手段として使う(ゲームコードは通常 Play() の戻り値だけを
        // 使うため、本番経路から呼ぶ想定はない)。0 alloc ではないため定常経路(Tick 等)からは呼ばない。
        public List<Handle<PresentationMarker>> DebugActiveHandles()
        {
            var copy = new List<Handle<PresentationMarker>>(_active.Count);
            for (var i = 0; i < _active.Count; i++)
            {
                copy.Add(_active[i]);
            }

            return copy;
        }

        // ── 問い合わせ ──

        // 終了済み Handle の問い合わせは正常系(ポーリング/WaitAsync)なので警告を出さない。
        public bool IsPlaying(Handle<PresentationMarker> handle) => _instances.IsValidSilent(handle);

        public float GetNormalizedTime(Handle<PresentationMarker> handle)
        {
            if (!TryGetInstanceSilent(handle, out var instance))
            {
                return -1f;
            }

            var duration = PresentationTiming.EffectiveDuration(instance.Data);
            return duration > 0f ? Mathf.Clamp01(instance.Elapsed / duration) : 0f;
        }

        public Observable<Unit> OnCompleted(Handle<PresentationMarker> handle)
            => TryGetInstanceSilent(handle, out var instance) ? instance.CompletedSubject : Observable.Empty<Unit>();

        public Observable<Unit> OnCancelled(Handle<PresentationMarker> handle)
            => TryGetInstanceSilent(handle, out var instance) ? instance.CancelledSubject : Observable.Empty<Unit>();

        public Observable<string> OnMarker(Handle<PresentationMarker> handle)
            => TryGetInstanceSilent(handle, out var instance) ? instance.MarkerSubject : Observable.Empty<string>();

        public Observable<PresentationTrack> OnTrackFired(Handle<PresentationMarker> handle)
            => TryGetInstanceSilent(handle, out var instance) ? instance.TrackFiredSubject : Observable.Empty<PresentationTrack>();

        public UniTask WaitAsync(Handle<PresentationMarker> handle, CancellationToken ct)
        {
            if (!TryGetInstanceSilent(handle, out var instance))
            {
                return UniTask.CompletedTask;
            }

            if (instance.Waiter == null)
            {
                instance.Waiter = new UniTaskCompletionSource();
                if (ct.CanBeCanceled)
                {
                    var waiter = instance.Waiter;
                    ct.Register(() => waiter.TrySetCanceled(ct));
                }
            }

            return instance.Waiter.Task;
        }

        private bool TryGetInstanceSilent(Handle<PresentationMarker> handle, out PresentationInstance instance)
        {
            if (_instances.IsValidSilent(handle))
            {
                return _instances.TryGet(handle, out instance);
            }

            instance = null;
            return false;
        }

        // ── Tick / Pause / StopAll ──

        public void Tick(float dt)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var handle = _active[i];
                if (!_instances.TryGet(handle, out var instance))
                {
                    _active.RemoveAt(i);
                    continue;
                }

                if (instance.Paused)
                {
                    continue;
                }

                instance.Elapsed += dt * instance.Speed;
                FireDueTracks(handle, instance);

                if (!instance.Done && instance.Elapsed >= PresentationTiming.EffectiveDuration(instance.Data))
                {
                    Complete(handle, instance);
                }
            }
        }

        private void Complete(Handle<PresentationMarker> handle, PresentationInstance instance)
        {
            instance.Done = true;
            instance.CompletedSubject.OnNext(Unit.Default);
            instance.Waiter?.TrySetResult();
            Cleanup(handle, instance);
        }

        private void Cleanup(Handle<PresentationMarker> handle, PresentationInstance instance)
        {
            _active.Remove(handle);
            _instances.Remove(handle);

            if (instance.HandleNetKey != 0)
            {
                _networkedHandles.Remove(instance.HandleNetKey);
                if (_netBridge != null && _netBridge.IsServer)
                {
                    // [14_networking.md] §5(5-9) — 完了/Cancel された Presentation は Late Join の
                    // 復元対象台帳から外す(ワンショットは尺が短いためここで即座に外れ、自然に復元されない)。
                    _activeNetworked.Remove(instance.HandleNetKey);
                }
            }

            instance.CompletedSubject.Dispose();
            instance.CancelledSubject.Dispose();
            instance.MarkerSubject.Dispose();
            instance.TrackFiredSubject.Dispose();
        }

        public void OnPause(PauseChannel channel, bool paused) => ApplyPause(paused, respectFlags: true);

        private void ApplyPause(bool paused, bool respectFlags)
        {
            for (var i = 0; i < _active.Count; i++)
            {
                if (!_instances.TryGet(_active[i], out var instance))
                {
                    continue;
                }

                if (respectFlags && instance.Data.Flags.Pause != PauseMode.PauseWithGame)
                {
                    continue;
                }

                instance.Paused = paused;
            }
        }

        // シーン破棄等の強制停止。Interruptible=false でも止める(通常の Cancel() とは別経路)。
        public void StopAll(StopReason reason)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                if (_instances.TryGet(_active[i], out var instance) && !instance.Done)
                {
                    CancelInternal(_active[i], instance);
                }
                else
                {
                    _active.RemoveAt(i);
                }
            }
        }

        public void OnSceneUnload() => StopAll(StopReason.SceneUnload);

        // ── トラック発火 ──

        private static Transform ResolveContextRoot(in PlayContext ctx, TrackTargetMode mode)
        {
            switch (mode)
            {
                case TrackTargetMode.Self:
                    return ctx.Self;
                case TrackTargetMode.ContextTarget:
                    return ctx.Target;
                default:
                    // World / Anchor: PlayContext を参照せず、Anchor.LocalOffset を絶対ワールド座標として使う
                    // (要判断: [08_presentation.md] 実装メモ参照。現状は両者を区別していない)。
                    return null;
            }
        }

        private void FireDueTracks(Handle<PresentationMarker> handle, PresentationInstance instance)
        {
            var tracks = instance.Data.Tracks;
            if (tracks == null)
            {
                return;
            }

            for (var t = 0; t < tracks.Length; t++)
            {
                if (instance.Fired[t] || tracks[t].Trigger != TrackTrigger.AtTime || tracks[t].Time > instance.Elapsed)
                {
                    continue;
                }

                FireTrack(handle, instance, t, in tracks[t]);
            }
        }

        // [14_networking.md] §5 実装メモ(5-8) — Play() 直後の初回発火専用。Tick()/デバッグ用 Seek() では
        // 使わない(そちらは常に FireDueTracks で通常発火する。挙動を変えない)。elapsedSeek==0(通常再生・
        // 予測再生・自分の Broadcast 待ち後の再生)のときは FireDueTracks と完全に同じ結果になる。
        // elapsedSeek>0(ネット越しに遅れて届いた Play。§5「開始時刻シーク」)のときだけ、既に過ぎた
        // ワンショットトラックは鳴らさずに Fired 済みとしてスキップし、継続(ループ)系だけは今から
        // 再生を開始する(位相の厳密な同期は Anim のみ実装。Bgm は BgmManager に Seek API が無いため
        // 頭から再生する。要判断は docs/28)。
        private void SeekInitialTracks(Handle<PresentationMarker> handle, PresentationInstance instance)
        {
            var tracks = instance.Data.Tracks;
            if (tracks == null)
            {
                return;
            }

            var elapsed = instance.Elapsed;
            for (var t = 0; t < tracks.Length; t++)
            {
                if (instance.Fired[t] || tracks[t].Trigger != TrackTrigger.AtTime || tracks[t].Time > elapsed)
                {
                    continue;
                }

                if (elapsed > 0f && !IsContinuousAtSeek(in tracks[t]))
                {
                    instance.Fired[t] = true;
                    continue;
                }

                FireTrack(handle, instance, t, in tracks[t]);
            }
        }

        // continuous(ループ)系だけ「今から再生開始」してよい。Anim/Anim2D/Bgm は常に継続系扱い。
        // Vfx/Se は「常駐 VFX/BGM が復元される」AC(5-9)を満たすため、データ側のループ設定
        // (VfxLifeMode.Loop / SeData.Loop)を見て判定する(一撃 VFX・単発 SE は依然ワンショットとして
        // スキップする)。それ以外(CameraShake/Haptic/HitStop/UiTween/Canvas/Marker/Signal/Timeline)は
        // 常にワンショット扱い。
        private bool IsContinuousAtSeek(in PresentationTrack track)
        {
            switch (track.Kind)
            {
                case TrackKind.Anim:
                case TrackKind.Anim2D:
                case TrackKind.Bgm:
                    return true;

                case TrackKind.Vfx:
                    var vfxData = _registry.ResolveOrPlaceholder<VfxData>(track.Asset.Id);
                    return vfxData != null && vfxData.LifeMode == VfxLifeMode.Loop;

                case TrackKind.Se:
                    var seData = _registry.ResolveOrPlaceholder<SeData>(track.Asset.Id);
                    return seData != null && seData.Loop;

                default:
                    return false;
            }
        }

        private void FireTrack(Handle<PresentationMarker> handle, PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            instance.Fired[trackIndex] = true;

            switch (track.Kind)
            {
                case TrackKind.Anim:
                case TrackKind.Anim2D:
                    FireAnim(instance, trackIndex, in track);
                    break;

                case TrackKind.Se:
                    FireSe(instance, trackIndex, in track);
                    break;

                case TrackKind.Bgm:
                    FireBgm(in track);
                    break;

                case TrackKind.Vfx:
                    FireVfx(instance, trackIndex, in track);
                    break;

                case TrackKind.Canvas:
                    FireCanvas(instance, trackIndex, in track);
                    break;

                case TrackKind.UiTween:
                    FireUiTween(instance, trackIndex, in track);
                    break;

                case TrackKind.HitStop:
                    FireHitStop(in track);
                    break;

                case TrackKind.Marker:
                    instance.MarkerSubject.OnNext(track.SignalKey);
                    break;

                case TrackKind.Signal:
                    instance.Ctx.OnSignal?.Invoke(track.SignalKey);
                    break;

                case TrackKind.CameraShake:
                    FireCameraShake(instance, trackIndex, in track);
                    break;

                case TrackKind.Haptic:
                    FireHaptic(instance, trackIndex, in track);
                    break;

                case TrackKind.Timeline:
                    WarnUnimplemented(track.Kind);
                    break;
            }

            instance.TrackFiredSubject.OnNext(track);
        }

        private void FireVfx(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_vfx == null)
            {
                WarnMissingManager(TrackKind.Vfx);
                return;
            }

            var data = _registry.ResolveOrPlaceholder<VfxData>(track.Asset.Id);
            var root = ResolveContextRoot(instance.Ctx, track.Target);
            var spec = AnchorSpawnSpec.FromDef(track.Anchor);
            var h = _vfx.SpawnData(data, in spec, root);

            ApplyVfxTrackParams(h, data, in track);

            if (track.StopOnCancel && _vfx.IsPlaying(h))
            {
                instance.FiredVfx.Add((trackIndex, h));
            }
        }

        // [08_presentation.md] §4 実装メモ(5-4 追補、2026-09-14) — パラメータ上書き。
        // PresentationTrack.Params(ParamValue[]、キー無し)を「Params[i] ↔ 参照先 VfxData.Params[i].Label」
        // のインデックス対応で既存の VfxManager.SetParam(Label 解決)へそのまま渡す。PresentationTrack
        // にラベル用フィールドを追加しない(シリアライズ追加を避ける。要判断はインデックス対応で
        // 表現できない場合のみ)。VfxData.Params の要素数を超える分は無視する(範囲外アクセスにしない)。
        private void ApplyVfxTrackParams(Handle<VfxMarker> handle, VfxData data, in PresentationTrack track)
        {
            if (track.Params == null || track.Params.Length == 0 || data?.Params == null || data.Params.Length == 0)
            {
                return;
            }

            var count = Mathf.Min(track.Params.Length, data.Params.Length);
            for (var i = 0; i < count; i++)
            {
                _vfx.SetParam(handle, data.Params[i].Label, track.Params[i]);
            }
        }

        private void FireSe(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_audio == null)
            {
                WarnMissingManager(TrackKind.Se);
                return;
            }

            var data = _registry.ResolveOrPlaceholder<SeData>(track.Asset.Id);
            var root = ResolveContextRoot(instance.Ctx, track.Target);
            var spec = AnchorSpawnSpec.FromDef(track.Anchor);
            var h = _audio.PlaySeData(data, in spec, root);

            if (track.StopOnCancel && _audio.IsPlaying(h))
            {
                instance.FiredSe.Add((trackIndex, h));
            }
        }

        private void FireBgm(in PresentationTrack track)
        {
            if (_bgm == null)
            {
                WarnMissingManager(TrackKind.Bgm);
                return;
            }

            var data = _registry.ResolveOrPlaceholder<BgmData>(track.Asset.Id);
            var fadeIn = track.Params != null && track.Params.Length > 0 ? track.Params[0].FloatValue : -1f;
            _bgm.PlayBgmData(data, fadeIn);
        }

        private void FireAnim(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_anim == null)
            {
                WarnMissingManager(track.Kind);
                return;
            }

            var root = ResolveContextRoot(instance.Ctx, track.Target);
            var animator = root != null ? root.GetComponentInChildren<Animator>(true) : null;
            if (animator == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning($"[DDrive] Presentation '{instance.Data.DisplayName}': track {trackIndex}({track.Kind}) に Animator が見つかりません(Target={track.Target})。");
#endif
                return;
            }

            var data = _registry.ResolveOrPlaceholder<AnimData>(track.Asset.Id);
            var h = _anim.PlayData(data, animator);

            // [14_networking.md] §5 実装メモ(5-8) — ネット越しのシークで「このトラックの開始時刻より後」から
            // 始まった場合は、Anim の再生位置を追いつかせる(ループ系の位相合わせ。§5「ループ系は位相を合わせる」)。
            // elapsed==track.Time(通常再生)のときは 0 のままで無害。
            var lateBy = instance.Elapsed - track.Time;
            if (lateBy > 0f && data != null && data.LengthSec > 0f && _anim.IsPlaying(h))
            {
                var normalized = Mathf.Repeat(lateBy / data.LengthSec, 1f);
                _anim.Seek(h, normalized);
            }

            if (track.StopOnCancel && _anim.IsPlaying(h))
            {
                instance.FiredAnim.Add((trackIndex, h));
            }
        }

        private void FireCanvas(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_ui == null)
            {
                WarnMissingManager(TrackKind.Canvas);
                return;
            }

            var id = new AssetId<CanvasMarker>(track.Asset.Id, AssetType.Canvas);
            var h = _ui.Open(id);

            if (track.StopOnCancel && _ui.IsOpen(h))
            {
                instance.FiredCanvas.Add((trackIndex, h));
            }
        }

        private void FireUiTween(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_uiTween == null)
            {
                WarnMissingManager(TrackKind.UiTween);
                return;
            }

            var root = ResolveContextRoot(instance.Ctx, track.Target);
            var rect = root as RectTransform;
            if (rect == null && root != null)
            {
                rect = root.GetComponentInChildren<RectTransform>(true);
            }

            if (rect == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning($"[DDrive] Presentation '{instance.Data.DisplayName}': track {trackIndex}(UiTween) に RectTransform が見つかりません(Target={track.Target})。");
#endif
                return;
            }

            var data = _registry.ResolveOrPlaceholder<UiTweenData>(track.Asset.Id);
            var h = _uiTween.PlayData(data, rect);

            if (track.StopOnCancel && _uiTween.IsPlaying(h))
            {
                instance.FiredUiTween.Add((trackIndex, h));
            }
        }

        // sourcePos には ctx.Position を渡す(ShakeSpace.FromSource 用。CameraLocal/World は無視するので常に渡してよい)。
        private void FireCameraShake(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_cameraFx == null)
            {
                WarnMissingManager(TrackKind.CameraShake);
                return;
            }

            var data = _registry.ResolveOrPlaceholder<CameraShakeData>(track.Asset.Id);
            var h = _cameraFx.ShakeData(data, instance.Ctx.Position);

            if (track.StopOnCancel && _cameraFx.IsPlaying(h))
            {
                instance.FiredShake.Add((trackIndex, h));
            }
        }

        private void FireHaptic(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_haptics == null)
            {
                WarnMissingManager(TrackKind.Haptic);
                return;
            }

            var data = _registry.ResolveOrPlaceholder<HapticsData>(track.Asset.Id);

            // [14_networking.md] §5 追加指示(2026-09-14) — SelfNetId/TargetNetId を常に 0 で送るため、
            // 受信側は「この事象が自分に起きたことか」を判定できない。LocalPlayerOnly=true な Haptic は
            // 誤爆(自分に関係ない振動)を避けるため、ネット受信で生成した Instance(PlayedViaNetworkReceive)
            // では安全側に倒して再生しない(要判断: NGO 統合で SelfNetId が解決できるようになったら見直す。
            // 予測再生した行為者自身の Instance はこのフラグが false のため影響を受けない)。
            if (instance.PlayedViaNetworkReceive && data.LocalPlayerOnly)
            {
                return;
            }

            var h = _haptics.PlayData(data);

            if (track.StopOnCancel && _haptics.IsPlaying(h))
            {
                instance.FiredHaptic.Add((trackIndex, h));
            }
        }

        private void FireHitStop(in PresentationTrack track)
        {
            if (_time == null)
            {
                WarnMissingManager(TrackKind.HitStop);
                return;
            }

            if (track.Params == null || track.Params.Length == 0)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("[DDrive] Presentation: HitStop トラックに Params[0](秒数)が設定されていません。");
#endif
                return;
            }

            _time.HitStop(track.Params[0].FloatValue);
        }

        private void WarnUnimplemented(TrackKind kind)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_unimplementedWarned.Add(kind))
            {
                Debug.LogWarning($"[DDrive] Presentation: TrackKind.{kind} は未実装です(5-2/5-2b/6-10 で対応予定)。no-op で継続します。");
            }
#endif
        }

        private void WarnMissingManager(TrackKind kind)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_missingManagerWarned.Add(kind))
            {
                Debug.LogWarning($"[DDrive] Presentation: TrackKind.{kind} を委譲する Manager が未設定です。no-op で継続します。");
            }
#endif
        }
    }
}
