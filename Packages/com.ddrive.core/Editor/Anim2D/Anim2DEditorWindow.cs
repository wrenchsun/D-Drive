using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Runtime.Anim2D;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim2D
{
    // [05_model_animation.md] Part C — Anim2DEditor(チケット 3-11)。
    // 既存ツール Katsuya.Tools.SpriteAnimation.SpriteAnimationToolWindow(IMGUI, 3355 行)の
    // Create / Edit フローを D-Drive の UI Toolkit 規約(09_editor_tools.md §6-7)に移植する。
    // Sequence Preview / Sound モードは共通 AssetEvent・共通プレビュー(チケット 3-13)に置換予定のため今回は対象外。
    //
    // U-8(2026-09-17): 以前は「作成」/「編集」をタブ(ToolbarToggle)で切り替えていたが、作成はタブに分ける
    // 必要が無いため Anim2DCreateWindow(ポップアップ、NewAssetDialog と同じ GetWindow<T>(utility: true, ...)
    // の作法)に切り出した。このウィンドウは常に編集(Edit)フローだけを表示する。
    // Create: スプライト分割(Grid/Automatic/既存) → 命名 → AnimationClip 生成 → BlendTree 登録 →
    //         Anim2DData 自動生成(AssetCreationService、ID 発行)まで一括 → Anim2DCreateWindow 側の実装。
    // Edit  : 既存 Anim2DData / AnimationClip のスプライト配置(Retiming)を編集し、Clip に焼き込む(このウィンドウ)。
    // 【アーカイブ】docs/1010 UX-7: 新デザインの Anim2DEditor(Editor/Anim2D/Anim2DEditor*.cs)に置き換え、メニューは Tools > D-Drive > Archive へ退避した。
    // 旧エディターへの機能追加はしない(バグ修正のみ)。
    [DataEditor(typeof(Anim2DData), "(旧) Anim2D Editor で開く", Order = 100)]
    public sealed partial class Anim2DEditorWindow : EditorWindow
    {
        private VisualElement _editRoot;

        [MenuItem(DDriveMenu.Archive + "Animation (2D)")]
        public static void Open() => Open(Selection.activeObject as Anim2DData);

        public static void Open(Anim2DData target)
        {
            var window = GetWindow<Anim2DEditorWindow>("(旧) Anim2D Editor");
            window.minSize = new Vector2(480, 480);
            if (target != null)
            {
                window.SetEditTarget(target);
            }

            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdatePreview;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdatePreview;
            DisposeScenePreview();
        }

        public void CreateGUI()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rootVisualElement.Add(scroll);

            scroll.Add(new Label("D-Drive (旧) Anim2D Editor") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14, marginBottom = 6 } });

            var toolbar = new Toolbar();
            // U-8(2026-09-17): 「作成」はタブではなくポップアップ(Anim2DCreateWindow)にした。
            toolbar.Add(new ToolbarButton(Anim2DCreateWindow.Open)
            {
                text = "スプライトから新規作成…",
                tooltip = "スプライト分割 → 命名 → AnimationClip 生成 → BlendTree 登録 → Anim2DData 作成をポップアップで行う",
            });
            toolbar.Add(NewAssetToolbarButton.CreateToolbarButton(typeof(Anim2DEditorWindow)));
            scroll.Add(toolbar);

            _editRoot = new VisualElement();
            scroll.Add(_editRoot);

            BuildEditSection(_editRoot);
            BuildValidationSection(scroll);
        }

        // Anim Editor(共通イベントエディタ)から戻ってきたときにイベント要約・検証を最新化する。
        private void OnFocus()
        {
            RefreshEventSummary();
            RefreshValidation();
        }
    }
}
