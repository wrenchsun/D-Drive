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
    // [1014_extension_platform.md] §2.3(X-3、X-R-04 で任意引数 17 個からこの形に置き換え) — DDriveBootstrapContext.ForEditorPreview /
    // EditorModuleHost に渡す指定。プロパティで必要なものだけ設定する(省略したものは null。PauseService / Events は省略時に新規)。
    // 項目の追加 = プロパティの追加(互換性ポリシー上は MINOR)。DDriveBootstrapContext の項目を増やすときはここにも足す。
    public sealed class EditorPreviewContextOptions
    {
        public IAssetRegistry Registry { get; set; }
        public IPoolService Pool { get; set; }
        // EditorModuleHost は自分の GameLoop を使うので、ここで指定しても無視される(ForEditorPreview に直接渡すときだけ有効)。
        public GameLoop GameLoop { get; set; }
        public PauseService PauseService { get; set; }
        public ITimeSource TimeSource { get; set; }
        public EventBus Events { get; set; }
        public INetBridge NetBridge { get; set; }

        public AudioManager Audio { get; set; }
        public BgmManager Bgm { get; set; }
        public VfxManager Vfx { get; set; }
        public AnimManager Anim { get; set; }
        public MaterialManager Materials { get; set; }
        public ModelsManager Models { get; set; }
        public PrefabsManager Prefabs { get; set; }
        public UiManager Ui { get; set; }
        public UiTweenManager UiTweens { get; set; }
        public CameraFxManager CameraFx { get; set; }
        public HapticsManager Haptics { get; set; }
        public PresentationManager Presentation { get; set; }
        public CutsceneManager Cutscene { get; set; }

        // 浅いコピー(EditorModuleHost が GameLoop だけ差し替えるのに使う)。
        public EditorPreviewContextOptions Clone() => (EditorPreviewContextOptions)MemberwiseClone();
    }
}
