using System;
using System.Collections.Generic;
using DDrive.Editor.Anim;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Materials
{
    // Material のエディター(docs/1010 UX-8。旧 MaterialEditorWindow は Archive メニューへ退避。TextureData は TextureEditor)。
    // 共通基盤(UxEditorWindow<MaterialData> / FieldGuide / UxRow / UxCard / TuneKnobs)で組み、ここに書くのは Material 固有の部分だけ:
    // 欄の表(MaterialFieldGuide)・Specific を調整つまみに(MaterialTuneSource)・シェーダーとの同期・パス / キーワード(MaterialPassKeywordView)・
    // サムネイルとシーン配置の確認(実 MaterialManager が作った共有 Material を使う。ADR-4)。
    // 外部契約(docs/42 §5.14 E-10〜E-15: _Toon* の扱い・MaterialCommonNaming の分類・Specific の Merge)は変えない。
    // 部品ごとにファイルを分けている: .Rows.cs = Specific・パス / キーワード・シェーダー同期 / .Preview.cs = サムネイル・シーン配置・比較。
    [DataEditor(typeof(MaterialData), "Material Editor で開く")]
    internal sealed partial class MaterialEditor : UxEditorWindow<MaterialData>
    {
        private const string Page = "material-editor";
        public const string PreviewRootName = "[D-Drive] Material Preview (UX)";

        // 再コンパイルを跨いで残す状態([SerializeField])。VisualElement / PreviewRenderUtility 等は対象外。
        [SerializeField] private MaterialPreviewShape _shape = MaterialPreviewShape.Sphere;
        [SerializeField] private ModelData _previewModel;
        [SerializeField] private MaterialData _compareTarget;
        [SerializeField] private bool _turntableEnabled;
        [SerializeField] private float _thumbnailAngle;
        [SerializeField] private float _thumbnailPitch;
        [SerializeField] private float _thumbnailLightDeg;
        [SerializeField] private CompareMode _compareMode = CompareMode.SideBySide;
        [SerializeField] private bool _showCompareInToggle;

        private MaterialManager _manager;
        private AssetRegistry _registry;
        private PoolService _pool;
        private ModelsManager _modelsManager;
        private Shader _trackedShader;
        private TuneKnobs _knobs;

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "Material")]
        public static void OpenFromMenu() => Open(Selection.activeObject as MaterialData);

        public static void Open(MaterialData target)
        {
            var w = OpenWindow<MaterialEditor>("Material Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "Material";
        protected override FieldGuide<MaterialData> Guide => MaterialFieldGuide.Instance;
        protected override string ManualPage => Page;
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("Material Icon").image;
        protected override string SearchHint => "ラベル・説明・検索語、固有のつまみの名前に一致する設定だけ出す(例: Toon / 影 / ShadowCaster)";

        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case MaterialFieldGuide.PurLook: return "look";
                case MaterialFieldGuide.PurSurface: return "surface";
                case MaterialFieldGuide.PurSpecific: return "specific";
                case MaterialFieldGuide.PurRender: return "render";
                case MaterialFieldGuide.PurAnim: return "anim";
                case MaterialFieldGuide.PurPass: return "pass";
                case MaterialFieldGuide.PurCheck: return "check";
                case FieldGuide<MaterialData>.AdminPurpose: return "admin";
                default: return null;
            }
        }

        protected override string[] ModeTips => new[]
        {
            "必須の欄だけ。シェーダー",
            "よく使う欄まで。ベースの絵と色・透け方・質感と発光・固有のつまみ・描画の順序・常時アニメ・パスとキーワード・サムネイル",
            "すべての欄(切り抜きのしきい値・凹凸の強さ・ライトレイヤー・取り込み元など)",
        };

        // 検証メッセージ(ValidationResult.Field が無い結果)を欄に結びつける語。上にあるものを先に当てる。
        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("Common.Albedo", "Common.Albedo"),
            new KeyValuePair<string, string>("RenderingLayerMask", "RenderingLayerMask"),
            new KeyValuePair<string, string>("RenderQueue", "RenderQueueOffset"),
            new KeyValuePair<string, string>("EmissionIntensity", "Common.EmissionIntensity"),
            new KeyValuePair<string, string>("Specific", "Specific"),
            new KeyValuePair<string, string>("DisabledPasses", "DisabledPasses"),
            new KeyValuePair<string, string>("EnabledKeywords", "EnabledKeywords"),
            new KeyValuePair<string, string>("Anims", "Anims"),
            new KeyValuePair<string, string>("Shader", "Shader"),
            new KeyValuePair<string, string>("シェーダー", "Shader"),
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => MessageTokens;

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case MaterialFieldGuide.PurLook: return "どのシェーダーで、どんな絵と色で、どう透けるか";
                case MaterialFieldGuide.PurSurface: return "凹凸・金属っぽさ・つや・光る部分";
                case MaterialFieldGuide.PurSpecific: return "シェーダー専用のつまみ。1 つ = 1 行で、分類のチップで絞れます";
                case MaterialFieldGuide.PurRender: return "半透明どうしの前後など、描く順序の調整";
                case MaterialFieldGuide.PurAnim: return "UV スクロール・点滅など、出している間ずっと動かす設定";
                case MaterialFieldGuide.PurPass: return "描かないパス(影など)と、ON にするシェーダーキーワード";
                case MaterialFieldGuide.PurCheck: return "実際の Manager で作った Material を、この窓のサムネイルとシーンで確かめる";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // ── ヒーロー ──

        protected override void AddLeadingHeroActions(VisualElement actions)
        {
            var scene = PreviewPlacementButton.Create(
                "確認用シーン",
                "確認用シーンを開き(無ければ生成)、選んだ形に Material を貼って置く。右クリックで、いま開いているシーンへ配置",
                OpenPreviewScene);
            scene.AddToClassList("ux-btn");
            actions.Add(scene);
            actions.Add(MakeButton("シェーダー変換…", null, () => MaterialConvertWindow.Open(Target), "別のシェーダーへ乗り換える変換ウィンドウを開く(変換前後の見た目比較つき)"));
        }

        // ── ライフサイクル ──

        protected override void OnWindowEnable()
        {
            _registry = EditorAnchorRegistry.Build();
            _manager = new MaterialManager(_registry);
            _pool = new PoolService();
            _modelsManager = new ModelsManager(_pool, _registry, null, _manager);
            _lastTickTime = EditorApplication.timeSinceStartup;
            _registryDirty = false;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.projectChanged += OnProjectChanged;
            ObjectChangeEvents.changesPublished += OnObjectChanges;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
        }

        protected override void OnWindowDisable()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            ObjectChangeEvents.changesPublished -= OnObjectChanges;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.update -= OnEditorUpdate;
            RemovePreview();
            _thumbnail?.Dispose();
            _thumbnail = null;
            _compareThumbnail?.Dispose();
            _compareThumbnail = null;
            _manager?.Clear();
            _manager = null;
            _modelsManager = null;
            _pool = null;
        }

        protected override void OnTargetChanged()
        {
            _trackedShader = Target != null ? Target.Shader : null;
            _lastDirtyCount = Target != null ? EditorUtility.GetDirtyCount(Target) : 0;
            _previewDirty = true;
            _thumbnailDirty = true;
            _specStatusSig = int.MinValue;
            if (_primaryPreview != null)
            {
                RebuildPreview();
            }
        }

        protected override void OnBuildBodyStart()
        {
            _knobs = null;
            _specificLabel = null;
            _removeConflictsButton = null;
            _passKeyword = null;
            _thumbnailImage = null;
            _compareThumbnailImage = null;
            _thumbnailCaptionA = null;
            _thumbnailCaptionB = null;
            _thumbnailLabel = null;
            _compareColumn = null;
            _compareControls = null;
            _compareModeButton = null;
            _abButton = null;
            _modelField = null;
            _modelLine = null;
            _lightRotationSlider = null;
            _sceneStatus = null;
        }

        // 既定はすべて折りたたみ: 構造体・配列の欄(PropertyField)が作る Foldout も、組み終えたあとに閉じる。
        protected override void OnBodyBuilt(VisualElement page)
        {
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(30);
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(300);
            _thumbnailDirty = true;
            UpdateCompareUi();
        }

        private static void CollapseFoldouts(VisualElement page)
        {
            foreach (var f in page.Query<Foldout>().ToList())
            {
                if (f.value)
                {
                    f.SetValueWithoutNotify(false);
                }
            }
        }

        protected override void OnSideEffect()
        {
            _previewDirty = true;
        }

        protected override void OnPropertyEdited(string path)
        {
            _previewDirty = true;
        }

        protected override void OnFiltersApplied(string query)
        {
            _knobs?.SetQuery(query);
        }

        // 定期更新(150ms): Shader が変わったら固有を自動で同期する。
        protected override void OnTick()
        {
            if (Target == null || So == null)
            {
                return;
            }

            if (Target.Shader != _trackedShader)
            {
                _trackedShader = Target.Shader;
                var mat = Target;
                // Undo 処理中に Undo を積まないよう 1 フレーム遅らせる。
                EditorApplication.delayCall += () =>
                {
                    if (Target == mat && mat != null)
                    {
                        SyncSpecificFromShader();
                    }
                };
            }
        }

        // ── 欄 ──

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            switch (e.Field)
            {
                case "Specific":
                    return CreateSpecificRow(e);
                case "Common.Albedo":
                case "Common.Normal":
                case "Common.Mask":
                case "Common.Emission":
                    // TextureData の ID 欄(AssetId)は 1 行の選び箱。構造体でも縦に積まず、他の欄と同じ 1 行にする。
                    return new UxRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, false, GuidedField.MakeInput(So, e.Field), RowChanged);
                case MaterialFieldGuide.RowSpecificTools:
                    return CreateSpecificToolsRow(e);
                case "DisabledPasses":
                case "EnabledKeywords":
                    return null; // (passkw) の行が両方まとめて見せる
                case MaterialFieldGuide.RowPassKeyword:
                    return CreatePassKeywordRow(e);
                case MaterialFieldGuide.RowThumb:
                    return CreateThumbnailRow(e);
                case MaterialFieldGuide.RowShape:
                    return CreateShapeRow(e);
                case MaterialFieldGuide.RowCompare:
                    return CreateCompareRow(e);
                case MaterialFieldGuide.RowScene:
                    return CreateSceneRow(e);
                default:
                    return base.CreateRow(e, card, isAdmin);
            }
        }

        // 外から Data を書いたあと: 検証・行の状態・つまみを最新にする。
        private void AfterDataWrite()
        {
            if (Target == null)
            {
                return;
            }

            RefreshAfterExternalWrite();
            _knobs?.Sync();
            _previewDirty = true;
            SceneView.RepaintAll();
        }

        private static void SetLabel(Label l, string text)
        {
            if (l != null && l.text != text)
            {
                l.text = text;
            }
        }

        private static string DisplayNameOf(MaterialData data)
            => data == null ? string.Empty : string.IsNullOrEmpty(data.DisplayName) ? data.name : data.DisplayName;
    }
}
