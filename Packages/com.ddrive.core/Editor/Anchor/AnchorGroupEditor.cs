using System;
using System.Collections.Generic;
using DDrive.Editor.Anim;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anchor
{
    // Anchor Group(配置セット = AnchorGroupData)のエディター(docs/1010 UX-11。旧 AnchorGroupEditorWindow は Archive メニューへ退避)。
    // 共通基盤(UxEditorWindow<AnchorGroupData> / FieldGuide / UxRow / UxCard)で組み、ここに書くのは配置セット固有の部分だけ:
    // 欄の表(AnchorGroupFieldGuide)・点の一覧(1 点 = 1 行、CanvasItemList)・配置パターンごとの欄の出し分け・手置きへの変換・試し出し(実 Manager 経由 = ADR-4)・
    // 原点の連鎖(親子合成の確認)・SceneView の全点表示とハンドル。
    // 試し出しは SceneVfxPreviewDriver / PreviewService が実 VfxManager / AudioManager を駆動する(計画は AnchorGroupPlanner)。
    [DataEditor(typeof(AnchorGroupData), "Anchor Group Editor で開く")]
    internal sealed partial class AnchorGroupEditor : UxEditorWindow<AnchorGroupData>
    {
        // 空状態の「サンプルを作る」(docs/1010 UX-19)
        protected override bool HasSample => true;

        protected override AnchorGroupData CreateSample() => UxSamples.AnchorGroup();

        private const string Page = "anchor-group";
        private static readonly string[] PointKinds = { "パターン", "手置き" };

        [SerializeField] private GameObject _attachTarget;
        [SerializeField] private bool _sceneHandle = true;
        [SerializeField] private int _selectedIndex = -1;

        private SceneVfxPreviewDriver _driver;
        private PreviewService _sePreview;
        private readonly List<Handle<VfxMarker>> _handles = new List<Handle<VfxMarker>>();
        private readonly List<AnchorGroupAction> _plan = new List<AnchorGroupAction>();
        private readonly AnchorSpawnSpec[] _specs = new AnchorSpawnSpec[AnchorGroupData.MaxPoints];
        private int _pointCount;

        private VfxAnchorToolsRow _toolsRow;
        private HelpBox _sceneHelp;
        private string _sceneHelpKey;
        private Label _selectionLabel;
        private Button _overrideButton;
        private CanvasItemList _pointList;
        private AnimSyncBox _pointBox;
        private readonly Dictionary<int, VisualElement> _pointElements = new Dictionary<int, VisualElement>();
        private readonly List<ConditionalRow> _conditional = new List<ConditionalRow>();
        private List<AnchorData> _originChain;
        private bool _handleDirty;

        private struct ConditionalRow
        {
            public UxRow Row;
            public Func<bool> Visible;
        }

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "Anchor Group")]
        public static void OpenFromMenu() => Open(Selection.activeObject as AnchorGroupData);

        public static void Open(AnchorGroupData target)
        {
            var w = OpenWindow<AnchorGroupEditor>("Anchor Group Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // PresentationEditor の AnchorGroup トラックの「一緒に調整」から開くときに使う(docs/08 指摘3/4。確認用シーンを開き直さない・プレビューを止めない)。
        // attachTarget は「スポーン先」欄にそのまま渡すだけ(原点の Space / Path の検索起点)。
        public static void Open(AnchorGroupData target, GameObject attachTarget)
        {
            var w = OpenWindow<AnchorGroupEditor>("Anchor Group Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }

            w._attachTarget = attachTarget;
            w._toolsRow?.Sync();
            SceneView.RepaintAll();
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "AnchorGroup";
        protected override FieldGuide<AnchorGroupData> Guide => AnchorGroupFieldGuide.Instance;
        protected override string ManualPage => Page;
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("d_Grid.Default").image;
        protected override string SearchHint => "ラベル・説明・検索語(点の一覧の中身も)に一致する設定だけ出す(例: 格子 / 半径 / 上書き / 入れ子)";
        protected override bool ValidationIncludesSameTypeAssets => true;
        protected override bool SupportsPlayback => true;
        protected override bool SupportsSpeed => false;

        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("点が", nameof(AnchorGroupData.Layout)),
            new KeyValuePair<string, string>("Circle の半径", nameof(AnchorGroupData.CircleRadius)),
            new KeyValuePair<string, string>("Line の長さ", nameof(AnchorGroupData.LineLength)),
            new KeyValuePair<string, string>("原点の", nameof(AnchorGroupData.Origin)),
            new KeyValuePair<string, string>("出すもの", nameof(AnchorGroupData.SharedVfx)),
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => MessageTokens;

        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case AnchorGroupFieldGuide.PurTry: return "try";
                case AnchorGroupFieldGuide.PurPoints: return "points";
                case AnchorGroupFieldGuide.PurLayout: return "layout";
                case AnchorGroupFieldGuide.PurOrigin: return "origin";
                case AnchorGroupFieldGuide.PurContent: return "content";
                case AnchorGroupFieldGuide.PurTiming: return "timing";
                case FieldGuide<AnchorGroupData>.AdminPurpose: return "admin";
                default: return null;
            }
        }

        protected override string[] ModeTips => new[]
        {
            "試し出しと点の一覧だけ。点を選んで ▶ で確かめる",
            "よく使う欄まで。配置パターン・原点・出すもの・ディレイと確率",
            "すべての欄(パターンごとの細かい項目・ばらつき)",
        };

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case AnchorGroupFieldGuide.PurTry: return "全点、または選んだ 1 点だけを実際に出す。スポーン先と今の状態";
                case AnchorGroupFieldGuide.PurPoints: return "パターンと手置きの全点を番号順に 1 行ずつ。選択・上書き・1 点だけ試し出し";
                case AnchorGroupFieldGuide.PurLayout: return "点をどう並べるか(格子・円・直線・ランダム・手置き)";
                case AnchorGroupFieldGuide.PurOrigin: return "配置全体の基準(Anchor アセットまたは埋め込みの原点)";
                case AnchorGroupFieldGuide.PurContent: return "各点に何を出すか(共通・点ごとの上書き・入れ子の配置セット)";
                case AnchorGroupFieldGuide.PurTiming: return "点ごとにずらす・遅らせる・間引く";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // ── ライフサイクル・再生(実 Manager) ──

        private Transform AttachTransform => _attachTarget != null ? _attachTarget.transform : null;

        protected override void OnWindowEnable()
        {
            _driver = new SceneVfxPreviewDriver();
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
        }

        protected override void OnWindowDisable()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
            _driver?.Dispose();
            _driver = null;
            _sePreview?.Dispose();
            _sePreview = null;
            _handles.Clear();
            SceneView.RepaintAll();
        }

        private void OnSceneChanged(UnityEngine.SceneManagement.Scene a, UnityEngine.SceneManagement.Scene b)
        {
            _handles.Clear();
            ResetPlayback();
        }

        protected override void OnTargetChanged()
        {
            _selectedIndex = -1;
            _originChain = null;
            _handles.Clear();
            _sePreview?.StopAll();
        }

        protected override void OnBuildBodyStart()
        {
            _toolsRow = null;
            _sceneHelp = null;
            _sceneHelpKey = null;
            _selectionLabel = null;
            _overrideButton = null;
            _pointList = null;
            _pointBox = null;
            _pointElements.Clear();
            _conditional.Clear();
        }

        protected override bool IsPlaying
        {
            get
            {
                if (_driver == null)
                {
                    return false;
                }

                for (var i = 0; i < _handles.Count; i++)
                {
                    if (_driver.IsPlaying(_handles[i]))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        protected override void StartPlayback(bool focus) => PlayPreview(-1);

        protected override void StopPlayback()
        {
            if (_driver != null)
            {
                for (var i = 0; i < _handles.Count; i++)
                {
                    _driver.Kill(_handles[i]);
                }
            }

            _handles.Clear();
            _sePreview?.StopAll();
        }

        // onlyIndex = -1 なら全点、それ以外は番号が一致する点(入れ子の子はその点番号を持つ)だけ。
        private void PlayPreview(int onlyIndex)
        {
            if (Target == null || _driver == null)
            {
                return;
            }

            StopPlayback();
            EditorAnchorRegistry.Refresh(_driver.Registry);
            _plan.Clear();
            AnchorGroupPlanner.Plan(_driver.Registry, Target, sampleRandom: true, _plan);
            var attach = AttachTransform;

            foreach (var action in _plan)
            {
                if (onlyIndex >= 0 && action.PointIndex != onlyIndex)
                {
                    continue;
                }

                if (action.Kind == AnchorGroupActionKind.Vfx)
                {
                    var data = _driver.Registry.ResolveOrPlaceholder<VfxData>(action.AssetId);
                    var h = _driver.Play(data, attach, action.Spec);
                    if (_driver.IsPlaying(h))
                    {
                        _handles.Add(h);
                    }
                }
                else
                {
                    if (_sePreview == null)
                    {
                        _sePreview = new PreviewService();
                        _sePreview.Initialize();
                    }

                    var data = _driver.Registry.ResolveOrPlaceholder<SeData>(action.AssetId);
                    _sePreview.PlaySe(data, attach, action.Spec);
                }
            }

            _plan.Clear();
        }

        protected override void RestartPlayback() => PlayPreview(-1);

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
            _originChain = null; // 原点の Anchor が他のウィンドウで変わっても約 1 秒で追従
            RefreshSceneHelp();
        }

        protected override void OnTick()
        {
            RefreshPoints();
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

        protected override void OnSideEffect() => RefreshPoints();

        protected override void OnPropertyEdited(string path)
        {
            if (path.StartsWith("OriginAnchorId"))
            {
                _originChain = null;
                if (_driver != null)
                {
                    EditorAnchorRegistry.Refresh(_driver.Registry);
                }
            }

            RefreshPoints();
            SceneView.RepaintAll();
        }

        protected override void OnRowsRefreshed()
        {
            RefreshPoints();
            RefreshSelectionUi();
            SceneView.RepaintAll();
        }

        // 配置パターンごとの欄(Grid の欄は Layout = Grid のときだけ…)と「手置きに変換」の出し分け。モード・検索の絞り込みのあとに隠す。
        protected override void OnFiltersApplied(string query)
        {
            _pointList?.SetExternalQuery(query);
            if (query.Length > 0)
            {
                return; // 検索中は一致した欄をすべて見せる
            }

            for (var i = 0; i < _conditional.Count; i++)
            {
                if (!_conditional[i].Visible())
                {
                    _conditional[i].Row.Show(false);
                }
            }
        }

        private void RefreshPoints()
        {
            _pointCount = Target != null ? AnchorGroupPlanner.EnumeratePoints(_driver?.Registry, Target, _specs) : 0;
            if (_selectedIndex >= _pointCount)
            {
                _selectedIndex = -1;
            }
        }

        private int ManualCount => Target?.Points?.Length ?? 0;

        private AnchorDef OriginDef()
        {
            if (Target.OriginAnchorId.IsValid && _driver != null)
            {
                return AnchorChain.Resolve(_driver.Registry, Target.OriginAnchorId, sampleRandom: false).Def;
            }

            return Target.Origin;
        }
    }
}
