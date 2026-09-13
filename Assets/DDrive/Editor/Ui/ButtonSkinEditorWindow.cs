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
    // プロジェクト方針(2026-09-10)によりウィンドウ内では何も描画しない。設定欄は ControlSkinPreviewSection
    // (各状態の演出欄の横に再生、各 SE 欄の横に試聴)で、確認は「確認用シーンに配置」した実 UiButton を
    // シーン/Game ビュー側で見る(ADR-4)。
    [DDrive.Editor.Inspector.DataEditor(typeof(ButtonSkinData), "Skin Editor で開く")]
    public sealed class ButtonSkinEditorWindow : EditorWindow
    {
        private const string PreviewCanvasName = "[D-Drive] Ui Preview";

        [SerializeField] private ButtonSkinData _target;

        private ObjectField _targetField;
        private ControlSkinPreviewSection _settings;
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

            _settings = new ControlSkinPreviewSection(BuildOptions());
            scrollView.Add(_settings);

            if (_target == null && Selection.activeObject is ButtonSkinData selected)
            {
                SetTarget(selected);
            }
            else
            {
                _targetField.SetValueWithoutNotify(_target);
                _settings.SetSkin(_target);
            }
        }

        private void SetTarget(ButtonSkinData target)
        {
            _target = target;
            _targetField?.SetValueWithoutNotify(_target);
            _settings?.SetSkin(_target);
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

        // SE の欄と状態遷移の並び。遷移は実行時(UiButton)で音が鳴るタイミングに合わせる
        // (Hover / Selected に入ると HoverSe、押して離すと ClickSe、Disabled / Locked で押すと DeniedSe)。
        private ControlSkinPreviewSection.Options BuildOptions()
        {
            const string hover = nameof(ButtonSkinData.HoverSe);
            const string click = nameof(ButtonSkinData.ClickSe);
            const string denied = nameof(ButtonSkinData.DeniedSe);
            static ControlSkinPreviewSection.TransitionStep S(ControlState state, string se = null) => new(state, se);

            return new ControlSkinPreviewSection.Options
            {
                EnsurePreview = EnsurePreview,
                CurrentPreview = () => _previewButton,
                SeFields = new[]
                {
                    new ControlSkinPreviewSection.SeField(hover, s => ((ButtonSkinData)s).HoverSe),
                    new ControlSkinPreviewSection.SeField(click, s => ((ButtonSkinData)s).ClickSe),
                    new ControlSkinPreviewSection.SeField(nameof(ButtonSkinData.LongPressSe), s => ((ButtonSkinData)s).LongPressSe),
                    new ControlSkinPreviewSection.SeField(denied, s => ((ButtonSkinData)s).DeniedSe),
                },
                Sequences = new[]
                {
                    new ControlSkinPreviewSection.TransitionSequence("マウスでクリック",
                        S(ControlState.Normal), S(ControlState.Hover, hover), S(ControlState.Pressed), S(ControlState.Hover, click), S(ControlState.Normal)),
                    new ControlSkinPreviewSection.TransitionSequence("パッドで選んで決定",
                        S(ControlState.Normal), S(ControlState.Selected, hover), S(ControlState.Pressed), S(ControlState.Selected, click), S(ControlState.Normal)),
                    new ControlSkinPreviewSection.TransitionSequence("押せない(Disabled)ボタンを押す",
                        S(ControlState.Normal), S(ControlState.Disabled), S(ControlState.Disabled, denied), S(ControlState.Normal)),
                    new ControlSkinPreviewSection.TransitionSequence("ロック中(Locked)のボタンを押す",
                        S(ControlState.Normal), S(ControlState.Locked), S(ControlState.Locked, denied), S(ControlState.Normal)),
                    new ControlSkinPreviewSection.TransitionSequence("全状態を順に",
                        S(ControlState.Normal), S(ControlState.Hover), S(ControlState.Pressed), S(ControlState.Selected), S(ControlState.Disabled), S(ControlState.Locked), S(ControlState.Normal)),
                },
            };
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
