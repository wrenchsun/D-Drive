using DDrive.Editor.Menu;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ui
{
    // [18_ui_controls.md] Part B — SliderSkinData 専用エディタ(4-15/4-18)。
    // プロジェクト方針(2026-09-10)によりウィンドウ内では何も描画しない。設定欄は ControlSkinPreviewSection
    // (各状態の演出欄の横に再生、各 SE 欄の横に試聴)で、確認は「確認用シーンに配置」した実 UiSlider を
    // シーン/Game ビュー側で見る(ADR-4)。
    // 本格的な SliderEditor(応答曲線グラフ・ノッチ可視化・追従比較・Skin プレビュー一覧、4-17)は別チケット。
    // ここではプリセットで Response/Step/Notches/FollowMotion を配り、「動かしてみる」で値を動かしたときの
    // 見た目(つまみ・バー・追従)と SE(掴む・離す・目盛り・端・拒否)を確認する(2026-09-14)。
    [DDrive.Editor.Inspector.DataEditor(typeof(SliderSkinData), "Skin Editor で開く")]
    public sealed class SliderSkinEditorWindow : EditorWindow
    {
        // エディタ固有の名前(2026-09-14。以前は Button Skin / UI Tween と共有していて、互いのプレビューを消していた)。
        private const string PreviewCanvasName = "[D-Drive] Slider Skin Preview";
        private const float DragSimSeconds = 0.8f;

        [SerializeField] private SliderSkinData _target;

        private ObjectField _targetField;
        private EnumField _presetField;
        private UiSlider _previewSlider;
        private ControlSkinPreviewSection _settings;

        // 「動かしてみる」
        private UnityEngine.UIElements.Slider _moveValue;
        private Label _moveStatus;
        private UiSlider _subscribedSlider;
        private bool _dragSimActive;
        private float _dragFrom;
        private float _dragTo;
        private float _dragT;
        private double _lastNotchSeTime;
        private double _lastTime;
        private double _repaintUntil;
        private string _lastEvent = string.Empty;

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

        // 前回閉じ損ねた残骸を消し、閉じる(ドメインリロード含む)ときは自分のプレビューを必ず片付ける
        // (以前は閉じてもシーンに残っていた。2026-09-14 ユーザー報告)。
        private void OnEnable()
        {
            DDrive.Editor.Preview.EditorPreviewRoots.DestroyAll(PreviewCanvasName);
            _lastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            RemoveFromScene();
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

            scrollView.Add(BuildMoveBlock());

            // パーツ(Track/Fill/Handle/DelayFill)は 2026-09-14 から画像・色・拡大率が反映される(状態に依らない固定の見た目)。
            // 状態ごとの演出(Enter Tween / Preset)はスライダー本体に掛かるため、パーツには再生ボタンを付けない。
            scrollView.Add(new HelpBox("パーツ(Track / Fill / Handle / Delay Fill)の画像・色・拡大率は、状態に関係なく常に反映されます(色が未設定=透明な黒、拡大率 0 のままなら変えません)。状態ごとの演出はスライダー本体に掛かります。", HelpBoxMessageType.None));

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

        private UiInteractable EnsurePreview() => EnsurePreviewSlider();

        private UiSlider EnsurePreviewSlider()
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
                // このウィンドウが毎フレーム UiSlider.Advance を回す(その中でスプライトアニメも進む)ので二重に進めない。
                TickPreviewVisuals = false,
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
                // つまみ(Handle)の当たり判定も「当たり判定を表示」で青い枠として出し、SceneView でドラッグ調整できる。
                ExtraHitAreas = new[]
                {
                    new ControlSkinPreviewSection.HitAreaTarget(
                        () => _previewSlider != null ? _previewSlider.HandleRect : null,
                        s => ((SliderSkinData)s).HandleHitAreaExpand,
                        (s, d) => ((SliderSkinData)s).HandleHitAreaExpand += d),
                },
            };
        }

        // ── 動かしてみる ──

        private VisualElement BuildMoveBlock()
        {
            var block = new VisualElement
            {
                style =
                {
                    marginTop = 6, marginBottom = 4, paddingLeft = 6, paddingTop = 2, paddingBottom = 4,
                    borderLeftWidth = 3, borderLeftColor = new Color(0.4f, 0.85f, 0.5f, 0.8f),
                },
            };

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            row.Add(new Label("動かしてみる") { style = { unityFontStyleAndWeight = FontStyle.Bold, width = 90 } });
            row.Add(new UnityEngine.UIElements.Button(() => PadMove(MoveDirection.Left)) { text = "◀", tooltip = "キーボード / パッドの左 1 回分" });
            row.Add(new UnityEngine.UIElements.Button(() => PadMove(MoveDirection.Right)) { text = "▶", tooltip = "キーボード / パッドの右 1 回分" });
            row.Add(new UnityEngine.UIElements.Button(() => StartDragSim(0f, 1f)) { text = "ドラッグ 0 → 1", tooltip = "マウスで端から端まで掴んで動かす操作を再現(掴む・目盛り・端・離すの SE も鳴る)" });
            row.Add(new UnityEngine.UIElements.Button(() => StartDragSim(1f, 0f)) { text = "ドラッグ 1 → 0" });
            block.Add(row);

            _moveValue = new UnityEngine.UIElements.Slider("値(0〜1)", 0f, 1f) { showInputField = true };
            _moveValue.tooltip = "ゲームのコードから値を変えたとき(HP の増減など)と同じ動き。Follow Motion の追従も確認できる";
            _moveValue.RegisterValueChangedCallback(evt => SetPreviewNormalized(evt.newValue));
            block.Add(_moveValue);

            _moveStatus = new Label { style = { opacity = 0.85f, whiteSpace = WhiteSpace.Normal } };
            block.Add(_moveStatus);
            block.Add(new HelpBox("掴む・離す・目盛り(Notch)・端(Limit)・拒否(Denied)の SE と、表示が追いつく動き(Follow Motion)をここで確認できます。「入力の許可」を OFF にした部品(HP バー等)でも、ここからは動かせます。", HelpBoxMessageType.None));
            return block;
        }

        private void PadMove(MoveDirection dir)
        {
            var s = EnsurePreviewSlider();
            if (s == null)
            {
                return;
            }

            s.Move(dir);
            s.MoveRelease(); // 押しっぱなしのリピート扱いにしない
            KeepRepainting();
        }

        private void StartDragSim(float from, float to)
        {
            var s = EnsurePreviewSlider();
            if (s == null)
            {
                return;
            }

            if (_dragSimActive)
            {
                s.EndDrag();
            }

            _dragFrom = from;
            _dragTo = to;
            _dragT = 0f;
            _dragSimActive = true;
            s.BeginDragAt(from);
            KeepRepainting();
        }

        private void SetPreviewNormalized(float normalized)
        {
            var s = EnsurePreviewSlider();
            if (s == null)
            {
                return;
            }

            s.Value = Mathf.Lerp(s.Min, s.Max, normalized);
            KeepRepainting();
        }

        private void KeepRepainting() => _repaintUntil = EditorApplication.timeSinceStartup + 1.5;

        private void Subscribe(UiSlider slider)
        {
            Unsubscribe();
            if (slider == null)
            {
                return;
            }

            slider.OnDragBegin += OnPreviewDragBegin;
            slider.OnDragEnd += OnPreviewDragEnd;
            slider.OnNotchPassed += OnPreviewNotch;
            slider.OnLimitReached += OnPreviewLimit;
            slider.OnValueChanged += OnPreviewValueChanged;
            slider.OnDenied += OnPreviewDenied;
            _subscribedSlider = slider;
            UpdateMoveStatus();
        }

        private void Unsubscribe()
        {
            if (_subscribedSlider == null)
            {
                return;
            }

            _subscribedSlider.OnDragBegin -= OnPreviewDragBegin;
            _subscribedSlider.OnDragEnd -= OnPreviewDragEnd;
            _subscribedSlider.OnNotchPassed -= OnPreviewNotch;
            _subscribedSlider.OnLimitReached -= OnPreviewLimit;
            _subscribedSlider.OnValueChanged -= OnPreviewValueChanged;
            _subscribedSlider.OnDenied -= OnPreviewDenied;
            _subscribedSlider = null;
        }

        // Editor では Audio が未 Bind のため UiSlider 自身の SE は鳴らない。同じタイミングのイベントで試聴側から鳴らす。
        private void OnPreviewDragBegin()
        {
            _settings?.PlaySeField(nameof(SliderSkinData.GrabSe));
            Log("掴む");
        }

        private void OnPreviewDragEnd()
        {
            _settings?.PlaySeField(nameof(SliderSkinData.ReleaseSe));
            Log("離す");
        }

        private void OnPreviewNotch(int index)
        {
            var now = EditorApplication.timeSinceStartup;
            var minInterval = _target != null ? _target.NotchSeMinIntervalSec : 0.04f;
            if (now - _lastNotchSeTime >= minInterval)
            {
                _lastNotchSeTime = now;
                _settings?.PlaySeField(nameof(SliderSkinData.NotchSe));
            }

            Log($"目盛り {index}");
        }

        private void OnPreviewLimit(bool isMax)
        {
            _settings?.PlaySeField(nameof(SliderSkinData.LimitSe));
            Log(isMax ? "端(Max)" : "端(Min)");
        }

        private void OnPreviewDenied()
        {
            _settings?.PlaySeField(nameof(SliderSkinData.DeniedSe));
            Log("拒否(Disabled / Locked)");
        }

        private void OnPreviewValueChanged(float value)
        {
            if (_moveValue != null && _previewSlider != null)
            {
                _moveValue.SetValueWithoutNotify(_previewSlider.NormalizedValue);
            }

            KeepRepainting();
            UpdateMoveStatus();
        }

        private void Log(string text)
        {
            _lastEvent = text;
            UpdateMoveStatus();
        }

        private void UpdateMoveStatus()
        {
            if (_moveStatus == null)
            {
                return;
            }

            _moveStatus.text = _previewSlider == null
                ? "(未配置。操作すると自動で確認用シーンに配置します)"
                : $"値 {_previewSlider.Value:0.###}(範囲 {_previewSlider.Min:0.##}〜{_previewSlider.Max:0.##})  {_lastEvent}";
        }

        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Min((float)(now - _lastTime), 0.1f);
            _lastTime = now;
            if (dt <= 0f || _previewSlider == null)
            {
                return;
            }

            if (_dragSimActive)
            {
                _dragT += dt / DragSimSeconds;
                _previewSlider.DragTo(Mathf.Lerp(_dragFrom, _dragTo, Mathf.Clamp01(_dragT)));
                if (_dragT >= 1f)
                {
                    _previewSlider.EndDrag();
                    _dragSimActive = false;
                }

                KeepRepainting();
            }

            // FollowMotion / DelayFill の追従は Advance が進める(ExecuteAlways を付けない方針のため手動で駆動)。
            _previewSlider.Advance(dt);

            // Edit Mode の Game ビューは自動では再描画されないため、動いている間だけ描き直す。
            if (now < _repaintUntil)
            {
                InternalEditorUtility.RepaintAllViews();
            }
        }

        // Data 自体は編集しない。実配置での見た目確認だけをシーン上で行う(ADR-4: プレビューは実 Manager/実コンポーネントを駆動する)。
        private void PlaceInScene()
        {
            if (_target == null)
            {
                return;
            }

            RemoveFromScene();

            var canvasGo = DDrive.Editor.Preview.EditorPreviewRoots.CreateRoot(PreviewCanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
            Subscribe(_previewSlider);

            Selection.activeGameObject = trackGo;
        }

        private void RemoveFromScene()
        {
            Unsubscribe();
            _dragSimActive = false;
            DDrive.Editor.Preview.EditorPreviewRoots.DestroyAll(PreviewCanvasName);
            _previewSlider = null;
            UpdateMoveStatus();
        }

        // 実体は共有の SliderPresets(4-17 で SliderEditor と共通化)。
        private void ApplyPreset(SliderPresets.SliderPreset preset)
        {
            SliderPresets.Apply(_previewSlider, preset);
            UpdateMoveStatus();
        }
    }
}
