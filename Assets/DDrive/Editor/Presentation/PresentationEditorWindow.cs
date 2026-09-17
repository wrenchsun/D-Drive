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

        // タイムラインの表示範囲(ズーム/パン、5-4 追補 2026-09-14)。PresentationData にはシリアライズしない
        // (ウィンドウの状態のみ)。0,0 は「未初期化」の目印で、DrawTimeline が最初の描画で全体表示に直す。
        [SerializeField] private float _viewStart;
        [SerializeField] private float _viewEnd;
        [SerializeField] private bool _followPlayhead = true;

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

            UpdateSignalButtonsEnabledState(playing); // U-25: Signal ボタンの有効/無効を再生状態に追従させる

            var normalized = PresentationPreviewPlayback.ComputeSeekSliderValue(playing, _preview.NormalizedTime);
            if (playing)
            {
                _timelineContainer?.MarkDirtyRepaint();
                // U-7(2026-09-17): シークバーは IMGUIContainer(SeekBarGui)化したため、UI Toolkit の
                // Slider.SetValueWithoutNotify のような明示同期は不要(再生ヘッドは毎フレーム _preview.NormalizedTime
                // を読んで描く。AnimEditorWindow.DrawTimeline と同じ方式)。MarkDirtyRepaint だけ呼べばよい。
                _seekBarContainer?.MarkDirtyRepaint();
                Repaint();

                // 「再生ヘッドに追従」(既定 ON): 表示範囲の外に再生ヘッドが出ないよう自動スクロールする。
                if (_followPlayhead)
                {
                    var duration = PresentationTiming.EffectiveDuration(_target);
                    var elapsed = normalized * Mathf.Max(0f, duration);
                    SetView(PresentationTimelineZoom.FollowPlayhead(_viewStart, _viewEnd, elapsed, DisplayDuration));
                }
            }

            var elapsedSec = normalized * Mathf.Max(0f, PresentationTiming.EffectiveDuration(_target));
            _statusLabel.text = playing
                ? (_paused ? $"⏸ 一時停止中  {normalized:P0} ({elapsedSec:0.00}s)" : $"● 再生中  {normalized:P0} ({elapsedSec:0.00}s)")
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

            BuildTimelineControlsRow(root);
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
            // U-6(2026-09-17): 確認用シーンを「開くだけ」で、モデル配置は別ボタン(下の「配置」)だったため、
            // 押しても確認用シーンで演出が確認できる状態にならなかった。Model / Anim / Anim2D と同じく
            // 「片付ける → 確認用シーンを開く → 配置する」を 1 ボタンにまとめる。
            toolbar.Add(PreviewPlacementButton.CreateToolbarButton(
                "確認用シーンを開く",
                "ライト/カメラ/Volume/床を備えた確認用シーンを開き(無ければ生成)、モデル(ctx.Self)を配置する。統合プレビューはここで実行する",
                OpenPreviewScene));
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
                _commonFieldsFoldout.Add(new Button(FitTotalDurationToTracks)
                {
                    text = "トラックの最後に合わせる",
                    tooltip = "TotalDuration を各トラックの終了時刻(分かる場合はアセットの長さを加味、分からなければ最大時刻+0.5秒)の最大値に設定します",
                });
            }
        }

        // [08_presentation.md] 5-4 追補(2026-09-14) — 上の HelpBox にある「トラックの最後に合わせる」ボタン。
        // 実際の計算 + Undo は PresentationTrackEditOps.FitTotalDurationToTracks(ウィンドウを起動せずテスト可能)。
        private void FitTotalDurationToTracks()
        {
            if (_target == null)
            {
                return;
            }

            PresentationTrackEditOps.FitTotalDurationToTracks(_target);
            _serializedTarget?.Update();
            RebuildCommonFields();
            RefreshValidation();
            ResetViewToFit();
            _timelineContainer?.MarkDirtyRepaint();
        }

        // ── 対象アセット ──

        public void SetTarget(PresentationData data)
        {
            StopPreview();
            _target = data;
            _targetField?.SetValueWithoutNotify(data);
            ResetViewToFit(); // 対象を切り替えたら表示範囲(ズーム/パン)は全体表示に戻す(5-4 追補 2026-09-14)。
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
