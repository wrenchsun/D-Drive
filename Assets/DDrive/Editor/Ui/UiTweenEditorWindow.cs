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
    // スプライン制御点のハンドル編集を追加)。プロジェクト方針(2026-09-10)により
    // ウィンドウ内には「動く」ものは描画しない。カーブのグラフ表示・SceneView ハンドルは
    // 「静的な編集 UI」であり禁止対象ではない(動く Tween プレビュー自体は
    // 「確認用シーンに配置」が DontSave の Canvas+Image を出し、「▶ 再生」が実 UiTweenManager を
    // EditorApplication.update から駆動して見せる。ADR-4)。
    [DDrive.Editor.Inspector.DataEditor(typeof(UiTweenData), "UI Tween Editor で開く")]
    public sealed class UiTweenEditorWindow : EditorWindow
    {
        private const string PreviewRootName = "[D-Drive] Ui Preview";
        private const int SplinePreviewSamples = 32;

        [SerializeField] private UiTweenData _target;
        [SerializeField] private string _presetSelection;
        [SerializeField] private int _selectedTrackIndex;

        private ObjectField _targetField;
        private VisualElement _inspectorContainer;
        private VisualElement _validationContainer;
        private VisualElement _tracksListContainer;
        private VisualElement _selectedTrackContainer;
        private VisualElement _splineToolsContainer;
        private Label _statusLabel;
        private Label _sceneOwnerLabel;
        private DropdownField _presetDropdown;

        private readonly List<(string label, UiTweenData tween)> _catalogChoices = new();

        private UiTweenManager _previewManager;
        private RectTransform _previewTarget;
        private DDrive.Foundation.Handle.Handle<UiTweenMarker> _previewHandle;
        private double _lastEditorTime;

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

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
            // Codex レビュー対応(2026-09-11): CreateGUI は Show() のたびに複数回呼ばれ得るのに対し、
            // 解除は OnDestroy でしか行っていなかったため、購読が重複する余地があった。
            // OnEnable/OnDisable(必ず対になる)へ移す。
            EditorApplication.update += OnEditorUpdate;
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
        }

        private void CreateGUI()
        {
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);
            scrollView.style.paddingLeft = 6;
            scrollView.style.paddingRight = 6;
            scrollView.style.paddingTop = 6;

            _targetField = new ObjectField("対象 UiTweenData") { objectType = typeof(UiTweenData) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as UiTweenData));
            scrollView.Add(_targetField);

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

            const int samples = 64;
            var motion = track.Motion;
            var raw = new float[samples];
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

            var points = new Vector3[samples];
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
                "このウィンドウがフォーカスされている間、SceneView 上のハンドルで制御点をドラッグ編集できます(「確認用シーンに配置」した対象のローカル座標系)。",
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

        // SceneView 上の制御点ハンドル。対象は「確認用シーンに配置」した _previewTarget のローカル座標系
        // (world = _previewTarget.TransformPoint(local))。複数の D-Drive エディタが同時に開ける前提のため、
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

            _presetSelection = _presetDropdown.value;

            foreach (var (label, tween) in _catalogChoices)
            {
                if (label != _presetSelection)
                {
                    continue;
                }

                if (tween == null || tween.Tracks == null)
                {
                    _statusLabel.text = "カタログの Tween に Tracks がありません";
                    return;
                }

                Undo.RecordObject(_target, "UiTweenData: カタログから Tracks をコピー");
                var copy = new TweenTrack[tween.Tracks.Length];
                System.Array.Copy(tween.Tracks, copy, copy.Length);
                _target.Tracks = copy;
                EditorUtility.SetDirty(_target);
                _statusLabel.text = $"カタログ '{label}' から Tracks を生成しました";
                RebuildAll();
                return;
            }

            if (!System.Enum.TryParse<UiPreset>(_presetSelection, out var preset))
            {
                return;
            }

            var target = _previewTarget != null ? _previewTarget : CreateScratchRect();
            var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
            var refValue = new UiPresetRef { Preset = preset };
            var count = UiPresetFactory.Build(in refValue, target, buffer);

            Undo.RecordObject(_target, "UiTweenData: プリセットから Tracks を生成");
            var tracks = new TweenTrack[count];
            System.Array.Copy(buffer, tracks, count);
            _target.Tracks = tracks;
            EditorUtility.SetDirty(_target);

            if (target != _previewTarget)
            {
                Object.DestroyImmediate(target.gameObject);
            }

            _statusLabel.text = $"プリセット '{preset}' から Tracks を生成しました";
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

            _presetSelection = _presetDropdown.value;
            var existing = _target.Tracks ?? System.Array.Empty<TweenTrack>();
            var room = UiTweenManager.MaxTracksPerTween - existing.Length;
            if (room <= 0)
            {
                _statusLabel.text = $"Track が上限({UiTweenManager.MaxTracksPerTween} 本)に達しているため追加できません";
                return;
            }

            foreach (var (label, tween) in _catalogChoices)
            {
                if (label != _presetSelection)
                {
                    continue;
                }

                if (tween == null || tween.Tracks == null || tween.Tracks.Length == 0)
                {
                    _statusLabel.text = "カタログの Tween に Tracks がありません";
                    return;
                }

                var addCount = Mathf.Min(room, tween.Tracks.Length);
                Undo.RecordObject(_target, "UiTweenData: カタログの Tracks を追加");
                var merged = new TweenTrack[existing.Length + addCount];
                System.Array.Copy(existing, merged, existing.Length);
                System.Array.Copy(tween.Tracks, 0, merged, existing.Length, addCount);
                _target.Tracks = merged;
                EditorUtility.SetDirty(_target);
                _selectedTrackIndex = merged.Length - 1;
                _statusLabel.text = addCount < tween.Tracks.Length
                    ? $"カタログ '{label}' から Track を {addCount} 本追加しました(上限のため一部省略)"
                    : $"カタログ '{label}' から Track を {addCount} 本追加しました";
                RebuildAll();
                return;
            }

            if (!System.Enum.TryParse<UiPreset>(_presetSelection, out var preset))
            {
                return;
            }

            var target = _previewTarget != null ? _previewTarget : CreateScratchRect();
            var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
            var refValue = new UiPresetRef { Preset = preset };
            var generated = UiPresetFactory.Build(in refValue, target, buffer);
            var toAdd = Mathf.Min(room, generated);

            Undo.RecordObject(_target, "UiTweenData: プリセットの Track を追加");
            var result = new TweenTrack[existing.Length + toAdd];
            System.Array.Copy(existing, result, existing.Length);
            System.Array.Copy(buffer, 0, result, existing.Length, toAdd);
            _target.Tracks = result;
            EditorUtility.SetDirty(_target);

            if (target != _previewTarget)
            {
                Object.DestroyImmediate(target.gameObject);
            }

            _selectedTrackIndex = result.Length - 1;
            _statusLabel.text = toAdd < generated
                ? $"プリセット '{preset}' から Track を {toAdd} 本追加しました(上限のため一部省略)"
                : $"プリセット '{preset}' から Track を {toAdd} 本追加しました";
            RebuildAll();
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

        // ── 確認用シーンプレビュー(4-8。ADR-4: 実 UiTweenManager を EditorApplication.update から駆動) ──

        private void PlaceInScene()
        {
            RemoveFromScene();

            var canvasGo = new GameObject(PreviewRootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
            {
                hideFlags = HideFlags.DontSave,
            };
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var imageGo = new GameObject("PreviewImage", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            imageGo.transform.SetParent(canvasGo.transform, false);
            _previewTarget = (RectTransform)imageGo.transform;
            _previewTarget.sizeDelta = new Vector2(200f, 80f);

            Selection.activeGameObject = imageGo;
            _statusLabel.text = "確認用シーンに配置しました";
        }

        private void RemoveFromScene()
        {
            Stop();
            var existing = GameObject.Find(PreviewRootName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            _previewTarget = null;
        }

        // ADR-4: プレビューは実 Manager(UiTweenManager)を EditorApplication.update から駆動する。
        // EditorWindow 内で自前描画はしない。
        private void Play()
        {
            if (_target == null || _previewTarget == null)
            {
                _statusLabel.text = "先に「確認用シーンに配置」してください";
                return;
            }

            _previewManager ??= new UiTweenManager(EditorAnchorRegistryFallback());
            _previewHandle = _previewManager.PlayData(_target, _previewTarget);
            _lastEditorTime = EditorApplication.timeSinceStartup;
            _statusLabel.text = "再生中";
        }

        private void Stop()
        {
            if (_previewManager != null)
            {
                _previewManager.Stop(_previewHandle);
            }

            if (_statusLabel != null)
            {
                _statusLabel.text = "停止";
            }
        }

        private void OnEditorUpdate()
        {
            if (_previewManager == null || _previewTarget == null)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - _lastEditorTime);
            _lastEditorTime = now;
            _previewManager.Tick(dt);

            if (_statusLabel != null && !_previewManager.IsPlaying(_previewHandle))
            {
                _statusLabel.text = "完了";
            }
        }

        // UiTweenData は AssetId を引かないため、プレビュー用 Registry は Placeholder 解決さえできれば十分。
        private static DDrive.Foundation.Registry.IAssetRegistry EditorAnchorRegistryFallback()
            => DDrive.Editor.Preview.EditorAnchorRegistry.Build();
    }
}
