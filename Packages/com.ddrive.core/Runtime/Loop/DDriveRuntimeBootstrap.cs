using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Loading;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using DDrive.Runtime.Net;
using DDrive.Runtime.Prefab;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace DDrive.Runtime.Loop
{
    // [01_architecture.md] §11 / [02_core_framework.md] §14 — ランタイム唯一の Composition Root(2026-09-09)。
    // シーンに 1 つ置く(Tools > D-Drive > Generate > 起動オブジェクトをシーンに配置)。Awake で
    //   Registry(Addressables ローダー) → Pool → 各 Manager → AnchorGroupPlayer → AssetEventDispatcher
    // を生成し、GameLoop(GameLoopDriver)へ登録、静的ファサード(Audio / Vfx / Anim / Models / Anchors)を Bind する。
    // Start でカタログ(Inspector の直参照 + Addressables ラベル)を Registry に登録し、IsReady になる。
    // 破棄時は StopAll → ファサード Unbind → GameLoop から解除。
    //
    // 例外で止めない: Addressables が未設定でも Manager 群は組み上がり、未登録 ID は Placeholder で動く。
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(GameLoopDriver))]
    [DisallowMultipleComponent]
    public sealed class DDriveRuntimeBootstrap : MonoBehaviour
    {
        public const string DefaultCatalogLabel = "DDriveCatalog";

        // [14_networking.md] §12 / [11_tasks.md] 6-0(A) — LocalLoopbackBridge(シングルプレイ相当。既定)
        // と NgoNetBridge(NGO 2.13.2)のどちらを NetBridge として使うか。コマンドライン引数
        // (-ddrive-net host|client|off、[docs/29])で上書きできる。既定は Loopback のため、6-0 適用前と
        // 挙動は変わらない([14] §1 の原則どおり)。
        public enum NetBridgeMode
        {
            Loopback,
            Ngo,
        }

        // [14_networking.md] N-1(2026-09-22) — 開発用の手動接続(実行中に IP を入力 → StartClient
        // を呼ぶ)向け。Auto は既存の挙動(Start() で自動 StartHost/StartClient)、Manual は
        // ResolveNetBridge() で NGO ブリッジの解決・NetworkManager/NgoNetBridge の検索までは行うが
        // 自動接続はせず、StartHost/StartClient/StopNetworking(公開 API)を呼ぶまで待つ。
        public enum NetStartMode
        {
            Auto,
            Manual,
        }

        [Header("ネットワーク(6-0)")]
        [Tooltip("既定のネットブリッジ。コマンドライン引数 -ddrive-net host|client|off|manual で上書きできる(未指定時はこの値を使う)。既定は Loopback(シングルプレイ、既存の挙動を変えない)")]
        public NetBridgeMode DefaultNetBridge = NetBridgeMode.Loopback;

        [Tooltip("DefaultNetBridge=Ngo のとき、起動時に自動で StartHost/StartClient するか(Auto、既定)。Manual にすると自動接続せず、StartHost/StartClient/StopNetworking(公開 API)を呼ぶまで待つ(開発用: 実行中に IP を入力して接続するテストプレイ向け、N-1)。コマンドライン引数 -ddrive-net manual でも同じ効果")]
        public NetStartMode DefaultNetStart = NetStartMode.Auto;

        // [42_distribution.md] §2.3-9/§7 A-7(P1-1、2026-09-20) — NGO は versionDefines(DDRIVE_NGO)で
        // 必須依存から切り離した。DDrive.Runtime 自身は Unity.Netcode.Runtime を参照しないため、
        // NetworkManager/NgoNetBridge への Inspector 直参照は DDrive.Runtime.Ngo アセンブリ側の
        // 補助コンポーネント `DDriveNgoBootstrapHook` へ移した(NGO を使うシーンでは、この Bootstrap と
        // 同じ GameObject にそのコンポーネントを追加して割り当てる。[docs/29] のセットアップ手順)。

        [Tooltip("コマンドライン引数 -ddrive-host が無いときに使う既定 IP(PC-A=Host、[docs/29])")]
        public string DefaultHostAddress = "192.168.137.1";

        [Tooltip("コマンドライン引数 -ddrive-port が無いときに使う既定 Port")]
        public ushort DefaultPort = 7777;

        [Tooltip("Ngo モードのとき、画面左上にデバッグオーバーレイ(役割/接続状態/RTT/NetworkTime/受信数)を出す")]
        public bool ShowNetDebugOverlay = true;

        // [M-1d、2026-09-25] ShowNetDebugOverlay=true のままリリースビルドしても、オーバーレイは
        // 既定では出さない(Debug.isDebugBuild || Application.isEditor のときだけ生成する。
        // NgoBridgeFactoryInstaller.Create 参照)。リリースビルドでもオーバーレイを出したい場合だけ
        // これを true にする(MS2026 TeamNotes 2026-09-25「リリース前に ShowNetDebugOverlay を false に」の
        // 本修正: 個別プロジェクトが値を管理しなくても既定でリリースには出ない)。
        [Tooltip("リリースビルドでも(開発ビルド/エディタでなくても)ネットデバッグオーバーレイを出す(既定 OFF)")]
        public bool ShowNetDebugOverlayInRelease = false;

        [Tooltip("カタログ ContentHash 照合(6-5)の待ち時間。この秒数内に相手のハッシュが届かなければタイムアウト扱い(不一致と同じ方針を適用する。[14_networking.md] §7)")]
        public double ContentHashTimeoutSeconds = 5d;

        [Tooltip("起動時に Registry へ登録するカタログ(GameData/Catalogs)。Generate メニュー / Inspector の「カタログを再収集」で自動設定される")]
        public AssetCatalog[] Catalogs;

        [Tooltip("このラベルを持つ Addressables のカタログも起動時に集めて登録する(ビルド後にカタログを増やす運用向け)。空なら無効")]
        public string CatalogLabel = DefaultCatalogLabel;

        [Tooltip("シーンをまたいで生かす(通常 ON)。OFF ならシーン破棄と同時に全 Manager を停止する")]
        public bool KeepAcrossScenes = true;

        [Tooltip("静的ファサード(Audio.PlaySe 等)をこのインスタンスへ Bind する。テストや多重起動の検証で OFF にする")]
        public bool BindFacades = true;

        [Tooltip("UiLayer ごとの既定 Skin/SE/Appear/Disappear(4-9 + 4-7 残り)。未設定(null)ならフォールバック無し")]
        public UiLayerSettings LayerSettings;

        [Tooltip("仕様書「調整値」タブの取り込み先(5-13)。未設定(null)なら Tuning.Get* は常に既定値を返す")]
        public DDrive.Runtime.Tuning.TuningTable TuningTable;

        public static DDriveRuntimeBootstrap Instance { get; private set; }

        public GameLoopDriver Loop { get; private set; }
        public AssetRegistry Registry { get; private set; }
        public PoolService Pool { get; private set; }
        public INetBridge NetBridge { get; private set; }
        // [14_networking.md] §7(6-5) — カタログ ContentHash の接続時照合。Loopback でも生成する
        // (ClientConnected が通常発火しないため実質 no-op。他 Manager と同じ「通信の有無で挙動を変えない」原則)。
        public CatalogContentHashGate NetHashGate { get; private set; }
        public AudioManager Audio { get; private set; }
        public BgmManager Bgm { get; private set; }
        public VfxManager Vfx { get; private set; }
        public AnimManager Anim { get; private set; }
        public ModelsManager Models { get; private set; }
        public MaterialManager Materials { get; private set; }
        public PrefabsManager Prefabs { get; private set; }
        public UiManager Ui { get; private set; }
        public UiTweenManager UiTweens { get; private set; }
        // [16_camera_haptics.md] Part A/B / [11_tasks.md] 5-2 / 5-2b。
        public CameraFxManager CameraFx { get; private set; }
        public HapticsManager Haptics { get; private set; }
        // [08_presentation.md] / [11_tasks.md] 5-1 — 演出統合(Presentation)のオーケストレータ。
        public PresentationManager Presentation { get; private set; }
        // [26_timeline.md] / [11_tasks.md] 6-10a — Maya FBX 取り込み + D-Drive トラックの Timeline 基盤。
        public CutsceneManager Cutscene { get; private set; }
        // [18_ui_controls.md] B-4(4-16) — 音量/アクセシビリティ/UI 速度の永続化ストア。起動時に PlayerPrefs から読み込む。
        public OptionStore Options { get; private set; }
        public AnchorGroupPlayer Groups { get; private set; }
        public AssetEventDispatcher Dispatcher { get; private set; }
        // Prefabs.Events(OnSpawn/OnDestroy)を SE/VFX/配置セットへ配線する 2 つ目の Dispatcher
        // (Anim.Events とは別セッション空間のため、同じ Dispatcher に相乗りさせず独立させる。[07] B-3)。
        public AssetEventDispatcher PrefabDispatcher { get; private set; }
        // Ui.Events(OnSpawn/OnEnable/OnDisable/OnDestroy)を SE/VFX へ配線する 3 つ目の Dispatcher([07] A-3)。
        public AssetEventDispatcher UiDispatcher { get; private set; }
        // Cutscene.Events(OnSpawn/OnEnable/OnDisable/OnDestroy/Custom)を SE/VFX へ配線する 4 つ目の Dispatcher(6-10a)。
        public AssetEventDispatcher CutsceneDispatcher { get; private set; }

        // カタログ登録が終わったか(IsReady 前の Play は未登録 ID として Placeholder になる)。
        public bool IsReady { get; private set; }
        public int RegisteredCatalogCount { get; private set; }
        public event Action OnReady;

        private readonly UniTaskCompletionSource _ready = new();
        private AnchorGroupLoopAdapter _groupAdapter;
        private UnscaledCameraFxAdapter _cameraFxAdapter;
        private bool _built;

        // [42_distribution.md] §2.3-9/§7 A-7(P1-1、2026-09-20) — NGO ブリッジ生成(NetBridgeFactoryRegistry
        // 経由)の戻り値。StartHost/StartClient の実行は Start() まで遅延する(NetworkManager.Awake/OnEnable が
        // 済んでいないと NullReferenceException になるため。元 _pendingNetworkManager 等と同じ理由)。
        private Action _pendingNetStart;
        private Action<CatalogContentHashGate> _assignHashGateToOverlay;

        // [14_networking.md] N-1(2026-09-22) — 開発用の手動接続 API(StartHost/StartClient/StopNetworking/
        // IsNetworkStarted)の実処理。NetBridgeMode.Loopback、または NGO 未導入/シーンに NetworkManager+
        // NgoNetBridge が無い場合は null のまま(公開 API 側が警告 + no-op にフォールバックする)。
        private Func<bool> _isNetworkStartedQuery;
        private Func<ushort, bool> _manualStartHost;
        private Func<string, ushort, bool> _manualStartClient;
        private Action _manualStop;

        public UniTask WhenReady => _ready.Task;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[DDrive] DDriveRuntimeBootstrap は既に '{Instance.name}' にあります。'{name}' の分は破棄します。");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (KeepAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }

            Loop = GetComponent<GameLoopDriver>();
            if (Loop == null)
            {
                Loop = gameObject.AddComponent<GameLoopDriver>();
            }

            Build();
        }

        private void Start()
        {
            RegisterCatalogsAsync().Forget();
            StartNetworkingIfPending();
        }

        // [14_networking.md] §7(6-5) — ContentHash 照合のタイムアウト検出(偽装: ハッシュを送らない/
        // 遅延させる Client への対処)。GameLoop(TimeService.ScaledDeltaTime)には乗せず、NetBridge.NetworkTime
        // を直接見る(HitStop 等でゲーム内時間が止まってもタイムアウト判定自体は進む必要があるため。
        // AnchorGroupLoopAdapter 等と同じ「専用の薄いアダプタ」パターンをここでは Bootstrap 自身の
        // Update() で済ませている。呼び出し頻度は 1 秒未満で十分だが、フレームごとでも
        // Dictionary が空なら早期 return するだけなので実質無害)。
        private void Update()
        {
            NetHashGate?.Tick(NetBridge.NetworkTime);
        }

        // [14_networking.md] §12(6-0) — NetworkManager.StartHost()/StartClient() は NetworkManager 自身の
        // Awake()/OnEnable()(内部状態の初期化)が済んでいないと NullReferenceException になる
        // (実機確認で発見。DDriveRuntimeBootstrap は DefaultExecutionOrder(-1000) で他の全 Awake より先に
        // 走るため、Awake() 内から直接 StartHost/StartClient を呼ぶと NetworkManager がまだ初期化されて
        // いない)。そのため ResolveNetBridge()(Awake 内、Build() 経由)では役割の決定・ブリッジの選定・
        // Transport の設定だけを行い、実際の StartHost/StartClient 呼び出しは全オブジェクトの Awake が
        // 終わった後の Start() まで遅延する。
        private void StartNetworkingIfPending()
        {
            var pending = _pendingNetStart;
            _pendingNetStart = null;
            pending?.Invoke();
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Options?.SaveIfDirty(); // Codex レビュー対応(2026-09-11): これまで一度も永続化されていなかった
            Teardown();
            Instance = null;
        }

        // アプリ終了時は OnDestroy より先にここが呼ばれることがある(DontDestroyOnLoad でも同様)。
        // Teardown 前に保存だけ済ませておく(Teardown 自体は OnDestroy 側に任せる)。
        private void OnApplicationQuit()
        {
            Options?.SaveIfDirty();
            // [16_camera_haptics.md] Part B — アプリ終了時は必ずモーターを 0 に戻す(繋いだままのパッドが
            // 振動し続けるのを防ぐ)。
            Haptics?.ResetOutput();
        }

        // フォーカス喪失時(Alt+Tab 等)もモーターを 0 に戻す([16] Part B)。再生中の Instance 自体は
        // 止めない(フォーカス復帰後に自然な減衰で終わる)。
        // P5 レビュー対応(2026-09-14): ResetOutput() は 1 回だけ 0 を出すため、runInBackground=true で
        // フォーカス喪失後も Tick が回り続けると次の Tick で振動が復活してしまっていた。
        // フォーカス喪失中は出力 0 を固定するフラグ(SetFocusLost)に切り替える。
        private void OnApplicationFocus(bool hasFocus)
        {
            Haptics?.SetFocusLost(!hasFocus);
        }

        // ── 組み立て ──

        private void Build()
        {
            if (_built)
            {
                return;
            }

            var instances = new GameObject("Instances");
            instances.transform.SetParent(transform, false);

            Pool = new PoolService();
            Pool.SetInstanceParent(instances.transform);
            Registry = new AssetRegistry(new AddressablesAssetLoader());
            NetBridge = ResolveNetBridge(); // [14_networking.md] §12(6-0) — Loopback/Ngo の選択点
            // [14_networking.md] §7(6-5) — Debug.isDebugBuild は Editor 実行時、または「Development Build」を
            // 付けたプレイヤーで true になる(NgoNetBridge.ConfigureAppLayerSimLatency と同じ判定基準)。
            NetHashGate = new CatalogContentHashGate(NetBridge, ContentHashTimeoutSeconds, Debug.isDebugBuild);
            _assignHashGateToOverlay?.Invoke(NetHashGate);
            _assignHashGateToOverlay = null;

            var seTemplate = new GameObject("SeSourceTemplate");
            seTemplate.transform.SetParent(transform, false);
            seTemplate.AddComponent<AudioSource>();
            seTemplate.SetActive(false);

            Audio = new AudioManager(Pool, Registry, seTemplate, NetBridge);
            Bgm = new BgmManager(Registry, CreateAudioChannel("BgmChannelA"), CreateAudioChannel("BgmChannelB"));
            Vfx = new VfxManager(Pool, Registry, NetBridge);
            Anim = new AnimManager(Registry);
            Materials = new MaterialManager(Registry);
            Models = new ModelsManager(Pool, Registry, Anim, Materials);
            Prefabs = new PrefabsManager(Pool, Registry, netBridge: NetBridge); // NetMode.Simulated のサーバー権威生成([14] §3/§10、4-13)
            UiTweens = new UiTweenManager(Registry);
            Ui = new UiManager(Pool, Registry, Loop.PauseService, tweens: UiTweens);
            Ui.SetLayerSettings(LayerSettings);
            CameraFx = new CameraFxManager(Registry);
            Haptics = new HapticsManager(Registry);
            // Codex レビュー対応(2026-09-11): Storage を保持しておき、OnDestroy/OnApplicationQuit で
            // SaveIfDirty() を呼べるようにする(これまでは Load するだけで一度も保存していなかった)。
            var optionStorage = new PlayerPrefsOptionStorage();
            Options = new OptionStore { UiTweens = UiTweens, CameraFx = CameraFx, Haptics = Haptics, Storage = optionStorage };
            Options.Load(optionStorage);
            Ui.SetOptionStore(Options);
            Groups = new AnchorGroupPlayer(Registry, Vfx, Audio);
            // [26_timeline.md] §4.5/§6(6-10a) — CutsceneManager は Presentation より前に作る
            // (PresentationManager.TrackKind.Timeline がこの参照を必要とするため)。
            Cutscene = new CutsceneManager(Registry, Models, NetBridge);
            // [11_tasks.md] 5-1 — Loop.TimeService を渡すことで、HitStop トラックが TimeService.HitStop を
            // 呼ぶだけで AtTime の進行も(他の全 Manager と同じく)自動的に止まる(GameLoopDriver が
            // ScaledDeltaTime を配るため、Presentation 側で特別な配線は不要)。
            // [14_networking.md] §5(5-8/5-9) — Audio/Vfx/Prefabs と同じく NetBridge を渡す(現状は
            // LocalLoopbackBridge のため常に完全ローカル。NGO 統合は Phase 6 でここを差し替える)。
            Presentation = new PresentationManager(Registry, Loop.TimeService, Audio, Bgm, Vfx, Anim, Ui, UiTweens, CameraFx, Haptics, NetBridge, cutscene: Cutscene, groups: Groups);
            // [11_tasks.md] 6-0 修正3(実機確認で発見した課題3) — カタログ登録(RegisterCatalogsAsync、Start())が
            // 完了する前に接続直後のスナップショット(PresentationPlayMsg)を受信すると、Registry にまだ
            // 存在しない PresId が Unregistered として Placeholder に解決されてしまう(Late Join 直後の実機確認で
            // 発見)。RegisterCatalogsAsync 完了までネット受信の Play/Signal/Cancel を PresentationManager 内部で
            // キューに保留し(SetRegistryReady(false))、完了後に登録順で処理する(SetRegistryReady(true) が
            // まとめて flush する)。ローカル(手で Play() を呼ぶ)経路は影響を受けない([14_networking.md] §5)。
            Presentation.SetRegistryReady(false);
            // [26_timeline.md] §4.7 / docs/45 P1-3(2026-09-20) — Presentation と同じ穴が CutsceneManager
            // にもあった(Late Join 直後の CutscenePlayMsg が「未登録」として破棄される)。Presentation の
            // 6-0 修正3をそのまま移植したので、同じタイミングで false → RegisterCatalogsAsync 完了で true にする。
            Cutscene.SetRegistryReady(false);
            // [14_networking.md] §5(6-0 修正7、実機確認 v3 で発見した実バグの修正) — Client 視点で
            // Host との接続を失ったときに、ネット経由で開始した Presentation(StopOnCancel=true の
            // Vfx/Se 等を含む)を強制終了する。[42_distribution.md] §2.3-9/§7 A-7(P1-1、2026-09-20) —
            // `ClientDisconnected` は `INetBridge` 自体が持つイベントなので、NGO 型を経由せず常に
            // `NetBridge`(INetBridge)へ購読する(Loopback は発火しない=既存のシングルプレイ挙動を変えない、
            // [14] §1)。
            NetBridge.ClientDisconnected += OnNetClientDisconnected;

            Dispatcher = new AssetEventDispatcher(Anim.Events, Registry, Audio, Vfx, Anim.GetContextTransform, Groups);
            PrefabDispatcher = new AssetEventDispatcher(Prefabs.Events, Registry, Audio, Vfx, Prefabs.GetContextTransform, Groups);
            UiDispatcher = new AssetEventDispatcher(Ui.Events, Registry, Audio, Vfx, Ui.GetContextTransform, Groups);
            CutsceneDispatcher = new AssetEventDispatcher(Cutscene.Events, Registry, Audio, Vfx, Cutscene.GetContextTransform, Groups);

            var loop = Loop.GameLoop;
            loop.Register(Audio);
            loop.Register(Bgm);
            loop.Register(Vfx);
            loop.Register(Anim);
            loop.Register(Materials);
            loop.Register(Models);
            loop.Register(Prefabs);
            loop.Register(Ui);
            loop.Register(UiTweens);
            loop.Register(Haptics);
            loop.Register(Cutscene);
            loop.Register(Presentation);
            _groupAdapter = new AnchorGroupLoopAdapter(Groups);
            loop.Register(_groupAdapter);
            // [16_camera_haptics.md] Part A 実装メモ — CameraFx は HitStop 中も揺れを止めないため、
            // 他 Manager と同じ TimeService.ScaledDeltaTime ではなく Unscaled dt で駆動する(アダプタ経由)。
            _cameraFxAdapter = new UnscaledCameraFxAdapter(CameraFx);
            loop.Register(_cameraFxAdapter);

            if (BindFacades)
            {
                Runtime.Audio.Audio.Bind(Audio);
                Runtime.Audio.Audio.Bind(Bgm);
                Runtime.Vfx.Vfx.Bind(Vfx);
                Runtime.Anim.Anim.Bind(Anim);
                Runtime.Anim2D.Anim2D.Bind(Anim, Registry);
                Mats.Bind(Materials);
                Runtime.Model.Models.Bind(Models);
                Runtime.Prefab.Prefabs.Bind(Prefabs);
                Runtime.Ui.Ui.Bind(Ui);
                Runtime.Ui.UiSkins.Bind(Registry);
                Runtime.Ui.UiFx.Bind(UiTweens);
                Runtime.Ui.Options.Bind(Options);
                Runtime.CameraShake.CameraFx.Bind(CameraFx);
                Runtime.Haptics.Haptics.Bind(Haptics);
                Anchors.Bind(Groups);
                Runtime.Tuning.Tuning.Bind(TuningTable);
                Runtime.Loading.ScenePreload.Bind(Registry); // [11_tasks.md] 5-7
                Runtime.Cutscene.Cutscene.Bind(Cutscene); // [11_tasks.md] 6-10a
                Runtime.Presentation.Presentation.Bind(Presentation); // [11_tasks.md] 5-1
            }

            _built = true;
        }

        // 実際に解決されたネットワーク起動オプション(コマンドライン引数 + Inspector 既定値)。
        // NetCheckRunner(6-0, D)等が -ddrive-autotest / シミュレータ設定を参照するために公開する。
        public NetLaunchOptions LaunchOptions { get; private set; }

        // [14_networking.md] N-1(2026-09-22) — 開発用の手動接続 API。
        //
        // 背景: MS2026(4人対戦)へ持ち込む前提の開発用テストプレイで「LAN 外の特定 IP を入力 → 接続」を
        // したいが、既存の StartNetworkingIfPending() は Start() で自動的に StartHost/StartClient を
        // 呼んでしまうため、実行中に IP を選ぶ余地が無かった。DefaultNetStart=Manual(または
        // -ddrive-net manual)のときは ResolveNetBridge() が NGO ブリッジの解決・NetworkManager/
        // NgoNetBridge の検索までは行うが、Transport 設定と StartHost/StartClient の実行はここで
        // 遅延し、下記 API を呼んだときに初めて行う(NgoTransportConfigurator.TryConfigure を再利用)。
        //
        // NetBridgeMode.Loopback、または NGO 未導入/シーンに NetworkManager+NgoNetBridge が無い場合は
        // 警告して no-op する(例外で止めない、[CLAUDE.md] TL;DR 4)。既に接続中のときも警告して false を
        // 返す(先に StopNetworking() を呼ぶ運用)。現在の役割(Host/Client)は既存の公開プロパティ
        // `NetBridge.IsServer`/`NetBridge.IsClient`(NetDebugOverlay と同じ判定)で読める(重複させない)。
        public bool IsNetworkStarted => _isNetworkStartedQuery != null && _isNetworkStartedQuery();

        public bool StartHost(ushort port)
        {
            if (_manualStartHost == null)
            {
                Debug.LogWarning("[Net] DDriveRuntimeBootstrap.StartHost: NGO ブリッジが使えないため何もしません(NetBridgeMode.Loopback、または NGO 未導入/シーンに NetworkManager+NgoNetBridge が見つかりません)。");
                return false;
            }

            return _manualStartHost(port);
        }

        public bool StartClient(string address, ushort port)
        {
            if (_manualStartClient == null)
            {
                Debug.LogWarning("[Net] DDriveRuntimeBootstrap.StartClient: NGO ブリッジが使えないため何もしません(NetBridgeMode.Loopback、または NGO 未導入/シーンに NetworkManager+NgoNetBridge が見つかりません)。");
                return false;
            }

            return _manualStartClient(address, port);
        }

        public void StopNetworking()
        {
            if (_manualStop == null)
            {
                Debug.LogWarning("[Net] DDriveRuntimeBootstrap.StopNetworking: NGO ブリッジが使えないため何もしません(NetBridgeMode.Loopback、または NGO 未導入/シーンに NetworkManager+NgoNetBridge が見つかりません)。");
                return;
            }

            _manualStop();

            // [14_networking.md] §18(N-5、2026-09-24) — Host 引き継ぎ(同一プロセスで Stop → 別ロールで
            // 再 Start)向け。MS2026 の手順(docs/14 §18)は各端末がまず NetworkManager.Shutdown() を自分で
            // 呼んでから StopNetworking() を呼ぶため、_manualStop() 呼び出し時点で既に IsListening=false の
            // こともある(DoManualStop 側で Shutdown の二重呼び出しは避けつつ、リセットは常に実行するよう
            // 修正済み)。NetHashGate.Reset()(D-1: 再接続後にハッシュを再送できるようにする)と
            // ResetNetworkedState()(D-3/D-4: 全 Manager の networked 状態を捨てる)は、ここでは
            // 「_manualStop が実際に呼べた(NGO ブリッジがある)」ときだけ行う。
            NetHashGate?.Reset();
            ResetNetworkedState();
        }

        // [14_networking.md] §18(N-5、2026-09-24、D-3/D-4) — 「Stop 時に全 Manager の networked 状態を
        // 捨てる」仕様の実体。StopNetworking() と Client 視点の切断時(OnNetClientDisconnected)の両方から
        // 呼ぶ。ローカル(ネット非経由)の Instance/State には触れない(各 Manager の ResetNetworkedState
        // 実装を参照)。
        private void ResetNetworkedState()
        {
            Presentation?.ResetNetworkedState();
            Cutscene?.ResetNetworkedState();
            Prefabs?.ResetNetworkedState();
            Audio?.ResetNetworkedState();
            Vfx?.ResetNetworkedState();
        }

        // [14_networking.md] §12(6-0, A) — コマンドライン引数(未指定なら Inspector の既定値)に従って
        // LocalLoopbackBridge か NgoNetBridge を選ぶ。Ngo を要求されたのにシーンに NetworkManager/
        // NgoNetBridge が無い場合は警告して Loopback にフォールバックする(例外で止めない、[CLAUDE.md] TL;DR 4)。
        private INetBridge ResolveNetBridge()
        {
            LaunchOptions = NetLaunchArgs.Parse(Environment.GetCommandLineArgs());

            // [14_networking.md] N-1(2026-09-22) — 役割解決を純関数(NetLaunchArgs.ResolveEffectiveRole、
            // EditMode テスト済み)に切り出した。既定(DefaultNetBridge=Loopback, DefaultNetStart=Auto)では
            // 常に Off を返すため、既存の挙動は変わらない。
            var role = NetLaunchArgs.ResolveEffectiveRole(LaunchOptions.Role, DefaultNetBridge == NetBridgeMode.Ngo, DefaultNetStart == NetStartMode.Manual);

            if (role == NetLaunchRole.Off)
            {
                return new LocalLoopbackBridge();
            }

            // [42_distribution.md] §2.3-9/§7 A-7(P1-1、2026-09-20) — NGO(com.unity.netcode.gameobjects)は
            // versionDefines(DDRIVE_NGO)で任意依存に切り離した。DDrive.Runtime 自身は Unity.Netcode.Runtime を
            // 参照しないため、実際の生成は DDrive.Runtime.Ngo アセンブリが登録するファクトリへ委譲する
            // (NGO 未導入、または NGO 導入済みだがシーンに NetworkManager が無い場合は、どちらも
            // ファクトリが null/生成失敗を返すだけなので、ここでは 1 箇所の分岐で両方をまとめて扱える)。
            var factory = NetBridgeFactoryRegistry.Current;
            if (factory == null)
            {
                Debug.LogWarning("[Net] DDriveRuntimeBootstrap: NGO(com.unity.netcode.gameobjects)が導入されていないため、Host/Client/Manual の要求を無視して LocalLoopbackBridge を使います。");
                return new LocalLoopbackBridge();
            }

            var host = LaunchOptions.Host ?? DefaultHostAddress;
            var port = (ushort)(LaunchOptions.Port ?? DefaultPort);
            var result = factory.Create(new NgoBridgeCreateArgs(role, host, port, LaunchOptions, ShowNetDebugOverlay, transform, ShowNetDebugOverlayInRelease));

            if (result?.Bridge == null)
            {
                Debug.LogWarning("[Net] DDriveRuntimeBootstrap: -ddrive-net で Host/Client/Manual が要求されましたが、シーンに NetworkManager + NgoNetBridge が見つかりません。LocalLoopbackBridge にフォールバックします([docs/29] のセットアップ手順を確認してください)。");
                return new LocalLoopbackBridge();
            }

            // StartHost/StartClient は Start() まで遅延する(上記 StartNetworkingIfPending 参照)。
            // role==Manual のときは result.PendingStart が null なので自動接続は起きない([14] N-1)。
            _pendingNetStart = result.PendingStart;
            _assignHashGateToOverlay = result.AssignHashGate;
            _isNetworkStartedQuery = result.IsListening;
            _manualStartHost = result.ManualStartHost;
            _manualStartClient = result.ManualStartClient;
            _manualStop = result.ManualStop;

            return result.Bridge;
        }

        private AudioSource CreateAudioChannel(string channelName)
        {
            var go = new GameObject(channelName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<AudioSource>();
        }

        // [14_networking.md] §5(6-0 修正7) — 自分(Client)が Host との接続を失ったときだけ、ネット経由の
        // Presentation を強制終了する。Host 視点(相手が抜けた)は自分の接続は継続しているため対象外
        // ([42_distribution.md] §2.3-9/§7 A-7(P1-1、2026-09-20) — NGO 型ではなく INetBridge.IsServer で
        // 判定する。Loopback は常に IsServer=true 相当のため、このイベント自体が発火しない=対象外)。
        private void OnNetClientDisconnected(ulong clientId, string reason)
        {
            if (!NetBridge.IsServer)
            {
                // [14_networking.md] §18(N-5、2026-09-24) — D-1: 再接続後に ContentHash を再送できるよう
                // Gate をリセットする。D-3/D-4: Presentation.CancelAllNetworked()/Cutscene.CancelAllNetworked()
                // の単独呼び出しを ResetNetworkedState()(CancelAllNetworked を内包しつつ台帳・保留キュー・
                // レート制限窓も捨てる)に置き換えた。
                NetHashGate?.Reset();
                ResetNetworkedState();
            }
        }

        private void Teardown()
        {
            if (!_built)
            {
                return;
            }

            NetBridge.ClientDisconnected -= OnNetClientDisconnected;
            _pendingNetStart = null;
            _assignHashGateToOverlay = null;
            _isNetworkStartedQuery = null;
            _manualStartHost = null;
            _manualStartClient = null;
            _manualStop = null;

            NetHashGate?.Dispose();
            NetHashGate = null;

            var loop = Loop != null ? Loop.GameLoop : null;
            if (loop != null)
            {
                loop.StopAll(StopReason.SceneUnload);
                loop.Unregister(Audio);
                loop.Unregister(Bgm);
                loop.Unregister(Vfx);
                loop.Unregister(Anim);
                loop.Unregister(Materials);
                loop.Unregister(Models);
                loop.Unregister(Prefabs);
                loop.Unregister(Ui);
                loop.Unregister(UiTweens);
                loop.Unregister(Haptics);
                loop.Unregister(Cutscene);
                loop.Unregister(Presentation);
                loop.Unregister(_groupAdapter);
                loop.Unregister(_cameraFxAdapter);
            }

            Dispatcher?.Dispose();
            Dispatcher = null;
            PrefabDispatcher?.Dispose();
            PrefabDispatcher = null;
            UiDispatcher?.Dispose();
            UiDispatcher = null;
            CutsceneDispatcher?.Dispose();
            CutsceneDispatcher = null;

            if (BindFacades)
            {
                Runtime.Audio.Audio.Bind((AudioManager)null);
                Runtime.Audio.Audio.Bind((BgmManager)null);
                Runtime.Vfx.Vfx.Bind(null);
                Runtime.Anim.Anim.Bind(null);
                Runtime.Anim2D.Anim2D.Bind(null, null);
                Mats.Bind(null);
                Runtime.Model.Models.Bind(null);
                Runtime.Prefab.Prefabs.Bind(null);
                Runtime.Ui.Ui.Bind(null);
                Runtime.Ui.UiSkins.Bind((IAssetRegistry)null);
                Runtime.Ui.UiFx.Bind(null);
                Runtime.Ui.Options.Bind(null);
                Runtime.CameraShake.CameraFx.Bind(null);
                Runtime.Haptics.Haptics.Bind(null);
                Anchors.Bind(null);
                Runtime.Tuning.Tuning.Bind(null);
                Runtime.Loading.ScenePreload.Bind(null); // [11_tasks.md] 5-7
                Runtime.Cutscene.Cutscene.Bind(null); // [11_tasks.md] 6-10a
                Runtime.Presentation.Presentation.Bind(null); // [11_tasks.md] 5-1
            }

            Pool?.Clear(PoolScope.Global);
            _built = false;
        }

        // ── カタログ ──

        public async UniTask RegisterCatalogsAsync()
        {
            if (IsReady)
            {
                return;
            }

            var count = 0;
            // [14_networking.md] §7(6-5) — ContentHash はこの回で実際に Registry へ登録したカタログ
            // (Catalogs[] + ラベル集め分)から計算する。Editor Play Mode でもビルド実行でも同じ
            // コードパスを通るため、生成タイミングを分ける必要が無い(CatalogContentHasher.cs 冒頭コメント参照)。
            var allCatalogs = new List<AssetCatalog>();
            if (Catalogs != null)
            {
                foreach (var catalog in Catalogs)
                {
                    if (catalog == null)
                    {
                        continue;
                    }

                    await Registry.RegisterCatalogAsync(catalog);
                    allCatalogs.Add(catalog);
                    count++;
                }
            }

            count += await RegisterLabeledCatalogsAsync(allCatalogs);

            RegisteredCatalogCount = count;
            IsReady = true;
            // [11_tasks.md] 6-0 修正3 — カタログ登録完了後にネット受信の保留分(Play/Signal/Cancel)を
            // 受信順に処理する。Presentation は Build() で常に生成されるため null チェックは不要。
            Presentation.SetRegistryReady(true);
            // docs/45 P1-3(2026-09-20) — CutsceneManager も同じタイミングで保留分(Play/Seek/Cancel)を flush する。
            Cutscene.SetRegistryReady(true);

            var catalogHashes = new CatalogContentHasher.CatalogHashEntry[allCatalogs.Count];
            for (var i = 0; i < allCatalogs.Count; i++)
            {
                catalogHashes[i] = CatalogContentHasher.HashCatalog(allCatalogs[i]);
            }

            NetHashGate?.SetLocalSummary(CatalogContentHasher.CombineCatalogHashes(catalogHashes), catalogHashes);

            _ready.TrySetResult();
            OnReady?.Invoke();
            if (count == 0)
            {
                Debug.LogWarning("[DDrive] カタログが 1 つも登録されていません。Inspector の Catalogs か Addressables のラベルを設定してください(全 ID が Placeholder になります)。");
            }
        }

        private async UniTask<int> RegisterLabeledCatalogsAsync(List<AssetCatalog> collected)
        {
            if (string.IsNullOrEmpty(CatalogLabel))
            {
                return 0;
            }

            try
            {
                var locations = Addressables.LoadResourceLocationsAsync(CatalogLabel, typeof(AssetCatalog));
                await UniTask.WaitUntil(() => locations.IsDone);
                if (locations.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded || locations.Result == null || locations.Result.Count == 0)
                {
                    Addressables.Release(locations);
                    return 0;
                }

                var load = Addressables.LoadAssetsAsync<AssetCatalog>(locations.Result, null);
                await UniTask.WaitUntil(() => load.IsDone);
                var count = 0;
                if (load.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded && load.Result != null)
                {
                    foreach (var catalog in load.Result)
                    {
                        if (catalog == null || Contains(Catalogs, catalog))
                        {
                            continue;
                        }

                        await Registry.RegisterCatalogAsync(catalog);
                        collected.Add(catalog);
                        count++;
                    }
                }

                Addressables.Release(locations);
                return count;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive] Addressables のラベル '{CatalogLabel}' からカタログを集められませんでした: {e.Message}");
                return 0;
            }
        }

        private static bool Contains(AssetCatalog[] list, AssetCatalog catalog)
        {
            if (list == null)
            {
                return false;
            }

            for (var i = 0; i < list.Length; i++)
            {
                if (list[i] == catalog)
                {
                    return true;
                }
            }

            return false;
        }

        // AnchorGroupPlayer は IAssetManager ではない(Tick() のみ)ため、GameLoop に載せる薄い橋渡し。
        private sealed class AnchorGroupLoopAdapter : IAssetManager
        {
            private readonly AnchorGroupPlayer _player;

            public AnchorGroupLoopAdapter(AnchorGroupPlayer player) => _player = player;

            public Foundation.Identity.AssetType Type => Foundation.Identity.AssetType.AnchorGroup;

            public void Tick(float dt) => _player.Tick();

            public void OnPause(PauseChannel channel, bool paused)
            {
            }

            public void StopAll(StopReason reason) => _player.StopAll();

            public void OnSceneUnload() => _player.StopAll();
        }

        // [16_camera_haptics.md] Part A 実装メモ — CameraFxManager だけは GameLoop 共有の
        // TimeService.ScaledDeltaTime ではなく Time.unscaledDeltaTime で駆動する(HitStop 中も揺れを
        // 止めないため)。CameraFxManager 自身の Tick(dt) は渡された dt をそのまま使う純関数のままにして
        // テスト容易性を保ち、実配線だけをこのアダプタで差し替える(AnchorGroupLoopAdapter と同じ考え方)。
        private sealed class UnscaledCameraFxAdapter : IAssetManager
        {
            private readonly CameraFxManager _cameraFx;

            public UnscaledCameraFxAdapter(CameraFxManager cameraFx) => _cameraFx = cameraFx;

            public Foundation.Identity.AssetType Type => Foundation.Identity.AssetType.Shake;

            public void Tick(float dt) => _cameraFx.Tick(Time.unscaledDeltaTime);

            public void OnPause(PauseChannel channel, bool paused) => _cameraFx.OnPause(channel, paused);

            public void StopAll(StopReason reason) => _cameraFx.StopAll(reason);

            public void OnSceneUnload() => _cameraFx.OnSceneUnload();
        }
    }
}
