using System.Collections.Generic;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ui
{
    // Button Skin(ButtonSkinData)のエディター(docs/1010 UX-13。旧 ButtonSkinEditorWindow は Archive メニューへ退避)。
    // 共通基盤(UxEditorWindow<ButtonSkinData> / FieldGuide / UxRow / UxCard)で組み、ここに書くのは Button Skin 固有の部分だけ:
    // 欄の表(ButtonSkinFieldGuide)・状態の一覧(1 状態 = 1 行。SkinStateList)・SE の試聴付きの行(SkinSeRow)・確認する(ControlSkinPreviewSection)。
    // ウィンドウ内には何も動かして描かない: 確認は「確認用シーンに配置」した実 UiButton を Game / Scene ビューで見る(ADR-4)。
    [DataEditor(typeof(ButtonSkinData), "Skin Editor で開く")]
    internal sealed class ButtonSkinEditor : UxEditorWindow<ButtonSkinData>, ISkinStateHost
    {
        private const string Page = "ui-skin";

        // エディタ固有の名前(旧 ButtonSkinEditorWindow / Slider Skin / UI Tween と共有すると、互いのプレビューを消してしまう)。
        private const string PreviewCanvasName = "[D-Drive] Button Skin Editor Preview";

        private ControlSkinPreviewSection _check;
        private SkinStateList _stateList;
        private UiButton _previewButton;

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "Button Skin")]
        public static void OpenFromMenu() => Open(Selection.activeObject as ButtonSkinData);

        public static void Open(ButtonSkinData target)
        {
            var w = OpenWindow<ButtonSkinEditor>("Button Skin Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "ButtonSkin";
        protected override FieldGuide<ButtonSkinData> Guide => ButtonSkinFieldGuide.Instance;
        protected override string ManualPage => Page;
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("d_Button Icon").image;
        protected override string SearchHint => "ラベル・説明・検索語(状態の中身も)に一致する設定だけ出す(例: Hover / 画像 / クリック / 当たり判定)";
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
                case ButtonSkinFieldGuide.PurStates: return "button-states";
                case ButtonSkinFieldGuide.PurSe: return "button-se";
                case ButtonSkinFieldGuide.PurHit: return "button-hit";
                case ButtonSkinFieldGuide.PurCheck: return "button-check";
                case FieldGuide<ButtonSkinData>.AdminPurpose: return "button-admin";
                default: return null;
            }
        }

        protected override string[] ModeTips => new[]
        {
            "状態ごとの見た目の一覧だけ。▶ で確かめる",
            "よく使う欄まで。音(SE)・押せる範囲・確認する",
            "すべての欄(長押し・透明部分の判定・スクロール用マテリアル・動作の取り決め・イベント)",
        };

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case ButtonSkinFieldGuide.PurStates: return "Normal / Hover / Pressed / Selected / Disabled / Locked を 1 行ずつ。▶ で確かめ、⚙ で色・拡大率・画像・コマ・演出を開く";
                case ButtonSkinFieldGuide.PurSe: return "ホバー・クリック・長押し・拒否の SE。右の ▶ で試聴";
                case ButtonSkinFieldGuide.PurHit: return "押せる範囲の広げ幅と、透明部分を押せなくする設定";
                case ButtonSkinFieldGuide.PurCheck: return "確認用シーンに実際のボタンを置いて、状態の遷移・当たり判定・SE を確かめる";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // ── ヒーロー ──

        protected override void AddLeadingHeroActions(VisualElement actions)
        {
            actions.Add(MakeButton("確認用シーン", null, () => CanvasPreviewSceneSetup.TryOpenOrCreate(), "UI 確認用シーン(CanvasPreviewScene)を開く(無ければ生成)"));
        }

        // ── ライフサイクル ──

        // 前回閉じ損ねた残骸を消す。閉じる(ドメインリロード含む)ときは自分のプレビューを必ず片付ける。
        protected override void OnWindowEnable() => EditorPreviewRoots.DestroyAll(PreviewCanvasName);

        protected override void OnWindowDisable()
        {
            _check?.Dispose();
            RemoveFromScene();
        }

        protected override void OnBuildBodyStart()
        {
            _check?.Dispose();
            _check = null;
            _stateList = null;
        }

        // 別の Skin に変えたら、配置済みのプレビューにも新しい Skin を当てる(外したときも SetVisual(null) で元の見た目に戻す)。
        protected override void OnTargetChanged()
        {
            _check?.StopAll();
            if (_previewButton != null)
            {
                _previewButton.SetVisual(Target);
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
                case nameof(ControlSkinData.Normal):
                {
                    _stateList = new SkinStateList(this, SkinParts.States, null);
                    var entries = new List<FieldGuideEntry>();
                    foreach (var p in SkinParts.States)
                    {
                        entries.Add(Guide.Find(p.Field));
                    }

                    var row = new UxVisualRow(So, DefaultSo, entries, "状態ごとの見た目", e.Hint, FieldTier.Required, _stateList, RowChanged) { NoFold = true, NoBulkReset = true };
                    row.Q<Label>(className: "ux-badge")?.Show(false);
                    row.ExtraMatch = q => _stateList != null && _stateList.AnyMatch(q);
                    return row;
                }

                case nameof(ControlSkinData.Hover):
                case nameof(ControlSkinData.Pressed):
                case nameof(ControlSkinData.Selected):
                case nameof(ControlSkinData.Disabled):
                case nameof(ControlSkinData.Locked):
                    return null; // 状態の一覧の行に含める

                case nameof(ButtonSkinData.HoverSe):
                case nameof(ButtonSkinData.ClickSe):
                case nameof(ButtonSkinData.LongPressSe):
                case nameof(ButtonSkinData.DeniedSe):
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
                "UI 確認用シーン(CanvasPreviewScene)を開き、実 UiButton を置いて SceneView をそこへ向ける",
                PlaceInScene);
            place.AddToClassList("ux-btn");
            line.Add(place);
            line.Add(MakeButton("撤去", null, RemoveFromScene, "「確認用シーンに配置」で置いたボタンを片付ける"));
            box.Add(line);

            _check = new ControlSkinPreviewSection(BuildOptions());
            _check.SetSkin(Target);
            box.Add(_check);
            return box;
        }

        // SE の欄と状態遷移の並び。遷移は実行時(UiButton)で音が鳴るタイミングに合わせる
        // (Hover / Selected に入ると HoverSe、押して離すと ClickSe、Disabled / Locked で押すと DeniedSe)。
        private ControlSkinPreviewSection.Options BuildOptions()
        {
            const string hover = nameof(ButtonSkinData.HoverSe);
            const string click = nameof(ButtonSkinData.ClickSe);
            const string denied = nameof(ButtonSkinData.DeniedSe);
            ControlSkinPreviewSection.TransitionStep S(ControlState state, string se = null) => new ControlSkinPreviewSection.TransitionStep(state, se);

            return new ControlSkinPreviewSection.Options
            {
                CheckOnly = true,
                EnsurePreview = EnsurePreview,
                CurrentPreview = () => _previewButton,
                SeFields = new[]
                {
                    new ControlSkinPreviewSection.SeField(hover, s => ((ButtonSkinData)s).HoverSe),
                    new ControlSkinPreviewSection.SeField(click, s => ((ButtonSkinData)s).ClickSe),
                    new ControlSkinPreviewSection.SeField(nameof(ButtonSkinData.LongPressSe), s => ((ButtonSkinData)s).LongPressSe),
                    new ControlSkinPreviewSection.SeField(denied, s => ((ButtonSkinData)s).DeniedSe),
                },
                Sequences = new[]
                {
                    new ControlSkinPreviewSection.TransitionSequence("マウスでクリック",
                        S(ControlState.Normal), S(ControlState.Hover, hover), S(ControlState.Pressed), S(ControlState.Hover, click), S(ControlState.Normal)),
                    new ControlSkinPreviewSection.TransitionSequence("パッドで選んで決定",
                        S(ControlState.Normal), S(ControlState.Selected, hover), S(ControlState.Pressed), S(ControlState.Selected, click), S(ControlState.Normal)),
                    new ControlSkinPreviewSection.TransitionSequence("押せない(Disabled)ボタンを押す",
                        S(ControlState.Normal), S(ControlState.Disabled), S(ControlState.Disabled, denied), S(ControlState.Normal)),
                    new ControlSkinPreviewSection.TransitionSequence("ロック中(Locked)のボタンを押す",
                        S(ControlState.Normal), S(ControlState.Locked), S(ControlState.Locked, denied), S(ControlState.Normal)),
                    new ControlSkinPreviewSection.TransitionSequence("全状態を順に",
                        S(ControlState.Normal), S(ControlState.Hover), S(ControlState.Pressed), S(ControlState.Selected), S(ControlState.Disabled), S(ControlState.Locked), S(ControlState.Normal)),
                },
            };
        }

        private UiInteractable EnsurePreview()
        {
            if (_previewButton == null)
            {
                // ▶ 等からの暗黙の配置ではシーンを勝手に切り替えない(今開いているシーンに置く)。
                PlaceInScene(PreviewPlaceMode.CurrentScene);
            }

            return _previewButton;
        }

        // Data 自体は編集しない。実配置での見た目確認だけをシーン上で行う(ADR-4: プレビューは実コンポーネントを駆動する)。
        // 左クリック = UI 確認用シーンを開いてから配置 / 右クリック = このシーンに配置・本配置(PreviewPlacementButton)。
        private void PlaceInScene(PreviewPlaceMode mode)
        {
            if (Target == null)
            {
                return;
            }

            RemoveFromScene();
            if (!PreviewPlacement.PrepareScene(mode, CanvasPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            // 子も DontSave で作る(プレビューを置いたままシーンを保存すると PreviewButton だけ親無しで保存されてしまう)。
            var canvasGo = EditorPreviewRoots.CreateOverlayCanvas(PreviewCanvasName);
            var buttonGo = EditorPreviewRoots.CreateChild(canvasGo.transform, "PreviewButton", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UiButton));
            ((RectTransform)buttonGo.transform).sizeDelta = new Vector2(200f, 60f);

            var button = buttonGo.GetComponent<UiButton>();
            button.TargetGraphic = buttonGo.GetComponent<UnityEngine.UI.Image>();
            button.SetVisual(Target);
            _previewButton = button;
            EditorPreviewRoots.MarkDontSaveRecursive(canvasGo);

            if (PreviewPlacement.IsPersistent(mode))
            {
                // Canvas ごと本配置する(ボタン単体を外すと描画できないため)。以後この窓の所有物ではない。
                PreviewPlacement.Persist(canvasGo, Target.DisplayName ?? Target.name);
                _previewButton = null;
                return;
            }

            Selection.activeGameObject = buttonGo;
            PreviewPlacement.Focus(buttonGo);
        }

        private void RemoveFromScene()
        {
            // 遷移の自動再生・演出・SE も止める(止めないと次の段がプレビューを置き直してしまう)。
            _check?.StopAll();
            EditorPreviewRoots.DestroyAll(PreviewCanvasName);
            _previewButton = null;
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

        internal ControlSkinPreviewSection Check => _check;

        internal UiButton PreviewButton => _previewButton;

        internal void PlaceForTest() => PlaceInScene(PreviewPlaceMode.CurrentScene);
    }
}
