using DDrive.Editor.Menu;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using DDrive.Editor.Validation;

namespace DDrive.Editor.Ui
{
    // [15_ui_interaction.md] / [18_ui_controls.md] Part A — ButtonSkinData 専用エディタ(4-6)。
    // プロジェクト方針(2026-09-10)によりウィンドウ内では何も描画しない。設定欄は ControlSkinPreviewSection
    // (各状態の演出欄の横に再生、各 SE 欄の横に試聴)で、確認は「確認用シーンに配置」した実 UiButton を
    // シーン/Game ビュー側で見る(ADR-4)。
    [DDrive.Editor.Inspector.DataEditor(typeof(ButtonSkinData), "Skin Editor で開く")]
    public sealed class ButtonSkinEditorWindow : EditorWindow
    {
        // エディタ固有の名前(2026-09-14。以前は Slider Skin / UI Tween と共有していて、互いのプレビューを消していた)。
        private const string PreviewCanvasName = "[D-Drive] Button Skin Preview";

        [SerializeField] private ButtonSkinData _target;

        private ObjectField _targetField;
        private DataValidationSection _validationSection; // 2026-09-17 U-13([09] §11)
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

        // 前回閉じ損ねた残骸を消し、閉じる(ドメインリロード含む)ときは自分のプレビューを必ず片付ける
        // (以前は閉じてもシーンに残っていた。2026-09-14 ユーザー報告)。
        private void OnEnable() => DDrive.Editor.Preview.EditorPreviewRoots.DestroyAll(PreviewCanvasName);

        // (レビュー対応 2026-09-14) 試聴用の PreviewService も必ず閉じる(DetachFromPanelEvent 頼みだとドメインリロードで漏れる)。
        private void OnDisable()
        {
            _settings?.Dispose();
            RemoveFromScene();
        }

        private void CreateGUI()
        {
            // 5-15: 上部に「＋ 新規作成」ツールバー(スクロールしても見える固定行)。
            var toolbar = new Toolbar();
            toolbar.Add(DDrive.Editor.Inspector.NewAssetToolbarButton.CreateToolbarButton(typeof(ButtonSkinEditorWindow)));
            rootVisualElement.Add(toolbar);

            // ルートをスクロール可能にする([09_editor_tools.md] §7)。
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);
            scrollView.style.paddingLeft = 6;
            scrollView.style.paddingRight = 6;
            scrollView.style.paddingTop = 6;

            _targetField = new ObjectField("対象 Skin") { objectType = typeof(ButtonSkinData) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as ButtonSkinData));
            scrollView.Add(_targetField);

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 4, marginBottom = 4 } }; // [09] §7.1
            row.Add(DDrive.Editor.Preview.PreviewPlacementButton.Create(
                "確認用シーンに配置",
                "UI 確認用シーン(CanvasPreviewScene)を開き、実 UiButton を置いて SceneView をそこへ向ける",
                PlaceInScene));
            row.Add(new UnityEngine.UIElements.Button(RemoveFromScene) { text = "撤去" });
            scrollView.Add(row);

            _settings = new ControlSkinPreviewSection(BuildOptions());
            scrollView.Add(_settings);

            // 2026-09-17(U-13): 「検証」を全エディタで揃える([09] §11)。
            _validationSection = new DataValidationSection();
            scrollView.Add(_validationSection);

            if (_target == null && Selection.activeObject is ButtonSkinData selected)
            {
                SetTarget(selected);
            }
            else
            {
                _targetField.SetValueWithoutNotify(_target);
                _settings.SetSkin(_target);
                _validationSection.Bind(_target);
            }
        }

        private void SetTarget(ButtonSkinData target)
        {
            _target = target;
            _targetField?.SetValueWithoutNotify(_target);
            _settings?.SetSkin(_target);
            _validationSection?.Bind(_target);

            // (レビュー対応 2026-09-14) Skin を外したときも SetVisual(null) で差し替え前の見た目に戻す
            // (以前は null のとき呼ばず、古い Skin の見た目のまま残っていた)。
            if (_previewButton != null)
            {
                _previewButton.SetVisual(_target);
            }
        }

        private UiInteractable EnsurePreview()
        {
            if (_previewButton == null)
            {
                // ▶ 等からの暗黙の配置ではシーンを勝手に切り替えない(今開いているシーンに置く)。
                PlaceInScene(DDrive.Editor.Preview.PreviewPlaceMode.CurrentScene);
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
        // U-5(2026-09-17): 左クリック = UI 確認用シーンを開いてから配置 / 右クリック = このシーンに配置・本配置。
        private void PlaceInScene(DDrive.Editor.Preview.PreviewPlaceMode mode)
        {
            if (_target == null)
            {
                return;
            }

            RemoveFromScene();
            if (!DDrive.Editor.Preview.PreviewPlacement.PrepareScene(mode, DDrive.Editor.Preview.CanvasPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            // (レビュー対応 2026-09-14) 子も DontSave で作る(以前は子が HideFlags.None で、プレビューを置いたまま
            // シーンを保存すると PreviewButton だけ親無しで保存されていた)。
            var canvasGo = DDrive.Editor.Preview.EditorPreviewRoots.CreateOverlayCanvas(PreviewCanvasName);

            var buttonGo = DDrive.Editor.Preview.EditorPreviewRoots.CreateChild(canvasGo.transform, "PreviewButton", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UiButton));
            ((RectTransform)buttonGo.transform).sizeDelta = new Vector2(200f, 60f);

            var button = buttonGo.GetComponent<UiButton>();
            button.TargetGraphic = buttonGo.GetComponent<UnityEngine.UI.Image>();
            button.SetVisual(_target);
            _previewButton = button;
            DDrive.Editor.Preview.EditorPreviewRoots.MarkDontSaveRecursive(canvasGo);

            if (DDrive.Editor.Preview.PreviewPlacement.IsPersistent(mode))
            {
                // Canvas ごと本配置する(ボタン単体を外すと描画できないため)。以後このウィンドウの所有物ではない。
                DDrive.Editor.Preview.PreviewPlacement.Persist(canvasGo, _target.DisplayName ?? _target.name);
                _previewButton = null;
                return;
            }

            Selection.activeGameObject = buttonGo;
            DDrive.Editor.Preview.PreviewPlacement.Focus(buttonGo);
        }

        private void RemoveFromScene()
        {
            // (レビュー対応 2026-09-14) 遷移の自動再生・演出・SE も止める(止めないと次の段がプレビューを置き直していた)。
            _settings?.StopAll();
            DDrive.Editor.Preview.EditorPreviewRoots.DestroyAll(PreviewCanvasName);
            _previewButton = null;
        }
    }
}
