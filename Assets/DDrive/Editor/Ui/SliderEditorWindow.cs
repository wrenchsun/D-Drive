using System.Collections.Generic;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Validation;
using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace DDrive.Editor.Ui
{
    // [18_ui_controls.md] B-6/B-7(4-17) — UiSlider 専用の本格エディタ。
    // owner instruction 2026-09-10: ウィンドウ内には「動く」ものを描画しない。
    //  - 応答曲線グラフ・ノッチ可視化バーは静的な IMGUI 描画(グラフ/ダイアグラム)なので許容する。
    //  - 実際に動く確認(追従・ドラッグ・状態切替)は全て「確認用シーンに配置」した DontSave の実 UiSlider を
    //    EditorApplication.update から Advance(dt) で駆動し、Scene/Game ビュー側で見せる(ADR-4)。
    [DDrive.Editor.Inspector.DataEditor(typeof(SliderSkinData), "Slider Editor で開く", Order = 5)]
    public sealed class SliderEditorWindow : EditorWindow
    {
        private const string PreviewRootName = "[D-Drive] Slider Editor Preview";
        private const int ResponseSamples = 64;

        [SerializeField] private UiSlider _target;

        private ObjectField _targetField;
        private VisualElement _validationContainer;
        private VisualElement _compareContainer;
        private VisualElement _responseFieldContainer;
        private Label _statusLabel;
        private Label _eventLogLabel;
        private UnityEngine.UIElements.Toggle _fineToggle;
        private ObjectField _compareFollowTarget;

        private readonly List<string> _eventLog = new();
        private bool _fineAdjust;

        // Slider Skin の「Slider Editor で開く」から開いたときのスキン(サンプルは Skin Id を持たないため、
        // 「全状態を並べる」と SE はこれを使う。2026-09-14)。
        // (レビュー対応 2026-09-14) ドメインリロードで失われないよう保存し、OnEnable でサンプルに当て直す。
        [SerializeField] private SliderSkinData _explicitSkin;

        // Editor では Audio が未 Bind で UiSlider 自身の SE は鳴らないため、同じタイミングのイベントで
        // プレビュー用の実 AudioManager から鳴らす(2026-09-14、docs/23 の確認項目どおりに鳴るように)。
        // (レビュー対応 2026-09-14) イベント → SE の対応・目盛りの間引き・SeData の検索(キャッシュ付き)は SliderSePreview。
        private PreviewService _audio;
        private readonly SliderSePreview _sePreview = new();

        // イベントを購読している UiSlider(_target が差し替わった・破棄された後でも確実に解除するため別に持つ)。
        private UiSlider _subscribedTarget;

        // 追従比較(d)・Skin プレビュー(e)の DontSave 実体。
        private UiSlider _compareSlider;
        private readonly List<UiSlider> _skinPreviewSliders = new();

        // ドラッグ模擬(c)。EditorApplication.update で 1 ステップずつ進める(ウィンドウ内では描画しない)。
        private bool _dragSimActive;
        private int _dragSimStep;
        private const int DragSimSteps = 20;
        private bool _dragSimIncludesCompare;

        private double _lastEditorTime;

        [MenuItem(DDriveMenu.Editors + "Slider")]
        public static void OpenFromMenu() => Open(Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<UiSlider>() : null);

        public static void Open(UiSlider target)
        {
            var window = GetWindow<SliderEditorWindow>("Slider Editor");
            window.minSize = new Vector2(420, 480);
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        // [DataEditor] 経由: SliderSkinData の Inspector から「エディターで開く」で開いたときは
        // そのスキンを当てたサンプルスライダーを確認用シーンに配置してから開く。
        public static void Open(SliderSkinData skin)
        {
            var window = GetWindow<SliderEditorWindow>("Slider Editor");
            window.minSize = new Vector2(420, 480);
            window.PlaceSampleInScene(skin);
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.update += OnEditorUpdate;
            _lastEditorTime = EditorApplication.timeSinceStartup;

            // (レビュー対応 2026-09-14) ドメインリロード後は _target([SerializeField])だけが戻り、イベントの購読と
            // サンプルに当てた Skin(UiInteractable 側の参照はシリアライズされない)が失われて、SE とイベントログが
            // 黙って止まっていた。購読し直し、サンプル(プレビュー)なら Skin も当て直す(実物には触らない)。
            if (_target != null)
            {
                if (_explicitSkin != null && IsPreviewObject(_target))
                {
                    _target.SetVisual(_explicitSkin);
                }

                SubscribeEvents();
            }
        }

        private void OnFocus() => SceneGuiOwner.Claim(this);

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            EditorApplication.update -= OnEditorUpdate;
            UnsubscribeEvents();
            SceneGuiOwner.Release(this);
            _audio?.Dispose();
            _audio = null;
        }

        private void OnDestroy() => RemoveFromScene();

        private void CreateGUI()
        {
            // 5-15: 上部に「＋ 新規作成」ツールバー(スクロールしても見える固定行)。
            var toolbar = new Toolbar();
            toolbar.Add(DDrive.Editor.Inspector.NewAssetToolbarButton.CreateToolbarButton(typeof(SliderEditorWindow)));
            rootVisualElement.Add(toolbar);

            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);
            scrollView.style.paddingLeft = 6;
            scrollView.style.paddingRight = 6;
            scrollView.style.paddingTop = 6;

            _targetField = new ObjectField("対象 UiSlider") { objectType = typeof(UiSlider), allowSceneObjects = true };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as UiSlider));
            scrollView.Add(_targetField);

            var targetButtons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 4 } };
            targetButtons.Add(new Button(() => SetTarget(Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<UiSlider>() : null)) { text = "選択から取得" });
            targetButtons.Add(new Button(() => PlaceSampleInScene(null)) { text = "確認用シーンにサンプルを配置" });
            targetButtons.Add(new Button(RemoveFromScene) { text = "撤去" });
            scrollView.Add(targetButtons);

            _statusLabel = new Label();
            scrollView.Add(_statusLabel);

            // (a) 応答曲線グラフ
            scrollView.Add(SectionHeader("応答曲線"));
            scrollView.Add(new IMGUIContainer(() => DrawResponseCurve(GUILayoutUtility.GetRect(200, 160))) { style = { height = 164 } });
            _responseFieldContainer = new VisualElement();
            scrollView.Add(_responseFieldContainer);
            RebuildResponseField(_responseFieldContainer);

            // (b) ノッチ可視化
            scrollView.Add(SectionHeader("ノッチ / Step"));
            scrollView.Add(new IMGUIContainer(() => DrawNotchBar(GUILayoutUtility.GetRect(200, 40))) { style = { height = 44 } });
            scrollView.Add(new HelpBox("SceneView 上でも同じ帯を TrackRect の上に重ねて表示します(このウィンドウがフォーカス中のみ)。", HelpBoxMessageType.Info));

            // (c) 実操作
            scrollView.Add(SectionHeader("実操作"));
            var padRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            padRow.Add(new Button(() => MovePad(MoveDirection.Left)) { text = "◀" });
            padRow.Add(new Button(() => MovePad(MoveDirection.Right)) { text = "▶" });
            _fineToggle = new UnityEngine.UIElements.Toggle("微調整") { style = { marginLeft = 8 } };
            _fineToggle.RegisterValueChangedCallback(evt => _fineAdjust = evt.newValue);
            padRow.Add(_fineToggle);
            scrollView.Add(padRow);
            scrollView.Add(new Button(() => StartDragSimulate(includeCompare: false)) { text = "ドラッグ模擬: 0 → 1" });
            _eventLogLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4, opacity = 0.85f } };
            scrollView.Add(_eventLogLabel);

            // (d) 追従比較
            scrollView.Add(SectionHeader("追従比較"));
            _compareContainer = new VisualElement();
            scrollView.Add(_compareContainer);
            scrollView.Add(new Button(SpawnCompareSlider) { text = "2 体目を配置して FollowMotion を比較" });
            scrollView.Add(new Button(() => StartDragSimulate(includeCompare: true)) { text = "同時に 0→1 を模擬" });

            // (e) Skin プレビュー
            scrollView.Add(SectionHeader("Skin プレビュー"));
            scrollView.Add(new Button(SpawnSkinStatesPreview) { text = "全状態を並べる(Normal/Hover/Pressed/Selected/Disabled/Locked)" });

            // (f) プリセット
            scrollView.Add(SectionHeader("プリセット"));
            var presetRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            foreach (SliderPresets.SliderPreset preset in System.Enum.GetValues(typeof(SliderPresets.SliderPreset)))
            {
                if (preset == SliderPresets.SliderPreset.None)
                {
                    continue;
                }

                var captured = preset;
                presetRow.Add(new Button(() => ApplyPreset(captured)) { text = preset.ToString() });
            }

            scrollView.Add(presetRow);

            // (g) Validation
            scrollView.Add(SectionHeader("Validation"));
            _validationContainer = new VisualElement();
            scrollView.Add(_validationContainer);

            if (_target == null && Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<UiSlider>() is UiSlider selected)
            {
                SetTarget(selected);
            }
            else
            {
                _targetField.SetValueWithoutNotify(_target);
                RefreshAll();
            }
        }

        private static Label SectionHeader(string text) => new(text) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10 } };

        private void RebuildResponseField(VisualElement container)
        {
            container.Clear();
            if (_target == null)
            {
                return;
            }

            var so = new SerializedObject(_target);
            var field = new PropertyField(so.FindProperty(nameof(UiSlider.Response)), "Response");
            field.Bind(so);
            field.RegisterCallback<SerializedPropertyChangeEvent>(_ => SceneView.RepaintAll());
            container.Add(field);
        }

        // ── ターゲット ──

        private void SetTarget(UiSlider target)
        {
            UnsubscribeEvents();

            // (レビュー対応 2026-09-14) 別のスライダーに変わったときだけ明示 Skin を捨てる
            // (以前は同じサンプルを「選択から取得」し直しただけで Skin を失い、SE と全状態プレビューが無地になっていた)。
            if (target != _target)
            {
                _explicitSkin = null;
            }

            _target = target;
            _targetField?.SetValueWithoutNotify(_target);
            SubscribeEvents();
            RefreshAll();
        }

        private void RefreshAll()
        {
            RebuildValidation();
            if (_responseFieldContainer != null)
            {
                RebuildResponseField(_responseFieldContainer);
            }

            SceneView.RepaintAll();
            if (_statusLabel != null)
            {
                _statusLabel.text = _target == null ? "UiSlider を選択してください" : $"対象: {_target.name}";
            }
        }

        private void SubscribeEvents()
        {
            UnsubscribeEvents(); // 二重購読しない
            if (_target == null)
            {
                return;
            }

            _subscribedTarget = _target;
            _target.OnValueChanged += OnTargetValueChanged;
            _target.OnCommit += OnTargetCommit;
            _target.OnNotchPassed += OnTargetNotch;
            _target.OnLimitReached += OnTargetLimit;
            _target.OnDragBegin += OnTargetDragBegin;
            _target.OnDragEnd += OnTargetDragEnd;
            _target.OnDenied += OnTargetDenied;
        }

        private void UnsubscribeEvents()
        {
            // 破棄済み(Unity の null)でも C# のイベントは外せるので、参照そのもので判定する。
            if (ReferenceEquals(_subscribedTarget, null))
            {
                return;
            }

            _subscribedTarget.OnValueChanged -= OnTargetValueChanged;
            _subscribedTarget.OnCommit -= OnTargetCommit;
            _subscribedTarget.OnNotchPassed -= OnTargetNotch;
            _subscribedTarget.OnLimitReached -= OnTargetLimit;
            _subscribedTarget.OnDragBegin -= OnTargetDragBegin;
            _subscribedTarget.OnDragEnd -= OnTargetDragEnd;
            _subscribedTarget.OnDenied -= OnTargetDenied;
            _subscribedTarget = null;
        }

        private void OnTargetValueChanged(float v) => AppendLog($"Changed: {v:0.###}");
        private void OnTargetCommit(float v) => AppendLog($"Commit: {v:0.###}");

        private void OnTargetNotch(int index)
        {
            AppendLog($"NotchPassed: {index}");
            PlaySkinSe(SliderSeEvent.Notch);
        }

        private void OnTargetLimit(bool isMax)
        {
            AppendLog($"LimitReached: {(isMax ? "Max" : "Min")}");
            PlaySkinSe(SliderSeEvent.Limit);
        }

        private void OnTargetDragBegin() => PlaySkinSe(SliderSeEvent.Grab);

        private void OnTargetDragEnd() => PlaySkinSe(SliderSeEvent.Release);

        private void OnTargetDenied()
        {
            AppendLog("Denied(Disabled / Locked)");
            PlaySkinSe(SliderSeEvent.Denied);
        }

        // (レビュー対応 2026-09-14) 以前は 1 ノッチごとに全 SeData をロードして探していた。SliderSePreview(DataIdLookup)で引く。
        private void PlaySkinSe(SliderSeEvent e)
        {
            var data = _sePreview.Resolve(GetTargetSkin(), e);
            if (data == null)
            {
                return;
            }

            _audio ??= new PreviewService();
            _audio.Initialize();
            _audio.PlaySe(data);
        }

        private void AppendLog(string line)
        {
            _eventLog.Add(line);
            if (_eventLog.Count > 12)
            {
                _eventLog.RemoveAt(0);
            }

            if (_eventLogLabel != null)
            {
                _eventLogLabel.text = string.Join("\n", _eventLog);
            }
        }

        // ── (a) 応答曲線グラフ ──

        private void DrawResponseCurve(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));
            if (_target == null)
            {
                return;
            }

            var samples = SliderEditorMath.SampleResponse(_target, ResponseSamples);
            var points = new Vector3[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                var p = samples.Length == 1 ? 0f : i / (float)(samples.Length - 1);
                points[i] = new Vector3(rect.x + p * rect.width, rect.yMax - Mathf.Clamp01(samples[i]) * rect.height, 0f);
            }

            Handles.BeginGUI();
            var prev = Handles.color;

            // 対角線(線形の目安)
            Handles.color = new Color(1f, 1f, 1f, 0.15f);
            Handles.DrawAAPolyLine(1f, new Vector3(rect.x, rect.yMax, 0f), new Vector3(rect.xMax, rect.y, 0f));

            Handles.color = new Color(0.4f, 0.85f, 1f);
            Handles.DrawAAPolyLine(2f, points);

            // 現在の測定点(つまみ位置 = InverseResponse(現在の正規化値))
            var normalized = _target.NormalizedValue;
            var knobPos = _target.InverseResponse(normalized);
            var dot = new Vector3(rect.x + knobPos * rect.width, rect.yMax - normalized * rect.height, 0f);
            Handles.color = Color.yellow;
            Handles.DrawSolidDisc(dot, Vector3.forward, 4f);

            Handles.color = prev;
            Handles.EndGUI();
        }

        // ── (b) ノッチ可視化 ──

        private void DrawNotchBar(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));
            if (_target == null || _target.Notches <= 0)
            {
                if (_target != null)
                {
                    GUI.Label(rect, "Notches=0(連続値)", EditorStyles.centeredGreyMiniLabel);
                }

                return;
            }

            DrawNotchOverlayOn(rect.x, rect.xMax, rect.y, rect.yMax, isImgui: true);
        }

        // ノッチ位置 + SnapThreshold の帯を [x0,x1] x [y0,y1] の矩形へ描く(IMGUI/SceneView 共用ロジック)。
        private void DrawNotchOverlayOn(float x0, float x1, float y0, float y1, bool isImgui)
        {
            var threshold = _target.SnapThreshold;
            var mid = (y0 + y1) * 0.5f;
            var positions = SliderEditorMath.NotchPositions(_target.Notches);

            foreach (var frac in positions)
            {
                var x = Mathf.Lerp(x0, x1, frac);
                var bandHalf = threshold * (x1 - x0);

                Handles.color = new Color(1f, 0.85f, 0.2f, 0.25f);
                if (isImgui)
                {
                    Handles.BeginGUI();
                }

                Handles.DrawAAConvexPolygon(
                    new Vector3(x - bandHalf, y0, 0f), new Vector3(x + bandHalf, y0, 0f),
                    new Vector3(x + bandHalf, y1, 0f), new Vector3(x - bandHalf, y1, 0f));

                Handles.color = Color.white;
                Handles.DrawAAPolyLine(2f, new Vector3(x, y0, 0f), new Vector3(x, y1, 0f));

                if (isImgui)
                {
                    Handles.EndGUI();
                }
            }

            var normalized = _target.NormalizedValue;
            var knobX = Mathf.Lerp(x0, x1, normalized);
            Handles.color = Color.cyan;
            if (isImgui)
            {
                Handles.BeginGUI();
            }

            Handles.DrawSolidDisc(new Vector3(knobX, mid, 0f), Vector3.forward, 4f);
            if (isImgui)
            {
                Handles.EndGUI();
            }
        }

        private void OnSceneGui(SceneView sceneView)
        {
            if (_target == null || _target.TrackRect == null || !SceneGuiOwner.IsOwner(this) || _target.Notches <= 0)
            {
                return;
            }

            var rt = _target.TrackRect;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            // corners[0]=左下, corners[2]=右上(uGUI の GetWorldCorners 順)。
            var x0 = HandleUtility.WorldToGUIPointWithDepth(corners[0]).x;
            var x1 = HandleUtility.WorldToGUIPointWithDepth(corners[2]).x;
            var y0 = HandleUtility.WorldToGUIPointWithDepth(corners[2]).y;
            var y1 = HandleUtility.WorldToGUIPointWithDepth(corners[0]).y;

            Handles.BeginGUI();
            DrawNotchOverlayOn(Mathf.Min(x0, x1), Mathf.Max(x0, x1), Mathf.Min(y0, y1), Mathf.Max(y0, y1), isImgui: false);
            Handles.EndGUI();
        }

        // ── (c) 実操作 ──

        private void MovePad(MoveDirection dir)
        {
            if (_target == null)
            {
                return;
            }

            // (レビュー対応 2026-09-14, docs/11 4-R) Move は UiSlider だけでなく子(Handle / Fill)の RectTransform も
            // 書き換えるため、実物は階層ごと Undo に積む(以前は UiSlider だけ RecordObject していて、Undo で値だけが戻り
            // 見た目が戻らなかった)。確認用シーンのサンプル(DontSave)は保存されないので Undo に積まない。
            var isPreview = IsPreviewObject(_target);
            if (!isPreview)
            {
                Undo.RegisterFullObjectHierarchyUndo(_target.gameObject, "UiSlider: パッド操作");
            }

            _target.Move(dir, _fineAdjust);
            if (!isPreview)
            {
                EditorUtility.SetDirty(_target);
            }
        }

        private void StartDragSimulate(bool includeCompare)
        {
            if (_target == null)
            {
                return;
            }

            // (レビュー対応 2026-09-14, docs/11 4-R) ドラッグ模擬は 20 フレームかけて値と見た目を書き換える。シーン上の
            // 実物に Undo 無しで掛かっていたため、Advance と同じく確認用シーンのサンプルだけに限る。
            if (!IsPreviewObject(_target))
            {
                if (_statusLabel != null)
                {
                    _statusLabel.text = "ドラッグ模擬は「確認用シーンにサンプルを配置」したスライダーでのみ動きます(シーン上の実物は書き換えません)";
                }

                AppendLog("ドラッグ模擬: 実物のため実行しません");
                return;
            }

            _dragSimActive = true;
            _dragSimStep = 0;
            _dragSimIncludesCompare = includeCompare && _compareSlider != null;
            _target.BeginDragAt(0f);
            if (_dragSimIncludesCompare)
            {
                _compareSlider.BeginDragAt(0f);
            }

            AppendLog("ドラッグ模擬開始");
        }

        private void TickDragSimulate()
        {
            if (!_dragSimActive || _target == null)
            {
                return;
            }

            _dragSimStep++;
            var t = Mathf.Clamp01(_dragSimStep / (float)DragSimSteps);
            _target.DragTo(t);
            if (_dragSimIncludesCompare && _compareSlider != null)
            {
                _compareSlider.DragTo(t);
            }

            if (_dragSimStep >= DragSimSteps)
            {
                _target.EndDrag();
                if (_dragSimIncludesCompare && _compareSlider != null)
                {
                    _compareSlider.EndDrag();
                }

                _dragSimActive = false;
                AppendLog("ドラッグ模擬完了");
            }
        }

        // ── (d) 追従比較 ──

        private void SpawnCompareSlider()
        {
            if (_target == null)
            {
                _statusLabel.text = "先に対象 UiSlider を用意してください";
                return;
            }

            if (_compareSlider != null)
            {
                Object.DestroyImmediate(_compareSlider.gameObject);
            }

            var canvas = EnsurePreviewCanvas();
            var slider = PreviewSliderFactory.Create(canvas.transform, "CompareSlider", new Vector2(320f, 0f));
            slider.Response = _target.Response;
            slider.FollowMotion = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(DDrive.Foundation.Easing.Ease.OutBack), Time = TimeDef.Duration(0.4f), Loop = LoopMode.Once };
            slider.SetRange(_target.Min, _target.Max);
            EditorPreviewRoots.MarkDontSaveRecursive(canvas);
            _compareSlider = slider;

            RebuildCompareSection();
            AppendLog("比較用 2 体目を配置しました");
        }

        private void RebuildCompareSection()
        {
            if (_compareContainer == null)
            {
                return;
            }

            _compareContainer.Clear();
            if (_compareSlider == null)
            {
                _compareContainer.Add(new Label("未配置") { style = { opacity = 0.6f } });
                return;
            }

            var so = new SerializedObject(_compareSlider);
            var field = new PropertyField(so.FindProperty(nameof(UiSlider.FollowMotion)), "2 体目の FollowMotion");
            field.Bind(so);
            _compareContainer.Add(field);
        }

        // ── (e) Skin プレビュー ──

        private void SpawnSkinStatesPreview()
        {
            if (_target == null)
            {
                _statusLabel.text = "先に対象 UiSlider を用意してください";
                return;
            }

            foreach (var s in _skinPreviewSliders)
            {
                if (s != null)
                {
                    Object.DestroyImmediate(s.gameObject);
                }
            }

            _skinPreviewSliders.Clear();

            var canvas = EnsurePreviewCanvas();
            var skin = GetTargetSkin();
            var states = new[] { ControlState.Normal, ControlState.Hover, ControlState.Pressed, ControlState.Selected, ControlState.Disabled, ControlState.Locked };
            for (var i = 0; i < states.Length; i++)
            {
                var slider = PreviewSliderFactory.Create(canvas.transform, $"SkinPreview_{states[i]}", new Vector2(i * 340f, -80f));
                if (skin != null)
                {
                    slider.SetVisual(skin);
                }

                slider.ForceStateForPreview(states[i]);
                _skinPreviewSliders.Add(slider);
            }

            EditorPreviewRoots.MarkDontSaveRecursive(canvas);
            AppendLog("6 状態を並べました");
        }

        private SliderSkinData GetTargetSkin()
        {
            // ResolvedSkin は protected のため、実行時の見た目を再現するには SkinId 解決結果を直接は読めない。
            // SliderEditor では「明示的に SetVisual した Skin」を優先し、無ければ SkinId 経由の解決は
            // UiSkins.Resolver に依存する実行時専用の仕組みなので、エディタでは AssetDatabase から探す。
            if (_target == null)
            {
                return null;
            }

            if (!_target.SkinId.IsValid)
            {
                return _explicitSkin;
            }

            // (レビュー対応 2026-09-14) 全件走査をやめ、Id ごとにキャッシュする DataIdLookup で引く。
            return DataIdLookup.Find<SliderSkinData>(_target.SkinId.Value);
        }

        // ── (f) プリセット ──

        private void ApplyPreset(SliderPresets.SliderPreset preset)
        {
            if (_target == null)
            {
                _statusLabel.text = "先に対象 UiSlider を用意してください";
                return;
            }

            SliderPresets.Apply(_target, preset);
            RefreshAll();
            AppendLog($"プリセット '{preset}' を適用しました");
        }

        // ── (g) Validation ──

        private void RebuildValidation()
        {
            if (_validationContainer == null)
            {
                return;
            }

            _validationContainer.Clear();
            if (_target == null)
            {
                return;
            }

            var results = new List<ValidationResult>();
            UiSliderValidation.Validate(_target, _target.name, results);
            if (results.Count == 0)
            {
                _validationContainer.Add(new Label("問題なし") { style = { opacity = 0.7f } });
                return;
            }

            foreach (var result in results)
            {
                var boxType = result.Severity == ValidationSeverity.Error ? HelpBoxMessageType.Error
                    : result.Severity == ValidationSeverity.Warning ? HelpBoxMessageType.Warning
                    : HelpBoxMessageType.Info;
                _validationContainer.Add(new HelpBox(result.Message, boxType));
            }
        }

        // ── 確認用シーン配置 ──

        // (レビュー対応 2026-09-14) 生の new GameObject + GameObject.Find をやめ、他のエディタと同じ EditorPreviewRoots を使う
        // (Find は非アクティブも見つかり、同名の本物のオブジェクト(DontSave でない)は拾わない)。
        private GameObject EnsurePreviewCanvas()
        {
            var existing = EditorPreviewRoots.Find(PreviewRootName);
            return existing != null ? existing : EditorPreviewRoots.CreateOverlayCanvas(PreviewRootName);
        }

        // (レビュー対応 2026-09-14) 組み立ては SliderSkinEditorWindow と共通の PreviewSliderFactory(子も DontSave。
        // 以前は子が HideFlags.None で、プレビューを置いたままシーンを保存すると子だけ親無しで保存されていた)。
        private void PlaceSampleInScene(SliderSkinData skin)
        {
            var canvas = EnsurePreviewCanvas();
            var slider = PreviewSliderFactory.Create(canvas.transform, "SampleSlider", Vector2.zero);
            if (skin != null)
            {
                slider.SetVisual(skin);
            }

            EditorPreviewRoots.MarkDontSaveRecursive(canvas);
            SetTarget(slider);
            _explicitSkin = skin;
            Selection.activeGameObject = slider.gameObject;
        }

        private void RemoveFromScene()
        {
            UnsubscribeEvents();
            _dragSimActive = false;
            EditorPreviewRoots.DestroyAll(PreviewRootName);

            _target = null;
            _explicitSkin = null;
            _compareSlider = null;
            _skinPreviewSliders.Clear();
        }

        // ── EditorApplication.update: Advance(dt) 駆動(ADR-4。ExecuteAlways を付けない方針のため手動で進める) ──

        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - _lastEditorTime);
            _lastEditorTime = now;
            if (dt <= 0f || dt > 1f)
            {
                return;
            }

            TickDragSimulate();

            // Codex レビュー対応(2026-09-11): 「対象 UiSlider」は ObjectField(allowSceneObjects=true)/
            // 「選択から取得」でシーン上の実物(ユーザーの配置物)を直接指せてしまう。Advance() は
            // anchoredPosition 等を Undo 無しで毎フレーム書き換えるため、実物を選ぶと気づかないうちに
            // 動かし続けてしまっていた。DontSave のプレビュー配下([確認用シーンに配置]で生成した実体)
            // のときだけ駆動し、それ以外は駆動しない(ステータスラベルで理由を示す)。
            if (IsPreviewObject(_target))
            {
                _target.Advance(dt);
            }
            else if (_target != null && _statusLabel != null)
            {
                _statusLabel.text = "シーン上の実物は駆動しません(「確認用シーンに配置」したサンプルを使ってください)";
            }

            _compareSlider?.Advance(dt);
            foreach (var s in _skinPreviewSliders)
            {
                s?.Advance(dt);
            }
        }

        // _target がこのウィンドウの「確認用シーンに配置」(PreviewRootName 配下、DontSave)で生成した
        // 実体かどうか。ユーザーがシーン上の実物を指定した場合は false になる。
        private static bool IsPreviewObject(UiSlider target)
        {
            if (target == null)
            {
                return false;
            }

            // (レビュー対応 2026-09-14) 名前に加えて「[D-Drive] で始まる DontSave のルート」であることも見る
            // (同じ名前を付けた本物のオブジェクトを駆動しない)。このウィンドウが作ったサンプルは従来どおり true。
            var root = target.transform.root;
            return root != null && root.name == PreviewRootName && EditorPreviewSweeper.IsPreviewRoot(root.gameObject);
        }
    }
}
