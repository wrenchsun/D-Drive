using DDrive.Editor.Menu;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ui
{
    // [18_ui_controls.md] Part B — SliderSkinData 専用エディタ(4-15/4-18)。
    // プロジェクト方針(2026-09-10)によりウィンドウ内では何も描画しない。設定欄は ControlSkinPreviewSection
    // (各状態の演出欄の横に再生、各 SE 欄の横に試聴)で、確認は「確認用シーンに配置」した実 UiSlider を
    // シーン/Game ビュー側で見る(ADR-4)。
    // 本格的な SliderEditor(応答曲線グラフ・ノッチ可視化・追従比較・Skin プレビュー一覧、4-17)は別チケット。
    // ここではプリセットで Response/Step/Notches/FollowMotion を配って最低限の見た目確認だけ行う。
    [DDrive.Editor.Inspector.DataEditor(typeof(SliderSkinData), "Skin Editor で開く")]
    public sealed class SliderSkinEditorWindow : EditorWindow
    {
        private const string PreviewCanvasName = "[D-Drive] Ui Preview";

        [SerializeField] private SliderSkinData _target;

        private ObjectField _targetField;
        private EnumField _presetField;
        private UiSlider _previewSlider;
        private ControlSkinPreviewSection _settings;

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

            scrollView.Add(new Label("本格的なグラフ/ノッチ可視化/追従比較/全状態プレビューは SliderEditor(Tools > D-Drive > Editors > Slider)を使ってください。ここは最低限の見た目・プリセット確認のみ。") { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 4 } });
            scrollView.Add(new UnityEngine.UIElements.Button(() => SliderEditorWindow.Open(_target)) { text = "SliderEditor で開く" });

            _targetField = new ObjectField("対象 Skin") { objectType = typeof(SliderSkinData) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as SliderSkinData));
            scrollView.Add(_targetField);

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 4 } };
            row.Add(new UnityEngine.UIElements.Button(PlaceInScene) { text = "確認用シーンに配置" });
            row.Add(new UnityEngine.UIElements.Button(RemoveFromScene) { text = "撤去" });
            scrollView.Add(row);

            _presetField = new EnumField("プリセット", SliderPresets.SliderPreset.None);
            _presetField.RegisterValueChangedCallback(evt => ApplyPreset((SliderPresets.SliderPreset)evt.newValue));
            scrollView.Add(_presetField);

            // パーツ(Track/Fill/Handle/DelayFill)の StateVisual は UiSlider.OnSkinApplied が未実装で実行時に
            // 反映されないため、再生対象にしない(見た目を偽って見せない)。
            scrollView.Add(new HelpBox("パーツ(Track / Fill / Handle / Delay Fill)の見た目はまだ実行時に反映されないため、再生ボタンは付けていません。状態の演出はスライダー本体に掛かります。", HelpBoxMessageType.None));

            _settings = new ControlSkinPreviewSection(BuildOptions());
            scrollView.Add(_settings);

            if (_target == null && Selection.activeObject is SliderSkinData selected)
            {
                SetTarget(selected);
            }
            else
            {
                _targetField.SetValueWithoutNotify(_target);
                _settings.SetSkin(_target);
            }
        }

        private void SetTarget(SliderSkinData target)
        {
            _target = target;
            _targetField?.SetValueWithoutNotify(_target);
            _settings?.SetSkin(_target);
            if (_previewSlider != null && _target != null)
            {
                _previewSlider.SetVisual(_target);
            }
        }

        private UiInteractable EnsurePreview()
        {
            if (_previewSlider == null)
            {
                PlaceInScene();
            }

            return _previewSlider;
        }

        // SE の欄と状態遷移の並び。遷移は実行時(UiSlider)で音が鳴るタイミングに合わせる
        // (掴むと GrabSe、離すと ReleaseSe、Disabled / Locked で触ると DeniedSe)。
        private ControlSkinPreviewSection.Options BuildOptions()
        {
            const string grab = nameof(SliderSkinData.GrabSe);
            const string release = nameof(SliderSkinData.ReleaseSe);
            const string denied = nameof(SliderSkinData.DeniedSe);
            static ControlSkinPreviewSection.TransitionStep S(ControlState state, string se = null) => new(state, se);

            return new ControlSkinPreviewSection.Options
            {
                EnsurePreview = EnsurePreview,
                CurrentPreview = () => _previewSlider,
                SeFields = new[]
                {
                    new ControlSkinPreviewSection.SeField(grab, s => ((SliderSkinData)s).GrabSe),
                    new ControlSkinPreviewSection.SeField(release, s => ((SliderSkinData)s).ReleaseSe),
                    new ControlSkinPreviewSection.SeField(nameof(SliderSkinData.NotchSe), s => ((SliderSkinData)s).NotchSe),
                    new ControlSkinPreviewSection.SeField(nameof(SliderSkinData.LimitSe), s => ((SliderSkinData)s).LimitSe),
                    new ControlSkinPreviewSection.SeField(denied, s => ((SliderSkinData)s).DeniedSe),
                },
                Sequences = new[]
                {
                    new ControlSkinPreviewSection.TransitionSequence("マウスで掴んで離す",
                        S(ControlState.Normal), S(ControlState.Hover), S(ControlState.Pressed, grab), S(ControlState.Hover, release), S(ControlState.Normal)),
                    new ControlSkinPreviewSection.TransitionSequence("パッドで選ぶ",
                        S(ControlState.Normal), S(ControlState.Selected), S(ControlState.Normal)),
                    new ControlSkinPreviewSection.TransitionSequence("押せない(Disabled)スライダーを触る",
                        S(ControlState.Normal), S(ControlState.Disabled), S(ControlState.Disabled, denied), S(ControlState.Normal)),
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

        // 実体は共有の SliderPresets(4-17 で SliderEditor と共通化)。
        private void ApplyPreset(SliderPresets.SliderPreset preset)
        {
            SliderPresets.Apply(_previewSlider, preset);
        }
    }
}
