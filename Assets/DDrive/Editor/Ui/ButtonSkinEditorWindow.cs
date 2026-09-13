using DDrive.Editor.Menu;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ui
{
    // [15_ui_interaction.md] / [18_ui_controls.md] Part A — ButtonSkinData 専用エディタ(4-6)。
    // プロジェクト方針(2026-09-10)によりウィンドウ内では何も描画しない。SerializedObject をそのまま
    // InspectorElement で表示し、確認は「確認用シーンに配置」で開いているシーン/Game ビュー側に出す(ADR-4)。
    [DDrive.Editor.Inspector.DataEditor(typeof(ButtonSkinData), "Skin Editor で開く")]
    public sealed class ButtonSkinEditorWindow : EditorWindow
    {
        private const string PreviewCanvasName = "[D-Drive] Ui Preview";

        [SerializeField] private ButtonSkinData _target;

        private ObjectField _targetField;
        private VisualElement _inspectorContainer;
        private ControlSkinPreviewSection _previewSection;
        private UiButton _previewButton;

        [MenuItem(DDriveMenu.Editors + "Button Skin")]
        public static void OpenFromMenu() => Open(Selection.activeObject as ButtonSkinData);

        public static void Open(ButtonSkinData target)
        {
            var window = GetWindow<ButtonSkinEditorWindow>("Button Skin Editor");
            window.minSize = new Vector2(360, 320);
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        private void CreateGUI()
        {
            // ルートをスクロール可能にする([09_editor_tools.md] §7)。
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);
            scrollView.style.paddingLeft = 6;
            scrollView.style.paddingRight = 6;
            scrollView.style.paddingTop = 6;

            _targetField = new ObjectField("対象 Skin") { objectType = typeof(ButtonSkinData) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as ButtonSkinData));
            scrollView.Add(_targetField);

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 4 } };
            row.Add(new UnityEngine.UIElements.Button(PlaceInScene) { text = "確認用シーンに配置" });
            row.Add(new UnityEngine.UIElements.Button(RemoveFromScene) { text = "撤去" });
            scrollView.Add(row);

            _previewSection = new ControlSkinPreviewSection(
                EnsurePreview,
                new ControlSkinPreviewSection.SeField("Hover Se", s => ((ButtonSkinData)s).HoverSe),
                new ControlSkinPreviewSection.SeField("Click Se", s => ((ButtonSkinData)s).ClickSe),
                new ControlSkinPreviewSection.SeField("Long Press Se", s => ((ButtonSkinData)s).LongPressSe),
                new ControlSkinPreviewSection.SeField("Denied Se", s => ((ButtonSkinData)s).DeniedSe));
            scrollView.Add(_previewSection);

            _inspectorContainer = new VisualElement();
            scrollView.Add(_inspectorContainer);

            if (_target == null && Selection.activeObject is ButtonSkinData selected)
            {
                SetTarget(selected);
            }
            else
            {
                _targetField.SetValueWithoutNotify(_target);
                RebuildInspector();
                _previewSection.SetSkin(_target);
            }
        }

        private void SetTarget(ButtonSkinData target)
        {
            _target = target;
            _targetField?.SetValueWithoutNotify(_target);
            RebuildInspector();
            _previewSection?.SetSkin(_target);
            if (_previewButton != null && _target != null)
            {
                _previewButton.SetVisual(_target);
            }
        }

        private UiInteractable EnsurePreview()
        {
            if (_previewButton == null)
            {
                PlaceInScene();
            }

            return _previewButton;
        }

        private void RebuildInspector()
        {
            if (_inspectorContainer == null)
            {
                return;
            }

            _inspectorContainer.Clear();
            if (_target == null)
            {
                _inspectorContainer.Add(new Label("ButtonSkinData を選択してください"));
                return;
            }

            var so = new SerializedObject(_target);
            _inspectorContainer.Add(new InspectorElement(so));
        }

        // Data 自体は編集しない。実配置での見た目確認だけをシーン上で行う(ADR-4: プレビューは実 Manager/実コンポーネントを駆動する)。
        private void PlaceInScene()
        {
            if (_target == null)
            {
                return;
            }

            RemoveFromScene();

            var canvasGo = new GameObject(PreviewCanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
            {
                hideFlags = HideFlags.DontSave,
            };
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var buttonGo = new GameObject("PreviewButton", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UiButton));
            buttonGo.transform.SetParent(canvasGo.transform, false);
            ((RectTransform)buttonGo.transform).sizeDelta = new Vector2(200f, 60f);

            var button = buttonGo.GetComponent<UiButton>();
            button.TargetGraphic = buttonGo.GetComponent<UnityEngine.UI.Image>();
            button.SetVisual(_target);
            _previewButton = button;

            Selection.activeGameObject = buttonGo;
        }

        private void RemoveFromScene()
        {
            var existing = GameObject.Find(PreviewCanvasName);
            if (existing != null)
            {
                DestroyImmediate(existing);
            }

            _previewButton = null;
        }
    }
}
