using System.Collections.Generic;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace DDrive.Editor.Ui
{
    // [15_ui_interaction.md] B-6 — UiTweenData 専用エディタ(4-8 の最小実装 → 4-10 でカーブ一覧 +
    // スプライン制御点のハンドル編集、2026-09-12 でプレビュー対象の選択を追加)。プロジェクト方針
    // (2026-09-10)によりウィンドウ内には「動く」ものは描画しない。カーブのグラフ表示・SceneView
    // ハンドルは「静的な編集 UI」であり禁止対象ではない(動く Tween プレビュー自体は、「収集元」から
    // 実要素を選ぶか「確認用シーンに配置」が出す DontSave の仮画像を対象にして、「▶ 再生」が実
    // UiTweenManager を EditorApplication.update から駆動して見せる。ADR-4)。
    [DDrive.Editor.Inspector.DataEditor(typeof(UiTweenData), "UI Tween Editor で開く")]
    public sealed class UiTweenEditorWindow : EditorWindow
    {
        // エディタ固有の名前(2026-09-14。以前は Button Skin / Slider Skin と共有していて、互いのプレビューを消していた)。
        private const string PreviewRootName = "[D-Drive] UI Tween Preview";
        private const int SplinePreviewSamples = 32;
        private const string NoElementChoice = "(なし)";
        // Canvas Editor / Preset Gallery と共有する(レビュー対応 2026-09-14。"(ルート)" の直書きが各所に散っていた)。
        internal const string RootElementLabel = "(ルート)";

        [SerializeField] private UiTweenData _target;
        [SerializeField] private string _presetSelection;
        [SerializeField] private int _selectedTrackIndex;
        [SerializeField] private string _selectedElementLabel = NoElementChoice;

        private ObjectField _targetField;
        private VisualElement _inspectorContainer;
        private VisualElement _validationContainer;
        private VisualElement _tracksListContainer;
        private VisualElement _selectedTrackContainer;
        private VisualElement _splineToolsContainer;
        private Label _statusLabel;
        private Label _sceneOwnerLabel;
        private DropdownField _presetDropdown;
        private ObjectField _collectRootField;
        private DropdownField _elementDropdown;

        private readonly List<(string label, UiTweenData tween)> _catalogChoices = new();
        // 「要素を自動収集」で集めた候補(2026-09-12)。CanvasEditor 等の「収集元」の子 RectTransform 一覧。
        // 実際に動かしたい要素(ボタンやパネル)を直接プレビュー対象にできるようにする(「確認用シーンに配置」の
        // 無関係な仮画像しか選べない、という声への対応)。
        private readonly List<(string label, RectTransform rt)> _elementChoices = new();

        [SerializeField] private GameObject _collectRoot;
        // PlaceInScene() が自分で作ったプレースホルダだけを覚えておく。「撤去」は所有物だけを消す
        // (収集/自動割り当てで得た「よそのオブジェクト」は触らない)。
        private GameObject _ownedPlaceholderRoot;

        private UiTweenManager _previewManager;
        [SerializeField] private RectTransform _previewTarget;
        private DDrive.Foundation.Handle.Handle<UiTweenMarker> _previewHandle;
        private double _lastEditorTime;

        // (レビュー対応 2026-09-14) 「▶ 再生」は収集元から選んだユーザーの実オブジェクトを直接動かすのに、Undo も
        // 復元も無かった(UiTweenManager が CanvasGroup を AddComponent することもある)。UiPresetGalleryWindow と同じく
        // 再生前の状態を保存し、停止・完了・対象の切り替え・ウィンドウを閉じるときに必ず戻す。元々無かった CanvasGroup は
        // 取り除く(差し引きでシーンに変更が残らないので Undo には積まない)。
        private RectTransform _snapTarget;
        private Vector2 _snapAnchoredPosition;
        private Vector2 _snapSizeDelta;
        private Vector3 _snapLocalScale;
        private Vector3 _snapLocalEuler;
        private bool _snapHadCanvasGroup;
        private float _snapAlpha;
        private UnityEngine.UI.Graphic _snapGraphic;
        private Color _snapColor;
        private UnityEngine.UI.Image _snapImage;
        private float _snapFillAmount;
        // 「再生中 → 終了」の変化を 1 回だけ拾うため(以前は終了後も毎フレーム「完了」を書き込んでいた。レビュー対応 2026-09-14)。
        private bool _wasPlaying;

        // DrawCurvePreview の作業バッファ(描画のたびに 2 配列を確保していた。レビュー対応 2026-09-14)。
        private const int CurvePreviewSamples = 64;
        private static readonly float[] CurveRawBuffer = new float[CurvePreviewSamples];
        private static readonly Vector3[] CurvePointBuffer = new Vector3[CurvePreviewSamples];

        [MenuItem(DDriveMenu.Editors + "UI Tween")]
        public static void OpenFromMenu() => Open(Selection.activeObject as UiTweenData);

        public static void Open(UiTweenData target)
        {
            var window = GetWindow<UiTweenEditorWindow>("UI Tween Editor");
            window.minSize = new Vector2(380, 360);
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        // Canvas Editor 等、確認したい実要素が既に分かっている呼び出し元向け(2026-09-12)。
        // collectRoot(実 Prefab インスタンスのルート)を渡すと「要素を自動収集」を自動実行し、
        // assignedElement があればそれを最初からプレビュー対象として選択状態にする。
        public static void Open(UiTweenData target, GameObject collectRoot, RectTransform assignedElement, string assignedElementLabel)
        {
            var window = GetWindow<UiTweenEditorWindow>("UI Tween Editor");
            window.minSize = new Vector2(380, 360);
            if (target != null)
            {
                window.SetTarget(target);
            }

            if (collectRoot != null)
            {
                window._collectRoot = collectRoot;
                window._collectRootField?.SetValueWithoutNotify(collectRoot);
                window.CollectElements();
            }

            if (assignedElement != null)
            {
                var label = string.IsNullOrEmpty(assignedElementLabel) ? RootElementLabel : assignedElementLabel;
                if (window._previewTarget != assignedElement)
                {
                    window.Stop(); // 前の対象で再生中なら止めて元に戻してから切り替える(レビュー対応 2026-09-14)
                }

                window._previewTarget = assignedElement;
                window._selectedElementLabel = label;
                window._elementDropdown?.SetValueWithoutNotify(label);
                if (window._statusLabel != null)
                {
                    window._statusLabel.text = $"プレビュー対象: {label}(Canvas Editor から自動割り当て)";
                }
            }
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
            // Codex レビュー対応(2026-09-11): CreateGUI は Show() のたびに複数回呼ばれ得るのに対し、
            // 解除は OnDestroy でしか行っていなかったため、購読が重複する余地があった。
            // OnEnable/OnDisable(必ず対になる)へ移す。
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;

            // 前回閉じ損ねた・ドメインリロードで参照を失った仮画像の残骸を消す(2026-09-14)。
            DDrive.Editor.Preview.EditorPreviewRoots.DestroyAll(PreviewRootName);
        }

        // (レビュー対応 2026-09-14) Undo/Redo を購読していなかったため、Track の追加・削除を Undo すると一覧が古いまま残り、
        // 選択中 Track の PropertyField が存在しない Tracks.Array.data[i] にバインドされたままになっていた。
        private void OnUndoRedoPerformed()
        {
            if (_target == null)
            {
                return;
            }

            RebuildAll(); // RebuildTrackList が _selectedTrackIndex を範囲内に丸める
            SceneView.RepaintAll();
        }

        private void OnFocus()
        {
            SceneGuiOwner.Claim(this);
            RefreshSceneOwnerLabel();
        }

        private void OnLostFocus() => RefreshSceneOwnerLabel();

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            SceneGuiOwner.Release(this);
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;

            // 閉じる / ドメインリロードの前に自分の仮画像を片付ける(参照を失うと残骸になるため。2026-09-14)。
            // 収集・自動割り当てで指している「よその実要素」は所有していないので触らない(RemoveFromScene の仕様)。
            RemoveFromScene();
        }

        private void CreateGUI()
        {
            // 5-15: 上部に「＋ 新規作成」ツールバー(スクロールしても見える固定行)。
            var toolbar = new Toolbar();
            toolbar.Add(DDrive.Editor.Inspector.NewAssetToolbarButton.CreateToolbarButton(typeof(UiTweenEditorWindow)));
            rootVisualElement.Add(toolbar);

            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);
            scrollView.style.paddingLeft = 6;
            scrollView.style.paddingRight = 6;
            scrollView.style.paddingTop = 6;

            _targetField = new ObjectField("対象 UiTweenData") { objectType = typeof(UiTweenData) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as UiTweenData));
            scrollView.Add(_targetField);

            // プレビュー対象(2026-09-12): 「確認用シーンに配置」の仮画像ではなく、実際に動かしたい
            // 要素(ボタンやパネル)で確認したいという要望への対応。収集元(シーン上のオブジェクト)の
            // 子を「要素を自動収集」で一覧化し、そこから選ぶ。Canvas Editor の「▶」から開いた場合は
            // 収集元・要素とも自動で入る(Open(target, collectRoot, assignedElement, label))。
            scrollView.Add(new Label("プレビュー対象") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            scrollView.Add(new HelpBox(
                "実際の要素で確認したいときは「収集元」にシーン上のオブジェクトを指定して「要素を自動収集」→「要素」で選びます" +
                "(Canvas Editor の「▶」から開いた場合は自動で入ります)。適当な仮物でよければ下の「確認用シーンに配置」を使ってください。",
                HelpBoxMessageType.Info));

            var collectRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, alignItems = Align.Center } };
            _collectRootField = new ObjectField("収集元") { objectType = typeof(GameObject), allowSceneObjects = true, style = { flexGrow = 1f } };
            _collectRootField.RegisterValueChangedCallback(evt => _collectRoot = evt.newValue as GameObject);
            collectRow.Add(_collectRootField);
            collectRow.Add(new Button(CollectElements) { text = "要素を自動収集" });
            scrollView.Add(collectRow);

            _elementDropdown = new DropdownField("要素", new List<string> { NoElementChoice }, 0) { style = { flexGrow = 1f } };
            _elementDropdown.SetValueWithoutNotify(_selectedElementLabel);
            _elementDropdown.RegisterValueChangedCallback(evt => SelectElement(evt.newValue));
            scrollView.Add(_elementDropdown);

            var presetRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6, alignItems = Align.Center } };
            _presetDropdown = new DropdownField("プリセット / カタログ", new List<string> { string.Empty }, 0) { style = { flexGrow = 1f } };
            presetRow.Add(_presetDropdown);
            presetRow.Add(new Button(GenerateFromSelection) { text = "プリセットから Tracks を生成", tooltip = "いまの Tracks を丸ごと置き換える" });
            presetRow.Add(new Button(AppendFromSelection) { text = "＋ プリセットを追加", tooltip = "いまの Tracks は残したまま、選んだプリセットの Track を末尾に追加する(スライドイン + フェードインのような組み合わせに)" });
            scrollView.Add(presetRow);
            RebuildPresetChoices();

            var sceneRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, marginBottom = 4 } };
            sceneRow.Add(new Button(PlaceInScene) { text = "確認用シーンに配置" });
            sceneRow.Add(new Button(Play) { text = "▶ 再生" });
            sceneRow.Add(new Button(Stop) { text = "■ 停止" });
            sceneRow.Add(new Button(RemoveFromScene) { text = "撤去" });
            scrollView.Add(sceneRow);

            _statusLabel = new Label();
            scrollView.Add(_statusLabel);

            scrollView.Add(new Label("カーブ一覧") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            _tracksListContainer = new VisualElement();
            scrollView.Add(_tracksListContainer);

            _selectedTrackContainer = new VisualElement { style = { marginTop = 4 } };
            scrollView.Add(_selectedTrackContainer);

            _splineToolsContainer = new VisualElement();
            scrollView.Add(_splineToolsContainer);

            _sceneOwnerLabel = new Label { style = { opacity = 0.65f, marginTop = 2 } };
            scrollView.Add(_sceneOwnerLabel);
            RefreshSceneOwnerLabel();

            _inspectorContainer = new VisualElement { style = { marginTop = 8 } };
            scrollView.Add(new Label("Inspector(全フィールド)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            scrollView.Add(_inspectorContainer);

            _validationContainer = new VisualElement { style = { marginTop = 8 } };
            scrollView.Add(_validationContainer);

            if (_target == null && Selection.activeObject is UiTweenData selected)
            {
                SetTarget(selected);
            }
            else
            {
                _targetField.SetValueWithoutNotify(_target);
                RebuildAll();
            }

            // ドメインリロード等でウィンドウが生き残った場合、収集元(SerializeField)から要素一覧を
            // 作り直す(_previewTarget 自体は SerializeField だが、参照先が張り替わっている可能性もあるため)。
            if (_collectRoot != null)
            {
                _collectRootField.SetValueWithoutNotify(_collectRoot);
                CollectElements();
            }
        }

        private void OnDestroy()
        {
            RemoveFromScene();
        }

        private void SetTarget(UiTweenData target)
        {
            _target = target;
            _selectedTrackIndex = 0;
            _targetField?.SetValueWithoutNotify(_target);
            RebuildAll();
        }

        private void RebuildAll()
        {
            RebuildInspector();
            RebuildValidation();
            RebuildTrackList();
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
                _inspectorContainer.Add(new Label("UiTweenData を選択してください"));
                return;
            }

            var so = new SerializedObject(_target);
            _inspectorContainer.Add(new InspectorElement(so));
        }

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

            _validationContainer.Add(new Label("Validation") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            var validator = new UiTweenDataValidator();
            var ctx = new ValidationContext(new List<DDrive.Foundation.Data.AssetDataBase>());
            var any = false;
            foreach (var result in validator.Validate(_target, ctx))
            {
                any = true;
                _validationContainer.Add(new Label($"[{result.Severity}] {result.Message}"));
            }

            if (!any)
            {
                _validationContainer.Add(new Label("問題なし"));
            }
        }

        // ── カーブ一覧(4-10) ──

        private void RebuildTrackList()
        {
            if (_tracksListContainer == null)
            {
                return;
            }

            _tracksListContainer.Clear();
            if (_target == null || _target.Tracks == null || _target.Tracks.Length == 0)
            {
                _tracksListContainer.Add(new Label("Tracks がありません(上のプリセット生成、または Inspector で追加してください)") { style = { opacity = 0.7f } });
                _selectedTrackIndex = 0;
                RebuildSelectedTrack();
                return;
            }

            if (_selectedTrackIndex >= _target.Tracks.Length)
            {
                _selectedTrackIndex = _target.Tracks.Length - 1;
            }

            if (_selectedTrackIndex < 0)
            {
                _selectedTrackIndex = 0;
            }

            for (var i = 0; i < _target.Tracks.Length; i++)
            {
                var index = i;
                var track = _target.Tracks[i];
                var selected = index == _selectedTrackIndex;
                var row = new VisualElement
                {
                    style =
                    {
                        flexDirection = FlexDirection.Row,
                        alignItems = Align.Center,
                        marginBottom = 2,
                        backgroundColor = selected ? new Color(0.25f, 0.42f, 0.58f, 0.35f) : new Color(0f, 0f, 0f, 0f),
                    },
                };

                var button = new Button(() => SelectTrack(index))
                {
                    text = $"[{index}] {TweenTrackSummary.Describe(in track)}",
                    style = { flexGrow = 1f, unityTextAlign = TextAnchor.MiddleLeft },
                };
                row.Add(button);

                var curve = new IMGUIContainer(() => DrawCurvePreview(GUILayoutUtility.GetRect(64, 32), track))
                {
                    style = { width = 70, height = 36 },
                };
                row.Add(curve);

                _tracksListContainer.Add(row);
            }

            RebuildSelectedTrack();
        }

        // 64 サンプルでイージング曲線の形だけを描く(From/To は使わず 0..1 の形のみ。TweenTrack.Motion の仕様通り)。
        private static void DrawCurvePreview(Rect rect, in TweenTrack track)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));

            const int samples = CurvePreviewSamples;
            var motion = track.Motion;
            var raw = CurveRawBuffer; // 使い回し(レビュー対応 2026-09-14)
            var min = float.MaxValue;
            var max = float.MinValue;
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)(samples - 1);
                var v = motion.Evaluate(t);
                raw[i] = v;
                if (v < min) min = v;
                if (v > max) max = v;
            }

            if (max - min < 0.0001f)
            {
                min -= 0.5f;
                max += 0.5f;
            }

            var points = CurvePointBuffer; // 使い回し(レビュー対応 2026-09-14)
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)(samples - 1);
                var norm = Mathf.InverseLerp(min, max, raw[i]);
                points[i] = new Vector3(rect.x + t * rect.width, rect.yMax - norm * rect.height, 0f);
            }

            Handles.BeginGUI();
            var prevColor = Handles.color;
            Handles.color = new Color(0.4f, 0.85f, 1f);
            Handles.DrawAAPolyLine(2f, points);
            Handles.color = prevColor;
            Handles.EndGUI();
        }

        private void SelectTrack(int index)
        {
            _selectedTrackIndex = index;
            RebuildTrackList();
            SceneView.RepaintAll();
        }

        private void RebuildSelectedTrack()
        {
            if (_selectedTrackContainer == null)
            {
                return;
            }

            _selectedTrackContainer.Clear();
            if (_target == null || _target.Tracks == null || _target.Tracks.Length == 0 ||
                _selectedTrackIndex < 0 || _selectedTrackIndex >= _target.Tracks.Length)
            {
                _splineToolsContainer?.Clear();
                return;
            }

            var so = new SerializedObject(_target);
            var tracksProp = so.FindProperty(nameof(UiTweenData.Tracks));
            var elementProp = tracksProp.GetArrayElementAtIndex(_selectedTrackIndex);
            var field = new PropertyField(elementProp, $"選択中 Track [{_selectedTrackIndex}]");
            field.Bind(so);
            _selectedTrackContainer.Add(field);
            _selectedTrackContainer.Add(new Button(RemoveSelectedTrack) { text = "－ 選択中の Track を削除", style = { marginTop = 2 } });

            RebuildSplineTools();
        }

        // ── スプライン制御点(4-10。SceneView ハンドルは OnSceneGui、ここは追加/削除ボタンのみ) ──

        private void RebuildSplineTools()
        {
            if (_splineToolsContainer == null)
            {
                return;
            }

            _splineToolsContainer.Clear();
            if (!TryGetSelectedTrack(out var track) || track.Property != TweenProperty.PathMove)
            {
                return;
            }

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            row.Add(new Button(AddSplinePoint) { text = "＋点を追加" });
            row.Add(new Button(RemoveLastSplinePoint) { text = "−最後の点を削除" });
            _splineToolsContainer.Add(row);
            _splineToolsContainer.Add(new HelpBox(
                "このウィンドウがフォーカスされている間、SceneView 上のハンドルで制御点をドラッグ編集できます(プレビュー対象のローカル座標系)。",
                HelpBoxMessageType.Info));
        }

        private bool TryGetSelectedTrack(out TweenTrack track)
        {
            if (_target == null || _target.Tracks == null || _selectedTrackIndex < 0 || _selectedTrackIndex >= _target.Tracks.Length)
            {
                track = default;
                return false;
            }

            track = _target.Tracks[_selectedTrackIndex];
            return true;
        }

        private void AddSplinePoint()
        {
            if (!TryGetSelectedTrack(out var track))
            {
                return;
            }

            var points = track.Path.Points ?? System.Array.Empty<Vector3>();
            var last = points.Length > 0 ? points[points.Length - 1] : Vector3.zero;
            var newPoints = new Vector3[points.Length + 1];
            System.Array.Copy(points, newPoints, points.Length);
            newPoints[points.Length] = last + new Vector3(50f, 0f, 0f);

            Undo.RecordObject(_target, "UiTweenData: スプライン点を追加");
            track.Path.Points = newPoints;
            _target.Tracks[_selectedTrackIndex] = track;
            EditorUtility.SetDirty(_target);
            RebuildInspector();
            RebuildTrackList();
            SceneView.RepaintAll();
        }

        private void RemoveLastSplinePoint()
        {
            if (!TryGetSelectedTrack(out var track) || track.Path.Points == null || track.Path.Points.Length == 0)
            {
                return;
            }

            var points = track.Path.Points;
            var newPoints = new Vector3[points.Length - 1];
            System.Array.Copy(points, newPoints, newPoints.Length);

            Undo.RecordObject(_target, "UiTweenData: スプライン点を削除");
            track.Path.Points = newPoints;
            _target.Tracks[_selectedTrackIndex] = track;
            EditorUtility.SetDirty(_target);
            RebuildInspector();
            RebuildTrackList();
            SceneView.RepaintAll();
        }

        // SceneView 上の制御点ハンドル。対象はプレビュー対象(_previewTarget。仮画像 or 収集した実要素)の
        // ローカル座標系(world = _previewTarget.TransformPoint(local))。複数の D-Drive エディタが同時に開ける前提のため、
        // SceneGuiOwner が最後にフォーカスしたウィンドウだけに描画権を渡す([04]§5 と同じ調停)。
        private void OnSceneGui(SceneView sceneView)
        {
            if (_target == null || _previewTarget == null || !SceneGuiOwner.IsOwner(this))
            {
                return;
            }

            if (!TryGetSelectedTrack(out var track) || track.Property != TweenProperty.PathMove)
            {
                return;
            }

            var points = track.Path.Points;
            if (points == null || points.Length == 0)
            {
                return;
            }

            var xform = _previewTarget;
            var changed = false;
            for (var i = 0; i < points.Length; i++)
            {
                var world = SplineHandleMath.LocalToWorld(xform, points[i]);
                EditorGUI.BeginChangeCheck();
                var moved = Handles.PositionHandle(world, xform.rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    if (!changed)
                    {
                        Undo.RecordObject(_target, "UiTweenData: スプライン点を移動");
                        changed = true;
                    }

                    points[i] = SplineHandleMath.WorldToLocal(xform, moved);
                }

                Handles.Label(world, $"P{i}");
            }

            if (points.Length >= 2)
            {
                var spline = new SplinePath(points, track.Path.Type);
                var poly = new Vector3[SplinePreviewSamples + 1];
                for (var i = 0; i <= SplinePreviewSamples; i++)
                {
                    poly[i] = SplineHandleMath.LocalToWorld(xform, spline.Evaluate(i / (float)SplinePreviewSamples));
                }

                var prevColor = Handles.color;
                Handles.color = new Color(0.3f, 0.8f, 1f);
                Handles.DrawAAPolyLine(3f, poly);
                Handles.color = prevColor;
            }

            if (changed)
            {
                EditorUtility.SetDirty(_target);
                RebuildInspector();
            }
        }

        private void RefreshSceneOwnerLabel()
        {
            if (_sceneOwnerLabel != null)
            {
                _sceneOwnerLabel.text = SceneGuiOwner.DescribeFor(this);
            }
        }

        // ── プリセット / カタログからの Tracks 生成 ──

        private void RebuildPresetChoices()
        {
            if (_presetDropdown == null)
            {
                return;
            }

            var choices = new List<string>(System.Enum.GetNames(typeof(UiPreset)));
            _catalogChoices.Clear();
            foreach (var (name, tween) in UiPresetCatalogUtility.Collect())
            {
                var label = $"[Catalog] {name}";
                choices.Add(label);
                _catalogChoices.Add((label, tween));
            }

            _presetDropdown.choices = choices;
            var current = string.IsNullOrEmpty(_presetSelection) ? nameof(UiPreset.None) : _presetSelection;
            if (!choices.Contains(current))
            {
                current = choices.Count > 0 ? choices[0] : string.Empty;
            }

            _presetSelection = current;
            _presetDropdown.SetValueWithoutNotify(current);
        }

        // Data 自体は編集しない。プリセット/カタログの展開結果を Tracks へ書き込むだけ(Undo 対応)。
        private void GenerateFromSelection()
        {
            if (_target == null || _presetDropdown == null)
            {
                return;
            }

            if (!TryResolveSelectionTracks(out var source, out var sourceLabel))
            {
                return;
            }

            Undo.RecordObject(_target, "UiTweenData: プリセット / カタログから Tracks を生成");
            var copy = new TweenTrack[source.Length];
            System.Array.Copy(source, copy, copy.Length);
            _target.Tracks = copy;
            EditorUtility.SetDirty(_target);
            _statusLabel.text = $"{sourceLabel} から Tracks を生成しました";
            RebuildAll();
        }

        // 「＋ プリセットを追加」: 上の GenerateFromSelection と違い、いまの Tracks を残したまま末尾に足す
        // (スライドイン + フェードインのように、複数のプリセット/手動 Track を組み合わせたいという要望への対応。2026-09-12)。
        // MaxTracksPerTween を超える分は追加しない(UiTweenManager 側もそれ以上再生しないため)。
        private void AppendFromSelection()
        {
            if (_target == null || _presetDropdown == null)
            {
                return;
            }

            var existing = _target.Tracks ?? System.Array.Empty<TweenTrack>();
            var room = UiTweenManager.MaxTracksPerTween - existing.Length;
            if (room <= 0)
            {
                _statusLabel.text = $"Track が上限({UiTweenManager.MaxTracksPerTween} 本)に達しているため追加できません";
                return;
            }

            if (!TryResolveSelectionTracks(out var source, out var sourceLabel))
            {
                return;
            }

            var addCount = Mathf.Min(room, source.Length);
            Undo.RecordObject(_target, "UiTweenData: プリセット / カタログの Track を追加");
            var merged = new TweenTrack[existing.Length + addCount];
            System.Array.Copy(existing, merged, existing.Length);
            System.Array.Copy(source, 0, merged, existing.Length, addCount);
            _target.Tracks = merged;
            EditorUtility.SetDirty(_target);
            _selectedTrackIndex = merged.Length - 1;
            _statusLabel.text = addCount < source.Length
                ? $"{sourceLabel} から Track を {addCount} 本追加しました(上限のため一部省略)"
                : $"{sourceLabel} から Track を {addCount} 本追加しました";
            RebuildAll();
        }

        // (レビュー対応 2026-09-14) Generate / Append で重複していた「ドロップダウンの選択(カタログ or 組み込みプリセット)
        // → Track 列」の解決を共通化。カタログの場合は元の配列をそのまま返す(呼び出し側がコピーする)。
        private bool TryResolveSelectionTracks(out TweenTrack[] tracks, out string sourceLabel)
        {
            tracks = null;
            sourceLabel = null;
            _presetSelection = _presetDropdown.value;

            foreach (var (label, tween) in _catalogChoices)
            {
                if (label != _presetSelection)
                {
                    continue;
                }

                if (tween == null || tween.Tracks == null || tween.Tracks.Length == 0)
                {
                    _statusLabel.text = "カタログの Tween に Tracks がありません";
                    return false;
                }

                tracks = tween.Tracks;
                sourceLabel = $"カタログ '{label}'";
                return true;
            }

            if (!System.Enum.TryParse<UiPreset>(_presetSelection, out var preset))
            {
                return false;
            }

            var target = _previewTarget != null ? _previewTarget : CreateScratchRect();
            var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
            var refValue = new UiPresetRef { Preset = preset };
            var count = UiPresetFactory.Build(in refValue, target, buffer);

            if (target != _previewTarget)
            {
                Object.DestroyImmediate(target.gameObject);
            }

            tracks = new TweenTrack[count];
            System.Array.Copy(buffer, tracks, count);
            sourceLabel = $"プリセット '{preset}'";
            return true;
        }

        private void RemoveSelectedTrack()
        {
            if (_target == null || _target.Tracks == null || _selectedTrackIndex < 0 || _selectedTrackIndex >= _target.Tracks.Length)
            {
                return;
            }

            Undo.RecordObject(_target, "UiTweenData: Track を削除");
            var tracks = _target.Tracks;
            var result = new TweenTrack[tracks.Length - 1];
            System.Array.Copy(tracks, 0, result, 0, _selectedTrackIndex);
            System.Array.Copy(tracks, _selectedTrackIndex + 1, result, _selectedTrackIndex, tracks.Length - _selectedTrackIndex - 1);
            _target.Tracks = result;
            EditorUtility.SetDirty(_target);
            _selectedTrackIndex = Mathf.Clamp(_selectedTrackIndex, 0, result.Length - 1);
            _statusLabel.text = "Track を削除しました";
            RebuildAll();
        }

        private static RectTransform CreateScratchRect()
        {
            var go = new GameObject("ScratchRect", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(200f, 80f);
            return rt;
        }

        // ── プレビュー対象の選択(2026-09-12) ──
        // 「収集元」の子 RectTransform を一覧化し、そこから実際にプレビューしたい要素を選べるようにする。
        // Canvas Editor の「▶」から開いたときは Open(target, collectRoot, assignedElement, label) が
        // これを自動実行する。手動でも、シーン上の好きなオブジェクトを「収集元」に入れて使える。

        private void CollectElements()
        {
            if (_elementDropdown == null)
            {
                return;
            }

            _elementChoices.Clear();
            var choices = new List<string> { NoElementChoice };

            if (_collectRoot != null)
            {
                // (レビュー対応 2026-09-14) 同名の兄弟があると同じラベルになり、どれを選んでも最初の要素になっていた。
                // 2 つ目以降に " #2" のような番号を付けてラベルを一意にする(選択はラベルで引くため)。
                var usedLabels = new HashSet<string>(System.StringComparer.Ordinal) { NoElementChoice };

                if (_collectRoot.transform is RectTransform rootRect)
                {
                    usedLabels.Add(RootElementLabel);
                    _elementChoices.Add((RootElementLabel, rootRect));
                    choices.Add(RootElementLabel);
                }

                foreach (var rect in _collectRoot.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rect.gameObject == _collectRoot)
                    {
                        continue;
                    }

                    var path = TransformPath.GetRelative(_collectRoot.transform, rect.transform); // 共通ヘルパーへ集約(レビュー対応 2026-09-14)
                    var label = path;
                    for (var n = 2; !usedLabels.Add(label); n++)
                    {
                        label = $"{path} #{n}";
                    }

                    _elementChoices.Add((label, rect));
                    choices.Add(label);
                }
            }

            _elementDropdown.choices = choices;
            var keep = choices.Contains(_selectedElementLabel) ? _selectedElementLabel : NoElementChoice;
            _elementDropdown.SetValueWithoutNotify(keep);
            ApplyElementSelection(keep);

            if (_statusLabel != null)
            {
                _statusLabel.text = _collectRoot == null
                    ? "収集元(シーン上のオブジェクト)を指定してください"
                    : $"要素を {_elementChoices.Count} 件収集しました";
            }
        }

        // ドロップダウンの値変更(ユーザー操作)経由。ステータス表示も更新する。
        private void SelectElement(string label)
        {
            ApplyElementSelection(label);
            if (label != NoElementChoice && _statusLabel != null)
            {
                _statusLabel.text = $"プレビュー対象: {label}";
            }
        }

        // CollectElements からの再適用(初期化・再収集時)とドロップダウン操作の共通処理。
        private void ApplyElementSelection(string label)
        {
            _selectedElementLabel = label;
            if (label == NoElementChoice)
            {
                return;
            }

            var match = _elementChoices.Find(c => c.label == label);
            if (match.rt != null)
            {
                if (match.rt != _previewTarget)
                {
                    Stop(); // 前の対象で再生中なら止めて元に戻してから切り替える(レビュー対応 2026-09-14)
                }

                _previewTarget = match.rt;
            }
        }

        // ── 確認用シーンプレビュー(4-8。ADR-4: 実 UiTweenManager を EditorApplication.update から駆動) ──
        // 「収集元」から実要素を選ばず手早く確認したいときの、無関係な仮画像 1 枚(旧来の挙動)。

        private void PlaceInScene()
        {
            RemoveFromScene();

            var canvasGo = new GameObject(PreviewRootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
            {
                hideFlags = HideFlags.DontSave,
            };
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _ownedPlaceholderRoot = canvasGo;

            var imageGo = new GameObject("PreviewImage", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            imageGo.transform.SetParent(canvasGo.transform, false);
            _previewTarget = (RectTransform)imageGo.transform;
            _previewTarget.sizeDelta = new Vector2(200f, 80f);

            // 仮画像は「要素を自動収集」の候補ではないので、選択表示を明示的に外しておく。
            _selectedElementLabel = NoElementChoice;
            _elementDropdown?.SetValueWithoutNotify(NoElementChoice);

            Selection.activeGameObject = imageGo;
            _statusLabel.text = "確認用シーンに配置しました";
        }

        private void RemoveFromScene()
        {
            Stop();
            if (_ownedPlaceholderRoot != null)
            {
                Object.DestroyImmediate(_ownedPlaceholderRoot);
                _ownedPlaceholderRoot = null;
            }

            // 自分で置いたプレースホルダを指していた場合は Destroy で自動的に(Unity の)null になる。
            // 収集/自動割り当てで得た「よそのオブジェクト」は撤去の対象外(所有していないため触らない)。
            if (_previewTarget == null)
            {
                _selectedElementLabel = NoElementChoice;
                _elementDropdown?.SetValueWithoutNotify(NoElementChoice);
            }
        }

        // ADR-4: プレビューは実 Manager(UiTweenManager)を EditorApplication.update から駆動する。
        // EditorWindow 内で自前描画はしない。
        private void Play()
        {
            if (_target == null || _previewTarget == null)
            {
                _statusLabel.text = "プレビュー対象がありません(上で要素を選ぶか、「確認用シーンに配置」してください)";
                return;
            }

            // (レビュー対応 2026-09-14) 収集元の ObjectField は Prefab アセットも受け付けるため、アセットを直接書き換えないよう弾く。
            if (EditorUtility.IsPersistent(_previewTarget.gameObject))
            {
                _statusLabel.text = "Prefab アセットは再生対象にできません(シーン上のオブジェクトを指定してください)";
                return;
            }

            // (レビュー対応 2026-09-14) ▶ の連打で前の Handle を止めずに上書きしていたため、ループが止められなくなっていた。
            // 前の再生を止めて元に戻してから始める。
            Stop();

            _previewManager ??= new UiTweenManager(EditorAnchorRegistryFallback());
            TakeSnapshot(_previewTarget);
            _previewHandle = _previewManager.PlayData(_target, _previewTarget);
            _lastEditorTime = EditorApplication.timeSinceStartup;
            _wasPlaying = _previewManager.IsPlaying(_previewHandle);
            if (!_wasPlaying)
            {
                RestoreSnapshot(); // Tracks 0 本などで即完了した
                _statusLabel.text = "完了";
                return;
            }

            _statusLabel.text = "再生中";
        }

        // 途中で止め(最終状態には進めない)、再生前の状態へ戻す(レビュー対応 2026-09-14)。
        private void Stop()
        {
            if (_previewManager != null)
            {
                _previewManager.Stop(_previewHandle);
            }

            _previewHandle = DDrive.Foundation.Handle.Handle<UiTweenMarker>.Invalid;
            _wasPlaying = false;
            RestoreSnapshot();

            if (_statusLabel != null)
            {
                _statusLabel.text = "停止";
            }
        }

        private void OnEditorUpdate()
        {
            if (_previewManager == null)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - _lastEditorTime);
            _lastEditorTime = now;
            if (!_wasPlaying)
            {
                return;
            }

            _previewManager.Tick(dt);

            // 「再生中 → 終了」に変わった 1 回だけ、元の状態へ戻してステータスを書く(レビュー対応 2026-09-14)。
            if (!_previewManager.IsPlaying(_previewHandle))
            {
                _wasPlaying = false;
                RestoreSnapshot();
                if (_statusLabel != null)
                {
                    _statusLabel.text = "完了(再生前の状態に戻しました)";
                }
            }
        }

        // UiTweenManager.ApplyTrack が書き込み得る値(位置・サイズ・スケール・回転・CanvasGroup.alpha・Graphic.color・
        // Image.fillAmount)を保存する(レビュー対応 2026-09-14)。
        private void TakeSnapshot(RectTransform target)
        {
            _snapTarget = target;
            _snapAnchoredPosition = target.anchoredPosition;
            _snapSizeDelta = target.sizeDelta;
            _snapLocalScale = target.localScale;
            _snapLocalEuler = target.localEulerAngles;

            var group = target.GetComponent<CanvasGroup>();
            _snapHadCanvasGroup = group != null;
            _snapAlpha = group != null ? group.alpha : 1f;

            _snapGraphic = target.GetComponent<UnityEngine.UI.Graphic>();
            _snapColor = _snapGraphic != null ? _snapGraphic.color : Color.white;
            _snapImage = target.GetComponent<UnityEngine.UI.Image>();
            _snapFillAmount = _snapImage != null ? _snapImage.fillAmount : 1f;
        }

        // 保存が無い / 対象が既に破棄されている場合は何もしない。1 回戻したら保存は捨てる(二重に戻さない)。
        private void RestoreSnapshot()
        {
            var target = _snapTarget;
            _snapTarget = null;
            if (target == null)
            {
                _snapGraphic = null;
                _snapImage = null;
                return;
            }

            target.anchoredPosition = _snapAnchoredPosition;
            target.sizeDelta = _snapSizeDelta;
            target.localScale = _snapLocalScale;
            target.localEulerAngles = _snapLocalEuler;

            var group = target.GetComponent<CanvasGroup>();
            if (group != null)
            {
                if (_snapHadCanvasGroup)
                {
                    group.alpha = _snapAlpha;
                }
                else
                {
                    Object.DestroyImmediate(group); // UiTweenManager が Alpha Track のために追加したもの
                }
            }

            if (_snapGraphic != null)
            {
                _snapGraphic.color = _snapColor;
            }

            if (_snapImage != null)
            {
                _snapImage.fillAmount = _snapFillAmount;
            }

            _snapGraphic = null;
            _snapImage = null;
        }

        // UiTweenData は AssetId を引かないため、プレビュー用 Registry は Placeholder 解決さえできれば十分。
        private static DDrive.Foundation.Registry.IAssetRegistry EditorAnchorRegistryFallback()
            => DDrive.Editor.Preview.EditorAnchorRegistry.Build();
    }
}
