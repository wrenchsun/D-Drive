using System.Collections.Generic;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using AnchorId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anchoring.AnchorMarker>;

namespace DDrive.Editor.Anchor
{
    // Anchor(AnchorData)のエディター(docs/1010 UX-11。旧 AnchorEditorWindow は Archive メニューへ退避)。
    // 共通基盤(UxEditorWindow<AnchorData> / FieldGuide / UxRow / UxCard / AnchorPad)で組み、ここに書くのは Anchor 固有の部分だけ:
    // 欄の表(AnchorFieldGuide)・位置のパッド(AnchorPad が主部品)・親 Anchor の連鎖・スポーン先の基準・試し出し(実 Manager 経由 = ADR-4)・
    // 既存ヒエラルキーから作る・SceneView の連鎖表示とハンドル(AnchorSceneHandles)。
    // 試し出しは SceneVfxPreviewDriver / PreviewService が実 VfxManager / AudioManager を駆動する。ウィンドウ自身は SceneView 以外に描かない。
    [DataEditor(typeof(AnchorData), "Anchor Editor で開く")]
    internal sealed partial class AnchorEditor : UxEditorWindow<AnchorData>
    {
        private const string Page = "anchor-data";

        [SerializeField] private GameObject _attachTarget;
        [SerializeField] private bool _sceneHandle = true;
        [SerializeField] private VfxData _testVfx;
        [SerializeField] private SeData _testSe;

        private SceneVfxPreviewDriver _driver;
        private PreviewService _sePreview;
        private Handle<VfxMarker> _handle = Handle<VfxMarker>.Invalid;

        private AnchorPad _pad;
        private VfxAnchorToolsRow _toolsRow;
        private HelpBox _sceneHelp;
        private string _sceneHelpKey;
        private Label _tryStatus;
        private List<AnchorData> _chain;
        private bool _handleDirty;

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "Anchor")]
        public static void OpenFromMenu() => Open(Selection.activeObject as AnchorData);

        public static void Open(AnchorData target)
        {
            var w = OpenWindow<AnchorEditor>("Anchor Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "Anchor";
        protected override FieldGuide<AnchorData> Guide => AnchorFieldGuide.Instance;
        protected override string ManualPage => Page;
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("d_Transform Icon").image;
        protected override string SearchHint => "ラベル・説明・検索語に一致する設定だけ出す(例: 高さ / ボーン / ランダム / 確率)";
        protected override bool ValidationIncludesSameTypeAssets => true;
        protected override bool SupportsPlayback => true;
        protected override bool SupportsSpeed => false;

        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("生成ディレイ", nameof(AnchorData.DelaySec)),
            new KeyValuePair<string, string>("入れ子が", nameof(AnchorData.Parent)),
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => MessageTokens;

        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case AnchorFieldGuide.PurPlace: return "place";
                case AnchorFieldGuide.PurTry: return "try";
                case AnchorFieldGuide.PurBase: return "base";
                case AnchorFieldGuide.PurChain: return "chain";
                case AnchorFieldGuide.PurRandom: return "random";
                case AnchorFieldGuide.PurCreate: return "create";
                case FieldGuide<AnchorData>.AdminPurpose: return "admin";
                default: return null;
            }
        }

        protected override string[] ModeTips => new[]
        {
            "位置のパッドと試し出しだけ。パッドで位置を決めて、確認用の VFX / SE を出して確かめる",
            "よく使う欄まで。基準・親 Anchor・向き・ばらつき・出る確率",
            "すべての欄(位置の全設定・大きさ・追従・ランダムの細かい項目)",
        };

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case AnchorFieldGuide.PurPlace: return "基準からどれだけずらして置くか(位置・向き・大きさ)";
                case AnchorFieldGuide.PurTry: return "確認用の VFX / SE を、この Anchor の位置で実際に出す";
                case AnchorFieldGuide.PurBase: return "何を基準にして置くか(ワールド・ボーン・スポーン先そのもの)";
                case AnchorFieldGuide.PurChain: return "親 Anchor の姿勢の上に、自分のオフセットを積む(入れ子)";
                case AnchorFieldGuide.PurRandom: return "出すたびにずらす・遅らせる・間引く";
                case AnchorFieldGuide.PurCreate: return "Hierarchy のボーン配下の空オブジェクトや AnchorRig から Anchor を作る";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // ── ライフサイクル・再生(実 Manager) ──

        private Transform AttachTransform => _attachTarget != null ? _attachTarget.transform : null;

        protected override void OnWindowEnable()
        {
            _driver = new SceneVfxPreviewDriver();
            _handle = Handle<VfxMarker>.Invalid;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
        }

        protected override void OnWindowDisable()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
            _driver?.Dispose();
            _driver = null;
            _sePreview?.Dispose();
            _sePreview = null;
            SceneView.RepaintAll();
        }

        private void OnSceneChanged(UnityEngine.SceneManagement.Scene a, UnityEngine.SceneManagement.Scene b)
        {
            _handle = Handle<VfxMarker>.Invalid;
            ResetPlayback();
        }

        protected override void OnTargetChanged()
        {
            _chain = null;
            _handle = Handle<VfxMarker>.Invalid;
            _sePreview?.StopAll();
        }

        protected override void OnBuildBodyStart()
        {
            _pad = null;
            _toolsRow = null;
            _sceneHelp = null;
            _sceneHelpKey = null;
            _tryStatus = null;
        }

        protected override bool IsPlaying => _driver != null && _driver.IsPlaying(_handle);

        protected override void StartPlayback(bool focus)
        {
            PlayVfx();
            PlaySe();
            if (_tryStatus != null && _testVfx == null && _testSe == null)
            {
                _tryStatus.text = "確認用の VFX か SE を選んでください";
            }
        }

        protected override void StopPlayback()
        {
            if (_driver != null && _driver.IsPlaying(_handle))
            {
                _driver.Kill(_handle);
            }

            _handle = Handle<VfxMarker>.Invalid;
            _sePreview?.StopAll();
            if (_tryStatus != null)
            {
                _tryStatus.text = string.Empty;
            }
        }

        // 連打: 前の実体を消して出し直す(ランダム・確率・ディレイを毎回サンプリングし直す)。
        private void PlayVfx()
        {
            if (Target == null || _testVfx == null || _driver == null)
            {
                return;
            }

            if (_driver.IsPlaying(_handle))
            {
                _driver.Kill(_handle);
            }

            EditorAnchorRegistry.Refresh(_driver.Registry);
            _handle = _driver.Play(_testVfx, AttachTransform, new AnchorId(Target.Id, AssetType.Anchor));
            if (_tryStatus != null)
            {
                _tryStatus.text = _driver.IsPlaying(_handle)
                    ? (_driver.Manager.IsPending(_handle) ? "● 生成待ち(Delay)" : "● 再生中")
                    : "(SpawnChance に外れた、または Prefab 未設定)";
            }
        }

        private void PlaySe()
        {
            if (Target == null || _testSe == null)
            {
                return;
            }

            if (_sePreview == null)
            {
                _sePreview = new PreviewService();
                _sePreview.Initialize();
            }

            _sePreview.PlaySe(_testSe, AttachTransform, new AnchorId(Target.Id, AssetType.Anchor));
        }

        protected override void RestartPlayback()
        {
            StopPlayback();
            PlayVfx();
            PlaySe();
        }

        // ── ヒーロー ──

        protected override void AddLeadingHeroActions(VisualElement actions) =>
            actions.Add(MakeButton("確認用シーン", null, () => VfxPreviewSceneSetup.OpenOrCreate(),
                "AnchorRig 配置済みの VFX 確認用シーンを開く(無ければ生成)"));

        protected override void AfterHero(VisualElement page)
        {
            _sceneHelp = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            _sceneHelp.AddToClassList("ux-scenehelp");
            _sceneHelp.Show(false);
            page.Add(_sceneHelp);
        }

        protected override void OnSlowTick()
        {
            _chain = null; // 親 Anchor が他のウィンドウで変わっても約 1 秒で追従
            RefreshSceneHelp();
        }

        protected override void OnTick()
        {
            // SceneView のハンドルのドラッグが終わったら(つかんでいるコントロールが無くなったら)検証と行の更新をまとめて 1 回。
            if (_handleDirty && GUIUtility.hotControl == 0)
            {
                _handleDirty = false;
                RunValidation();
                RefreshAll(true);
            }
        }

        private void RefreshSceneHelp()
        {
            if (_sceneHelp == null)
            {
                return;
            }

            var hasCamera = Camera.main != null || FindFirstObjectByType<Camera>() != null;
            var hasLight = FindFirstObjectByType<Light>() != null;
            string text = null;
            if (!hasCamera || !hasLight)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                text = $"開いているシーン '{scene.name}' に{(hasCamera ? string.Empty : "カメラ")}{(!hasCamera && !hasLight ? "・" : string.Empty)}{(hasLight ? string.Empty : "ライト")}がありません。試し出しの見た目確認には「確認用シーン」を開いてください。";
            }

            if (text == _sceneHelpKey)
            {
                return;
            }

            _sceneHelpKey = text;
            _sceneHelp.text = text ?? string.Empty;
            _sceneHelp.Show(!string.IsNullOrEmpty(text));
        }

        // ── 編集のあと ──

        protected override void OnSideEffect() => _driver?.ReapplyAnchorToAll();

        protected override void OnPropertyEdited(string path)
        {
            if (path.StartsWith("Parent"))
            {
                _chain = null;
            }

            if (path.StartsWith("Parent") || path == "Space" || path == "Path")
            {
                _driver?.ReapplyAnchorToAll();
                RestartIfPlaying();
            }
            else
            {
                _driver?.ReapplyAnchorToAll();
            }

            SceneView.RepaintAll();
        }

        protected override void OnRowsRefreshed()
        {
            RefreshPadNote();
            SceneView.RepaintAll();
        }

        // ルート → 対象の連鎖(キャッシュ。AssetDatabase を引くので毎フレームは作り直さない)。
        private List<AnchorData> Chain => _chain ??= Target != null ? AnchorChainEditor.CollectRootToTarget(Target) : new List<AnchorData>();

        private void RefreshPadNote()
        {
            if (_pad == null || Target == null)
            {
                return;
            }

            string note = null;
            if (Target.Parent.IsValid)
            {
                note = "親 Anchor の姿勢を基準にした相対位置(メートル)です。ルートの基準は「基準を決める」で決まります。";
            }
            else if (Target.Space != AnchorSpace.World)
            {
                note = $"基準が {Target.Space} です。位置は基準(スポーン先の「{Target.Path}」など)からの相対(メートル)になります。";
            }

            _pad.SetNote(note);
        }
    }
}
