using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Cutscene.Tracks;
using DDrive.Runtime.Model;
using DDrive.Runtime.Net;
using DDrive.Runtime.Presentation;
using R3;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using CutsceneId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Cutscene.CutsceneMarker>;

namespace DDrive.Runtime.Cutscene
{
    // [26_timeline.md] §4.5(6-10a) — Timeline 基盤の再生オーケストレータ。TimelineAsset の Track/Clip 自体は
    // PlayableDirector に委譲し(ADR-4)、CutsceneManager は「役割名 → バインド解決」「原点」「Manual 更新の
    // Tick 駆動」「ネット同期(Cosmetic)」「入力ロックの公開」だけを薄く上乗せする。
    //
    // D-Drive 独自トラック(SE/VFX/AnchorGroup/Shake/Haptic/UI/Presentation クリップ、Signal マーカー)は
    // 6-10b の範囲。6-10a では標準 Timeline(Animation/Activation 等の標準トラック)がそのまま再生されるのみ。
    public sealed class CutsceneManager : IAssetManager
    {
        private sealed class CutsceneInstance
        {
            public CutsceneData Data;
            public PlayContext Ctx;
            public CutsceneDirectorSlot Slot;
            public double Elapsed;
            public double Duration;
            public float Speed = 1f;
            public bool Paused;
            public bool Done;
            public bool LockCounted;
            public InstanceContext EventCtx;
            public readonly List<Handle<ModelMarker>> SpawnedModels = new();

            public Subject<Unit> CompletedSubject;
            public Subject<Unit> CancelledSubject;
            public Subject<string> MarkerSubject;
            public UniTaskCompletionSource Waiter;

            // [14_networking.md] §5 / [26] §4.7 と同じ流儀。
            public uint HandleNetKey;
            public bool IsNetworked;
            public ushort Seed;
            public bool PlayedViaNetworkReceive;

            // [26_timeline.md] §4.6(6-10b) — このプレイバックがカメラ所有権を失って終了した(G-5/検出3)
            // ことを示す。一度 true になったら、このプレイバックはカメラを再取得しない([26] §4.6.5)。
            public bool CameraOwnershipEnded;

            // [26_timeline.md] §4.3(6-10b) — Play() 時に時刻順で集めた D-Drive マーカー一覧とカーソル
            // (elapsed が跨いだら発火し、カーソルだけ前進する。巻き戻しても再発火しない)。
            public readonly List<(double, CutsceneEventNotification)> EventMarkers = new();
            public int EventMarkerCursor;
            public readonly List<(double, CutsceneSignalNotification)> SignalMarkers = new();
            public int SignalMarkerCursor;
            public readonly List<(double, CutsceneShakeNotification)> ShakeMarkers = new();
            public int ShakeMarkerCursor;
            public readonly List<(double, CutsceneHapticNotification)> HapticMarkers = new();
            public int HapticMarkerCursor;
        }

        // [26] §4.5「PlayableDirector は Pool から借用」の実装。PoolService はプレハブの Instantiate を前提に
        // しており、CutsceneRoot には元になる Prefab アセットが無いため、専用の軽量な free-list で代替する
        // (意図(使い捨てない・再利用する)は同じ。カットシーンは頻度・同時数が小さいためこれで十分)。
        private sealed class CutsceneDirectorSlot
        {
            public GameObject Root;
            public PlayableDirector Director;
            // [26_timeline.md] §4.4(Edit Mode プレビュー、2026-09-19) — クリップ 5 種・Advance*Markers が
            // `Application.isPlaying` の代わりに見るゲート。CutsceneManager は Play Mode/テスト専用の
            // 経路なので常に true 固定にする(RentDirector 参照)。
            public CutsceneDirectorContext Context;
        }

        // Host のみが保持する「アクティブな Cosmetic Cutscene」台帳(Late Join 用。PresentationManager と同じ設計)。
        private struct ActiveNetworkedEntry
        {
            public CutsceneData Data;
            public PlayContext Ctx;
            public double StartNetTime;
            public ushort Seed;
        }

        private readonly IAssetRegistry _registry;
        private readonly ModelsManager _models;
        private readonly INetBridge _netBridge;

        private readonly InstanceStore<CutsceneMarker, CutsceneInstance> _instances = new();
        private readonly List<Handle<CutsceneMarker>> _active = new();
        private readonly Stack<CutsceneDirectorSlot> _freeDirectors = new();
        private readonly HashSet<string> _unresolvedBindingWarned = new();
        private readonly HashSet<CutsceneData> _skipWarned = new();

        // [26_timeline.md] §4.6(6-10b) — Camera クリップを持つ Cutscene のうち、現在カメラを実際に駆動して
        // いる 1 本(単純化: 同時に複数本がカメラを取り合う場合は最初の 1 本が勝ち、2 本目以降は警告 1 回で
        // カメラ以外のトラックだけ普通に再生する。TODO: 複数同時駆動の合成は将来必要になれば対応する)。
        private CutsceneInstance _cameraOwner;
        private DDriveCutsceneCameraApplier _cameraApplier;
        private Camera _cameraOwnerCamera;
        private readonly HashSet<CutsceneData> _multiCameraWarned = new();

        private readonly EventBus _events = new();
        private int _lockDepth;
        private readonly Subject<bool> _inputLockChangedSubject = new();

        private readonly Dictionary<uint, Handle<CutsceneMarker>> _networkedHandles = new();
        private readonly Dictionary<uint, ActiveNetworkedEntry> _activeNetworked = new();
        private readonly uint _instanceSalt;
        private uint _nextLocalSeq;

        // [14_networking.md] §5(6-0 修正3)/docs/45 P1-3(2026-09-20) — PresentationManager.SetRegistryReady
        // と同じ仕組みをそのまま移植する。DDriveRuntimeBootstrap のカタログ登録が完了するまで(Build() 直後
        // ～RegisterCatalogsAsync 完了)、Late Join 直後に届く CutscenePlayMsg 等が「未登録」として破棄される
        // 穴を塞ぐ。既定 true(Bootstrap を経由しない既存テスト/シングルプレイは今までどおり即時処理)。
        private bool _registryReady = true;

        private enum PendingNetMessageKind
        {
            Play,
            Seek,
            Cancel,
        }

        private struct PendingNetMessage
        {
            public PendingNetMessageKind Kind;
            public ulong SenderId;
            public CutscenePlayMsg Play;
            public CutsceneSeekMsg Seek;
            public CutsceneCancelMsg Cancel;
        }

        private readonly Queue<PendingNetMessage> _pendingNetMessages = new();

        private const int HandleNetKeyIssuerBits = 8;
        private const uint HandleNetKeyIssuerMask = 0xFFu;
        private const uint HandleNetKeyLowerMask = 0x00FFFFFFu;
        private const ulong TrustedRelayClientId = 0UL;
        private const int SeekCancelRateLimitPerSecond = 60;

        private struct RateBudget
        {
            public double WindowStart;
            public int Count;
        }

        private readonly Dictionary<ulong, RateBudget> _seekCancelBudgets = new();

        public AssetType Type => AssetType.Cutscene;

        public EventBus Events => _events;

        // [26_timeline.md] §4.5.1 — 「LockInput な Cutscene が 1 つでも再生中か」。0→1/1→0 の変化時だけ発火する。
        public bool IsInputLockedAny => _lockDepth > 0;
        public Observable<bool> OnInputLockChanged => _inputLockChangedSubject;

        public CutsceneManager(IAssetRegistry registry, ModelsManager models = null, INetBridge netBridge = null)
        {
            _registry = registry;
            _models = models;
            _netBridge = netBridge;
            _instanceSalt = (uint)Random.Range(int.MinValue, int.MaxValue);

            if (_netBridge != null)
            {
                _netBridge.Subscribe<CutscenePlayMsg>(OnReceivePlayMsg);
                _netBridge.Subscribe<CutsceneSeekMsg>(OnReceiveSeekMsg);
                _netBridge.Subscribe<CutsceneCancelMsg>(OnReceiveCancelMsg);
                _netBridge.ClientConnected += OnClientConnected;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterPlaceholder()
        {
            PlaceholderProvider.Register(CreatePlaceholder);
        }

        // 未登録 ID は Timeline 無し(即完了)の Placeholder。
        private static CutsceneData CreatePlaceholder()
        {
            var data = ScriptableObject.CreateInstance<CutsceneData>();
            data.DisplayName = "<Placeholder:CUTSCENE>";
            data.Bindings = System.Array.Empty<CutsceneBinding>();
            return data;
        }

        // ── Play ──

        public Handle<CutsceneMarker> Play(CutsceneId id, in PlayContext ctx)
            => PlayData(_registry.ResolveOrPlaceholder<CutsceneData>(id.Value), in ctx);

        public Handle<CutsceneMarker> PlayData(CutsceneData data, in PlayContext ctx)
        {
            if (data == null)
            {
                return Handle<CutsceneMarker>.Invalid;
            }

            // [26_timeline.md] §4.7 — Flags.Net=Cosmetic かつ netBridge が居るときだけネット経路に乗る
            // (null は常にローカル。[14_networking.md] §1 の原則と同じ)。
            if (_netBridge != null && data.Flags.Net == DDrive.Foundation.Net.NetMode.Cosmetic)
            {
                return PlayCosmeticNetworked(data, in ctx);
            }

            return PlayLocalInternal(data, in ctx, elapsedSeek: 0d, seed: 0, handleNetKey: 0, isNetworked: false, playedViaNetworkReceive: false);
        }

        private Handle<CutsceneMarker> PlayLocalInternal(
            CutsceneData data,
            in PlayContext ctx,
            double elapsedSeek,
            ushort seed,
            uint handleNetKey,
            bool isNetworked,
            bool playedViaNetworkReceive)
        {
            var slot = RentDirector();
            var duration = data.Timeline != null ? data.Timeline.duration : 0d;

            var instance = new CutsceneInstance
            {
                Data = data,
                Ctx = ctx,
                Slot = slot,
                Duration = duration,
                Elapsed = System.Math.Max(0d, elapsedSeek),
                CompletedSubject = new Subject<Unit>(),
                CancelledSubject = new Subject<Unit>(),
                MarkerSubject = new Subject<string>(),
                HandleNetKey = handleNetKey,
                IsNetworked = isNetworked,
                PlayedViaNetworkReceive = playedViaNetworkReceive,
                Seed = seed,
            };

            var handle = _instances.Add(instance);
            instance.EventCtx = new InstanceContext(handle.Index, handle.Generation);

            slot.Director.playableAsset = data.Timeline;
            ApplyOrigin(instance);
            ApplyBindings(instance);
            CollectMarkers(instance);
            // elapsedSeek>0(ネット越しの遅延復元等)で始まる場合、既に過ぎたマーカーは無音でスキップする
            // (PresentationManager の SeekInitialTracks と同じ方針)。elapsedSeek==0 なら何も跨がない。
            AdvanceMarkers(instance, instance.Elapsed, fire: false);

            slot.Director.time = System.Math.Min(instance.Elapsed, System.Math.Max(0d, instance.Duration));
            slot.Director.extrapolationMode = data.Wrap;
            slot.Director.Evaluate();

            if (handleNetKey != 0)
            {
                _networkedHandles[handleNetKey] = handle;
            }

            _active.Add(handle);

            _events.Begin(instance.EventCtx, data.Events);
            _events.Fire(instance.EventCtx, EventTrigger.OnSpawn);
            _events.Fire(instance.EventCtx, EventTrigger.OnEnable);
            AcquireInputLockIfNeeded(instance);

            // 既に尺を超えているシーク開始(ネット越しの遅延復元等)は次の Tick で即完了させる。
            if (!instance.Done && instance.Duration > 0d && instance.Elapsed >= instance.Duration)
            {
                Complete(handle, instance);
            }

            return handle;
        }

        // ── 原点([26] §4.2.1) ──

        private void ApplyOrigin(CutsceneInstance instance)
        {
            var root = instance.Slot.Root.transform;
            var data = instance.Data;

            switch (data.Origin)
            {
                case CutsceneOrigin.Self:
                    if (instance.Ctx.Self != null)
                    {
                        var yaw = instance.Ctx.Self.eulerAngles.y;
                        root.SetPositionAndRotation(instance.Ctx.Self.position, Quaternion.Euler(0f, yaw, 0f));
                    }
                    else
                    {
                        WarnUnresolvedBinding(data, "<Origin>", "Origin=Self ですが ctx.Self が未設定です");
                        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    }

                    break;

                case CutsceneOrigin.AnchorPoint:
                    var anchor = FindAnchorPointByName(data.OriginAnchorName);
                    if (anchor != null)
                    {
                        root.SetPositionAndRotation(anchor.transform.position, anchor.transform.rotation);
                    }
                    else
                    {
                        WarnUnresolvedBinding(data, "<Origin>", $"Origin=AnchorPoint('{data.OriginAnchorName}') が見つかりません");
                        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    }

                    break;

                default: // World
                    root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    break;
            }
        }

        // ── バインド解決([26] §4.2) ──

        private void ApplyBindings(CutsceneInstance instance)
        {
            var data = instance.Data;
            if (data.Timeline == null || data.Bindings == null)
            {
                return;
            }

            for (var i = 0; i < data.Bindings.Length; i++)
            {
                var binding = data.Bindings[i];
                if (string.IsNullOrEmpty(binding.TrackName))
                {
                    continue;
                }

                var track = FindTrackByName(data.Timeline, binding.TrackName);
                if (track == null)
                {
                    WarnUnresolvedBinding(data, binding.TrackName, "TimelineAsset にこの名前のトラックが見つかりません");
                    continue;
                }

                var resolved = ResolveBindingObject(instance, in binding);
                instance.Slot.Director.SetGenericBinding(track, resolved);
            }
        }

        private static TrackAsset FindTrackByName(TimelineAsset timeline, string name)
        {
            foreach (var track in timeline.GetOutputTracks())
            {
                if (track != null && track.name == name)
                {
                    return track;
                }
            }

            return null;
        }

        // 未解決は null を返す(SetGenericBinding(track, null) はそのトラックをミュートしたまま継続する。[26] TL;DR #4)。
        private Object ResolveBindingObject(CutsceneInstance instance, in CutsceneBinding binding)
        {
            Transform target = null;

            switch (binding.Target)
            {
                case CutsceneBindTarget.MainCamera:
                    var cam = Camera.main;
                    target = cam != null ? cam.transform : null;
                    break;

                case CutsceneBindTarget.Self:
                    target = instance.Ctx.Self;
                    break;

                case CutsceneBindTarget.Target:
                    target = instance.Ctx.Target;
                    break;

                case CutsceneBindTarget.SpawnModel:
                    if (_models != null && binding.Model.IsValid)
                    {
                        var modelHandle = _models.Spawn(binding.Model, instance.Slot.Root.transform);
                        if (_models.IsValid(modelHandle))
                        {
                            instance.SpawnedModels.Add(modelHandle);
                            var animator = _models.GetAnimator(modelHandle);
                            if (animator != null)
                            {
                                return animator;
                            }

                            var go = _models.GetGameObject(modelHandle);
                            target = go != null ? go.transform : null;
                        }
                    }

                    break;

                case CutsceneBindTarget.SceneObjectByName:
                    if (!string.IsNullOrEmpty(binding.SceneObjectName))
                    {
                        var found = GameObject.Find(binding.SceneObjectName);
                        target = found != null ? found.transform : null;
                    }

                    break;

                case CutsceneBindTarget.AnchorPoint:
                    var anchor = FindAnchorPointByName(binding.SceneObjectName);
                    target = anchor != null ? anchor.transform : null;
                    break;
            }

            if (target == null)
            {
                WarnUnresolvedBinding(instance.Data, binding.TrackName, $"Target={binding.Target} を解決できませんでした");
                return null;
            }

            var boundAnimator = target.GetComponent<Animator>();
            return boundAnimator != null ? (Object)boundAnimator : target;
        }

        private static AnchorPoint FindAnchorPointByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var all = Object.FindObjectsByType<AnchorPoint>(FindObjectsSortMode.None);
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == name)
                {
                    return all[i];
                }
            }

            return null;
        }

        private void WarnUnresolvedBinding(CutsceneData data, string trackName, string reason)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var key = $"{(data != null ? data.GetInstanceID() : 0)}:{trackName}";
            if (_unresolvedBindingWarned.Add(key))
            {
                Debug.LogWarning($"[DDrive] Cutscene '{(data != null ? data.DisplayName : "?")}': トラック '{trackName}' が未解決です({reason})。そのトラックはミュートのまま継続します。");
            }
#endif
        }

        // [26_timeline.md] §4.3(6-10b 実装メモ) — D-Drive のマーカー(Event/Signal/Shake/Haptic)は Unity
        // 標準の Signal 通知配送(INotification/INotificationReceiver)を使わず、Play() 時に
        // TrackAsset.GetMarkers() で時刻順に集めておき、Tick() で elapsed が跨いだ瞬間に直接発火する
        // (EventBus.Tick の Frame/Time 判定・PresentationManager.FireDueTracks と同じ「跨いだら発火、
        // Seek は無音でスキップ」パターン)。理由: `TimeNotificationBehaviour.PrepareFrame` は
        // `FrameData.EvaluationType.Evaluate`(Evaluate() 単体呼び出し)のフレームで通知を送らない実装で、
        // CutsceneManager は DirectorUpdateMode.Manual + 毎 Tick Evaluate() で駆動するため native 通知に
        // 頼るとバージョン依存のリスクがある。
        private void CollectMarkers(CutsceneInstance instance)
        {
            var timeline = instance.Data.Timeline;
            if (timeline == null)
            {
                return;
            }

            foreach (var track in timeline.GetOutputTracks())
            {
                if (track == null)
                {
                    continue;
                }

                foreach (var marker in track.GetMarkers())
                {
                    switch (marker)
                    {
                        case CutsceneEventNotification evt:
                            instance.EventMarkers.Add((marker.time, evt));
                            break;

                        case CutsceneSignalNotification sig:
                            instance.SignalMarkers.Add((marker.time, sig));
                            break;

                        case CutsceneShakeNotification shake:
                            instance.ShakeMarkers.Add((marker.time, shake));
                            break;

                        case CutsceneHapticNotification haptic:
                            instance.HapticMarkers.Add((marker.time, haptic));
                            break;
                    }
                }
            }

            instance.EventMarkers.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            instance.SignalMarkers.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            instance.ShakeMarkers.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            instance.HapticMarkers.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        }

        // fire=false は Seek(Skip/ネット復元の開始点)用: 跨いだマーカーは「既に通過済み」として無音で
        // カーソルだけ進める(PresentationManager の SeekInitialTracks/ワンショットスキップと同じ方針)。
        private void AdvanceMarkers(CutsceneInstance instance, double newElapsed, bool fire)
        {
            AdvanceEventMarkers(instance, newElapsed, fire);
            AdvanceSignalMarkers(instance, newElapsed, fire);
            AdvanceShakeMarkers(instance, newElapsed, fire);
            AdvanceHapticMarkers(instance, newElapsed, fire);
        }

        // [26_timeline.md] §4.4(Edit Mode プレビュー、2026-09-19) — `Application.isPlaying` の代わりに
        // Slot に付けた `CutsceneDirectorContext.FireEnabled` を見る(RentDirector が常に true を入れるため、
        // Play Mode/テストでの挙動は変わらない)。Edit Mode 側は CutsceneManager を経由しない(TL;DR どおり
        // 別経路の `CutsceneEditModePreviewProvider` が独自にマーカーを監視する)ため、ここでは Context が
        // 無い(Root が既に破棄された等の異常系)場合のみ安全側で発火しない。
        private static bool IsFireEnabled(CutsceneInstance instance)
            => instance.Slot?.Context != null && instance.Slot.Context.FireEnabled;

        private void AdvanceEventMarkers(CutsceneInstance instance, double newElapsed, bool fire)
        {
            var list = instance.EventMarkers;
            while (instance.EventMarkerCursor < list.Count && list[instance.EventMarkerCursor].Item1 <= newElapsed)
            {
                var marker = list[instance.EventMarkerCursor].Item2;
                instance.EventMarkerCursor++;

                if (fire && IsFireEnabled(instance))
                {
                    _events.RaiseAdHoc(instance.EventCtx, marker.Event);
                }
            }
        }

        private void AdvanceSignalMarkers(CutsceneInstance instance, double newElapsed, bool fire)
        {
            var list = instance.SignalMarkers;
            while (instance.SignalMarkerCursor < list.Count && list[instance.SignalMarkerCursor].Item1 <= newElapsed)
            {
                var marker = list[instance.SignalMarkerCursor].Item2;
                instance.SignalMarkerCursor++;

                if (fire && IsFireEnabled(instance))
                {
                    instance.MarkerSubject.OnNext(marker.Key);
                }
            }
        }

        private void AdvanceShakeMarkers(CutsceneInstance instance, double newElapsed, bool fire)
        {
            var list = instance.ShakeMarkers;
            while (instance.ShakeMarkerCursor < list.Count && list[instance.ShakeMarkerCursor].Item1 <= newElapsed)
            {
                var marker = list[instance.ShakeMarkerCursor].Item2;
                instance.ShakeMarkerCursor++;

                if (fire && IsFireEnabled(instance) && marker.ShakeId.IsValid)
                {
                    // 完全修飾で呼ぶ(DDrive.Runtime 配下の子ネームスペース DDrive.Runtime.CameraShake が
                    // 素の `CameraFx` より先に解決されコンパイルエラーになるため、[26_timeline.md] §4.3 実装メモ)。
                    DDrive.Runtime.CameraShake.CameraFx.Shake(marker.ShakeId, instance.Ctx.Position);
                }
            }
        }

        private void AdvanceHapticMarkers(CutsceneInstance instance, double newElapsed, bool fire)
        {
            var list = instance.HapticMarkers;
            while (instance.HapticMarkerCursor < list.Count && list[instance.HapticMarkerCursor].Item1 <= newElapsed)
            {
                var marker = list[instance.HapticMarkerCursor].Item2;
                instance.HapticMarkerCursor++;

                if (fire && IsFireEnabled(instance) && marker.HapticId.IsValid)
                {
                    DDrive.Runtime.Haptics.Haptics.Play(marker.HapticId);
                }
            }
        }

        // [26_timeline.md] §4.1/§4.3(6-10b) — Skip=ToMarker の目標秒を、Timeline 上の D-Drive Signal
        // マーカー(CutsceneSignalNotification.Key が一致するもの)の時刻から求める。見つからなければ null。
        private static double? FindSignalMarkerTime(TimelineAsset timeline, string key)
        {
            if (timeline == null || string.IsNullOrEmpty(key))
            {
                return null;
            }

            foreach (var track in timeline.GetOutputTracks())
            {
                if (track == null)
                {
                    continue;
                }

                foreach (var marker in track.GetMarkers())
                {
                    if (marker is CutsceneSignalNotification signal && signal.Key == key)
                    {
                        return marker.time;
                    }
                }
            }

            return null;
        }

        // ── Pool 代替(PlayableDirector の free-list) ──

        private CutsceneDirectorSlot RentDirector()
        {
            while (_freeDirectors.Count > 0)
            {
                var slot = _freeDirectors.Pop();
                if (slot.Root != null)
                {
                    slot.Root.SetActive(true);
                    return slot;
                }
            }

            var go = new GameObject("CutsceneRoot");
            var director = go.AddComponent<PlayableDirector>();
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.playOnAwake = false;

            // [26_timeline.md] §4.6(6-10b) — Camera クリップの評価結果置き場。free-list で再利用されるため
            // CutsceneRoot 生成時に 1 回だけ付ける(マーカーは CollectMarkers/AdvanceMarkers が
            // TrackAsset.GetMarkers() から直接読むため、対応するコンポーネントは不要)。
            go.AddComponent<CutsceneCameraStateHolder>();

            // [26_timeline.md] §4.4(Edit Mode プレビュー、2026-09-19) — SE/VFX/UI/AnchorGroup クリップの
            // `CreatePlayable(graph, owner)` が owner から辿る文脈。Play Mode(CutsceneManager 経由)は
            // 常に発火してよいので固定 true、Manager 参照(ManagerRefs)は null のままにして各クリップを
            // 既存の静的ファサード経路へフォールバックさせる(Play Mode の挙動を変えない)。
            var context = go.AddComponent<CutsceneDirectorContext>();
            context.FireEnabled = true;

            return new CutsceneDirectorSlot { Root = go, Director = director, Context = context };
        }

        private void ReturnDirector(CutsceneDirectorSlot slot)
        {
            if (slot?.Root == null)
            {
                return;
            }

            slot.Director.playableAsset = null;
            slot.Root.transform.SetParent(null, worldPositionStays: false);
            slot.Root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            slot.Root.SetActive(false);

            // [26_timeline.md] §4.6/docs/45 P1-1(2026-09-20) — CutsceneCameraStateHolder は
            // CutsceneRoot 生成時に 1 回だけ付き free-list で使い回されるため、HasData をここでリセット
            // しないと、次にこのスロットを借りた「カメラトラックを持たない」Cutscene が前回の値を
            // 自分のカメラデータだと誤認してしまう(Cancel 直後の再現条件)。ミキサーは毎フレーム
            // ProcessFrame で上書きするので、カメラ付きの Cutscene が借りた場合はすぐ正しい値に戻る。
            var holder = slot.Root.GetComponent<CutsceneCameraStateHolder>();
            if (holder != null)
            {
                holder.HasData = false;
            }

            _freeDirectors.Push(slot);
        }

        // ── ネットワーク再生([26] §4.7) ──

        private Handle<CutsceneMarker> PlayCosmeticNetworked(CutsceneData data, in PlayContext ctx)
        {
            var handleNetKey = NextHandleNetKey();
            var seed = (ushort)Random.Range(0, ushort.MaxValue + 1);
            var startNetTime = _netBridge.NetworkTime;

            var predicted = Handle<CutsceneMarker>.Invalid;
            if (data.PredictLocal)
            {
                predicted = PlayLocalInternal(data, in ctx, elapsedSeek: 0d, seed: seed, handleNetKey: handleNetKey, isNetworked: true, playedViaNetworkReceive: false);

                if (_netBridge.IsServer)
                {
                    RegisterActiveIfServer(handleNetKey, data, ctx, startNetTime, seed);
                }
            }

            var selfNetId = _netBridge.ResolveNetId(ctx.Self);
            var targetNetId = _netBridge.ResolveNetId(ctx.Target);

            _netBridge.Broadcast(new CutscenePlayMsg
            {
                CutId = data.Id,
                SelfNetId = selfNetId,
                TargetNetId = targetNetId,
                Position = ctx.Position,
                StartNetTime = startNetTime,
                Seed = seed,
                HandleNetKey = handleNetKey,
            }, NetChannel.ReliableOrdered);

            return predicted;
        }

        private static uint IssuerOf(uint handleNetKey) => (handleNetKey >> (32 - HandleNetKeyIssuerBits)) & HandleNetKeyIssuerMask;

        private bool IsAuthorizedSender(ulong senderId, uint handleNetKey)
        {
            if (handleNetKey == 0)
            {
                return false;
            }

            if (senderId == TrustedRelayClientId)
            {
                return true;
            }

            return (senderId & HandleNetKeyIssuerMask) == IssuerOf(handleNetKey);
        }

        private bool ConsumeSeekCancelBudget(ulong senderId)
        {
            if (senderId == TrustedRelayClientId)
            {
                return true;
            }

            var now = _netBridge != null ? _netBridge.NetworkTime : 0d;
            _seekCancelBudgets.TryGetValue(senderId, out var budget);

            if (now - budget.WindowStart >= 1d)
            {
                budget.WindowStart = now;
                budget.Count = 0;
            }

            budget.Count++;
            _seekCancelBudgets[senderId] = budget;
            return budget.Count <= SeekCancelRateLimitPerSecond;
        }

        private uint NextHandleNetKey()
        {
            unchecked
            {
                _nextLocalSeq++;
                var timeBits = _netBridge != null ? System.BitConverter.DoubleToInt64Bits(_netBridge.NetworkTime) : 0L;
                var mixed = (uint)(timeBits ^ (timeBits >> 32));
                var lower = ((mixed ^ _instanceSalt) + _nextLocalSeq) & HandleNetKeyLowerMask;

                var clientId = _netBridge != null ? _netBridge.LocalClientId : 0UL;
                var issuerBits = ((uint)clientId & HandleNetKeyIssuerMask) << (32 - HandleNetKeyIssuerBits);
                var key = issuerBits | lower;
                return key == 0 ? 1u : key;
            }
        }

        // [14_networking.md] §5(6-0 修正3)/docs/45 P1-3(2026-09-20) — Registry が ready になるまで
        // 受信順にキューへ保留する(PresentationManager.SetRegistryReady と同じ実装)。
        // DDriveRuntimeBootstrap.Build() が false を、RegisterCatalogsAsync 完了時に true を呼ぶ。
        public void SetRegistryReady(bool ready)
        {
            _registryReady = ready;
            if (!ready)
            {
                return;
            }

            while (_pendingNetMessages.Count > 0)
            {
                var pending = _pendingNetMessages.Dequeue();
                switch (pending.Kind)
                {
                    case PendingNetMessageKind.Play:
                        OnReceivePlayMsgInternal(pending.SenderId, pending.Play);
                        break;
                    case PendingNetMessageKind.Seek:
                        OnReceiveSeekMsgInternal(pending.SenderId, pending.Seek);
                        break;
                    case PendingNetMessageKind.Cancel:
                        OnReceiveCancelMsgInternal(pending.SenderId, pending.Cancel);
                        break;
                }
            }
        }

        private void OnReceivePlayMsg(ulong senderId, CutscenePlayMsg msg)
        {
            if (!_registryReady)
            {
                _pendingNetMessages.Enqueue(new PendingNetMessage { Kind = PendingNetMessageKind.Play, SenderId = senderId, Play = msg });
                return;
            }

            OnReceivePlayMsgInternal(senderId, msg);
        }

        private void OnReceivePlayMsgInternal(ulong senderId, CutscenePlayMsg msg)
        {
            if (!IsAuthorizedSender(senderId, msg.HandleNetKey))
            {
                Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Cutscene: CutscenePlayMsg(HandleNetKey=0x{msg.HandleNetKey:X8}) の送信元 ClientId({senderId}) が発行者と一致しないため破棄しました。");
                return;
            }

            if (_networkedHandles.TryGetValue(msg.HandleNetKey, out var existingHandle) && _instances.TryGet(existingHandle, out var existingInstance))
            {
                if (existingInstance.Data.Id != msg.CutId)
                {
                    Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Cutscene: CutscenePlayMsg(HandleNetKey=0x{msg.HandleNetKey:X8}) の CutId が既存エントリと一致しないため破棄しました。");
                    return;
                }

                RegisterActiveIfServer(msg.HandleNetKey, existingInstance.Data, existingInstance.Ctx, msg.StartNetTime, msg.Seed);
                return;
            }

            if (!_registry.IsRegistered(msg.CutId, AssetType.Cutscene))
            {
                Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Cutscene: CutscenePlayMsg(CutId=0x{msg.CutId:X}) は未登録、または種別が Cutscene ではないため破棄しました。");
                return;
            }

            var data = _registry.ResolveOrPlaceholder<CutsceneData>(msg.CutId);
            var duration = data.Timeline != null ? data.Timeline.duration : 0d;
            var elapsed = System.Math.Max(0d, _netBridge.NetworkTime - msg.StartNetTime);

            if (duration > 0d && elapsed >= duration)
            {
                return; // 既に終わっている演出は復元しない([26] §4.7 は Presentation と同じ方針)。
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

        private void RegisterActiveIfServer(uint handleNetKey, CutsceneData data, PlayContext ctx, double startNetTime, ushort seed)
        {
            if (_netBridge == null || !_netBridge.IsServer || handleNetKey == 0)
            {
                return;
            }

            _activeNetworked[handleNetKey] = new ActiveNetworkedEntry { Data = data, Ctx = ctx, StartNetTime = startNetTime, Seed = seed };
        }

        // [26] §4.7 Late Join — Host のみ。新規接続に、アクティブな Cosmetic Cutscene を元の StartNetTime のまま再送する。
        private void OnClientConnected(ulong clientId)
        {
            if (_netBridge == null || !_netBridge.IsServer || clientId == _netBridge.LocalClientId)
            {
                return;
            }

            var count = _activeNetworked.Count;
            if (count == 0)
            {
                return;
            }

            var keys = new uint[count];
            var entries = new ActiveNetworkedEntry[count];
            var i = 0;
            foreach (var kv in _activeNetworked)
            {
                keys[i] = kv.Key;
                entries[i] = kv.Value;
                i++;
            }

            for (var j = 0; j < count; j++)
            {
                var entry = entries[j];
                _netBridge.SendTo(clientId, new CutscenePlayMsg
                {
                    CutId = entry.Data.Id,
                    SelfNetId = 0,
                    TargetNetId = 0,
                    Position = entry.Ctx.Position,
                    StartNetTime = entry.StartNetTime,
                    Seed = entry.Seed,
                    HandleNetKey = keys[j],
                }, NetChannel.ReliableOrdered);
            }
        }

        private void OnReceiveSeekMsg(ulong senderId, CutsceneSeekMsg msg)
        {
            if (!_registryReady)
            {
                _pendingNetMessages.Enqueue(new PendingNetMessage { Kind = PendingNetMessageKind.Seek, SenderId = senderId, Seek = msg });
                return;
            }

            OnReceiveSeekMsgInternal(senderId, msg);
        }

        private void OnReceiveSeekMsgInternal(ulong senderId, CutsceneSeekMsg msg)
        {
            if (!IsAuthorizedSender(senderId, msg.HandleNetKey))
            {
                Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Cutscene: CutsceneSeekMsg(HandleNetKey=0x{msg.HandleNetKey:X8}) の送信元 ClientId({senderId}) が発行者と一致しないため破棄しました。");
                return;
            }

            if (!ConsumeSeekCancelBudget(senderId))
            {
                Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Cutscene: Client {senderId} からの CutsceneSeekMsg がレート制限({SeekCancelRateLimitPerSecond}/秒)を超えたため破棄しました。");
                return;
            }

            if (!_networkedHandles.TryGetValue(msg.HandleNetKey, out var handle) || !_instances.TryGet(handle, out var instance) || instance.Done)
            {
                Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Cutscene: CutsceneSeekMsg(HandleNetKey=0x{msg.HandleNetKey:X8}) は未知のキー、または対象の演出が既に完了しているため破棄しました。");
                return;
            }

            ApplySeek(instance, msg.ToTime);
        }

        private void OnReceiveCancelMsg(ulong senderId, CutsceneCancelMsg msg)
        {
            if (!_registryReady)
            {
                _pendingNetMessages.Enqueue(new PendingNetMessage { Kind = PendingNetMessageKind.Cancel, SenderId = senderId, Cancel = msg });
                return;
            }

            OnReceiveCancelMsgInternal(senderId, msg);
        }

        private void OnReceiveCancelMsgInternal(ulong senderId, CutsceneCancelMsg msg)
        {
            if (!IsAuthorizedSender(senderId, msg.HandleNetKey))
            {
                Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Cutscene: CutsceneCancelMsg(HandleNetKey=0x{msg.HandleNetKey:X8}) の送信元 ClientId({senderId}) が発行者と一致しないため破棄しました。");
                return;
            }

            if (!ConsumeSeekCancelBudget(senderId))
            {
                Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Cutscene: Client {senderId} からの CutsceneCancelMsg がレート制限({SeekCancelRateLimitPerSecond}/秒)を超えたため破棄しました。");
                return;
            }

            if (!_networkedHandles.TryGetValue(msg.HandleNetKey, out var handle) || !_instances.TryGet(handle, out var instance) || instance.Done)
            {
                Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Cutscene: CutsceneCancelMsg(HandleNetKey=0x{msg.HandleNetKey:X8}) は未知のキー、または対象の演出が既に完了しているため破棄しました。");
                return;
            }

            CancelInternal(handle, instance);
        }

        // 自分(Client)が Host との接続を失ったときに、ネット経由の Cutscene を強制終了する
        // (PresentationManager.CancelAllNetworked と同じ設計。呼び出しは Bootstrap から)。
        public void CancelAllNetworked()
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var handle = _active[i];
                if (_instances.TryGet(handle, out var instance) && !instance.Done && instance.IsNetworked)
                {
                    CancelInternal(handle, instance);
                }
            }
        }

        // [14_networking.md] §18(N-5、2026-09-24) — Host 引き継ぎ向け(PresentationManager.
        // ResetNetworkedState と同じ設計)。CancelAllNetworked() を内包しつつ、ネット由来の台帳・保留キュー・
        // 受信レート制限窓を初期状態へ戻す。ローカル(IsNetworked=false)の Instance には触れない。
        // _registryReady は変更しない(カタログ登録状態を表すフラグでネットワークの生死とは無関係)。
        public void ResetNetworkedState()
        {
            CancelAllNetworked();

            _networkedHandles.Clear();
            _activeNetworked.Clear();
            _pendingNetMessages.Clear();
            _seekCancelBudgets.Clear();
        }

        // テスト/デバッグ専用: 現在再生中の Handle を列挙する(ネット受信で生成された Instance はゲーム
        // コードに Handle を返さないため、PresentationManager.DebugActiveHandles と同じ理由で用意する)。
        public List<Handle<CutsceneMarker>> DebugActiveHandles()
        {
            var copy = new List<Handle<CutsceneMarker>>(_active.Count);
            for (var i = 0; i < _active.Count; i++)
            {
                copy.Add(_active[i]);
            }

            return copy;
        }

        // ── Handle 操作 ──

        // [M-1c、2026-09-25] 冪等操作なので TryGetQuiet で警告なしにガードする。
        public void Cancel(Handle<CutsceneMarker> handle)
        {
            if (!_instances.TryGetQuiet(handle, out var instance) || instance.Done)
            {
                return;
            }

            if (instance.IsNetworked && _netBridge != null)
            {
                _netBridge.Broadcast(new CutsceneCancelMsg { HandleNetKey = instance.HandleNetKey }, NetChannel.ReliableOrdered);
                return;
            }

            CancelInternal(handle, instance);
        }

        private void CancelInternal(Handle<CutsceneMarker> handle, CutsceneInstance instance)
        {
            instance.Done = true;
            ReleaseInputLockIfNeeded(instance);
            _events.Fire(instance.EventCtx, EventTrigger.OnDisable);
            _events.End(instance.EventCtx);
            instance.CancelledSubject.OnNext(Unit.Default);
            instance.Waiter?.TrySetResult();
            Cleanup(handle, instance);
        }

        // [26_timeline.md] §4.1/§4.7 — Skip() は Data.Skip の方針に従って目標秒へシークする。
        // Cosmetic では Broadcast 前に自分だけ飛ばない(Cancel と同じ規則)。
        public void Skip(Handle<CutsceneMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance) || instance.Done)
            {
                return;
            }

            if (instance.Data.Skip == CutsceneSkip.Disabled)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (_skipWarned.Add(instance.Data))
                {
                    Debug.LogWarning($"[DDrive] Cutscene '{instance.Data.DisplayName}' は Skip=Disabled のため Skip() を無視しました。");
                }
#endif
                return;
            }

            var targetTime = ResolveSkipTargetTime(instance);

            if (instance.IsNetworked && _netBridge != null)
            {
                _netBridge.Broadcast(new CutsceneSeekMsg { HandleNetKey = instance.HandleNetKey, ToTime = targetTime }, NetChannel.ReliableOrdered);
                return;
            }

            ApplySeek(instance, targetTime);
        }

        private double ResolveSkipTargetTime(CutsceneInstance instance)
        {
            if (instance.Data.Skip == CutsceneSkip.ToMarker)
            {
                var markerTime = FindSignalMarkerTime(instance.Data.Timeline, instance.Data.SkipToMarkerKey);
                if (markerTime.HasValue)
                {
                    return markerTime.Value;
                }

                // マーカーが見つからない(未設定・削除済み・タイプミス)場合は Immediate と同じ(末尾まで飛ばす)
                // にフォールバックする([26] §4.1)。
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (_skipWarned.Add(instance.Data))
                {
                    Debug.LogWarning($"[DDrive] Cutscene '{instance.Data.DisplayName}': Skip=ToMarker のマーカー '{instance.Data.SkipToMarkerKey}' が見つかりません。末尾へ飛ばします。");
                }
#endif
            }

            return instance.Duration;
        }

        // デバッグ/エディタのスクラブ専用。ネット同期はしない([26] の Skip() とは別物、PresentationManager.Seek と同じ位置付け)。
        public void Seek(Handle<CutsceneMarker> handle, float time)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                ApplySeek(instance, time);
            }
        }

        private void ApplySeek(CutsceneInstance instance, double time)
        {
            instance.Elapsed = System.Math.Max(0d, time);
            if (instance.Slot?.Director != null)
            {
                instance.Slot.Director.time = System.Math.Min(instance.Elapsed, System.Math.Max(0d, instance.Duration));
                instance.Slot.Director.Evaluate();
            }

            // Skip()/デバッグ Seek() で跨いだマーカーは無音でスキップする(TL;DR「スクラブで連打しない」と
            // 同じ考え方。通常の前進 Tick() だけが実際に発火させる)。
            AdvanceMarkers(instance, instance.Elapsed, fire: false);
        }

        public void SetPaused(Handle<CutsceneMarker> handle, bool paused)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                instance.Paused = paused;
            }
        }

        public void SetSpeed(Handle<CutsceneMarker> handle, float speed)
        {
            if (_instances.TryGet(handle, out var instance))
            {
                instance.Speed = Mathf.Max(0f, speed);
            }
        }

        public bool IsPlaying(Handle<CutsceneMarker> handle) => _instances.IsValidSilent(handle);

        // [26_timeline.md] §4.5.1 — Data.LockInput && IsPlaying。BlendOut(6-10b のカメラブレンド)中も
        // IsPlaying のままなので true が続く。
        public bool IsInputLocked(Handle<CutsceneMarker> handle)
            => _instances.TryGet(handle, out var instance) && instance.Data.LockInput;

        public float GetNormalizedTime(Handle<CutsceneMarker> handle)
        {
            if (!TryGetInstanceSilent(handle, out var instance))
            {
                return -1f;
            }

            return instance.Duration > 0d ? Mathf.Clamp01((float)(instance.Elapsed / instance.Duration)) : 0f;
        }

        public Observable<Unit> OnCompleted(Handle<CutsceneMarker> handle)
            => TryGetInstanceSilent(handle, out var instance) ? instance.CompletedSubject : Observable.Empty<Unit>();

        public Observable<Unit> OnCancelled(Handle<CutsceneMarker> handle)
            => TryGetInstanceSilent(handle, out var instance) ? instance.CancelledSubject : Observable.Empty<Unit>();

        // 6-10b の D-Drive Signal マーカー導入までは発火元が無い(API 面のみ用意する)。
        public Observable<string> OnMarker(Handle<CutsceneMarker> handle)
            => TryGetInstanceSilent(handle, out var instance) ? instance.MarkerSubject : Observable.Empty<string>();

        public UniTask WaitAsync(Handle<CutsceneMarker> handle, CancellationToken ct)
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

        private bool TryGetInstanceSilent(Handle<CutsceneMarker> handle, out CutsceneInstance instance)
        {
            if (_instances.IsValidSilent(handle))
            {
                return _instances.TryGet(handle, out instance);
            }

            instance = null;
            return false;
        }

        // 発火元 Instance の Transform(AssetEventDispatcher が SE/VFX の contextRoot に使う)。
        public Transform GetContextTransform(InstanceContext ctx)
        {
            for (var i = 0; i < _active.Count; i++)
            {
                if (_instances.TryGet(_active[i], out var instance) && instance.EventCtx.Equals(ctx))
                {
                    return instance.Ctx.Self != null ? instance.Ctx.Self : instance.Slot.Root.transform;
                }
            }

            return null;
        }

        // ── 入力ロック(集計) ──

        private void AcquireInputLockIfNeeded(CutsceneInstance instance)
        {
            if (!instance.Data.LockInput)
            {
                return;
            }

            instance.LockCounted = true;
            _lockDepth++;
            if (_lockDepth == 1)
            {
                _inputLockChangedSubject.OnNext(true);
            }

            _events.Fire(instance.EventCtx, EventTrigger.Custom, "cutscene/input_lock");
        }

        private void ReleaseInputLockIfNeeded(CutsceneInstance instance)
        {
            if (!instance.LockCounted)
            {
                return;
            }

            instance.LockCounted = false;
            _events.Fire(instance.EventCtx, EventTrigger.Custom, "cutscene/input_unlock");
            _lockDepth = Mathf.Max(0, _lockDepth - 1);
            if (_lockDepth == 0)
            {
                _inputLockChangedSubject.OnNext(false);
            }
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

                instance.Elapsed += (double)dt * instance.Speed;

                if (instance.Slot?.Director != null)
                {
                    instance.Slot.Director.time = System.Math.Min(instance.Elapsed, System.Math.Max(0d, instance.Duration));
                    instance.Slot.Director.Evaluate();
                }

                UpdateCameraForInstance(handle, instance);
                AdvanceMarkers(instance, instance.Elapsed, fire: true);

                _events.Tick(instance.EventCtx, dt);

                if (!instance.Done && instance.Elapsed >= instance.Duration)
                {
                    Complete(handle, instance);
                }
            }
        }

        // [26_timeline.md] §4.6/§4.6.5(6-10b) — Camera クリップの評価結果(CutsceneCameraStateHolder)を
        // 読み、原点([26] §4.2.1)を掛けてワールド座標にしたうえで DDriveCutsceneCameraApplier へ渡す。
        // 実際の Camera/Volume への書き込みは Applier の LateUpdate(実行順 1000)が行う([26] §4.6.5)。
        private void UpdateCameraForInstance(Handle<CutsceneMarker> handle, CutsceneInstance instance)
        {
            if (instance.CameraOwnershipEnded || instance.Slot?.Root == null)
            {
                return;
            }

            var holder = instance.Slot.Root.GetComponent<CutsceneCameraStateHolder>();
            var hasData = holder != null && holder.HasData;

            if (_cameraOwner == instance && !hasData)
            {
                // カメラクリップの区間外(ギャップ、または尾まで再生し終えた)。所有権を手放す。
                ReleaseCameraOwnership();
                return;
            }

            if (!hasData)
            {
                return;
            }

            if (_cameraOwner == null)
            {
                _cameraOwner = instance;
            }
            else if (_cameraOwner != instance)
            {
                // [26] §4.6.2 — 「1 カメラ上書き方式」の単純化: 同時に複数本がカメラを取り合った場合は
                // 最初の 1 本を優先し、2 本目以降はカメラ以外のトラックだけ普通に再生する(警告 1 回)。
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (_multiCameraWarned.Add(instance.Data))
                {
                    Debug.LogWarning($"[DDrive] Cutscene '{instance.Data.DisplayName}': 別の Cutscene が既にカメラを駆動しているため、このプレイバックのカメラクリップは無視します([26_timeline.md] §4.6)。");
                }
#endif
                return;
            }

            var cam = Camera.main;
            if (cam == null || (_cameraOwnerCamera != null && cam != _cameraOwnerCamera))
            {
                // G-5([26] §4.6.5) — Camera.main が見つからない/差し替わった。このプレイバックは
                // BlendOut 無しで終了する(付け直しての継続はしない)。
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning($"[DDrive] Cutscene '{instance.Data.DisplayName}': 再生中に Camera.main が見つからなくなった/差し替わったため、カメラ演出を終了します(G-5、[26_timeline.md] §4.6.5)。");
#endif
                ReleaseCameraOwnership();
                instance.CameraOwnershipEnded = true;
                return;
            }

            var applier = DDriveCutsceneCameraApplier.EnsureOn(cam);
            if (applier == null)
            {
                return;
            }

            _cameraApplier = applier;
            _cameraOwnerCamera = cam;

            var root = instance.Slot.Root.transform;
            var request = new CutsceneCameraWriteRequest
            {
                WorldPos = root.TransformPoint(holder.LocalPos),
                WorldRot = root.rotation * holder.LocalRot,
                Fov = holder.Fov,
                FocusDistance = holder.FocusDistance,
                Aperture = holder.Aperture,
                FocalLength = holder.FocalLength,
                Weight = holder.GameBlendWeight,
                Focus = holder.Focus,
            };

            var applied = applier.Submit(in request, handle);
            if (!applied)
            {
                // 検出 3([26] §4.6.5) — 前回の Submit が LateUpdate で消費されなかった
                // (Applier が無効化/破棄された等)。このプレイバックは BlendOut 無しで終了する。
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning($"[DDrive] Cutscene '{instance.Data.DisplayName}': DDriveCutsceneCameraApplier が LateUpdate で実行されていません。カメラ演出を終了します([26_timeline.md] §4.6.5 検出3)。");
#endif
                ReleaseCameraOwnership();
                instance.CameraOwnershipEnded = true;
            }
        }

        // 所有権を手放し、控えていた画角・ピントを書き戻す([26] §4.6.2 の終了処理)。
        private void ReleaseCameraOwnership()
        {
            _cameraApplier?.Restore();
            _cameraOwner = null;
            _cameraApplier = null;
            _cameraOwnerCamera = null;
        }

        private void Complete(Handle<CutsceneMarker> handle, CutsceneInstance instance)
        {
            instance.Done = true;
            ReleaseInputLockIfNeeded(instance);
            _events.Fire(instance.EventCtx, EventTrigger.OnDisable);
            _events.Fire(instance.EventCtx, EventTrigger.OnDestroy);
            _events.End(instance.EventCtx);
            instance.CompletedSubject.OnNext(Unit.Default);
            instance.Waiter?.TrySetResult();
            Cleanup(handle, instance);
        }

        private void Cleanup(Handle<CutsceneMarker> handle, CutsceneInstance instance)
        {
            // [26_timeline.md] §4.6.2(6-10b) — Cancel()/Skip(Immediate) は Tick() の次の巡回を待たずに
            // ここへ来るため、カメラ所有権の解放(Volume weight=0、画角・ピントの書き戻し)もここで行う。
            if (_cameraOwner == instance)
            {
                ReleaseCameraOwnership();
            }

            _active.Remove(handle);
            _instances.Remove(handle);

            for (var i = 0; i < instance.SpawnedModels.Count; i++)
            {
                _models?.Despawn(instance.SpawnedModels[i]);
            }

            ReturnDirector(instance.Slot);

            if (instance.HandleNetKey != 0)
            {
                _networkedHandles.Remove(instance.HandleNetKey);
                if (_netBridge != null && _netBridge.IsServer)
                {
                    _activeNetworked.Remove(instance.HandleNetKey);
                }
            }

            instance.CompletedSubject.Dispose();
            instance.CancelledSubject.Dispose();
            instance.MarkerSubject.Dispose();
        }

        public void OnPause(PauseChannel channel, bool paused)
        {
            for (var i = 0; i < _active.Count; i++)
            {
                if (!_instances.TryGet(_active[i], out var instance))
                {
                    continue;
                }

                if (instance.Data.Flags.Pause != PauseMode.PauseWithGame)
                {
                    continue;
                }

                instance.Paused = paused;
            }
        }

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
    }
}
