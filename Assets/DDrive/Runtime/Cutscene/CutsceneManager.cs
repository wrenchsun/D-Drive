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
        }

        // [26] §4.5「PlayableDirector は Pool から借用」の実装。PoolService はプレハブの Instantiate を前提に
        // しており、CutsceneRoot には元になる Prefab アセットが無いため、専用の軽量な free-list で代替する
        // (意図(使い捨てない・再利用する)は同じ。カットシーンは頻度・同時数が小さいためこれで十分)。
        private sealed class CutsceneDirectorSlot
        {
            public GameObject Root;
            public PlayableDirector Director;
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

        private readonly EventBus _events = new();
        private int _lockDepth;
        private readonly Subject<bool> _inputLockChangedSubject = new();

        private readonly Dictionary<uint, Handle<CutsceneMarker>> _networkedHandles = new();
        private readonly Dictionary<uint, ActiveNetworkedEntry> _activeNetworked = new();
        private readonly uint _instanceSalt;
        private uint _nextLocalSeq;

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
            return new CutsceneDirectorSlot { Root = go, Director = director };
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

        private void OnReceivePlayMsg(ulong senderId, CutscenePlayMsg msg) => OnReceivePlayMsgInternal(senderId, msg);

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

        private void OnReceiveSeekMsg(ulong senderId, CutsceneSeekMsg msg) => OnReceiveSeekMsgInternal(senderId, msg);

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

        private void OnReceiveCancelMsg(ulong senderId, CutsceneCancelMsg msg) => OnReceiveCancelMsgInternal(senderId, msg);

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

        public void Cancel(Handle<CutsceneMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance) || instance.Done)
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
                // 6-10b の D-Drive Signal マーカーが無いうちは名前解決ができないため、Immediate と同じ
                // (末尾まで飛ばす)にフォールバックする。TODO(6-10b): マーカー実装後にここを接続する。
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (_skipWarned.Add(instance.Data))
                {
                    Debug.LogWarning($"[DDrive] Cutscene '{instance.Data.DisplayName}': Skip=ToMarker はまだ未対応です(6-10b で D-Drive Signal マーカーを実装後に対応)。末尾へ飛ばします。");
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

                _events.Tick(instance.EventCtx, dt);

                if (!instance.Done && instance.Elapsed >= instance.Duration)
                {
                    Complete(handle, instance);
                }
            }
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
