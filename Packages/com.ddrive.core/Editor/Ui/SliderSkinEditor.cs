using System.Collections.Generic;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ui
{
    // Slider Skin(SliderSkinData)のエディター(docs/1010 UX-13。旧 SliderSkinEditorWindow は Archive メニューへ退避)。
    // 共通基盤(UxEditorWindow<SliderSkinData> / FieldGuide / UxRow / UxCard)で組み、ここに書くのは Slider Skin 固有の部分だけ:
    // 欄の表(SliderSkinFieldGuide)・雛形 5 種(SkinPresetsRow。確認用スライダーに当てる)・状態とパーツの一覧(SkinStateList)・
    // SE の試聴付きの行(SkinSeRow)・動かしてみる・確認する(ControlSkinPreviewSection)。
    // ウィンドウ内には何も動かして描かない: 確認は「確認用シーンに配置」した実 UiSlider を Game / Scene ビューで見る(ADR-4)。
    // 応答曲線グラフ・ノッチ可視化・追従比較・全状態の並べ表示は Slider Editor(ヒーローの「Slider Editor へ」)。
    [DataEditor(typeof(SliderSkinData), "Skin Editor で開く")]
    internal sealed partial class SliderSkinEditor : UxEditorWindow<SliderSkinData>, ISkinStateHost
    {
        // 空状態の「サンプルを作る」(docs/1010 UX-19)
        protected override bool HasSample => true;

        protected override SliderSkinData CreateSample() => UxSamples.SliderSkin();

        private const string Page = "ui-skin";

        // エディタ固有の名前(旧 SliderSkinEditorWindow / Button Skin / UI Tween と共有すると、互いのプレビューを消してしまう)。
        private const string PreviewCanvasName = "[D-Drive] Slider Skin Editor Preview";
        private const float DragSimSeconds = 0.8f;

        private ControlSkinPreviewSection _check;
        private SkinStateList _stateList;
        private SkinPresetsRow _presets;
        private UiSlider _previewSlider;

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "Slider Skin")]
        public static void OpenFromMenu() => Open(Selection.activeObject as SliderSkinData);

        public static void Open(SliderSkinData target)
        {
            var w = OpenWindow<SliderSkinEditor>("Slider Skin Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "SliderSkin";
        protected override FieldGuide<SliderSkinData> Guide => SliderSkinFieldGuide.Instance;
        protected override string ManualPage => Page;
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("d_Slider Icon").image;
        protected override string SearchHint => "ラベル・説明・検索語(状態・パーツの中身も)に一致する設定だけ出す(例: Handle / 画像 / 目盛り / 当たり判定)";
        protected override bool SupportsPlayback => true;
        protected override bool SupportsSpeed => false;

        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("Tint.a", nameof(ControlSkinData.Normal)),
            new KeyValuePair<string, string>("Scroll Material", nameof(ControlSkinData.ScrollMaterial)),
            new KeyValuePair<string, string>("Override Sprite", nameof(ControlSkinData.Normal)),
            new KeyValuePair<string, string>("Anim Frames", nameof(ControlSkinData.Normal)),
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => MessageTokens;

        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case SliderSkinFieldGuide.PurPreset: return "sskin-presets";
                case SliderSkinFieldGuide.PurStates: return "sskin-states";
                case SliderSkinFieldGuide.PurParts: return "sskin-parts";
                case SliderSkinFieldGuide.PurSe: return "sskin-se";
                case SliderSkinFieldGuide.PurHit: return "sskin-hit";
                case SliderSkinFieldGuide.PurCheck: return "sskin-check";
                case FieldGuide<SliderSkinData>.AdminPurpose: return "sskin-admin";
                default: return null;
            }
        }

        protected override string[] ModeTips => new[]
        {
            "雛形と、状態・パーツの一覧だけ。雛形を選んで ▶ で確かめる",
            "よく使う欄まで。目盛りの装飾・音と触覚・押せる範囲・確認する",
            "すべての欄(つまみを隠す・触覚の Id・透明部分の判定・スクロール用マテリアル・動作の取り決め・イベント)",
        };

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case SliderSkinFieldGuide.PurPreset: return "確認用のスライダーに値の動き方を当てて、Skin の見た目と合わせて見る";
                case SliderSkinFieldGuide.PurStates: return "状態 6 つとパーツ 4 つ(Track / Fill / Handle / Delay Fill)を 1 行ずつ。▶ で確かめ、⚙ で色・拡大率・画像・コマ・演出を開く";
                case SliderSkinFieldGuide.PurParts: return "目盛りの装飾画像と、パッド操作時の見せ方";
                case SliderSkinFieldGuide.PurSe: return "掴む・離す・目盛り・端・拒否の SE とパッド振動。SE は右の ▶ で試聴";
                case SliderSkinFieldGuide.PurHit: return "押せる範囲の広げ幅(スライダー全体・つまみ)と、透明部分を押せなくする設定";
                case SliderSkinFieldGuide.PurCheck: return "確認用シーンに実際のスライダーを置いて、動かしてみる・状態の遷移・当たり判定を確かめる";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // ── ヒーロー ──

        protected override void AddLeadingHeroActions(VisualElement actions)
        {
            actions.Add(MakeButton("Slider Editor へ", null, () => SliderEditor.Open(Target),
                "応答曲線グラフ・ノッチ可視化・追従比較・全状態の並べ表示がある Slider Editor を開く(この Skin を当てたサンプルを確認用シーンに置く)"));
            actions.Add(MakeButton("確認用シーン", null, () => CanvasPreviewSceneSetup.TryOpenOrCreate(), "UI 確認用シーン(CanvasPreviewScene)を開く(無ければ生成)"));
        }

        // ── ライフサイクル ──

        protected override void OnWindowEnable()
        {
            // 前回閉じ損ねた残骸を消す。閉じる(ドメインリロード含む)ときは自分のプレビューを必ず片付ける。
            EditorPreviewRoots.DestroyAll(PreviewCanvasName);
            _lastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        protected override void OnWindowDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            // 試聴用の PreviewService も必ず閉じる(DetachFromPanelEvent 頼みだとドメインリロードで漏れる)。
            _check?.Dispose();
            RemoveFromScene();
        }

        protected override void OnBuildBodyStart()
        {
            _check?.Dispose();
            _check = null;
            _stateList = null;
            _presets = null;
            ClearMoveBlock();
        }

        // 別の Skin に変えたら、配置済みのプレビューにも新しい Skin を当てる(外したときも SetVisual(null) で元の見た目に戻す)。
        protected override void OnTargetChanged()
        {
            _check?.StopAll();
            if (_previewSlider != null)
            {
                _previewSlider.SetVisual(Target);
            }
        }

        protected override void OnFiltersApplied(string query) => _stateList?.SetExternalQuery(query);

        // ── 再生(固定バー = 「全状態を順に」の遷移) ──

        protected override bool IsPlaying => _check != null && _check.IsBusy;

        protected override void StartPlayback(bool focus)
        {
            if (Target != null)
            {
                _check?.PlaySequence(int.MaxValue); // 最後 = 「全状態を順に」
            }
        }

        protected override void StopPlayback() => _check?.StopAll();

        // ── 欄 ──

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            switch (e.Field)
            {
                case SkinGuideText.RowPresets:
                {
                    _presets = new SkinPresetsRow(ApplyPreset);
                    return new UiTweenAuxRow(e, _presets, true, null);
                }

                case nameof(ControlSkinData.Normal):
                {
                    var parts = new List<SkinPart>(SkinParts.States);
                    parts.AddRange(SkinParts.SliderParts);
                    _stateList = new SkinStateList(this, parts.ToArray(), SkinParts.KindNames);
                    var entries = new List<FieldGuideEntry>();
                    foreach (var p in parts)
                    {
                        entries.Add(Guide.Find(p.Field));
                    }

                    var row = new UxVisualRow(So, DefaultSo, entries, "状態・パーツの見た目", e.Hint, FieldTier.Required, _stateList, RowChanged) { NoFold = true, NoBulkReset = true };
                    row.Q<Label>(className: "ux-badge")?.Show(false);
                    row.ExtraMatch = q => _stateList != null && _stateList.AnyMatch(q);
                    return row;
                }

                case nameof(ControlSkinData.Hover):
                case nameof(ControlSkinData.Pressed):
                case nameof(ControlSkinData.Selected):
                case nameof(ControlSkinData.Disabled):
                case nameof(ControlSkinData.Locked):
                case nameof(SliderSkinData.Track):
                case nameof(SliderSkinData.Fill):
                case nameof(SliderSkinData.Handle):
                case nameof(SliderSkinData.DelayFill):
                    return null; // 状態・パーツの一覧の行に含める

                case nameof(SliderSkinData.GrabSe):
                case nameof(SliderSkinData.ReleaseSe):
                case nameof(SliderSkinData.NotchSe):
                case nameof(SliderSkinData.LimitSe):
                case nameof(SliderSkinData.DeniedSe):
                    return SkinSeRow.Create(this, e, RowChanged);

                case SkinGuideText.RowCheck:
                    return new UiTweenAuxRow(e, CreateCheckBox(), false, null);

                default:
                    return base.CreateRow(e, card, isAdmin);
            }
        }

        // ── 確認する ──

        private VisualElement CreateCheckBox()
        {
            var box = new VisualElement();
            box.AddToClassList("ux-sk-check");

            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            var place = PreviewPlacementButton.Create(
                "確認用シーンに配置",
                "UI 確認用シーン(CanvasPreviewScene)を開き、実 UiSlider を置いて SceneView をそこへ向ける",
                PlaceInScene);
            place.AddToClassList("ux-btn");
            line.Add(place);
            line.Add(MakeButton("撤去", null, RemoveFromScene, "「確認用シーンに配置」で置いたスライダーを片付ける"));
            box.Add(line);

            box.Add(BuildMoveBlock());

            _check = new ControlSkinPreviewSection(BuildOptions());
            _check.SetSkin(Target);
            box.Add(_check);
            UpdateMoveStatus();
            return box;
        }

        // SE の欄と状態遷移の並び。遷移は実行時(UiSlider)で音が鳴るタイミングに合わせる
        // (掴むと GrabSe、離すと ReleaseSe、Disabled / Locked で触ると DeniedSe)。
        private ControlSkinPreviewSection.Options BuildOptions()
        {
            const string grab = nameof(SliderSkinData.GrabSe);
            const string release = nameof(SliderSkinData.ReleaseSe);
            const string denied = nameof(SliderSkinData.DeniedSe);
            ControlSkinPreviewSection.TransitionStep S(ControlState state, string se = null) => new ControlSkinPreviewSection.TransitionStep(state, se);

            return new ControlSkinPreviewSection.Options
            {
                CheckOnly = true,
                EnsurePreview = EnsurePreview,
                CurrentPreview = () => _previewSlider,
                // この窓が毎フレーム UiSlider.Advance を回す(その中でスプライトアニメも進む)ので二重に進めない。
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

        // ── ISkinStateHost ──

        ControlSkinData ISkinStateHost.Skin => Target;

        SerializedObject ISkinStateHost.So => So;

        SerializedObject ISkinStateHost.DefaultSo => DefaultSo;

        void ISkinStateHost.Changed() => RowChanged();

        void ISkinStateHost.PlayState(ControlState state) => _check?.PlayStatePreview(state);

        void ISkinStateHost.OpenTween(ControlState state) => _check?.OpenTweenEditorFor(state);

        VisualElement ISkinStateHost.AnimImport(SerializedProperty stateProp) => _check?.BuildAnimImportRowFor(stateProp);

        string ISkinStateHost.StatusText => _check != null ? _check.StatusText : string.Empty;

        bool ISkinStateHost.CanAudition(string field) => _check != null && _check.CanAudition(field);

        bool ISkinStateHost.IsAuditioning(string field) => _check != null && _check.IsAuditioning(field);

        void ISkinStateHost.Audition(string field) => _check?.AuditionSe(field);

        void ISkinStateHost.StopAudition() => _check?.StopAudition();

        // テスト・確認用の読み取り口。
        internal SkinStateList StateList => _stateList;

        internal SkinPresetsRow Presets => _presets;

        internal ControlSkinPreviewSection Check => _check;

        internal UiSlider PreviewSlider => _previewSlider;
    }
}
