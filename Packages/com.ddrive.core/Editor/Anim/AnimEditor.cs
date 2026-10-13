using System;
using System.Collections.Generic;
using DDrive.Editor.Anim2D;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Event;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim
{
    // Anim(3D)のエディター(docs/1010 UX-6。旧 AnimEditorWindow は Archive メニューへ退避)。
    // 共通基盤(UxEditorWindow<AnimData> / FieldGuide / UxRow / UxCard / CanvasItemList)で組み、ここに書くのは Anim 固有の部分だけ:
    // 欄の表(AnimFieldGuide)・イベント / BlendShape / ステートの一覧(1 件 = 1 行)・IK・タイムライン(IMGUI)・ブレンド確認・
    // 開いているシーン / プレハブモードのモデルを実 AnimManager で動かすプレビュー(SceneAnimPreviewDriver。ADR-4。ウィンドウ内には何も描かない)。
    // 対象の Animator は自動で決まる: 「確認用シーン」→ 確認用モデルをシーンに配置して対象にする、「Prefab で開く」/ プレハブモードに入る → その Prefab の Animator を対象にする。手動で差し替えも可。
    // 部品ごとにファイルを分けている: .Scene.cs = 対象・モデル・再生・ログ / .Events.cs = イベントとタイムライン / .Source.cs = Animator から選択 /
    // .Body.cs = IK・BlendShape / .Blend.cs = ブレンド確認 / AnimEditorRows.cs = 固有の行。
    // Anim2DData(AnimData の派生)は Anim2D の新エディター(Anim2DEditor、UX-7)で開く(この登録は Anim2DData に引き継がない)。
    // Anim2DEditor の「イベントを Anim Editor で編集」だけは OpenForAnim2D で Anim2DData を対象にできる(タイムライン上のマーカー D&D・SE / VFX の同時再生。動かす相手は SpriteRenderer + Animator のプレビュー物)。
    [DataEditor(typeof(AnimData), "Anim Editor で開く", ExceptFor = typeof(Anim2DData))]
    internal sealed partial class AnimEditor : UxEditorWindow<AnimData>
    {
        // 空状態の「サンプルを作る」(docs/1010 UX-19)
        protected override bool HasSample => true;

        protected override AnimData CreateSample() => UxSamples.Anim();

        private const string Page = "anim-editor";
        private const int MaxEventLog = 12;

        [SerializeField] private ModelData _model;
        [SerializeField] private Animator _sceneTarget;
        [SerializeField] private bool _allowAnim2D; // OpenForAnim2D で開かれたとき true(対象が Anim2DData の間)

        private SceneAnimPreviewDriver _scene;
        private Handle<AnimMarker> _animHandle = Handle<AnimMarker>.Invalid;
        private readonly List<string> _eventLog = new();

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "Animation (3D)")]
        public static void OpenFromMenu() => Open(Selection.activeObject as AnimData);

        public static void Open(AnimData target)
        {
            if (target is Anim2DData a2d)
            {
                Anim2DEditor.Open(a2d);
                return;
            }

            var w = OpenWindow<AnimEditor>("Anim Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // Anim2D の新エディターから: イベントのタイムライン編集・SE / VFX の同時再生のために、Anim2DData をこのエディターで開く。
        public static void OpenForAnim2D(Anim2DData target)
        {
            var w = OpenWindow<AnimEditor>("Anim Editor");
            w._allowAnim2D = true;
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // 「一緒に調整」: Presentation 等が配置した Self の Animator をそのまま対象にする。確認用シーンを開き直さない・配置物を変えない
        // (SetSceneTarget は「手動で Animator を差し替える」経路と同じ)。Animator が無ければ通常の Open(AnimData) と同じ。
        public static void Open(AnimData target, GameObject attachTarget)
        {
            Open(target);
            if (target is Anim2DData || attachTarget == null || !attachTarget.TryGetComponent<Animator>(out var animator))
            {
                return;
            }

            var w = GetWindow<AnimEditor>("Anim Editor");
            w.SetSceneTarget(animator);
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "Anim";
        protected override FieldGuide<AnimData> Guide => AnimFieldGuide.Instance;
        protected override string ManualPage => Page;
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("AnimationClip Icon").image;
        protected override string SearchHint => "ラベル・説明・検索語、イベント・BlendShape・ステートの名前に一致する設定だけ出す(例: SE / 表情 / Walk)";

        // Anim2DData は Anim2DEditor に任せる(OpenForAnim2D で開かれたときだけ対象にできる)。
        protected override bool AcceptsTarget(AnimData data) => data != null && (data is not Anim2DData || _allowAnim2D);

        protected override void OnTargetRejected(AnimData data)
        {
            if (data is Anim2DData a2d)
            {
                Anim2DEditor.Open(a2d);
            }
        }

        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case AnimFieldGuide.PurPlay: return "move";
                case AnimFieldGuide.PurEvents: return "events";
                case AnimFieldGuide.PurBody: return "body";
                case AnimFieldGuide.PurCheck: return "check";
                case FieldGuide<AnimData>.AdminPurpose: return "admin";
                default: return null;
            }
        }

        protected override string[] ModeTips => new[]
        {
            "必須の欄だけ。再生するクリップ",
            "よく使う欄まで。ステート・ループ・イベント・IK・表情・タイムライン・ログ",
            "すべての欄(レイヤー・マスク・動作の取り決め・ブレンド確認)",
        };

        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("BlendShape", "BlendShapes"),
            new KeyValuePair<string, string>("イベント", "Events"),
            new KeyValuePair<string, string>("DefaultCrossFade", "DefaultCrossFade"),
            new KeyValuePair<string, string>("StateName", "StateName"),
            new KeyValuePair<string, string>("Clip", "Clip"),
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => MessageTokens;

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case AnimFieldGuide.PurPlay: return "どのクリップを、どのステートで、どう切り替えて動かすか";
                case AnimFieldGuide.PurEvents: return "再生の節目に SE・VFX・配置セットを出す。1 イベント = 1 行";
                case AnimFieldGuide.PurBody: return "再生に合わせた手足の IK と、表情(BlendShape)の動き";
                case AnimFieldGuide.PurCheck: return "シーン上のモデルを実 AnimManager で動かして確かめる(ブレンド・ログ)";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // ── 再生(実 AnimManager。SceneAnimPreviewDriver) ──

        protected override bool SupportsPlayback => true;
        protected override bool IsPlaying => _scene != null && _scene.Manager.IsPlaying(_animHandle);
        protected override void StartPlayback(bool focus) => PlayFresh();
        protected override void StopPlayback() => StopAnim();
        protected override void OnSpeedChanged(float speed)
        {
            if (_scene != null)
            {
                _scene.Speed = speed;
            }
        }

        // ── ヒーロー ──

        protected override void AddLeadingHeroActions(VisualElement actions)
        {
            var scene = PreviewPlacementButton.Create(
                "確認用シーン",
                "ライト / カメラ / Volume / 床を備えた確認用シーンを開き(無ければ生成)、確認用モデルを配置して対象にする。右クリックで、いま開いているシーンへ配置",
                OpenPreviewScene);
            scene.AddToClassList("ux-btn");
            actions.Add(scene);
            actions.Add(MakeButton("Prefab で開く", null, OpenModelPrefab, "確認用モデルの Prefab をプレハブモードで開き、その Animator を対象にする"));
        }

        // ── ライフサイクル ──

        protected override void OnWindowEnable()
        {
            _scene = new SceneAnimPreviewDriver { Speed = Speed };
            _scene.Manager.Events.OnEventFired += OnEventFired;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.projectChanged += OnProjectChangedForChoices;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageOpened;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;
        }

        protected override void OnWindowDisable()
        {
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
            PrefabStage.prefabStageOpened -= OnPrefabStageOpened;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            EditorApplication.projectChanged -= OnProjectChangedForChoices;
            EditorApplication.update -= OnEditorUpdate;
            if (_scene != null)
            {
                _scene.Manager.Events.OnEventFired -= OnEventFired;
                _scene.Dispose();
                _scene = null;
            }
        }

        protected override void OnTargetChanged()
        {
            _allowAnim2D = Target is Anim2DData;
            if (Target is Anim2DData a2d && _sceneTarget != null && _sceneTarget.gameObject.name == DDrive.Editor.Anim2D.Anim2DPreviewObject.Name)
            {
                DDrive.Editor.Anim2D.Anim2DPreviewObject.ApplyFirstSprite(_sceneTarget.gameObject, a2d); // 前の対象の絵を残さない
            }

            _animHandle = Handle<AnimMarker>.Invalid;
            _seqIndex = -1;
            _openEvent = -1;
            _openShape = -1;
            _selectedEvent = -1;
            _draggingEvent = -1;
            _foldOpen.Clear();
        }

        protected override void OnBuildBodyStart()
        {
            _timelineContainer = null;
            _eventList = null;
            _eventSection = null;
            _eventFlags.Clear();
            _eventHeads.Clear();
            _shapeList = null;
            _shapeSection = null;
            _shapeFlags.Clear();
            _shapeHeads.Clear();
            _stateList = null;
            _stateSection = null;
            _sceneTargetField = null;
            _sceneHelpLabel = null;
            _modelField = null;
            _modelSummaryLabel = null;
            _modelDetailFoldout = null;
            _modelDetailLabel = null;
            _showExternalShapesToggle = null;
            _modelIssueBox = null;
            _modelEditorButton = null;
            _statusLabel = null;
            _logLabel = null;
            _pauseButton = null;
            _seqStatusLabel = null;
            _layerWeightsContainer = null;
            _paramsContainer = null;
            _padXField = null;
            _padYField = null;
            _padContainer = null;
            _stepsBox = null;
            _layerAnimsBox = null;
            _paramSliders.Clear();
            _eventDetailSyncs.Clear();
        }

        // 既定はすべて折りたたみ: 構造体・配列の欄(PropertyField)が作る Foldout も、組み終えたあとに 1 度だけ閉じる(一覧の欄の Foldout は開閉状態を持つので除く)。
        protected override void OnBodyBuilt(VisualElement page)
        {
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(30);
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(300);
            RefreshSceneUi();
        }

        private static void CollapseFoldouts(VisualElement page)
        {
            foreach (var f in page.Query<Foldout>().ToList())
            {
                if (f.value && !f.ClassListContains("ux-sec__fold"))
                {
                    f.SetValueWithoutNotify(false);
                }
            }
        }

        protected override void OnSideEffect()
        {
            _timelineContainer?.MarkDirtyRepaint();
            SceneView.RepaintAll();
        }

        protected override void OnRowsRefreshed()
        {
            RefreshEventSummaries();
            RefreshShapeSummaries();
            _timelineContainer?.MarkDirtyRepaint();
        }

        protected override void OnPropertyEdited(string path)
        {
            RefreshEventSummaries();
            RefreshShapeSummaries();
            _timelineContainer?.MarkDirtyRepaint();
            if (path.StartsWith("Clip", StringComparison.Ordinal) || path.StartsWith("StateName", StringComparison.Ordinal) || path.StartsWith("Layer", StringComparison.Ordinal)
                || path.StartsWith("BlendShapes", StringComparison.Ordinal))
            {
                // 対象モデルとの照合(StateName / BlendShape 名)を取り直す。プロジェクト検索を含む重い再走査はしない。
                CollectModelIssues(CurrentAnimator);
                ApplyShapeFlags();
            }
        }

        protected override void OnFiltersApplied(string query)
        {
            _eventList?.SetExternalQuery(query);
            _shapeList?.SetExternalQuery(query);
            _stateList?.SetExternalQuery(query);
        }

        protected override void OnValidated(IReadOnlyList<ValidationResult> results)
        {
            ApplyEventFlags();
            ApplyShapeFlags();
        }

        // ── 欄 ──

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            switch (e.Field)
            {
                case AnimFieldGuide.RowStates:
                    return CreateStatesRow(e);
                case AnimFieldGuide.RowTimeline:
                    return CreateTimelineRow(e);
                case nameof(AnimData.Events):
                    return CreateEventRows(e);
                case nameof(AnimData.Ik):
                    return CreateIkRow(e);
                case nameof(AnimData.BlendShapes):
                    return CreateShapeRows(e);
                case AnimFieldGuide.RowTarget:
                    return CreateTargetRow(e);
                case AnimFieldGuide.RowModel:
                    return CreateModelRow(e);
                case AnimFieldGuide.RowPlayCtl:
                    return CreatePlayControlRow(e);
                case AnimFieldGuide.RowBlend:
                    return CreateBlendRow(e);
                case AnimFieldGuide.RowLog:
                    return CreateLogRow(e);
                default:
                    return base.CreateRow(e, card, isAdmin);
            }
        }

        // 外から Data を書いたあと(イベント・BlendShape の追加 / 削除 / 編集・コピーなど): 検証・行の状態・一覧・タイムラインを最新にする。
        private void AfterDataWrite()
        {
            if (Target == null)
            {
                return;
            }

            RefreshAfterExternalWrite();
            _eventSection?.Sync();
            _shapeSection?.Sync();
            _timelineContainer?.MarkDirtyRepaint();
            SceneView.RepaintAll();
        }

        // テスト・他から使う(検証 + 対象モデルとの照合は含まない)。
        public static List<ValidationResult> CollectValidation(AnimData target)
            => DDrive.Editor.Validation.DataValidationRunner.Run(target);

        // ── 小さな部品 ──

        private static void SetLabel(Label l, string text)
        {
            if (l != null && l.text != text)
            {
                l.text = text;
            }
        }
    }
}
