using System;
using System.Collections.Generic;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Presentation
{
    // Presentation のエディター(docs/1010 UX-5。旧 PresentationEditorWindow は Archive メニューへ退避)。
    // 共通基盤(UxEditorWindow<PresentationData> / FieldGuide / UxRow / UxCard / AnchorPad / TuneKnobs)で組み、ここに書くのは Presentation 固有の部分だけ:
    // 欄の表(PresentationFieldGuide)・トラックの一覧(1 トラック = 1 行)・タイムライン(IMGUI)・Signal・統合プレビュー(実 PresentationManager)・SceneView の Anchor。
    // プレビューは実 Manager を Editor から駆動する(ADR-4。ScenePresentationPreviewDriver。ウィンドウ内には何も描かない)。
    // 部品ごとにファイルを分けている: .Tracks.cs = トラックの一覧・行・操作 / .Timeline.cs = タイムライン / .Preview.cs = 再生・Signal・環境・ログ /
    // .SceneAnchors.cs = SceneView 表示 / PresentationEditorRows.cs = 固有の行。
    [DataEditor(typeof(PresentationData), "Presentation Editor で開く")]
    internal sealed partial class PresentationEditor : UxEditorWindow<PresentationData>
    {
        // 空状態の「サンプルを作る」(docs/1010 UX-19)
        protected override bool HasSample => UxSamples.CanVfx;

        protected override PresentationData CreateSample() => UxSamples.Presentation();

        private const string Page = "presentation";

        [SerializeField] private ModelData _model;

        // タイムラインの表示範囲(ズーム / パン)。PresentationData にはシリアライズしない(ウィンドウの状態のみ)。0,0 は未初期化の目印。
        [SerializeField] private float _viewStart;
        [SerializeField] private float _viewEnd;
        [SerializeField] private bool _followPlayhead = true;

        private ScenePresentationPreviewDriver _preview;

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "Presentation Editor")]
        public static void OpenFromMenu() => Open(Selection.activeObject as PresentationData);

        public static void Open(PresentationData target)
        {
            var w = OpenWindow<PresentationEditor>("Presentation Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "Presentation";
        protected override FieldGuide<PresentationData> Guide => PresentationFieldGuide.Instance;
        protected override string ManualPage => Page;
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("AnimatorController Icon").image;
        protected override string SearchHint => "ラベル・説明・検索語、トラックのアセット名・Kind・Signal 名に一致する設定だけ出す(例: Vfx / hit / 位置)";

        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case PresentationFieldGuide.PurFlow: return "flow";
                case PresentationFieldGuide.PurTracks: return "tracks";
                case PresentationFieldGuide.PurSignal: return "signal";
                case PresentationFieldGuide.PurCheck: return "check";
                case FieldGuide<PresentationData>.AdminPurpose: return "admin";
                default: return null;
            }
        }

        protected override string[] ModeTips => new[]
        {
            "必須の欄だけ。トラックの一覧(追加・時刻・アセット)",
            "よく使う欄まで。タイムライン・長さ・Signal・確認用のモデルと SceneView 表示",
            "すべての欄(PredictLocal・Flags・イベント・環境)",
        };

        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("トラック ", "Tracks"),
            new KeyValuePair<string, string>("Flags.Net", "Flags"),
            new KeyValuePair<string, string>("尺", "TotalDuration"),
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => MessageTokens;

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case PresentationFieldGuide.PurFlow: return "全体の長さと、トラックを時間の上に並べて見る・動かす";
                case PresentationFieldGuide.PurTracks: return "1 トラック = 1 行。何を・いつ・どこに出すか";
                case PresentationFieldGuide.PurSignal: return "コードからの合図で動くトラックを、手で送って確かめる";
                case PresentationFieldGuide.PurCheck: return "確認用シーンのモデルで、実 Manager のまま同時に再生して確かめる";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // ── 再生(実 Manager。ScenePresentationPreviewDriver) ──

        protected override bool SupportsPlayback => true;
        protected override bool IsPlaying => _preview != null && _preview.IsPlaying;
        protected override void StartPlayback(bool focus) => StartFresh("▶");
        protected override void StopPlayback() => StopCurrentPlayback();
        protected override void OnSpeedChanged(float speed) => _preview?.SetSpeed(speed);

        // ── ヒーロー ──

        protected override void AddLeadingHeroActions(VisualElement actions)
        {
            var scene = PreviewPlacementButton.Create(
                "確認用シーン",
                "ライト / カメラ / Volume / 床を備えた確認用シーンを開き(無ければ生成)、モデル(ctx.Self)を配置する。統合プレビューはここで実行する。右クリックで、いま開いているシーンへ配置",
                OpenPreviewScene);
            scene.AddToClassList("ux-btn");
            actions.Add(scene);
        }

        // ── ライフサイクル ──

        protected override void OnWindowEnable()
        {
            _preview = new ScenePresentationPreviewDriver();
            _preview.OnDataSignal += OnDataSignalFromPresentation;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.projectChanged += OnProjectChangedForAssets;
        }

        protected override void OnWindowDisable()
        {
            DisposeSubscriptions();
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.projectChanged -= OnProjectChangedForAssets;
            DestroyAudition();
            if (_preview != null)
            {
                _preview.OnDataSignal -= OnDataSignalFromPresentation;
                _preview.Dispose();
                _preview = null;
            }

            SceneView.RepaintAll();
        }

        protected override void OnTargetChanged()
        {
            _selectedTrack = -1;
            _openTrack = -1;
            _paused = false;
            _rewindNoticeShown = false;
            DisposeSubscriptions();
            DestroyAudition();
            _assetCache.Clear();
            _trackItems.Clear();
            ResetViewToFit();
        }

        protected override void OnBuildBodyStart()
        {
            _trackList = null;
            _trackSection = null;
            _trackFlags.Clear();
            _trackHeads.Clear();
            _detailSyncs.Clear();
            _timelineContainer = null;
            _zoomSlider = null;
            _signalRow = null;
            _signalSig = string.Empty;
            _statusLabel = null;
            _logLabel = null;
            _pauseButton = null;
            _sceneOwnerLabel = null;
        }

        // 既定はすべて折りたたみ: 構造体・配列の欄(PropertyField)が作る Foldout も、組み終えたあとに 1 度だけ閉じる(開き直したものは触らない)。
        protected override void OnBodyBuilt(VisualElement page)
        {
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(30);
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(300);
        }

        private static void CollapseFoldouts(VisualElement page)
        {
            foreach (var f in page.Query<Foldout>().ToList())
            {
                if (f.value)
                {
                    f.SetValueWithoutNotify(false);
                }
            }
        }

        protected override void OnTick()
        {
            RefreshSignalButtonsIfChanged();
            UpdateSignalButtonsEnabled(IsPlaying);
            RefreshSceneOwnerLabel();
        }

        protected override void OnSideEffect()
        {
            _timelineContainer?.MarkDirtyRepaint();
            SceneView.RepaintAll();
        }

        protected override void OnRowsRefreshed()
        {
            RefreshTrackSummaries();
            _timelineContainer?.MarkDirtyRepaint();
        }

        protected override void OnPropertyEdited(string path)
        {
            RefreshTrackSummaries();
            _timelineContainer?.MarkDirtyRepaint();
            SceneView.RepaintAll();
        }

        protected override void OnFiltersApplied(string query) => _trackList?.SetExternalQuery(query);

        // 再生中のループ・追従・状態表示(フレームごと。再生ヘッドを滑らかに動かす)。
        private void OnEditorUpdate()
        {
            if (_preview == null || Target == null)
            {
                return;
            }

            var playing = _preview.IsPlaying;
            if (playing)
            {
                _timelineContainer?.MarkDirtyRepaint();
                var normalized = PresentationPreviewPlayback.ComputeSeekSliderValue(true, _preview.NormalizedTime);
                if (_followPlayhead && !_auditioning)
                {
                    var duration = PresentationTiming.EffectiveDuration(Target);
                    var elapsed = normalized * Mathf.Max(0f, duration);
                    SetView(PresentationTimelineZoom.FollowPlayhead(_viewStart, _viewEnd, elapsed, DisplayDuration));
                }
            }
            else if (_auditioning)
            {
                _auditioning = false;
            }

            UpdateStatus(playing);
        }

        // ── 欄 ──

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            switch (e.Field)
            {
                case nameof(PresentationData.Tracks):
                    return CreateTrackRows(e);
                case nameof(PresentationData.TotalDuration):
                    return CreateDurationRow(e);
                case PresentationFieldGuide.RowTimeline:
                    return CreateTimelineRow(e);
                case PresentationFieldGuide.RowSignals:
                    return CreateSignalsRow(e);
                case PresentationFieldGuide.RowModel:
                    return CreateModelRow(e);
                case PresentationFieldGuide.RowPlayCtl:
                    return CreatePlayControlRow(e);
                case PresentationFieldGuide.RowSceneView:
                    return CreateSceneViewRow(e);
                case PresentationFieldGuide.RowLog:
                    return CreateLogRow(e);
                case PresentationFieldGuide.RowEnv:
                    return CreateEnvironmentRow(e);
                default:
                    return base.CreateRow(e, card, isAdmin);
            }
        }

        // 変更後(トラックの追加・削除・Anchor の書き込みなど、外から Data を書いたあと): 検証・行の状態・一覧・タイムライン・SceneView を最新にする。
        private void AfterDataWrite()
        {
            if (Target == null)
            {
                return;
            }

            RefreshAfterExternalWrite();
            _trackSection?.Sync();
            RefreshSignalButtonsIfChanged();
            _timelineContainer?.MarkDirtyRepaint();
            SceneView.RepaintAll();
        }

        // ── 検証の行ごとの印 ──

        private readonly Dictionary<int, string> _trackIssue = new();

        protected override void OnValidated(IReadOnlyList<ValidationResult> results)
        {
            _trackIssue.Clear();
            for (var i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (r.Severity == ValidationSeverity.Info || string.IsNullOrEmpty(r.Message))
                {
                    continue;
                }

                if (TryTrackIndex(r.Message, out var index))
                {
                    _trackIssue[index] = r.Message;
                }
            }

            ApplyTrackFlags();
        }

        // 検証メッセージの先頭 "トラック {i}(…" から i を取る。
        private static bool TryTrackIndex(string message, out int index)
        {
            index = -1;
            const string prefix = "トラック ";
            var start = message.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return false;
            }

            start += prefix.Length;
            var end = start;
            while (end < message.Length && char.IsDigit(message[end]))
            {
                end++;
            }

            return end > start && int.TryParse(message.Substring(start, end - start), out index);
        }

        private void ApplyTrackFlags()
        {
            foreach (var kv in _trackFlags)
            {
                var has = _trackIssue.TryGetValue(kv.Key, out var msg);
                kv.Value.Show(has);
                kv.Value.tooltip = has ? msg : string.Empty;
            }
        }

        // テスト・他から使う(旧エディターと同じ実行部)。
        public static List<ValidationResult> CollectValidation(PresentationData target)
            => DDrive.Editor.Validation.DataValidationRunner.Run(target);
    }
}
