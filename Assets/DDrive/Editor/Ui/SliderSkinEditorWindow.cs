using DDrive.Editor.Menu;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ui
{
    // [18_ui_controls.md] Part B — SliderSkinData 専用エディタ(4-15/4-18)。
    // プロジェクト方針(2026-09-10)によりウィンドウ内では何も描画しない。SerializedObject をそのまま
    // InspectorElement で表示し、確認は「確認用シーンに配置」で開いているシーン/Game ビュー側に出す(ADR-4)。
    // 本格的な SliderEditor(応答曲線グラフ・ノッチ可視化・追従比較・Skin プレビュー一覧、4-17)は別チケット。
    // ここではプリセットで Response/Step/Notches/FollowMotion を配って最低限の見た目確認だけ行う。
    [DDrive.Editor.Inspector.DataEditor(typeof(SliderSkinData), "Skin Editor で開く")]
    public sealed class SliderSkinEditorWindow : EditorWindow
    {
        private const string PreviewCanvasName = "[D-Drive] Ui Preview";

        private enum Preset
        {
            None,
            音量,
            感度,
            HPバー,
            スタミナ,
            キャラメイク,
        }

        [SerializeField] private SliderSkinData _target;

        private ObjectField _targetField;
        private VisualElement _inspectorContainer;
        private EnumField _presetField;
        private UiSlider _previewSlider;

        [MenuItem(DDriveMenu.Editors + "Slider Skin")]
        public static void OpenFromMenu() => Open(Selection.activeObject as SliderSkinData);

        public static void Open(SliderSkinData target)
        {
            var window = GetWindow<SliderSkinEditorWindow>("Slider Skin Editor");
            window.minSize = new Vector2(360, 360);
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

            scrollView.Add(new Label("本格的なグラフ/ノッチ可視化/追従比較は SliderEditor(4-17)で提供予定です。ここは最低限の見た目・プリセット確認のみ。") { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 4 } });

            _targetField = new ObjectField("対象 Skin") { objectType = typeof(SliderSkinData) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as SliderSkinData));
            scrollView.Add(_targetField);

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 4 } };
            row.Add(new UnityEngine.UIElements.Button(PlaceInScene) { text = "確認用シーンに配置" });
            row.Add(new UnityEngine.UIElements.Button(RemoveFromScene) { text = "撤去" });
            scrollView.Add(row);

            _presetField = new EnumField("プリセット", Preset.None);
            _presetField.RegisterValueChangedCallback(evt => ApplyPreset((Preset)evt.newValue));
            scrollView.Add(_presetField);

            _inspectorContainer = new VisualElement();
            scrollView.Add(_inspectorContainer);

            if (_target == null && Selection.activeObject is SliderSkinData selected)
            {
                SetTarget(selected);
            }
            else
            {
                _targetField.SetValueWithoutNotify(_target);
                RebuildInspector();
            }
        }

        private void SetTarget(SliderSkinData target)
        {
            _target = target;
            _targetField?.SetValueWithoutNotify(_target);
            RebuildInspector();
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
                _inspectorContainer.Add(new Label("SliderSkinData を選択してください"));
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

            var trackGo = new GameObject("PreviewSlider", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UiSlider));
            trackGo.transform.SetParent(canvasGo.transform, false);
            var trackRect = (RectTransform)trackGo.transform;
            trackRect.sizeDelta = new Vector2(300f, 24f);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            fillGo.transform.SetParent(trackGo.transform, false);
            var fillRect = (RectTransform)fillGo.transform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            handleGo.transform.SetParent(trackGo.transform, false);
            var handleRect = (RectTransform)handleGo.transform;
            handleRect.sizeDelta = new Vector2(20f, 24f);

            _previewSlider = trackGo.GetComponent<UiSlider>();
            _previewSlider.TargetGraphic = trackGo.GetComponent<UnityEngine.UI.Image>();
            _previewSlider.TrackRect = trackRect;
            _previewSlider.FillRect = fillRect;
            _previewSlider.HandleRect = handleRect;
            _previewSlider.SetVisual(_target);

            Selection.activeGameObject = trackGo;
        }

        private void RemoveFromScene()
        {
            var existing = GameObject.Find(PreviewCanvasName);
            if (existing != null)
            {
                DestroyImmediate(existing);
            }

            _previewSlider = null;
        }

        // プリセットは Response/Step/Notches/FollowMotion を配るだけの最小実装(本格編集は 4-17 SliderEditor)。
        private void ApplyPreset(Preset preset)
        {
            if (_previewSlider == null || preset == Preset.None)
            {
                return;
            }

            switch (preset)
            {
                case Preset.音量:
                    _previewSlider.SetRange(0f, 1f);
                    _previewSlider.Step = 0f;
                    _previewSlider.Notches = 0;
                    _previewSlider.Response = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(Ease.InQuad), From = 0f, To = 1f };
                    _previewSlider.FollowMotion = ValueDef.Constant01(0f);
                    break;

                case Preset.感度:
                    _previewSlider.SetRange(0.1f, 5f);
                    _previewSlider.Step = 0f;
                    _previewSlider.Notches = 0;
                    _previewSlider.Response = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(Ease.OutQuad), From = 0f, To = 1f };
                    break;

                case Preset.HPバー:
                    _previewSlider.SetRange(0f, 100f);
                    _previewSlider.Step = 1f;
                    _previewSlider.Notches = 0;
                    _previewSlider.Response = default;
                    _previewSlider.FollowMotion = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(Ease.OutCubic), Time = TimeDef.Duration(0.25f), Loop = LoopMode.Once };
                    break;

                case Preset.スタミナ:
                    _previewSlider.SetRange(0f, 100f);
                    _previewSlider.Step = 1f;
                    _previewSlider.Notches = 4;
                    _previewSlider.SnapThreshold = 0.03f;
                    _previewSlider.Response = default;
                    break;

                case Preset.キャラメイク:
                    _previewSlider.SetRange(0f, 10f);
                    _previewSlider.Step = 1f;
                    _previewSlider.WholeNumbers = true;
                    _previewSlider.Notches = 10;
                    _previewSlider.Response = default;
                    break;
            }
        }
    }
}
