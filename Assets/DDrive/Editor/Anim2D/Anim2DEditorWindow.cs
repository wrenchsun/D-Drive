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
    // Create: スプライト分割(Grid/Automatic/既存) → 命名 → AnimationClip 生成 → BlendTree 登録 →
    //         Anim2DData 自動生成(AssetCreationService、ID 発行)まで一括。
    // Edit  : 既存 Anim2DData / AnimationClip のスプライト配置(Retiming)を編集し、Clip に焼き込む。
    [DataEditor(typeof(Anim2DData), "Anim2D Editor で開く")]
    public sealed partial class Anim2DEditorWindow : EditorWindow
    {
        private enum WindowMode
        {
            Create,
            Edit,
        }

        [SerializeField] private WindowMode _mode = WindowMode.Create;

        private Anim2DImportProfile _profile;
        private VisualElement _createRoot;
        private VisualElement _editRoot;

        [MenuItem(DDriveMenu.Editors + "Animation (2D)")]
        public static void Open() => Open(Selection.activeObject as Anim2DData);

        public static void Open(Anim2DData target)
        {
            var window = GetWindow<Anim2DEditorWindow>("Anim2D Editor");
            window.minSize = new Vector2(480, 480);
            if (target != null)
            {
                window._mode = WindowMode.Edit;
                window.SetEditTarget(target);
            }

            window.Show();
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
            _profile = Anim2DImportProfile.FindOrDefault();

            var scroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rootVisualElement.Add(scroll);

            scroll.Add(new Label("D-Drive Anim2D Editor") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14, marginBottom = 6 } });

            var toolbar = new Toolbar();
            var createToggle = new ToolbarToggle { text = "作成", value = _mode == WindowMode.Create };
            var editToggle = new ToolbarToggle { text = "編集", value = _mode == WindowMode.Edit };
            createToggle.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue)
                {
                    return;
                }

                editToggle.SetValueWithoutNotify(false);
                _mode = WindowMode.Create;
                RefreshModeVisibility();
            });
            editToggle.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue)
                {
                    return;
                }

                createToggle.SetValueWithoutNotify(false);
                _mode = WindowMode.Edit;
                RefreshModeVisibility();
            });
            toolbar.Add(createToggle);
            toolbar.Add(editToggle);
            toolbar.Add(NewAssetToolbarButton.CreateToolbarButton(typeof(Anim2DEditorWindow)));
            scroll.Add(toolbar);

            var profileField = new ObjectField("Import Profile") { objectType = typeof(Anim2DImportProfile), value = _profile };
            profileField.RegisterValueChangedCallback(evt => _profile = evt.newValue as Anim2DImportProfile ?? Anim2DImportProfile.FindOrDefault());
            scroll.Add(profileField);

            _createRoot = new VisualElement();
            _editRoot = new VisualElement();
            scroll.Add(_createRoot);
            scroll.Add(_editRoot);

            BuildCreateSection(_createRoot);
            BuildEditSection(_editRoot);
            BuildValidationSection(scroll); // Create / Edit どちらのモードでも見える共通ルート

            RefreshModeVisibility();
        }

        // Anim Editor(共通イベントエディタ)から戻ってきたときにイベント要約・検証を最新化する。
        private void OnFocus()
        {
            RefreshEventSummary();
            RefreshValidation();
        }

        private void RefreshModeVisibility()
        {
            if (_createRoot == null || _editRoot == null)
            {
                return;
            }

            _createRoot.style.display = _mode == WindowMode.Create ? DisplayStyle.Flex : DisplayStyle.None;
            _editRoot.style.display = _mode == WindowMode.Edit ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
