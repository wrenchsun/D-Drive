using System;
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
using DDrive.Runtime.Cutscene;
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
    // CameraFx/Haptics/Cutscene/AnchorGroupPlayer)へ委譲するだけ。Timeline は 6-10a で CutsceneManager に、
    // AnchorGroup(配置セット)は [22_anchor_group.md] §5 の予告どおり AnchorGroupPlayer に接続済み
    // (未配線時は警告 1 回 + no-op で継続)。
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
            // [26_timeline.md] §6(6-10a) — TrackKind.Timeline が委譲する CutsceneManager の Handle。
            public List<(int track, Handle<CutsceneMarker> handle)> FiredCutscene;
            // [22_anchor_group.md] §5 — TrackKind.AnchorGroup が委譲する AnchorGroupPlayer の Handle。
            public List<(int track, Handle<AnchorGroupMarker> handle)> FiredAnchorGroup;

            // ── [14_networking.md] §5(5-8/5-9) ネット関連の付帯情報 ──
            // HandleNetKey!=0 のとき「ネットワーク経路(Cosmetic)を通った Instance」であることを示す
            // (予測再生・確定受信・単純な自分の Broadcast 待ちのいずれも含む)。0 は完全ローカル。
            public uint HandleNetKey;
            public bool IsNetworked;
            public ushort Seed;

            // true は「PresentationPlayMsg を受信して生成した(=予測再生によるローカル直接生成ではない)」
            // Instance であることを示す。SelfNetId/TargetNetId は解決できたときだけ実値で送られる(6-0、[14] §4)。
            // HapticsData.LocalPlayerOnly=true な Haptic トラックは、解決できて「自分の Self/Target」と判定できた
            // 場合だけ再生し、それ以外(未解決 or 自分ではない)は誤発火防止のため安全側に倒して再生しない
            // (オーケストレーターの追加指示、2026-09-14 → 6-0 で NetId 解決を実装)。
            public bool PlayedViaNetworkReceive;

            // [14_networking.md] §5(N-4、2026-09-22) — ネット受信(PlayedViaNetworkReceive=true)の Instance に
            // 限り、元の PresentationPlayMsg.SelfNetId/TargetNetId をそのまま保持する(0 = 未解決)。
            // IsParticipant()/FireHaptic の LocalPlayerOnly 判定が共通の解決経路として使う。予測再生
            // (PlayedViaNetworkReceive=false)の Instance では設定しない(既定 0 のままで未使用)。
            public ulong SelfNetId;
            public ulong TargetNetId;
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
        // [26_timeline.md] §6(6-10a) — TrackKind.Timeline の委譲先。null なら未実装時と同じ警告 1 回 + no-op。
        private readonly CutsceneManager _cutscene;
        // [22_anchor_group.md] §5 — TrackKind.AnchorGroup の委譲先。null なら未配線時と同じ警告 1 回 + no-op。
        private readonly AnchorGroupPlayer _groups;

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

        // [14_networking.md] §5(6-0 修正3、実機確認で発見した課題3) — 接続直後のスナップショット受信が
        // Registry のカタログ登録完了より先に処理されると、まだ登録されていない PresId が Placeholder に
        // 解決されてしまう。既定は true(既存の全テスト/シングルプレイは Bootstrap を経由しないため常に
        // ready のまま今までどおり即時処理される)。DDriveRuntimeBootstrap だけが構築直後に false → カタログ
        // 登録完了後に true を明示的に呼ぶ。false の間は受信した Play/Signal/Cancel を到着順にキューへ保留する。
        private bool _registryReady = true;

        private enum PendingNetMessageKind
        {
            Play,
            Signal,
            Cancel,
        }

        private struct PendingNetMessage
        {
            public PendingNetMessageKind Kind;
            public ulong SenderId;
            public PresentationPlayMsg Play;
            public PresentationSignalMsg Signal;
            public PresentationCancelMsg Cancel;
        }

        private readonly Queue<PendingNetMessage> _pendingNetMessages = new();

        // [11_tasks.md] 6-0 修正2(実機確認で発見した課題2) — NetCheck 等の確認ツールが「ネットワーク受信で
        // 新規生成された Instance」の OnTrackFired を購読できるようにする最小限のフック。ゲームコードは
        // 通常 Play() の戻り値の Handle を使うため、このイベントは開発/確認ツール専用(定常経路のゲーム
        // ロジックからは購読しない想定。誰も購読していなければ delegate 呼び出し自体が発生しないため
        // 0 alloc を保つ)。
        public event Action<Handle<PresentationMarker>, uint> OnNetworkReceivedPlay;

        // [22_anchor_group.md] §5 — 開発/確認ツール(エディタの統合プレビュー等)専用。TrackKind.AnchorGroup が
        // 実際に再生を開始した(AnchorGroupPlayer.IsPlaying==true)瞬間に、その Handle を通知する。
        // ScenePresentationPreviewDriver がこれを購読し、AnimDriver.AdoptGroupVfx と同じ考え方で
        // 「配置セットが出した VFX」を SceneVfxPreviewDriver へ Adopt する(EditMode の手動 Simulate 対象にする、
        // [08_presentation.md] 実装メモ参照)。ゲームロジックからの購読は想定していない(誰も購読していなければ
        // delegate 呼び出し自体発生しないため、定常経路の 0 alloc 原則は保たれる。OnNetworkReceivedPlay と同じ設計)。
        public event Action<Handle<AnchorGroupMarker>> OnAnchorGroupPlayed;

        // [11_tasks.md] 6-0 修正4(実機確認で発見した課題4) — 未知の HandleNetKey(対象の演出が既に完了して
        // 台帳から外れた場合を含む)で Signal/Cancel を受信して破棄したことを、開発ビルドでは 1 キーにつき
        // 1 回だけログに出す(NetCheck の判定で「送信数 == 破棄数」を数えられるようにするため)。
        private readonly HashSet<uint> _unknownKeyDiscardWarned = new();

        // [14_networking.md] §9(6-6, K3 修正) — 実機確認 v4([docs/29] §12)で「通信が数秒止まってまとめて
        // 届いた区間で PresentationSignalMsg が対応する PresentationPlayMsg より先に処理され、未知のキーと
        // して破棄される」実バグが見つかった。真因は NgoNetBridge のアプリ層遅延キューが FIFO を保証して
        // いなかったこと(NgoNetBridge.cs 側の Queue<T> 化で修正済み)だが、Late Join・再送・将来の他
        // INetBridge 実装でも同種の順序崩れは起こりうるため、受信側にも防波堤を置く: 未知キーの
        // Signal/Cancel は即座に破棄せず、固定長リングバッファへ短時間(_pendingUnknownKeyHoldSec、既定
        // remoteOneShotGraceSec の 2 倍 = 1.0 秒)保留し、同じ key の Play が到着したら Play の生成直後に
        // 適用する。保留期限が切れたものは Tick() で従来どおりの破棄ログを出す。定常経路(Tick)での
        // alloc を避けるため、固定長 struct 配列を事前確保して使う(Queue<T> ではなく配列 + InUse フラグ)。
        private const int PendingUnknownKeyCapacity = 16;

        private struct PendingUnknownKeyEntry
        {
            public bool InUse;
            public bool IsCancel; // false=Signal, true=Cancel
            public ulong SenderId;
            public uint HandleNetKey;
            public ushort SignalKeyHash; // Signal のみ使用
            public uint InsertSeq; // 到着順の復元用(配列インデックスは再利用されるため挿入順とは限らない)
            public double ExpireAtNetworkTime;
        }

        private readonly PendingUnknownKeyEntry[] _pendingUnknownKeyMessages = new PendingUnknownKeyEntry[PendingUnknownKeyCapacity];
        private int _pendingUnknownKeyCount;
        private uint _pendingUnknownKeySeq;
        private readonly float _pendingUnknownKeyHoldSec;

        // [14_networking.md] §9/§10(6-6) — PresentationSignalMsg/PresentationCancelMsg のクライアント別
        // レート制限。NgoNetBridge.RequestBroadcastRpc の中継レート制限(60/秒/クライアント、[14] §9)は
        // 「Broadcast() 経由の全メッセージ種別の合算」であり、Presentation の Signal/Cancel だけを狙った
        // 高頻度送信を個別に制限できない。トランスポート実装(INetBridge)に依存せず Presentation 側でも
        // 同じ既定値(60/秒/クライアント)で受信検証する(§9「クライアント発の中継は無条件に行わない」の
        // 受信側版)。Host(TrustedRelayClientId)自身が発行した Signal/Cancel は対象外(権威側の操作)。
        private const int SignalCancelRateLimitPerSecond = 60;

        private struct RateBudget
        {
            public double WindowStart;
            public int Count;
        }

        private readonly Dictionary<ulong, RateBudget> _signalCancelBudgets = new();

        // [14_networking.md] §5(6-0 修正6、実機確認 v2 で発見した実バグの修正) — 遅延のある環境では
        // Client の ServerTime 推定が Host より遅れて見える(実機確認で約 80ms 観測)ため、
        // `elapsed = NetworkTime - StartNetTime` が正の値になり、Time=0 のワンショット(Vfx/Se 等)が
        // 「もう過ぎたトラック」としてリモートでは常にスキップされ、一切描画/再生されなかった。
        // 猶予秒(既定 0.5s)以内の遅れなら「遅れて届いただけ」として鳴らし、それより古い(Late Join で
        // 復元しようとしている等)ものだけ従来どおりスキップする。シリアライズフィールドは増やさず、
        // コンストラクタの任意引数として渡す(既定値 0.5f。要判断: 具体的な秒数は docs/31 参照)。
        private readonly float _remoteOneShotGraceSec;

        // [14_networking.md] §5(6-0 修正6) — 確認ツール(NetCheckRunner)専用。猶予を超えて実際にスキップした
        // ワンショットトラックを通知する(誰も購読していなければ delegate 呼び出し自体発生しないため
        // 定常経路への 0 alloc の原則は保たれる)。
        public event Action<PresentationTrack, uint, float> OnRemoteOneShotSkipped;

        // [14_networking.md] §5(6-0 修正6) — 確認ツール(NetCheckRunner)専用。TrackTrigger.AtTime のトラックが
        // 実際に FireTrack へ委譲された(=発火した)瞬間に、その時点の Elapsed とともに通知する(0 alloc の
        // 原則は上と同じ)。Manager 全体で 1 つの event にしているのは、Handle 単位の `OnTrackFired(handle)`
        // (既存の R3 Observable)を Play() 呼び出し後に購読する方式だと、Play() 自身が同期的に
        // Time=0 のトラックを発火させてしまうため「購読する前に発火が終わっている」タイミング問題が
        // あるため(実際に NetCheckRunner で踏んだ。要判断ではなく実装上の必然)。
        public event Action<PresentationTrack, uint, float> OnAtTimeTrackFired;

        // [14_networking.md] §9(6-0, P1-1/P1-2 レビュー対応) — HandleNetKey の上位 8bit に発行者(LocalClientId
        // の下位 8bit)を埋め込む。MS2026/NGO の LAN 1v1 前提(実クライアント数は極少数)では 8bit(256 通り)で
        // 十分。下位 24bit は従来どおりの salt/連番/NetworkTime 混合。
        private const int HandleNetKeyIssuerBits = 8;
        private const uint HandleNetKeyIssuerMask = 0xFFu;
        private const uint HandleNetKeyLowerMask = 0x00FFFFFFu;

        // NGO の NetworkManager.ServerClientId は常に 0(PrefabsManager.ServerClientId と同じ規約、[14] §12)。
        // Host は「発行者に関わらず中継/上書きしてよい」信頼された送信元として扱う(Host からの Signal/Cancel/
        // Late-Join 再送は元の行為者が誰であっても正規の権威操作のため)。NGO の SenderClientId はトランスポートが
        // 付与する値でクライアントが偽装できない([14] §2)ため、この定数と一致するには実際に Host である必要がある。
        private const ulong TrustedRelayClientId = 0UL;

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
            INetBridge netBridge = null,
            float remoteOneShotGraceSec = 0.5f,
            CutsceneManager cutscene = null,
            AnchorGroupPlayer groups = null)
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
            _cutscene = cutscene;
            _groups = groups;
            _remoteOneShotGraceSec = Mathf.Max(0f, remoteOneShotGraceSec);
            // 6-6(K3 修正) — 「例 1 秒、または remoteOneShotGraceSec の 2 倍」(要求どおり)。既定の
            // remoteOneShotGraceSec=0.5s なら 1.0s になる。シリアライズフィールドは増やさない
            // (_remoteOneShotGraceSec と同じ方針)。
            _pendingUnknownKeyHoldSec = _remoteOneShotGraceSec * 2f;
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
            bool playedViaNetworkReceive,
            ulong selfNetId = 0UL,
            ulong targetNetId = 0UL)
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
                FiredCutscene = new List<(int, Handle<CutsceneMarker>)>(),
                FiredAnchorGroup = new List<(int, Handle<AnchorGroupMarker>)>(),
                HandleNetKey = handleNetKey,
                IsNetworked = isNetworked,
                PlayedViaNetworkReceive = playedViaNetworkReceive,
                Seed = seed,
                SelfNetId = selfNetId,
                TargetNetId = targetNetId,
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

            // [14_networking.md] §9(6-6, K3 修正) — この Play より先に届いていた同じ key の Signal/Cancel
            // (保留中)を、Instance が完全に(_active/_instances 両方に)登録された直後に適用する。
            // SeekInitialTracks/_active.Add より前に適用すると、保留中の Cancel が CancelInternal→Cleanup
            // で _instances/_active から即座に取り除いてしまい、その後の SeekInitialTracks/_active.Add が
            // 矛盾した状態(Cleanup 済みの handle を _active に追加してしまう等)を作ってしまうため、
            // 順序を厳守する。
            if (handleNetKey != 0 && _pendingUnknownKeyCount > 0)
            {
                FlushPendingUnknownKey(handleNetKey, handle, instance);
            }

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

                // P2-3(6-0 レビュー対応) — Host が PredictLocal で行為者になった場合、自分の Broadcast の
                // 確定エコーを待たずに即座に台帳へ登録する。エコー到着前に別クライアントが接続してくると
                // Late Join のスナップショット送信対象から漏れていた(OnClientConnected は台帳しか見ないため)。
                if (_netBridge.IsServer)
                {
                    RegisterActiveIfServer(handleNetKey, data, ctx, startNetTime, seed);
                }
            }

            // [14_networking.md] §4/§5(6-0, C) — SelfNetId/TargetNetId を実際に解決できる場合は実値で送る
            // (NGO の NetworkObject を持つ Transform のみ。解決できなければ 0 を送り、受信側は既存のとおり
            // Position にフォールバックする)。
            var selfNetId = _netBridge.ResolveNetId(ctx.Self);
            var targetNetId = _netBridge.ResolveNetId(ctx.Target);

            _netBridge.Broadcast(new PresentationPlayMsg
            {
                PresId = data.Id,
                SelfNetId = selfNetId,
                TargetNetId = targetNetId,
                Position = ctx.Position,
                StartNetTime = startNetTime,
                Seed = seed,
                HandleNetKey = handleNetKey,
            }, NetChannel.ReliableOrdered);

            return predicted;
        }

        // [14_networking.md] §9(6-0) — HandleNetKey の上位 8bit(発行者)を取り出す。
        private static uint IssuerOf(uint handleNetKey) => (handleNetKey >> (32 - HandleNetKeyIssuerBits)) & HandleNetKeyIssuerMask;

        // senderId(トランスポートが付与する実際の送信元。NGO ではクライアントが偽装不能)が、HandleNetKey の
        // 発行者と一致するか、あるいは Host(TrustedRelayClientId)からの正規の中継/再送かを検証する([14] §9)。
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

        // 6-0 修正4 — 開発ビルドのみ。同じ HandleNetKey での重複ログを避けるため 1 回だけ出す
        // (NetCheckRunner が forged_cancel_sent と同じ回数だけこれを数えられるようにする狙い)。
        private void WarnUnknownKeyDiscardedOnce(uint handleNetKey, string messageTypeName)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (!_unknownKeyDiscardWarned.Add(handleNetKey))
            {
                return;
            }

            var prefix = _netBridge != null && _netBridge.IsServer ? "[Net/Host]" : "[Net/Client]";
            Debug.LogWarning($"{prefix} Presentation: {messageTypeName}(HandleNetKey=0x{handleNetKey:X8}) は未知のキー、または対象の演出が既に完了しているため破棄しました。");
#endif
        }

        // [14_networking.md] §9(6-6) — PresentationSignalMsg/PresentationCancelMsg 専用のクライアント別
        // レート制限(既定 60/秒/クライアント、NgoNetBridge.ConsumeRelayBudget と同じ考え方)。Host
        // (TrustedRelayClientId)自身は対象外。
        private bool ConsumeSignalCancelBudget(ulong senderId)
        {
            if (senderId == TrustedRelayClientId)
            {
                return true;
            }

            var now = _netBridge != null ? _netBridge.NetworkTime : 0d;
            _signalCancelBudgets.TryGetValue(senderId, out var budget);

            if (now - budget.WindowStart >= 1d)
            {
                budget.WindowStart = now;
                budget.Count = 0;
            }

            budget.Count++;
            _signalCancelBudgets[senderId] = budget;
            return budget.Count <= SignalCancelRateLimitPerSecond;
        }

        // [14_networking.md] §9(6-6, K3 修正) — 未知の HandleNetKey の Signal/Cancel を固定長リングバッファへ
        // 保留する(_pendingUnknownKeyHoldSec 経過で Tick() が期限切れとして従来どおりの破棄ログを出す)。
        // 定常経路(受信は Tick 相当の頻度になりうる)での alloc を避けるため、事前確保した配列を使い回す
        // (満杯のときは最も古いエントリ(ExpireAtNetworkTime が最小)を上書きする。無制限に貯め込まない
        // ための上限であり、フラッド対策としては ConsumeSignalCancelBudget の方が主防波堤)。
        private void HoldUnknownKeyMessage(ulong senderId, uint handleNetKey, bool isCancel, ushort signalKeyHash)
        {
            var netTime = _netBridge != null ? _netBridge.NetworkTime : 0d;
            var slot = -1;

            for (var i = 0; i < _pendingUnknownKeyMessages.Length; i++)
            {
                if (!_pendingUnknownKeyMessages[i].InUse)
                {
                    slot = i;
                    break;
                }
            }

            if (slot == -1)
            {
                // 満杯: 最も期限が近い(=最も古い)エントリを退避させて上書きする。
                var oldestIndex = 0;
                var oldestExpire = _pendingUnknownKeyMessages[0].ExpireAtNetworkTime;
                for (var i = 1; i < _pendingUnknownKeyMessages.Length; i++)
                {
                    if (_pendingUnknownKeyMessages[i].ExpireAtNetworkTime < oldestExpire)
                    {
                        oldestExpire = _pendingUnknownKeyMessages[i].ExpireAtNetworkTime;
                        oldestIndex = i;
                    }
                }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
                var prefix = _netBridge != null && _netBridge.IsServer ? "[Net/Host]" : "[Net/Client]";
                Debug.LogWarning($"{prefix} Presentation: 未知キーの保留バッファ({PendingUnknownKeyCapacity}件)が満杯のため、最も古い HandleNetKey=0x{_pendingUnknownKeyMessages[oldestIndex].HandleNetKey:X8} を破棄しました。");
#endif
                slot = oldestIndex;
                _pendingUnknownKeyCount--; // 直後に ++ するため相殺(上書きなので総数は変わらない)
            }

            _pendingUnknownKeyMessages[slot] = new PendingUnknownKeyEntry
            {
                InUse = true,
                IsCancel = isCancel,
                SenderId = senderId,
                HandleNetKey = handleNetKey,
                SignalKeyHash = signalKeyHash,
                InsertSeq = _pendingUnknownKeySeq++,
                ExpireAtNetworkTime = netTime + _pendingUnknownKeyHoldSec,
            };
            _pendingUnknownKeyCount++;
        }

        // [14_networking.md] §9(6-6, K3 修正) — 対応する Play が到着した直後(PlayLocalInternal 内)に
        // 呼ばれる。保留中の同じ HandleNetKey のエントリを到着順(InsertSeq 昇順)に適用してから解放する。
        // Tick() 同様に定常経路(Play() 呼び出し)からのクロージャ/alloc を避けるため、配列を直接走査する
        // 単純な選択方式(容量 16 なので O(n^2) でも無視できるコスト)にした。
        private void FlushPendingUnknownKey(uint handleNetKey, Handle<PresentationMarker> handle, PresentationInstance instance)
        {
            while (true)
            {
                var bestIndex = -1;
                var bestSeq = 0u;

                for (var i = 0; i < _pendingUnknownKeyMessages.Length; i++)
                {
                    if (!_pendingUnknownKeyMessages[i].InUse || _pendingUnknownKeyMessages[i].HandleNetKey != handleNetKey)
                    {
                        continue;
                    }

                    if (bestIndex == -1 || _pendingUnknownKeyMessages[i].InsertSeq < bestSeq)
                    {
                        bestIndex = i;
                        bestSeq = _pendingUnknownKeyMessages[i].InsertSeq;
                    }
                }

                if (bestIndex == -1)
                {
                    break;
                }

                var senderId = _pendingUnknownKeyMessages[bestIndex].SenderId;
                var isCancel = _pendingUnknownKeyMessages[bestIndex].IsCancel;
                var signalKeyHash = _pendingUnknownKeyMessages[bestIndex].SignalKeyHash;
                _pendingUnknownKeyMessages[bestIndex].InUse = false;
                _pendingUnknownKeyCount--;

                // 要求どおり、保留解除時にも発行者検証を再実施する(静的なビット演算なので受信時と結果は
                // 変わらないはずだが、防御的に再チェックする)。
                if (!IsAuthorizedSender(senderId, handleNetKey))
                {
                    continue;
                }

                // [11_tasks.md] 6-7 — K3(保留→適用)が実際に効いたことを、開発ビルドのみログで残す
                // ([docs/29] §13 の「保留していた Signal を適用したことが間接的に確認できること」への
                // 対応。効果自体〔HitStop/CameraShake 等の再発火〕は既存の signal_recv/track_fired 経路で
                // 分かるが、明示的な 1 行があると NetCheckRunner/外部スクリプトが機械的に検出しやすい)。
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                var prefix = _netBridge != null && _netBridge.IsServer ? "[Net/Host]" : "[Net/Client]";
                Debug.Log($"{prefix} Presentation: 保留していた HandleNetKey=0x{handleNetKey:X8} の{(isCancel ? "Cancel" : "Signal")}を Play 到着後に適用しました。(pending_applied=1)");
#endif

                if (isCancel)
                {
                    if (!instance.Done)
                    {
                        ApplyCancelIfInterruptible(handle, instance);
                    }
                }
                else if (!instance.Done)
                {
                    ApplySignal(handle, instance, signalKeyHash);
                }
            }
        }

        // [14_networking.md] §9(6-6, K3 修正) — Tick() から呼ばれる期限切れの掃除。期限切れになった
        // エントリは従来どおりの破棄ログ(WarnUnknownKeyDiscardedOnce)を出す。
        private void SweepExpiredPendingUnknownKey()
        {
            var netTime = _netBridge != null ? _netBridge.NetworkTime : 0d;

            for (var i = 0; i < _pendingUnknownKeyMessages.Length; i++)
            {
                if (!_pendingUnknownKeyMessages[i].InUse || _pendingUnknownKeyMessages[i].ExpireAtNetworkTime > netTime)
                {
                    continue;
                }

                var handleNetKey = _pendingUnknownKeyMessages[i].HandleNetKey;
                var isCancel = _pendingUnknownKeyMessages[i].IsCancel;
                _pendingUnknownKeyMessages[i].InUse = false;
                _pendingUnknownKeyCount--;

                WarnUnknownKeyDiscardedOnce(handleNetKey, isCancel ? "PresentationCancelMsg" : "PresentationSignalMsg");
            }
        }

        // [11_tasks.md] 6-0 修正3 — Registry が ready になるまで受信順にキューへ保留する。
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
                    case PendingNetMessageKind.Signal:
                        OnReceiveSignalMsgInternal(pending.SenderId, pending.Signal);
                        break;
                    case PendingNetMessageKind.Cancel:
                        OnReceiveCancelMsgInternal(pending.SenderId, pending.Cancel);
                        break;
                }
            }
        }

        // テスト/デバッグ専用: 指定 Handle の HandleNetKey を返す(0 = ネット非経由、または無効な Handle)。
        // NetCheckRunner が signal_fire/signal_recv ログの識別子として使う(6-0 修正2)。
        public uint DebugHandleNetKeyOf(Handle<PresentationMarker> handle)
            => TryGetInstanceSilent(handle, out var instance) ? instance.HandleNetKey : 0u;

        private void OnReceivePlayMsg(ulong senderId, PresentationPlayMsg msg)
        {
            if (!_registryReady)
            {
                _pendingNetMessages.Enqueue(new PendingNetMessage { Kind = PendingNetMessageKind.Play, SenderId = senderId, Play = msg });
                return;
            }

            OnReceivePlayMsgInternal(senderId, msg);
        }

        private void OnReceivePlayMsgInternal(ulong senderId, PresentationPlayMsg msg)
        {
            // P1-1/P1-2(6-0 レビュー対応) — 発行者検証。不一致・(0 の HandleNetKey は元々発生しないが)未知の
            // 発行者は破棄する。これにより改造 Client が他人の HandleNetKey を騙って PresId/StartNetTime/Seed を
            // 上書きする攻撃(A の台帳エントリが B の値で上書きされる)を防ぐ。
            if (!IsAuthorizedSender(senderId, msg.HandleNetKey))
            {
                Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Presentation: PresentationPlayMsg(HandleNetKey=0x{msg.HandleNetKey:X8}) の送信元 ClientId({senderId}) が発行者と一致しないため破棄しました。");
                return;
            }

            // 予測再生済み(または既にこの受信ハンドラで生成済み)の確定通知。二重生成しない([14] §5)。
            if (_networkedHandles.TryGetValue(msg.HandleNetKey, out var existingHandle) && _instances.TryGet(existingHandle, out var existingInstance))
            {
                // P1-2: 既存エントリと PresId が異なる = 同じ HandleNetKey を騙った別演出の上書き試行。破棄する。
                if (existingInstance.Data.Id != msg.PresId)
                {
                    Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Presentation: PresentationPlayMsg(HandleNetKey=0x{msg.HandleNetKey:X8}) の PresId が既存エントリと一致しないため破棄しました。");
                    return;
                }

                RegisterActiveIfServer(msg.HandleNetKey, existingInstance.Data, existingInstance.Ctx, msg.StartNetTime, msg.Seed);
                return;
            }

            // [14_networking.md] §9(6-6) — 「ID 存在検証: 受信 ID が Registry に無い → 破棄 + ログ
            // (Placeholder はローカル開発時のみ。ネット受信では出さない)」を実装する。以前は
            // ResolveOrPlaceholder を無条件に呼んでいたため、未登録(または種別が Presentation でない
            // =範囲外)PresId でも Placeholder(尺 0 秒)の Instance が実際に生成されていた。IsRegistered は
            // OnPlaceholderUsed を発火しない副作用なしの確認なので、ここで先に判定してから破棄する。
            if (!_registry.IsRegistered(msg.PresId, AssetType.Presentation))
            {
                Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Presentation: PresentationPlayMsg(PresId=0x{msg.PresId:X}) は未登録、または種別が Presentation ではないため破棄しました。");
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

            var handle = PlayLocalInternal(data, in ctx, elapsedSeek: elapsed, seed: msg.Seed, handleNetKey: msg.HandleNetKey, isNetworked: true, playedViaNetworkReceive: true, selfNetId: msg.SelfNetId, targetNetId: msg.TargetNetId);

            if (_instances.TryGet(handle, out var instance))
            {
                RegisterActiveIfServer(msg.HandleNetKey, instance.Data, instance.Ctx, msg.StartNetTime, msg.Seed);
                // 6-0 修正2 — このブランチは「ネット受信で新規に生成された Instance」の場合だけ通る
                // (既に予測再生/受信済みだった場合は上の existingHandle 分岐で早期 return している)。
                OnNetworkReceivedPlay?.Invoke(handle, msg.HandleNetKey);
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

            // P2-4(6-0 レビュー対応) — 自分自身の接続(Host が自分の OnClientConnectedCallback を受け取る
            // ケース)は早期 return する(自分に送っても意味がない)。
            if (clientId == _netBridge.LocalClientId)
            {
                return;
            }

            // P2-4 — 台帳をスナップショット(配列)してから送る。SendTo が LocalLoopback 経由で同期的に
            // 配送される場合、送信先の受信処理が(理論上)台帳を書き換える可能性があるため、foreach 中の
            // Dictionary を直接列挙しない。
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
                _netBridge.SendTo(clientId, new PresentationPlayMsg
                {
                    PresId = entry.Data.Id,
                    SelfNetId = 0,
                    TargetNetId = 0,
                    Position = entry.Ctx.Position,
                    StartNetTime = entry.StartNetTime,
                    Seed = entry.Seed,
                    HandleNetKey = keys[j],
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
                var lower = ((mixed ^ _instanceSalt) + _nextLocalSeq) & HandleNetKeyLowerMask;

                // [14_networking.md] §9(6-0, P1-2 対応) — 上位 8bit に発行者(LocalClientId の下位 8bit)を
                // 埋め込む。以後の Signal/Cancel/Play 受信検証(IsAuthorizedSender)がこれを使う。
                var clientId = _netBridge != null ? _netBridge.LocalClientId : 0UL;
                var issuerBits = ((uint)clientId & HandleNetKeyIssuerMask) << (32 - HandleNetKeyIssuerBits);
                var key = issuerBits | lower;
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
            if (!_registryReady)
            {
                _pendingNetMessages.Enqueue(new PendingNetMessage { Kind = PendingNetMessageKind.Signal, SenderId = senderId, Signal = msg });
                return;
            }

            OnReceiveSignalMsgInternal(senderId, msg);
        }

        private void OnReceiveSignalMsgInternal(ulong senderId, PresentationSignalMsg msg)
        {
            // P1-1(6-0 レビュー対応) — 発行者(または Host)以外からの Signal は破棄する
            // (改造 Client が他人の演出の HandleNetKey を騙って Signal できないようにする)。
            if (!IsAuthorizedSender(senderId, msg.HandleNetKey))
            {
                Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Presentation: PresentationSignalMsg(HandleNetKey=0x{msg.HandleNetKey:X8}) の送信元 ClientId({senderId}) が発行者と一致しないため破棄しました。");
                return;
            }

            // [14_networking.md] §9(6-6) — Broadcast() 経由の中継レート制限(NgoNetBridge、全種別合算)とは
            // 別に、Signal/Cancel 単体でも同じ既定値(60/秒/クライアント)で受信検証する。
            if (!ConsumeSignalCancelBudget(senderId))
            {
                Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Presentation: Client {senderId} からの PresentationSignalMsg がレート制限({SignalCancelRateLimitPerSecond}/秒)を超えたため破棄しました。");
                return;
            }

            if (!_networkedHandles.TryGetValue(msg.HandleNetKey, out var handle) || !_instances.TryGet(handle, out var instance))
            {
                // [14_networking.md] §9(6-6, K3 修正) — Play より先に届いた可能性があるため即座に破棄せず
                // 短時間保留する(Tick() で期限切れになったら従来どおり破棄ログを出す)。
                HoldUnknownKeyMessage(senderId, msg.HandleNetKey, isCancel: false, msg.SignalKeyHash);
                return;
            }

            ApplySignal(handle, instance, msg.SignalKeyHash);
        }

        // OnReceiveSignalMsgInternal と FlushPendingUnknownKey(K3 修正、Play 後着で保留分を適用する経路)の
        // 両方から呼ばれる共通処理。
        private void ApplySignal(Handle<PresentationMarker> handle, PresentationInstance instance, ushort signalKeyHash)
        {
            var tracks = instance.Data.Tracks;
            if (tracks == null)
            {
                return;
            }

            for (var t = 0; t < tracks.Length; t++)
            {
                if (instance.Fired[t] || tracks[t].Trigger != TrackTrigger.OnSignal || HashSignalKey(tracks[t].SignalKey) != signalKeyHash)
                {
                    continue;
                }

                FireTrack(handle, instance, t, in tracks[t]);
            }
        }

        private void OnReceiveCancelMsg(ulong senderId, PresentationCancelMsg msg)
        {
            if (!_registryReady)
            {
                _pendingNetMessages.Enqueue(new PendingNetMessage { Kind = PendingNetMessageKind.Cancel, SenderId = senderId, Cancel = msg });
                return;
            }

            OnReceiveCancelMsgInternal(senderId, msg);
        }

        private void OnReceiveCancelMsgInternal(ulong senderId, PresentationCancelMsg msg)
        {
            // P1-1(6-0 レビュー対応) — 発行者(または Host)以外からの Cancel は破棄する。
            if (!IsAuthorizedSender(senderId, msg.HandleNetKey))
            {
                Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Presentation: PresentationCancelMsg(HandleNetKey=0x{msg.HandleNetKey:X8}) の送信元 ClientId({senderId}) が発行者と一致しないため破棄しました。");
                return;
            }

            // [14_networking.md] §9(6-6) — Signal と同じ既定値(60/秒/クライアント)で受信検証する。
            if (!ConsumeSignalCancelBudget(senderId))
            {
                Debug.LogWarning($"[Net/{(_netBridge != null && _netBridge.IsServer ? "Host" : "Client")}] Presentation: Client {senderId} からの PresentationCancelMsg がレート制限({SignalCancelRateLimitPerSecond}/秒)を超えたため破棄しました。");
                return;
            }

            // 6-0 修正4(実機確認で発見した課題4) — 対象が見つからない(未知のキー、または対象の演出が
            // 既に完了して台帳から外れた)場合も、発行者不一致と同様に「破棄した」ことをログへ残す
            // (開発ビルドのみ、キーごとに1回)。NetCheck の判定で「送信数 == 破棄数」を数えられるようにする。
            if (!_networkedHandles.TryGetValue(msg.HandleNetKey, out var handle) || !_instances.TryGet(handle, out var instance) || instance.Done)
            {
                // [14_networking.md] §9(6-6, K3 修正) — Play より先に届いた可能性があるため即座に破棄せず
                // 短時間保留する(Tick() で期限切れになったら従来どおり破棄ログを出す)。対象が既に完了済み
                // (instance.Done)のケースは、Play() 直後の Flush(まだ Done になっていない)には引っかからず、
                // 保留期限切れで従来どおり破棄される(誤って生き返らせない、意図した挙動)。
                HoldUnknownKeyMessage(senderId, msg.HandleNetKey, isCancel: true, signalKeyHash: 0);
                return;
            }

            ApplyCancelIfInterruptible(handle, instance);
        }

        // OnReceiveCancelMsgInternal と FlushPendingUnknownKey(K3 修正)の両方から呼ばれる共通処理。
        private void ApplyCancelIfInterruptible(Handle<PresentationMarker> handle, PresentationInstance instance)
        {
            // P1-1(レビュー指摘の残り) — 公開 API Cancel() の入口は Interruptible=false を見て無視するが、
            // 受信 → CancelInternal 経路にはこのチェックが無かった。発行者検証を回避できない偽造 Cancel
            // でも、Interruptible=false な演出は依然止められないようにする。
            if (!instance.Data.Interruptible)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (_nonInterruptibleWarned.Add(instance.Data))
                {
                    Debug.LogWarning($"[DDrive] Presentation '{instance.Data.DisplayName}' は Interruptible=false のため受信した Cancel を無視しました。");
                }
#endif
                return;
            }

            CancelInternal(handle, instance);
        }

        // [M-1c、2026-09-25] 冪等操作なので TryGetQuiet で警告なしにガードする。
        public void Cancel(Handle<PresentationMarker> handle)
        {
            if (!_instances.TryGetQuiet(handle, out var instance) || instance.Done)
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

            for (var i = 0; i < instance.FiredCutscene.Count; i++)
            {
                var h = instance.FiredCutscene[i].handle;
                if (_cutscene != null && _cutscene.IsPlaying(h))
                {
                    _cutscene.Cancel(h);
                }
            }

            for (var i = 0; i < instance.FiredAnchorGroup.Count; i++)
            {
                var h = instance.FiredAnchorGroup[i].handle;
                if (_groups != null && _groups.IsPlaying(h))
                {
                    _groups.Stop(h);
                }
            }
        }

        // [14_networking.md] §5(6-0 修正7、実機確認 v3 で発見した実バグの修正) — 自分の接続が切れた
        // (Host との接続を失った)ときに、ネット経由で開始した Presentation を Interruptible に関係なく
        // 強制終了する(StopAll と同じ「Interruptible=false でも止める」扱い)。StopFiredForCancel が
        // StopOnCancel=true の Fired Vfx/Se/Anim 等を止める(通常の Cancel() と同じ規則。StopOnCancel=false
        // のトラックは対象外のまま。「それでもループ系 VFX が残る」設計上の広い論点は docs/31 の要判断に残す)。
        // ネット非経由(IsNetworked=false)の Instance には触れない([14] §1「netBridge==null は挙動を変えない」
        // 原則と対になる、通信していない演出は切断の影響を受けないという原則)。
        // 呼び出し元は DDriveRuntimeBootstrap(NgoNetBridge.ClientDisconnected を購読し、"自分視点の切断"
        // ─ Client が Host との接続を失った ─ のときだけ呼ぶ。Host 視点の「相手が抜けた」は対象外)。
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

        // [14_networking.md] §18(N-5、2026-09-24) — Host 引き継ぎ(同一プロセスで Stop → 別ロールで
        // 再 Start)向け。CancelAllNetworked() を内包しつつ、ネット由来の台帳・保留キュー・受信レート制限窓を
        // 初期状態へ戻す(HandleNetKey は発行時の LocalClientId を上位 8bit に埋めるため、役割変更後は
        // 新しい ClientId で発行され直す。台帳をクリアしておけば古い鍵が残らず IsAuthorizedSender に
        // 弾かれる経路自体が発生しない)。ローカル(IsNetworked=false)の Instance には触れない。
        // _registryReady はカタログ登録状態を表すフラグでネットワークの生死とは無関係のため変更しない。
        // 呼び出し元: DDriveRuntimeBootstrap.StopNetworking() / Client 視点の切断時(OnNetClientDisconnected)。
        public void ResetNetworkedState()
        {
            CancelAllNetworked();

            _networkedHandles.Clear();
            _activeNetworked.Clear();
            _pendingNetMessages.Clear();

            for (var i = 0; i < _pendingUnknownKeyMessages.Length; i++)
            {
                _pendingUnknownKeyMessages[i] = default;
            }

            _pendingUnknownKeyCount = 0;

            _signalCancelBudgets.Clear();
            _unknownKeyDiscardWarned.Clear();
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
            // [14_networking.md] §9(6-6, K3 修正) — 未知キー保留の期限切れ掃除。カウンタが 0 のときは
            // 配列を走査しない(0 alloc・実質 0 cost)。
            if (_pendingUnknownKeyCount > 0)
            {
                SweepExpiredPendingUnknownKey();
            }

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

        // public(InternalsVisibleTo 未設定のため、Editor 側の SceneView Anchor 表示
        // 〔PresentationTrackAnchorResolver、docs/08_presentation.md〕から同じ解決をコピペせずに再利用できるようにする。
        // UiButton.cs / SpecDiffService.cs と同じ理由)。
        public static Transform ResolveContextRoot(in PlayContext ctx, TrackTargetMode mode)
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

        // [14_networking.md] §5(N-4、2026-09-22) — 「SelfNetId/TargetNetId のどちらかが自分のプレイヤー
        // オブジェクトか」を解決する共有ロジック(FireHaptic の LocalPlayerOnly 判定と IsParticipant() の
        // 両方がここを通る。重複コード禁止)。未解決(bridge==null、netId==0、ResolveNetObject が null、
        // 自分の所有物でない)は false を返す(呼び出し元がそれぞれのポリシーで「未解決時どうするか」の
        // 安全側デフォルトを適用する。LocalPlayerOnly は false=鳴らさない、Scope は IsParticipant() 側で
        // true=全員発火に読み替える)。
        private static bool IsLocalParticipant(INetBridge bridge, ulong selfNetId, ulong targetNetId)
        {
            if (bridge == null)
            {
                return false;
            }

            if (selfNetId != 0)
            {
                var self = bridge.ResolveNetObject(selfNetId);
                if (self != null && bridge.IsLocalPlayerObject(self))
                {
                    return true;
                }
            }

            if (targetNetId != 0)
            {
                var target = bridge.ResolveNetObject(targetNetId);
                if (target != null && bridge.IsLocalPlayerObject(target))
                {
                    return true;
                }
            }

            return false;
        }

        // [14_networking.md] §5(N-4、2026-09-22) — PresentationTrack.Scope=ParticipantsOnly 用の当事者判定。
        // 純関数(0 alloc)。SelfNetId/TargetNetId が両方 0(未解決)のときは false ではなく true を返す
        // (安全側=従来どおり全員実行。Loopback/シングルプレイ〈netBridge==null で呼ばれることは無いが、
        // bridge==null が来ても同じ安全側〉でも挙動を変えない)。public static: EditMode テストから直接検証する。
        public static bool IsParticipant(INetBridge bridge, ulong selfNetId, ulong targetNetId)
        {
            if (selfNetId == 0 && targetNetId == 0)
            {
                return true;
            }

            return IsLocalParticipant(bridge, selfNetId, targetNetId);
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

        // [14_networking.md] §5 実装メモ(5-8/6-0 修正6) — Play() 直後の初回発火専用。Tick()/デバッグ用
        // Seek() では使わない(そちらは常に FireDueTracks で通常発火する。挙動を変えない)。
        // elapsedSeek==0(通常再生・予測再生・自分の Broadcast 待ち後の再生)のときは FireDueTracks と
        // 完全に同じ結果になる。elapsedSeek>0(ネット越しに遅れて届いた Play。§5「開始時刻シーク」、
        // Late Join のスナップショット再送も同じ経路を通る)のときは、継続(ループ)系は今から再生を
        // 開始する(位相の厳密な同期は Anim のみ実装。Bgm は BgmManager に Seek API が無いため頭から
        // 再生する。要判断は docs/28)。ワンショット(continuous でない AtTime トラック)は、
        // 「過ぎてからの遅れ」(elapsed - track.Time)が猶予(_remoteOneShotGraceSec、既定 0.5s)以内なら
        // 遅れて発火し(単に遅延ネットワークで少し遅れて届いただけと判断)、それより古い(Late Join で
        // 途中から復元しようとしている等、明らかに再生し直す意味が無い)ものだけ従来どおりスキップする
        // (6-0 実機確認 v2 で発見: 猶予が無かったため、遅延のある環境では開始直後のワンショット演出
        // 〈VFX/SE 等〉がリモートで一切発火しなかった)。
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
                    var lateBySec = elapsed - tracks[t].Time;
                    if (lateBySec > _remoteOneShotGraceSec)
                    {
                        instance.Fired[t] = true;
                        OnRemoteOneShotSkipped?.Invoke(tracks[t], instance.HandleNetKey, lateBySec);
                        continue;
                    }

                    // 猶予以内 — 「遅れて届いただけ」として下の FireTrack でそのまま発火する。
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
                    FireHitStop(instance, in track);
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
                    FireTimeline(instance, trackIndex, in track);
                    break;

                case TrackKind.AnchorGroup:
                    FireAnchorGroup(instance, trackIndex, in track);
                    break;
            }

            instance.TrackFiredSubject.OnNext(track);

            // [14_networking.md] §5(6-0 修正6) — 確認ツール専用の通知(0 alloc、購読者が無ければ何もしない)。
            // AtTime のみ(OnSignal/Marker 発火は元から即時観測できるため対象外)。
            if (track.Trigger == TrackTrigger.AtTime)
            {
                OnAtTimeTrackFired?.Invoke(track, instance.HandleNetKey, instance.Elapsed);
            }
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
            // [08_presentation.md] 実装メモ(2026-09-19) — トラックの Anchor とアセット側(Data.AnchorId/
            // 埋め込み Anchor)の両方を参照する(3 ケース、PresentationTrackAnchorComposer に集約)。
            var spec = PresentationTrackAnchorComposer.Compose(in track, data, _registry, sampleRandom: true);
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

        // [26_timeline.md] §6/§3.1(6-10a) — Presentation → Cutscene の入れ子(「Maya カメラも使うし、
        // ヒットも待ちたい」ケース。Presentation を親にして Timeline トラックで Cutscene を呼ぶ)。
        // track.Asset.Id を CutsceneId として CutsceneManager.Play に委譲するだけの薄い接続。
        private void FireTimeline(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_cutscene == null)
            {
                WarnMissingManager(TrackKind.Timeline);
                return;
            }

            var data = _registry.ResolveOrPlaceholder<CutsceneData>(track.Asset.Id);
            var ctx = instance.Ctx;
            var h = _cutscene.PlayData(data, in ctx);

            if (track.StopOnCancel && _cutscene.IsPlaying(h))
            {
                instance.FiredCutscene.Add((trackIndex, h));
            }
        }

        // [22_anchor_group.md] §5(Presentation 統合) — 配置セット(AnchorGroup)トラック。各点の VFX/SE の
        // 再生自体は AnchorGroupPlayer(Cutscene の CutsceneAnchorGroupClip / Anchors ファサードと同じ実体)に
        // そのまま委譲する薄い接続で、二重実装しない(ADR-4)。TrackTargetMode の解釈は Vfx/Se と同じ
        // (ResolveContextRoot で contextRoot を決めるだけ)。
        private void FireAnchorGroup(PresentationInstance instance, int trackIndex, in PresentationTrack track)
        {
            if (_groups == null)
            {
                WarnMissingManager(TrackKind.AnchorGroup);
                return;
            }

            var data = _registry.ResolveOrPlaceholder<AnchorGroupData>(track.Asset.Id);
            var root = ResolveContextRoot(instance.Ctx, track.Target);
            // [14_networking.md] §6/§12 — AnchorPoint のランダム散らばりは見た目専用のため、Cosmetic 配送でも
            // 各クライアントがローカルで独立にサンプリングしてよい(結果に影響しない)。AnchorGroupPlayer.PlayData
            // には Vfx/Se の PlaySeData(seed:)に相当する Seed 引数が無い(AnchorGroupPlanner.Plan が呼び出しの
            // たびに UnityEngine.Random で毎回サンプリングする設計、[22] §3.2/§3.4)ため、instance.Seed(ネット
            // 同期済みの乱数種)は消費しない。
            var h = _groups.PlayData(data, root);
            if (!_groups.IsPlaying(h))
            {
                return;
            }

            // エディタの統合プレビュー(ScenePresentationPreviewDriver)が「配置セットが出した VFX」を
            // SceneVfxPreviewDriver へ Adopt するためのフック。StopOnCancel の有無に関わらず通知する
            // (AnimDriver.AssetEventDispatcher.OnGroupPlayed と同じ設計)。
            OnAnchorGroupPlayed?.Invoke(h);

            if (track.StopOnCancel)
            {
                instance.FiredAnchorGroup.Add((trackIndex, h));
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
            // [08_presentation.md] 実装メモ(2026-09-19) — トラックの Anchor とアセット側(Data.AnchorId/
            // 埋め込み Anchor)の両方を参照する(3 ケース、PresentationTrackAnchorComposer に集約)。
            var spec = PresentationTrackAnchorComposer.Compose(in track, data, _registry, sampleRandom: true);
            // [14_networking.md] §6(6-0、Seed の実消費) — ネットワーク経路の Instance は Seed を渡し、
            // 全クライアントで同じ Clip/Pitch が選ばれるようにする(ローカル再生は今までどおり未指定)。
            var h = _audio.PlaySeData(data, in spec, root, seed: instance.IsNetworked ? instance.Seed : (ushort?)null);

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

            // [14_networking.md] §5(N-4) — ネット受信した Instance に限り Scope=ParticipantsOnly を見る。
            // 予測再生した行為者自身(PlayedViaNetworkReceive=false)は Scope に関わらず発火する。
            if (instance.PlayedViaNetworkReceive && track.Scope == PresentationEffectScope.ParticipantsOnly &&
                !IsParticipant(_netBridge, instance.SelfNetId, instance.TargetNetId))
            {
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

            // [14_networking.md] §5(6-0, C 対応) — SelfNetId/TargetNetId が実際に解決できていれば
            // 「自分の Self/Target か」で誤爆防止を判定する(NetworkObject の所有者比較。IsLocalParticipant
            // が共有の解決経路)。解決できない場合(SelfNetId/TargetNetId が 0、または対象が Spawn されて
            // いない等)は 5-8 の安全側デフォルトのまま(ネット受信 Instance では LocalPlayerOnly を鳴らさない)。
            // 予測再生した行為者自身の Instance は PlayedViaNetworkReceive=false のため、そもそもこの判定に
            // 入らず影響を受けない。
            if (instance.PlayedViaNetworkReceive)
            {
                if (data.LocalPlayerOnly && !IsLocalParticipant(_netBridge, instance.SelfNetId, instance.TargetNetId))
                {
                    return;
                }

                // [14_networking.md] §5(N-4、2026-09-22) — LocalPlayerOnly(「自分の事象なら鳴らす」)と
                // Scope=ParticipantsOnly(「当事者以外は発火しない」)は独立した条件で、両方 AND で通す
                // (docs/08 §Scope 参照)。Scope 側は未解決(SelfNetId/TargetNetId とも 0)のとき安全側で
                // true(全員発火)を返す IsParticipant() を使う(LocalPlayerOnly の安全側〈鳴らさない〉とは
                // 逆であることに注意。目的が異なるため意図的に別デフォルト)。
                if (track.Scope == PresentationEffectScope.ParticipantsOnly &&
                    !IsParticipant(_netBridge, instance.SelfNetId, instance.TargetNetId))
                {
                    return;
                }
            }

            var h = _haptics.PlayData(data);

            if (track.StopOnCancel && _haptics.IsPlaying(h))
            {
                instance.FiredHaptic.Add((trackIndex, h));
            }
        }

        private void FireHitStop(PresentationInstance instance, in PresentationTrack track)
        {
            if (_time == null)
            {
                WarnMissingManager(TrackKind.HitStop);
                return;
            }

            // [14_networking.md] §5(N-4) — [08_presentation.md] 実装メモ「HitStop は全員が実行する
            // (観戦者を区別しない、既定)」の要判断を解消する。Scope=ParticipantsOnly のときだけ、ネット受信
            // Instance に限り非当事者の HitStop をスキップする(既定 Everyone は今までどおり全員停止する)。
            if (instance.PlayedViaNetworkReceive && track.Scope == PresentationEffectScope.ParticipantsOnly &&
                !IsParticipant(_netBridge, instance.SelfNetId, instance.TargetNetId))
            {
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
