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
        private double _lastTime;
        private double _repaintUntil;
        private string _lastEvent = string.Empty;

        // (レビュー対応 2026-09-14) イベント → SE の対応と目盛り SE の間引きは SliderEditorWindow と共通の SliderSePreview、
        // 描き直しは 30fps 上限の ViewRepaintThrottle。
        private readonly SliderSePreview _sePreview = new();
        private readonly DDrive.Editor.Preview.ViewRepaintThrottle _repaint = new();

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
            // (レビュー対応 2026-09-14) 試聴用の PreviewService も必ず閉じる(DetachFromPanelEvent 頼みだとドメインリロードで漏れる)。
            _settings?.Dispose();
            RemoveFromScene();
        }

        private void CreateGUI()
        {
            // 5-15: 上部に「＋ 新規作成」ツールバー(スクロールしても見える固定行)。
            var toolbar = new Toolbar();
            toolbar.Add(DDrive.Editor.Inspector.NewAssetToolbarButton.CreateToolbarButton(typeof(SliderSkinEditorWindow)));
            rootVisualElement.Add(toolbar);

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

            // (レビュー対応 2026-09-14) Skin を外したときも SetVisual(null) で差し替え前の見た目に戻す
            // (以前は null のとき呼ばず、古い Skin の見た目のまま残っていた)。
            if (_previewSlider != null)
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
            PlaySliderSe(SliderSeEvent.Grab);
            Log("掴む");
        }

        private void OnPreviewDragEnd()
        {
            PlaySliderSe(SliderSeEvent.Release);
            Log("離す");
        }

        private void OnPreviewNotch(int index)
        {
            PlaySliderSe(SliderSeEvent.Notch);
            Log($"目盛り {index}");
        }

        private void OnPreviewLimit(bool isMax)
        {
            PlaySliderSe(SliderSeEvent.Limit);
            Log(isMax ? "端(Max)" : "端(Min)");
        }

        private void OnPreviewDenied()
        {
            PlaySliderSe(SliderSeEvent.Denied);
            Log("拒否(Disabled / Locked)");
        }

        // 鳴らすのは設定欄の SE 行と同じ経路(試聴の状態表示・■ 停止と揃える)。目盛り SE の間引きは SliderSePreview。
        private void PlaySliderSe(SliderSeEvent e)
        {
            if (_sePreview.ShouldPlay(_target, e))
            {
                _settings?.PlaySeField(SliderSePreview.PropertyName(e));
            }
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

            // Edit Mode の Game ビューは自動では再描画されないため、動いている間だけ描き直す(30fps 上限。レビュー対応 2026-09-14)。
            if (now < _repaintUntil)
            {
                _repaint.Request();
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

            // (レビュー対応 2026-09-14) 組み立ては SliderEditorWindow と共通の PreviewSliderFactory(子も DontSave)。
            var canvasGo = DDrive.Editor.Preview.EditorPreviewRoots.CreateOverlayCanvas(PreviewCanvasName);
            _previewSlider = PreviewSliderFactory.Create(canvasGo.transform, "PreviewSlider", Vector2.zero);
            _previewSlider.SetVisual(_target);
            DDrive.Editor.Preview.EditorPreviewRoots.MarkDontSaveRecursive(canvasGo);
            Subscribe(_previewSlider);

            // (レビュー対応 2026-09-14) 新しいスライダーにはまだプリセットを当てていないので、欄を None に戻す。
            _presetField?.SetValueWithoutNotify(SliderPresets.SliderPreset.None);

            Selection.activeGameObject = _previewSlider.gameObject;
        }

        private void RemoveFromScene()
        {
            // (レビュー対応 2026-09-14) 遷移の自動再生・演出・SE も止める(止めないと次の段がプレビューを置き直していた)。
            _settings?.StopAll();
            Unsubscribe();
            _dragSimActive = false;
            DDrive.Editor.Preview.EditorPreviewRoots.DestroyAll(PreviewCanvasName);
            _previewSlider = null;
            _presetField?.SetValueWithoutNotify(SliderPresets.SliderPreset.None);
            UpdateMoveStatus();
        }

        // 実体は共有の SliderPresets(4-17 で SliderEditor と共通化)。
        // (レビュー対応 2026-09-14) 未配置なら配置してから当てる(以前は何も起きないのに欄だけ選んだプリセットを表示していた)。
        // 配置で欄が None に戻るため、当てた後に選んだ値を表示し直す。
        private void ApplyPreset(SliderPresets.SliderPreset preset)
        {
            if (preset == SliderPresets.SliderPreset.None)
            {
                return;
            }

            var slider = EnsurePreviewSlider();
            if (slider == null)
            {
                _presetField?.SetValueWithoutNotify(SliderPresets.SliderPreset.None);
                return;
            }

            SliderPresets.Apply(slider, preset);
            _presetField?.SetValueWithoutNotify(preset);
            UpdateMoveStatus();
        }
    }
}
