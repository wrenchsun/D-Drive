using System;
using System.Collections.Generic;
using DDrive.Editor.Anim;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DDrive.Editor.Model
{
    // Model のエディター(docs/1010 UX-9。旧 ModelEditorWindow は Archive メニューへ退避)。
    // 共通基盤(UxEditorWindow<ModelData> / FieldGuide / UxRow / UxCard / CanvasItemList)で組み、ここに書くのは Model 固有の部分だけ:
    // 欄の表(ModelFieldGuide)・Material スロットの 1 スロット = 1 行の一覧(→ 新 MaterialEditor)・Default Animation(→ 新 AnimEditor)・
    // モデルと Avatar の情報・確認用シーンへの配置と並べて確認(実 ModelsManager / AnimManager。SceneAnimPreviewDriver。ADR-4)。
    // 部品ごとにファイルを分けている: .Slots.cs = スロットの一覧と自動収集 / .Preview.cs = 配置・並べて確認・ターンテーブル・情報。
    [DataEditor(typeof(ModelData), "Model Editor で開く")]
    internal sealed partial class ModelEditor : UxEditorWindow<ModelData>
    {
        // 空状態の「サンプルを作る」(docs/1010 UX-19)
        protected override bool HasSample => true;

        protected override ModelData CreateSample() => UxSamples.Model();

        private const string Page = "model-editor";
        private const int MaxParallel = 4;
        private const float ParallelSpacingMeters = 2f;

        // 再コンパイルを跨いで残す状態([SerializeField])。Handle / VisualElement / ドライバは対象外。
        [SerializeField] private bool _turntableEnabled;
        [SerializeField] private float _turntableSpeed = 30f;
        [SerializeField] private ModelData[] _parallelModels = new ModelData[MaxParallel];

        private SceneAnimPreviewDriver _scene;
        private double _lastTurntableTime;

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "Model")]
        public static void OpenFromMenu() => Open(Selection.activeObject as ModelData);

        public static void Open(ModelData target)
        {
            var w = OpenWindow<ModelEditor>("Model Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "Model";
        protected override FieldGuide<ModelData> Guide => ModelFieldGuide.Instance;
        protected override string ManualPage => Page;
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("Avatar Icon").image;
        protected override string SearchHint => "ラベル・説明・検索語、スロットの Renderer 名・Material 名に一致する設定だけ出す(例: hair / Humanoid / 並べて)";

        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case ModelFieldGuide.PurPrefab: return "prefab";
                case ModelFieldGuide.PurSlots: return "slots";
                case ModelFieldGuide.PurAnim: return "anim";
                case ModelFieldGuide.PurRender: return "render";
                case ModelFieldGuide.PurCheck: return "check";
                case FieldGuide<ModelData>.AdminPurpose: return "admin";
                default: return null;
            }
        }

        protected override string[] ModeTips => new[]
        {
            "必須の欄だけ。Prefab",
            "よく使う欄まで。モデルの情報・Material スロット・Default Animation・Avatar・描画・確認する",
            "すべての欄(LOD の上書き・動作の取り決め・イベント)",
        };

        // 検証メッセージ(ValidationResult.Field が無い結果)を欄に結びつける語。上にあるものを先に当てる。
        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("Avatar", "Avatar"),
            new KeyValuePair<string, string>("Slots", "Slots"),
            new KeyValuePair<string, string>("Prefab", "Prefab"),
            new KeyValuePair<string, string>("シェーダー", "Prefab"),
            new KeyValuePair<string, string>("Shader", "Prefab"),
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => MessageTokens;

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case ModelFieldGuide.PurPrefab: return "どの Prefab を実体にするか。Renderer・BlendShape・Listener の情報つき";
                case ModelFieldGuide.PurSlots: return "Renderer のスロットごとに、どの MaterialData を貼るか。1 スロット = 1 行";
                case ModelFieldGuide.PurAnim: return "出した直後に流すアニメと、Humanoid の Avatar";
                case ModelFieldGuide.PurRender: return "描画のレイヤー・ライトレイヤー・LOD の切り替え距離";
                case ModelFieldGuide.PurCheck: return "実際の Manager で作ったモデルを、確認用シーンで見る。別のモデルと並べて見比べる";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // ── 再生(▶ = 配置して DefaultAnimation を流す。実 AnimManager) ──

        protected override bool SupportsPlayback => true;
        protected override bool IsPlaying => _scene != null && _scene.OwnsCurrent && _scene.Manager.ActiveCount > 0;
        protected override void StartPlayback(bool focus) => PlayDefaultAnimation(focus);
        protected override void StopPlayback() => _scene?.Stop();
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
                "ライト / カメラ / Volume / 床を備えた確認用シーンを開き(無ければ生成)、対象を原点に配置する。右クリックで、いま開いているシーンへ配置",
                OpenPreviewScene);
            scene.AddToClassList("ux-btn");
            actions.Add(scene);
            actions.Add(MakeButton("Prefab で開く", null, OpenPrefab, "ModelData.Prefab をプレハブモードで開く(Renderer / Material をその場で編集)"));
        }

        // ── ライフサイクル ──

        protected override void OnWindowEnable()
        {
            _scene = new SceneAnimPreviewDriver { Speed = Speed };
            _lastTurntableTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
        }

        protected override void OnWindowDisable()
        {
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            EditorApplication.update -= OnEditorUpdate;
            if (_scene != null)
            {
                _scene.Dispose();
                _scene = null;
            }
        }

        protected override void OnTargetChanged()
        {
            RemoveMain();
            _note = string.Empty;
            _infoSig = int.MinValue;
        }

        protected override void OnBuildBodyStart()
        {
            _slotList = null;
            _slotBox = null;
            _slotStatus = null;
            _slotPlacedButton = null;
            _infoLabel = null;
            _avatarLabel = null;
            _avatarFixButton = null;
            _sceneStatus = null;
            for (var i = 0; i < MaxParallel; i++)
            {
                _parallelButtons[i] = null;
            }
        }

        // 既定はすべて折りたたみ: 構造体・配列の欄(PropertyField)が作る Foldout も、組み終えたあとに閉じる。
        protected override void OnBodyBuilt(VisualElement page)
        {
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(30);
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(300);
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(1000);
            RefreshInfoLabels();
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

        protected override void OnPropertyEdited(string path)
        {
            if (path.StartsWith("Prefab", StringComparison.Ordinal) || path.StartsWith("Avatar", StringComparison.Ordinal))
            {
                _infoSig = int.MinValue; // 次の更新で情報を取り直す
            }
        }

        protected override void OnFiltersApplied(string query)
        {
            _slotList?.SetExternalQuery(query);
        }

        // 定期更新(150ms): 配置の状態・並べて確認のボタンの文言。
        protected override void OnTick()
        {
            RefreshSceneUi();
        }

        // ── 欄 ──

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            switch (e.Field)
            {
                case ModelFieldGuide.RowInfo:
                    return CreateInfoRow(e);
                case ModelFieldGuide.RowSlotTools:
                    return CreateSlotToolsRow(e);
                case "Slots":
                    return CreateSlotsRow(e);
                case "DefaultAnimation":
                    return CreateDefaultAnimationRow(e);
                case ModelFieldGuide.RowAvatarInfo:
                    return CreateAvatarInfoRow(e);
                case ModelFieldGuide.RowParallel:
                    return CreateParallelRow(e);
                case ModelFieldGuide.RowScene:
                    return CreateSceneRow(e);
                default:
                    GuidedField.Collapse(So, e.Field); // 構造体・配列の Foldout は既定で閉じておく
                    return base.CreateRow(e, card, isAdmin);
            }
        }

        // DefaultAnimation: 通常の ID の欄 + 「Anim Editor で開く」(新 AnimEditor。Anim2D の Data なら Anim2D Editor)。
        private UxRow CreateDefaultAnimationRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            box.AddToClassList("ux-mdl-animline");
            var field = GuidedField.MakeInput(So, e.Field);
            field.AddToClassList("ux-mdl-animfield");
            box.Add(field);
            var open = MakeButton("Anim Editor で開く", null, OpenDefaultAnimation, "最初に流すアニメを専用エディター(Anim Editor)で開く(クリップ・ステート・イベントの調整)");
            box.Add(open);
            return new UxRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, false, box, RowChanged);
        }

        private void OpenDefaultAnimation()
        {
            if (Target == null || !Target.DefaultAnimation.IsValid)
            {
                SetNote("Default Animation が未設定です。先に選んでください");
                return;
            }

            var anim = DDrive.Editor.Ui.DataIdLookup.Find<DDrive.Runtime.Anim.AnimData>(Target.DefaultAnimation.Value);
            if (anim == null)
            {
                SetNote("Default Animation の Data が見つかりません(削除された可能性があります)");
                return;
            }

            DataEditorRegistry.OpenDefault(anim);
        }

        private static void SetLabel(Label l, string text)
        {
            if (l != null && l.text != text)
            {
                l.text = text;
            }
        }
    }
}
