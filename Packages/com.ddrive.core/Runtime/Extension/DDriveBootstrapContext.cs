using DDrive.Foundation.Event;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using DDrive.Runtime.Prefab;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;

namespace DDrive.Runtime.Extension
{
    // [1014_extension_platform.md] §2.2(X-2) — IDDriveModule.CreateManagers に渡す、Bootstrap が持つ部品への読み取り参照。
    // 将来の追加に耐えるよう get 専用プロパティ + internal コンストラクタ(Bootstrap だけが作る)。
    // 既存 Manager の参照は「組み込み Manager の生成が済んだ後」の値(module は組み込みの後に呼ばれる)。
    // Editor プレビュー(IsEditorPreview = true)では、プレビューが持っていない部品は null になりうる。
    public readonly struct DDriveBootstrapContext
    {
        public IAssetRegistry Registry { get; }
        public IPoolService Pool { get; }
        public ITimeSource TimeSource { get; }
        public PauseService PauseService { get; }
        // module 間で共有できる EventBus(Bootstrap が 1 つ作る。組み込み Manager のものとは別)。組み込みの AssetEventDispatcher
        // (Data.Events → SE / VFX)には繋がっていない。AssetEvent を鳴らしたい module は自分の Manager で Dispatcher を作って購読する(docs/1014 X-R-07)。
        public EventBus Events { get; }
        // null 可(Editor プレビューなど)。
        public INetBridge NetBridge { get; }
        public GameLoop GameLoop { get; }
        public bool IsEditorPreview { get; }

        public AudioManager Audio { get; }
        public BgmManager Bgm { get; }
        public VfxManager Vfx { get; }
        public AnimManager Anim { get; }
        public MaterialManager Materials { get; }
        public ModelsManager Models { get; }
        public PrefabsManager Prefabs { get; }
        public UiManager Ui { get; }
        public UiTweenManager UiTweens { get; }
        public CameraFxManager CameraFx { get; }
        public HapticsManager Haptics { get; }
        public PresentationManager Presentation { get; }
        public CutsceneManager Cutscene { get; }

        // [1014] §2.3(X-3) — Editor プレビュー用の Context(IsEditorPreview = true)。持ち込み先の窓が、確認用の Manager 群を
        // 自前で組むときに module へ渡す。options で渡さなかった組み込み Manager は null(module は null を許容して書くこと)。
        // X-R-04: 以降の項目追加は EditorPreviewContextOptions のプロパティ追加で済む(この関数のシグネチャは変わらない)。
        public static DDriveBootstrapContext ForEditorPreview(EditorPreviewContextOptions options)
        {
            options ??= new EditorPreviewContextOptions();
            return new DDriveBootstrapContext(
                options.Registry, options.Pool, options.TimeSource, options.PauseService ?? new PauseService(), options.Events ?? new EventBus(),
                options.NetBridge, options.GameLoop, true,
                options.Audio, options.Bgm, options.Vfx, options.Anim, options.Materials, options.Models, options.Prefabs, options.Ui,
                options.UiTweens, options.CameraFx, options.Haptics, options.Presentation, options.Cutscene);
        }

        internal DDriveBootstrapContext(
            IAssetRegistry registry, IPoolService pool, ITimeSource timeSource, PauseService pauseService,
            EventBus events, INetBridge netBridge, GameLoop gameLoop, bool isEditorPreview,
            AudioManager audio, BgmManager bgm, VfxManager vfx, AnimManager anim, MaterialManager materials,
            ModelsManager models, PrefabsManager prefabs, UiManager ui, UiTweenManager uiTweens,
            CameraFxManager cameraFx, HapticsManager haptics, PresentationManager presentation, CutsceneManager cutscene)
        {
            Registry = registry;
            Pool = pool;
            TimeSource = timeSource;
            PauseService = pauseService;
            Events = events;
            NetBridge = netBridge;
            GameLoop = gameLoop;
            IsEditorPreview = isEditorPreview;
            Audio = audio;
            Bgm = bgm;
            Vfx = vfx;
            Anim = anim;
            Materials = materials;
            Models = models;
            Prefabs = prefabs;
            Ui = ui;
            UiTweens = uiTweens;
            CameraFx = cameraFx;
            Haptics = haptics;
            Presentation = presentation;
            Cutscene = cutscene;
        }
    }
}
