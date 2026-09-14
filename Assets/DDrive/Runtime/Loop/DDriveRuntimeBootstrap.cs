using System;
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
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Loading;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
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
        // [18_ui_controls.md] B-4(4-16) — 音量/アクセシビリティ/UI 速度の永続化ストア。起動時に PlayerPrefs から読み込む。
        public OptionStore Options { get; private set; }
        public AnchorGroupPlayer Groups { get; private set; }
        public AssetEventDispatcher Dispatcher { get; private set; }
        // Prefabs.Events(OnSpawn/OnDestroy)を SE/VFX/配置セットへ配線する 2 つ目の Dispatcher
        // (Anim.Events とは別セッション空間のため、同じ Dispatcher に相乗りさせず独立させる。[07] B-3)。
        public AssetEventDispatcher PrefabDispatcher { get; private set; }
        // Ui.Events(OnSpawn/OnEnable/OnDisable/OnDestroy)を SE/VFX へ配線する 3 つ目の Dispatcher([07] A-3)。
        public AssetEventDispatcher UiDispatcher { get; private set; }

        // カタログ登録が終わったか(IsReady 前の Play は未登録 ID として Placeholder になる)。
        public bool IsReady { get; private set; }
        public int RegisteredCatalogCount { get; private set; }
        public event Action OnReady;

        private readonly UniTaskCompletionSource _ready = new();
        private AnchorGroupLoopAdapter _groupAdapter;
        private UnscaledCameraFxAdapter _cameraFxAdapter;
        private bool _built;

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
            NetBridge = new LocalLoopbackBridge(); // NGO 統合([14])時に差し替える注入点

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
            // [11_tasks.md] 5-1 — Loop.TimeService を渡すことで、HitStop トラックが TimeService.HitStop を
            // 呼ぶだけで AtTime の進行も(他の全 Manager と同じく)自動的に止まる(GameLoopDriver が
            // ScaledDeltaTime を配るため、Presentation 側で特別な配線は不要)。
            // [14_networking.md] §5(5-8/5-9) — Audio/Vfx/Prefabs と同じく NetBridge を渡す(現状は
            // LocalLoopbackBridge のため常に完全ローカル。NGO 統合は Phase 6 でここを差し替える)。
            Presentation = new PresentationManager(Registry, Loop.TimeService, Audio, Bgm, Vfx, Anim, Ui, UiTweens, CameraFx, Haptics, NetBridge);
            Dispatcher = new AssetEventDispatcher(Anim.Events, Registry, Audio, Vfx, Anim.GetContextTransform, Groups);
            PrefabDispatcher = new AssetEventDispatcher(Prefabs.Events, Registry, Audio, Vfx, Prefabs.GetContextTransform, Groups);
            UiDispatcher = new AssetEventDispatcher(Ui.Events, Registry, Audio, Vfx, Ui.GetContextTransform, Groups);

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
                Runtime.Presentation.Presentation.Bind(Presentation); // [11_tasks.md] 5-1
            }

            _built = true;
        }

        private AudioSource CreateAudioChannel(string channelName)
        {
            var go = new GameObject(channelName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<AudioSource>();
        }

        private void Teardown()
        {
            if (!_built)
            {
                return;
            }

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
            if (Catalogs != null)
            {
                foreach (var catalog in Catalogs)
                {
                    if (catalog == null)
                    {
                        continue;
                    }

                    await Registry.RegisterCatalogAsync(catalog);
                    count++;
                }
            }

            count += await RegisterLabeledCatalogsAsync();

            RegisteredCatalogCount = count;
            IsReady = true;
            _ready.TrySetResult();
            OnReady?.Invoke();
            if (count == 0)
            {
                Debug.LogWarning("[DDrive] カタログが 1 つも登録されていません。Inspector の Catalogs か Addressables のラベルを設定してください(全 ID が Placeholder になります)。");
            }
        }

        private async UniTask<int> RegisterLabeledCatalogsAsync()
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
