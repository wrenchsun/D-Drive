using System;
using System.Collections.Generic;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Presentation
{
    // [08_presentation.md] §4(5-4) — PresentationEditor(マルチトラック UI + 統合プレビュー + Signal 手動発火)。
    // トラック編集([09] タイムライン規約を再利用)は PresentationEditorWindow.Tracks.cs、
    // 統合プレビューの駆動は ScenePresentationPreviewDriver(実 Manager を Editor から駆動、ADR-4)。
    [DataEditor(typeof(PresentationData), "Presentation エディタで開く")]
    public sealed partial class PresentationEditorWindow : EditorWindow
    {
        private const int MaxLogLines = 16;

        [SerializeField] private PresentationData _target;
        [SerializeField] private bool _lockTarget;
        [SerializeField] private ModelData _model;
        [SerializeField] private float _speed = 1f;
        [SerializeField] private bool _loopPreview;

        private ScenePresentationPreviewDriver _preview;
        private SerializedObject _serializedTarget;
        private SerializedProperty _tracksProp;
        private readonly List<string> _log = new();

        private IDisposable _subMarker;
        private IDisposable _subTrackFired;
        private IDisposable _subCompleted;
        private IDisposable _subCancelled;

        private ObjectField _targetField;
        private ObjectField _modelField;
        private Label _statusLabel;
        private Label _logLabel;
        private Foldout _validationFoldout;
        private VisualElement _signalRow;
        private VisualElement _tracksListContainer;
        private IMGUIContainer _timelineContainer;

        [MenuItem(DDriveMenu.Root + "Presentation Editor")]
        public static void Open() => Open(Selection.activeObject as PresentationData);

        public static void Open(PresentationData target)
        {
            var window = GetWindow<PresentationEditorWindow>("Presentation Editor");
            window.minSize = new Vector2(620, 560);
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        // ── ライフサイクル ──

        private void OnEnable()
        {
            _preview = new ScenePresentationPreviewDriver { };
            _preview.OnDataSignal += OnDataSignalFromPresentation;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            DisposeSubscriptions();
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (_preview != null)
            {
                _preview.OnDataSignal -= OnDataSignalFromPresentation;
                _preview.Dispose();
                _preview = null;
            }
        }

        private void OnSelectionChange()
        {
            if (!_lockTarget && Selection.activeObject is PresentationData selected && selected != _target)
            {
                SetTarget(selected);
            }
        }

        private void OnUndoRedo()
        {
            _serializedTarget?.Update();
            RefreshValidation();
            RefreshTracksList();
            _timelineContainer?.MarkDirtyRepaint();
        }

        private void OnEditorUpdate()
        {
            if (_preview == null || _statusLabel == null || _target == null)
            {
                return;
            }

            var playing = _preview.IsPlaying;
            if (!playing && _loopPreview && _preview.HasSelf)
            {
                Play();
                playing = true;
            }

            if (playing)
            {
                _timelineContainer?.MarkDirtyRepaint();
                Repaint();
            }

            var t = _preview.NormalizedTime;
            _statusLabel.text = playing
                ? $"● 再生中  {t:P0}"
                : (_loopPreview ? "↻ ループ待機" : "■ 停止中");
        }

        // ── UI 構築 ──

        private void CreateGUI()
        {
            BuildToolbar(rootVisualElement);

            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);
            var root = scrollView;
            root.style.paddingLeft = 6;
            root.style.paddingRight = 6;
            root.style.paddingTop = 6;

            _targetField = new ObjectField("対象アセット") { objectType = typeof(PresentationData) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as PresentationData));
            root.Add(_targetField);

            _validationFoldout = new Foldout { text = "検証(Validation)", value = true };
            root.Add(_validationFoldout);

            BuildCommonFieldsSection(root);
            BuildPreviewSection(root);

            _timelineContainer = new IMGUIContainer(DrawTimeline);
            _timelineContainer.style.height = TimelineTotalHeight + 24f;
            root.Add(_timelineContainer);

            BuildAddTrackRow(root);
            _tracksListContainer = new VisualElement();
            root.Add(_tracksListContainer);

            var logFoldout = new Foldout { text = "ログ(Marker / TrackFired / Signal)", value = true };
            _logLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.8f } };
            logFoldout.Add(_logLabel);
            root.Add(logFoldout);

            if (_target == null && !_lockTarget && Selection.activeObject is PresentationData selected)
            {
                SetTarget(selected);
            }
            else
            {
                RefreshTargetUi();
            }
        }

        private void BuildToolbar(VisualElement root)
        {
            var toolbar = new Toolbar();
            var lockToggle = new ToolbarToggle { text = "🔒 対象を固定", value = _lockTarget };
            lockToggle.RegisterValueChangedCallback(evt => _lockTarget = evt.newValue);
            toolbar.Add(lockToggle);
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(new ToolbarButton(VfxPreviewSceneSetup.OpenOrCreate)
            {
                text = "確認用シーンを開く",
                tooltip = "ライト/カメラ/Volume/床を備えた確認用シーンを開く(無ければ生成)。統合プレビューはここで実行する",
            });
            toolbar.Add(new ToolbarButton(() => { if (_target != null) EditorGUIUtility.PingObject(_target); }) { text = "Project で表示" });
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(NewAssetToolbarButton.CreateToolbarButton(typeof(PresentationEditorWindow)));
            root.Add(toolbar);
        }

        private void BuildCommonFieldsSection(VisualElement root)
        {
            var foldout = new Foldout { text = "共通設定(Total Duration / Interruptible)", value = true };
            foldout.userData = new object(); // 用途なし。フィールド追加はターゲット決定後に RebuildCommonFields で行う
            root.Add(foldout);
            _commonFieldsFoldout = foldout;
        }

        private Foldout _commonFieldsFoldout;

        private void RebuildCommonFields()
        {
            if (_commonFieldsFoldout == null)
            {
                return;
            }

            _commonFieldsFoldout.Clear();
            if (_serializedTarget == null)
            {
                _commonFieldsFoldout.Add(new Label("対象アセットを選択してください") { style = { opacity = 0.6f } });
                return;
            }

            foreach (var name in new[] { "TotalDuration", "Interruptible" })
            {
                var prop = _serializedTarget.FindProperty(name);
                if (prop == null)
                {
                    continue;
                }

                var field = new PropertyField(prop);
                field.Bind(_serializedTarget);
                field.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
                {
                    RefreshValidation();
                    _timelineContainer?.MarkDirtyRepaint();
                });
                _commonFieldsFoldout.Add(field);
            }

            if (PresentationTiming.EffectiveDuration(_target) <= 0f)
            {
                _commonFieldsFoldout.Add(new HelpBox(
                    "尺が 0 です。OnSignal のみで構成された演出は Total Duration を明示しないと最初の Tick で即完了します([08] 実装メモ参照)。",
                    HelpBoxMessageType.Warning));
            }
        }

        // ── 対象アセット ──

        public void SetTarget(PresentationData data)
        {
            StopPreview();
            _target = data;
            _targetField?.SetValueWithoutNotify(data);
            RefreshTargetUi();
        }

        private void RefreshTargetUi()
        {
            if (_targetField == null)
            {
                return; // CreateGUI 前
            }

            _serializedTarget = _target != null ? new SerializedObject(_target) : null;
            _tracksProp = _serializedTarget?.FindProperty("Tracks");
            _targetField.SetValueWithoutNotify(_target);
            RebuildCommonFields();
            RefreshTracksList();
            RefreshValidation();
            _timelineContainer?.MarkDirtyRepaint();
        }

        // ── 検証 ──

        private void RefreshValidation()
        {
            if (_validationFoldout == null)
            {
                return;
            }

            _validationFoldout.Clear();
            if (_target == null)
            {
                _validationFoldout.Add(new Label("対象アセットを選択してください") { style = { opacity = 0.6f } });
                return;
            }

            var any = false;
            foreach (var result in new PresentationDataValidator().Validate(_target, new ValidationContext(new List<AssetDataBase> { _target })))
            {
                any = true;
                _validationFoldout.Add(new HelpBox(result.Message, result.Severity == ValidationSeverity.Error ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning));
            }

            if (!any)
            {
                _validationFoldout.Add(new Label("Validation に問題はありません。") { style = { opacity = 0.6f } });
            }
        }

        // ── ログ ──

        private void AppendLog(string line)
        {
            _log.Add(line);
            while (_log.Count > MaxLogLines)
            {
                _log.RemoveAt(0);
            }

            if (_logLabel != null)
            {
                _logLabel.text = string.Join("\n", _log);
            }
        }

        private void OnDataSignalFromPresentation(string key) => AppendLog($"→ PlayContext.OnSignal(\"{key}\")(Kind=Signal トラック、データ→コード通知)");

        private void DisposeSubscriptions()
        {
            _subMarker?.Dispose();
            _subMarker = null;
            _subTrackFired?.Dispose();
            _subTrackFired = null;
            _subCompleted?.Dispose();
            _subCompleted = null;
            _subCancelled?.Dispose();
            _subCancelled = null;
        }
    }
}
