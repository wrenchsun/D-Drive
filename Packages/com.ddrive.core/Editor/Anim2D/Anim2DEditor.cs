using System;
using System.Collections.Generic;
using DDrive.Editor.Anim;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim2D
{
    // Anim2D のエディター(docs/1010 UX-7。旧 Anim2DEditorWindow / Anim2DCreateWindow は Archive メニューへ退避)。
    // 共通基盤(UxEditorWindow<Anim2DData> / FieldGuide / UxRow / UxCard / CanvasItemList)で組み、ここに書くのは Anim2D 固有の部分だけ:
    // 欄の表(Anim2DFieldGuide)・コマ / 方向 Clip / イベントの一覧(1 件 = 1 行)・リタイミングの適用・SpriteRenderer + Animator のプレビュー物を実 AnimManager で動かす確認(SceneAnimPreviewDriver。ADR-4)。
    // 新規作成は「スプライトから作成」(Anim2DCreateWizard。雛形 → 素材 → 名前と動き → 確認と登録のページ送り)。
    // イベントの編集(タイムライン上のマーカー D&D・SE / VFX の同時再生)は Anim Editor に任せる(AnimEditor.OpenForAnim2D)。ここではイベントの要約を一覧で見せる。
    // 部品ごとにファイルを分けている: .Lists.cs = コマ・方向 Clip・イベントの一覧 / .Preview.cs = プレビュー・再生・ログ・リタイミングの適用。
    [DataEditor(typeof(Anim2DData), "Anim2D Editor で開く")]
    internal sealed partial class Anim2DEditor : UxEditorWindow<Anim2DData>
    {
        private const string Page = "anim2d-editor";
        private const int MaxLog = 12;

        [SerializeField] private PlacementMode _placement = PlacementMode.Uniform;

        private SceneAnimPreviewDriver _scene;
        private GameObject _previewObject;
        private Handle<AnimMarker> _handle = Handle<AnimMarker>.Invalid;
        private readonly List<string> _log = new();

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "Animation (2D)")]
        public static void OpenFromMenu() => Open(Selection.activeObject as Anim2DData);

        public static void Open(Anim2DData target)
        {
            var w = OpenWindow<Anim2DEditor>("Anim2D Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "Anim2D";
        protected override FieldGuide<Anim2DData> Guide => Anim2DFieldGuide.Instance;
        protected override string ManualPage => Page;
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("Sprite Icon").image;
        protected override string SearchHint => "ラベル・説明・検索語、コマ・方向・イベントの名前に一致する設定だけ出す(例: リタイミング / 8方向 / SE)";

        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case Anim2DFieldGuide.PurPlay: return "move";
                case Anim2DFieldGuide.PurDir: return "direction";
                case Anim2DFieldGuide.PurFrames: return "frames";
                case Anim2DFieldGuide.PurEvents: return "events";
                case Anim2DFieldGuide.PurCheck: return "check";
                case FieldGuide<Anim2DData>.AdminPurpose: return "admin";
                default: return null;
            }
        }

        protected override string[] ModeTips => new[]
        {
            "必須の欄だけ。再生するクリップ",
            "よく使う欄まで。ステート・ループ・方向・コマの一覧・リタイミング・イベント・プレビュー",
            "すべての欄(レイヤー・マスク・BlendTree のパラメータ名・動作の取り決め)",
        };

        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("DirectionClips", "DirectionClips"),
            new KeyValuePair<string, string>("Directions", "Directions"),
            new KeyValuePair<string, string>("ParamXName", "ParamXName"),
            new KeyValuePair<string, string>("ParamYName", "ParamYName"),
            new KeyValuePair<string, string>("FrameRate", "Clip"),
            new KeyValuePair<string, string>("スライス済みスプライト", "Clip"),
            new KeyValuePair<string, string>("BlendTree", "ParamXName"),
            new KeyValuePair<string, string>("Controller", "StateName"),
            new KeyValuePair<string, string>("イベント", "Events"),
            new KeyValuePair<string, string>("StateName", "StateName"),
            new KeyValuePair<string, string>("Clip", "Clip"),
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => MessageTokens;

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case Anim2DFieldGuide.PurPlay: return "どのクリップを、どのステートで、どう切り替えて動かすか";
                case Anim2DFieldGuide.PurDir: return "4 方向 / 8 方向の BlendTree に入れるクリップ。1 方向 = 1 行";
                case Anim2DFieldGuide.PurFrames: return "Clip のスプライトを 1 コマ = 1 行で見て、コマの間隔(リタイミング)を整える";
                case Anim2DFieldGuide.PurEvents: return "再生の節目に SE・VFX・配置セットを出す。1 イベント = 1 行の要約";
                case Anim2DFieldGuide.PurCheck: return "プレビュー物を実 AnimManager で動かして確かめる(SceneView + ここの絵)";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // ── 検証: Anim2DDataValidator + Anim2DEditorValidator は共通の検査に含まれる。基底の AnimDataValidator(Clip の長さ・イベント範囲など)もここで通す ──

        protected override void AppendValidation(List<ValidationResult> results)
        {
            if (Target == null)
            {
                return;
            }

            try
            {
                var ctx = new ValidationContext(new List<AssetDataBase> { Target });
                results.AddRange(new AnimDataValidator().Validate(Target, ctx));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[D-Drive] AnimDataValidator が失敗しました: {e.Message}");
            }
        }

        // ── 再生(実 AnimManager。SceneAnimPreviewDriver) ──

        protected override bool SupportsPlayback => true;
        protected override bool IsPlaying => _scene != null && _scene.Manager.IsPlaying(_handle);
        protected override void StartPlayback(bool focus) => PlayFresh();
        protected override void StopPlayback() => StopAnim();
        protected override void OnSpeedChanged(float speed)
        {
            if (_scene != null)
            {
                _scene.Speed = speed;
            }
        }

        // ── ヒーロー・空状態 ──

        protected override void AddLeadingHeroActions(VisualElement actions)
        {
            var scene = PreviewPlacementButton.Create(
                "確認用シーン",
                "確認用シーンを開き(無ければ生成)、SpriteRenderer 付きのプレビュー物を置く。右クリックで、いま開いているシーンへ配置",
                OpenPreviewScene);
            scene.AddToClassList("ux-btn");
            actions.Add(scene);
            actions.Add(MakeButton("＋ スプライトから作成", null, Anim2DCreateWizard.Open, "スプライトを選び、雛形に沿って Clip と Anim2DData を作る(ページ送り)"));
        }

        protected override void AddEmptyActions(VisualElement zone)
        {
            zone.Add(MakeButton("＋ 新規作成", "ux-empty__sample", Anim2DCreateWizard.Open, "スプライトを選び、雛形に沿って Clip と Anim2DData を作る(ページ送りのウィザード)"));
        }

        // ── ライフサイクル ──

        protected override void OnWindowEnable()
        {
            _scene = new SceneAnimPreviewDriver { Speed = Speed };
            _scene.Manager.Events.OnEventFired += OnEventFired;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
        }

        protected override void OnWindowDisable()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.update -= OnEditorUpdate;
            if (_scene != null)
            {
                _scene.Manager.Events.OnEventFired -= OnEventFired;
                StopAnim();
                _scene.Dispose();
                _scene = null;
            }

            // プレビュー物(DontSave)は破棄しない: Anim Editor に引き渡している場合も、単にコンパイルが走った場合もあり、消すと相手の対象や絵が消えるため。
            // 残った物は次の FindOrCreate が拾い直す。明示的な破棄は「撤去」だけ。
            _previewObject = null;
        }

        protected override void OnTargetChanged()
        {
            _handle = Handle<AnimMarker>.Invalid;
            _eventNames = null;
            ReloadFrames();
            if (_previewObject != null)
            {
                Anim2DPreviewObject.ApplyFirstSprite(_previewObject, Target); // 前の対象の絵を残さない
            }
        }

        protected override void OnBuildBodyStart()
        {
            _frameList = null;
            _frameSection = null;
            _dirList = null;
            _dirSection = null;
            _eventList = null;
            _eventSection = null;
            _mirror = null;
            _previewStatus = null;
            _statusLabel = null;
            _retimeStatus = null;
            _logLabel = null;
        }

        // 既定はすべて折りたたみ: 構造体・配列の欄(PropertyField)が作る Foldout も、組み終えたあとに 1 度だけ閉じる。
        protected override void OnBodyBuilt(VisualElement page)
        {
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(30);
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(300);
            RefreshMirror();
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

        protected override void OnSideEffect()
        {
            ReloadFrames();
            _frameSection?.Rebuild();
            SceneView.RepaintAll();
        }

        protected override void OnPropertyEdited(string path)
        {
            if (path.StartsWith("Clip", StringComparison.Ordinal))
            {
                ReloadFrames();
                _frameSection?.Rebuild();
                if (_previewObject != null)
                {
                    Anim2DPreviewObject.ApplyFirstSprite(_previewObject, Target);
                }
            }
        }

        protected override void OnFiltersApplied(string query)
        {
            _frameList?.SetExternalQuery(query);
            _dirList?.SetExternalQuery(query);
            _eventList?.SetExternalQuery(query);
        }

        // ── 欄 ──

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            switch (e.Field)
            {
                case "DirectionClips":
                    return CreateDirectionRows(e);
                case Anim2DFieldGuide.RowFrames:
                    return CreateFramesRow(e);
                case Anim2DFieldGuide.RowRetime:
                    return CreateRetimeRow(e);
                case "Events":
                    return CreateEventRows(e);
                case Anim2DFieldGuide.RowPreview:
                    return CreatePreviewRow(e);
                case Anim2DFieldGuide.RowLog:
                    return CreateLogRow(e);
                default:
                    return base.CreateRow(e, card, isAdmin);
            }
        }

        // 外から Data を書いたあと: 検証・行の状態・一覧を最新にする。
        private void AfterDataWrite()
        {
            if (Target == null)
            {
                return;
            }

            RefreshAfterExternalWrite();
            _dirSection?.Sync();
            _eventSection?.Sync();
            _frameSection?.Rebuild();
            SceneView.RepaintAll();
        }

        private static void SetLabel(Label l, string text)
        {
            if (l != null && l.text != text)
            {
                l.text = text;
            }
        }

        private void OnProjectChanged() => _eventNames = null;
    }
}
