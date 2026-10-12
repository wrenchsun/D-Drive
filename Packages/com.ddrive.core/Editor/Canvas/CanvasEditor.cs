using System;
using System.Collections.Generic;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ui;
using DDrive.Editor.Ux;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.CanvasTool
{
    // Canvas のエディター(docs/1010 UX-4。旧 CanvasEditorWindow は Archive メニューへ退避)。
    // 共通基盤(UxEditorWindow<CanvasData> / FieldGuide / UxRow / UxCard)で組み、ここに書くのは Canvas 固有の部分だけ:
    // 欄の表(CanvasFieldGuide)・要素の動き(ElementFx)・ボタンの配線・埋め込み Canvas・Navigation グラフ・確認用シーンでの再生(実 UiManager)。
    // 既定はすべて折りたたみ(v1.4.1 の決定を維持): 一覧の欄は Foldout で、開いたときだけ中身を見る。要素が多くても検索(要素名にも一致)と
    // 「未調整の折りたたみ」で目的の行に着ける。
    // プレビューは旧エディターと同じく、実 UiManager + 実 UiTweenManager で OpenData した実体を、開いているシーン / プレハブステージの
    // DontSave ルートに置いて SceneView / Game View で確認する(ADR-4。ウィンドウ内には UI を描画しない)。
    // 部品ごとにファイルを分けている: .Preview.cs = 確認用シーン・再生・Idle・選択 / .ElementFx.cs / .Wires.cs / .Embedded.cs / .Navigation.cs。
    [DDrive.Editor.Inspector.DataEditor(typeof(DDrive.Runtime.Ui.CanvasData), "Canvas Editor で開く")]
    internal sealed partial class CanvasEditor : UxEditorWindow<CanvasData>
    {
        private const string Page = "canvas-editor";
        private const string RootElementLabel = UiTweenEditorWindow.RootElementLabel;
        private const string PreviewRootName = "[D-Drive] Canvas Preview (UX)";
        private const string FollowSelectionPrefKey = "DDrive.CanvasEditor.FollowSelection";
        private const string SelectOnPlayPrefKey = "DDrive.CanvasEditor.SelectOnPlay";

        // 2026-10-03(Canvas の埋め込み): Target が別の Canvas(親)に埋め込まれた子として編集されているときの親の連なり
        // (外側 → 内側。各 Link.RootPath = その Data の Prefab ルートから、次の(内側の)Canvas のルートまでのパス)。空 = 単独で編集中。
        private List<CanvasEmbeddedEditing.Link> _ancestors = new();
        private CanvasEmbeddedEditing.CanvasLookup _lookup;
        private bool _keepAncestors;
        private bool _keepPreviewOnSwitch;
        private bool _followSelection = true;
        private string _highlightPath;
        private int _selfSelectedId; // このウィンドウが選択した GameObject(選択に追従で自分の選択に反応しないため)

        // 一覧の欄(Foldout)の開閉。既定はすべて閉じる。対象を切り替えたら閉じ直す。
        private readonly Dictionary<string, bool> _foldOpen = new();
        private readonly List<CanvasSectionView> _sections = new();

        private VisualElement _contextRow;
        private Button _backButton;
        private Label _contextLabel;
        private Toggle _followToggle;
        private Label _followLockHint;
        private Label _statusLabel;
        private string _statusText = string.Empty;

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "Canvas Editor")]
        public static void OpenFromMenu() => Open(Selection.activeObject as CanvasData);

        public static void Open(CanvasData target)
        {
            var w = OpenWindow<CanvasEditor>("Canvas Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "Canvas";
        protected override FieldGuide<CanvasData> Guide => CanvasFieldGuide.Instance;
        protected override string ManualPage => Page;

        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case CanvasFieldGuide.PurShow: return "show";
                case CanvasFieldGuide.PurFx: return "fx";
                case CanvasFieldGuide.PurWire: return "wire";
                case CanvasFieldGuide.PurEmbed: return "embed";
                case CanvasFieldGuide.PurNav: return "nav";
                case CanvasFieldGuide.PurLink: return "link";
                case FieldGuide<CanvasData>.AdminPurpose: return "admin";
                default: return null;
            }
        }

        protected override string SearchHint => "ラベル・説明・検索語、要素・ボタンのパスに一致する設定だけ出す(例: Start / ボタン / 入れ子)";
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("Canvas Icon").image;

        protected override string[] ModeTips => new[]
        {
            "必須の欄だけ。Prefab を入れて確認用シーンで開くところまで",
            "よく使う欄まで。レイヤー・開閉の演出・要素の動き・ボタンの配線・カーソル移動",
            "すべての欄(埋め込み・スライダー・重ね順など)",
        };

        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("ElementFx[", "ElementEffects"),
            new KeyValuePair<string, string>("ButtonWire[", "Buttons"),
            new KeyValuePair<string, string>("SliderWire[", "Sliders"),
            new KeyValuePair<string, string>("Selectable '", "Navigation"),
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => MessageTokens;

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case CanvasFieldGuide.PurShow: return "どの画面を、どのレイヤーに、どんな演出で出すか";
                case CanvasFieldGuide.PurFx: return "画面の中の要素ごとの、出る・出ている間・消える動き";
                case CanvasFieldGuide.PurWire: return "押したときに何が起きるか(開く・閉じる・シグナル)";
                case CanvasFieldGuide.PurEmbed: return "入れ子になっている子の Canvas の登録と、有効 / 無効";
                case CanvasFieldGuide.PurNav: return "十字キーで動かしたときのカーソルの移動先";
                case CanvasFieldGuide.PurLink: return "節目で別の音や演出を一緒に鳴らす";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // ── 再生(= 確認用の表示。実 UiManager で OpenData) ──

        protected override bool SupportsPlayback => true;
        protected override bool SupportsSpeed => false;
        protected override bool SupportsRepeat => false;

        protected override bool IsPlaying => _manager != null && _manager.IsOpen(_previewHandle);

        protected override void StartPlayback(bool focus) => PlacePreview();

        protected override void StopPlayback()
        {
            if (!_keepPreviewOnSwitch)
            {
                RemovePreview();
            }
        }

        protected override void RestartPlayback()
        {
            RemovePreview();
            PlacePreview();
        }

        // ── ヒーロー ──

        protected override void AddLeadingHeroActions(VisualElement actions)
        {
            var scene = PreviewPlacementButton.Create(
                "確認用シーン",
                "EventSystem だけを置いた空の専用シーン(CanvasPreviewScene)に切り替え、そのまま実 UiManager で OpenData する(Selectable / 要素の自動収集も併せて実行する)。既に確認用シーンを開いているときはシーンを開き直さず、表示だけやり直す(Appear / Idle をやり直す)",
                OpenPreviewSceneAndPlace);
            scene.AddToClassList("ux-btn");
            actions.Add(scene);
            actions.Add(MakeButton("Prefab で開く", null, OpenPrefab, "CanvasData.Prefab をプレハブモードで開く。要素を選択して Unity 標準の Move / Rotate / Rect ツールで移動・回転・リサイズできる(Ctrl+Z で戻せる)"));
        }

        protected override void AfterHero(VisualElement page)
        {
            // 親の中から子へ切り替えたときだけ出る「← 親へ戻る」。
            _contextRow = new VisualElement();
            _contextRow.AddToClassList("ux-ctxrow");
            _backButton = new Button(BackToParent) { text = "← 親へ戻る" };
            _backButton.AddToClassList("ux-btn");
            _backButton.AddToClassList("ux-btn--small");
            _contextRow.Add(_backButton);
            _contextLabel = new Label();
            _contextLabel.AddToClassList("ux-dim");
            _contextLabel.AddToClassList("ux-note");
            _contextRow.Add(_contextLabel);
            page.Add(_contextRow);

            _followSelection = EditorPrefs.GetBool(FollowSelectionPrefKey, true);
            _followToggle = new Toggle("選択に追従")
            {
                value = _followSelection,
                tooltip = "Hierarchy / プレハブステージ / 確認用プレビューで選んだ GameObject に合わせて編集対象を切り替える(埋め込み Canvas の配下なら子の CanvasData、それ以外は親)。入力中のフィールドがあるあいだは切り替えない。設定はエディタに保存される",
            };
            _followToggle.AddToClassList("ux-bar__repeat");
            _followToggle.RegisterValueChangedCallback(e =>
            {
                _followSelection = e.newValue;
                EditorPrefs.SetBool(FollowSelectionPrefKey, e.newValue);
            });
            // 🔒 が ON の間は「選択に追従」が効かない(動作は `_followSelection && !Locked` のまま)。関係が分かるよう灰色にして理由を出す。
            _followLockHint = new Label("🔒 がオンの間は追従しません");
            _followLockHint.AddToClassList("ux-dim");
            var followRow = new VisualElement();
            followRow.AddToClassList("ux-followrow");
            followRow.Add(_followToggle);
            followRow.Add(_followLockHint);
            page.Add(followRow);
            UpdateFollowLockUi();

            _statusLabel = new Label();
            _statusLabel.AddToClassList("ux-dim");
            _statusLabel.AddToClassList("ux-note");
            _statusLabel.AddToClassList("ux-canvas-status");
            page.Add(_statusLabel);
            UpdateContextRow();
            ShowStatus();
        }

        private void SetStatus(string text)
        {
            _statusText = text ?? string.Empty;
            ShowStatus();
        }

        private void ShowStatus()
        {
            if (_statusLabel == null)
            {
                return;
            }

            var text = _statusText;
            if (string.IsNullOrEmpty(text))
            {
                text = Target == null
                    ? string.Empty
                    : _manager != null && _manager.IsOpen(_previewHandle) ? "プレビュー表示中" : "「確認用シーン」で確認できます";
            }

            if (_statusLabel.text != text)
            {
                _statusLabel.text = text;
            }
        }

        private void UpdateFollowLockUi()
        {
            _followToggle?.SetEnabled(!Locked);
            _followLockHint?.Show(Locked);
        }

        private void UpdateContextRow()
        {
            if (_contextRow == null)
            {
                return;
            }

            if (_ancestors.Count == 0 || Target == null)
            {
                _contextRow.Show(false);
                return;
            }

            _contextRow.Show(true);
            var parent = _ancestors[_ancestors.Count - 1].Data;
            _backButton.text = $"← {NameOf(parent)} へ戻る";
            var names = new System.Text.StringBuilder();
            for (var i = 0; i < _ancestors.Count; i++)
            {
                names.Append(NameOf(_ancestors[i].Data)).Append(" > ");
            }

            names.Append(NameOf(Target));
            _contextLabel.text = $"埋め込みとして編集中: {names}";
        }

        private static string NameOf(CanvasData data)
            => data == null ? "(なし)" : string.IsNullOrEmpty(data.DisplayName) ? data.name : data.DisplayName;

        // ── 対象の切り替え ──

        // 親の連なり(ancestors。外側 → 内側)付きで子 Canvas を編集対象にする(「この Canvas を編集」・選択に追従・戻る)。
        // 親から子へは Data を手放さない(親の未保存の変更はそのまま残る)ので、未保存の確認は出さず、確認用プレビューも残す。
        private void SetTargetIn(CanvasData target, List<CanvasEmbeddedEditing.Link> ancestors)
        {
            _keepAncestors = true;
            _keepPreviewOnSwitch = true;
            try
            {
                _ancestors = ancestors != null ? new List<CanvasEmbeddedEditing.Link>(ancestors) : new List<CanvasEmbeddedEditing.Link>();
                _highlightPath = null;
                SetTarget(target, false);
            }
            finally
            {
                _keepAncestors = false;
                _keepPreviewOnSwitch = false;
            }
        }

        private void BackToParent()
        {
            if (_ancestors.Count == 0)
            {
                return;
            }

            var last = _ancestors[_ancestors.Count - 1];
            SetTargetIn(last.Data, _ancestors.GetRange(0, _ancestors.Count - 1));
        }

        protected override void OnTargetChanged()
        {
            if (!_keepAncestors)
            {
                _ancestors.Clear(); // 別の Canvas を選び直したら親の文脈は捨てる
            }

            // 入力途中(遅延確定)の欄の値を、切り替え前の対象に確定させる(レビュー PC-R-05)。
            FlushPendingInput();
            StopIdleFlow(); // 対象を切り替えたら流していた Idle を戻す(必要なら次の更新で新しい対象の分を流し直す)
            ReleaseStageStates();
            _phasePreviewHandles.Clear();
            _foldOpen.Clear();
            _fxOpen.Clear();
            _wireOpen.Clear();
            _embedOpen.Clear();
            _lookup = null;
            _simFocusPath = Target != null ? Target.FirstSelected : null;
            _statusText = string.Empty;
        }

        private void FlushPendingInput()
        {
            if (rootVisualElement?.panel?.focusController?.focusedElement is VisualElement focused)
            {
                focused.Blur();
            }
        }

        private CanvasEmbeddedEditing.CanvasLookup Lookup => _lookup ??= CanvasEmbeddedEditing.CanvasLookup.Build();

        // 編集対象を含む「Prefab を表示している側」の CanvasData(親の連なりの最外側、無ければ Target)。
        private CanvasData ViewData => _ancestors.Count > 0 ? _ancestors[0].Data : Target;

        // プロジェクトの変更(CanvasData の追加・削除・Prefab の差し替え)があったら、子 CanvasData の引き当て表を作り直す。
        private void OnProjectChange()
        {
            _lookup = null;
            if (Target != null)
            {
                SyncSections(true);
            }
        }

        // ── ライフサイクル ──

        protected override void OnWindowEnable()
        {
            _registry = EditorAnchorRegistry.Build();
            _pool = new PoolService();
            _tweenManager = new UiTweenManager(_registry);
            _manager = new UiManager(_pool, _registry, tweens: _tweenManager);
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            _lastEditorTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;
            PrefabStage.prefabSaving += OnPrefabSaving;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Selection.selectionChanged += OnSceneSelectionChanged;
            EditorApplication.projectChanged += OnProjectChange;
            _idleFlow = new CanvasIdleFlow(_tweenManager, FindUiTweenData);

            // 前回閉じ損ねた・ドメインリロードで参照を失ったプレビュールートの残骸を消す。
            EditorPreviewRoots.DestroyAll(PreviewRootName);
        }

        protected override void OnWindowDisable()
        {
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
            PrefabStage.prefabSaving -= OnPrefabSaving;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= OnEditorUpdate;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            Selection.selectionChanged -= OnSceneSelectionChanged;
            EditorApplication.projectChanged -= OnProjectChange;
            StopIdleFlow(); // 流していた Idle の値をプレハブに残さない(ドメインリロード・ウィンドウを閉じる前にも通る)
            _idleFlow = null;
            ReleaseStageStates(); // プレハブモードで再生した値をプレハブに残さない
            RemovePreview();
            // 閉じても "[D-Drive] UI Root"(レイヤー 5 枚 + プールに戻った Canvas 実体)が次にシーンを閉じるまで残らないよう、
            // RemovePreview(StopAll 済み)の後に UiManager 自身のルートを破棄する。
            _manager?.DestroyEditorRoot();
            _manager = null;
            _tweenManager = null;
            _pool = null;
            _registry = null;
        }

        // ADR-4: 確認用シーンのプレビューは実 UiManager + UiTweenManager を EditorApplication.update から駆動する
        // (ウィンドウ内には何も描画しない)。ElementFx の Appear / Idle / Disappear は UiTweenManager.Tick が進めないと動かない。
        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - _lastEditorTime);
            _lastEditorTime = now;
            if (_manager == null || dt <= 0f || dt > 1f)
            {
                return;
            }

            _tweenManager?.Tick(dt);
            _manager.Tick(dt);
            RefreshPhaseRowStatuses();
            ReleaseStageStatesIfIdle();
            UpdateIdleFlow(now);
        }

        protected override void OnBuildBodyStart()
        {
            _sections.Clear();
            _contextRow = null;
            _backButton = null;
            _contextLabel = null;
            _followToggle = null;
            _followLockHint = null;
            _statusLabel = null;
            _phaseRowWidgets.Clear();
            _fxLineByPath.Clear();
            _fxList = null;
            _fxSection = null;
            _wireList = null;
            _wireSection = null;
            _embedList = null;
            _embedSection = null;
            _navSection = null;
            _fxLineFlags.Clear();
            _wireLineFlags.Clear();
            _embedLineFlags.Clear();
            _idleFlowToggle = null;
            _idleFlowHint = null;
            _graphView = null;
            _unreachableContainer = null;
            _padRow = null;
            _focusLabel = null;
        }

        // 既定はすべて折りたたみ(v1.4.1 の決定。docs/1010 UX-4): 構造体・配列の欄(PropertyField)が作る Foldout も、組み終えたあとに 1 度だけ閉じる。
        // PropertyField は画面に載ってから中身を作るので、少し待って閉じる(開き直した Foldout は触らない)。
        protected override void OnBodyBuilt(VisualElement page)
        {
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(30);
            page.schedule.Execute(() => CollapseFoldouts(page)).StartingIn(300);
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

        protected override void OnTick()
        {
            UpdateFollowLockUi();
            ShowStatus();
        }

        protected override void OnRowsRefreshed() => ShowStatus();

        private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene previous, UnityEngine.SceneManagement.Scene current)
        {
            // 参照を捨てるだけだと UiManager の _stack / _instances に実体を失った CanvasInstance が残り、Idle 等の Tween も走り続ける。
            // StopAll で Manager 側の状態を片付け、UI Root も破棄して次の OpenData で新しいシーンに作り直させる。
            RemovePreview();
            _manager?.DestroyEditorRoot();
        }

        // 一覧の欄を、データの現在の内容に合わせて作り直す(署名が変わったものだけ。force = 全部)。
        private void SyncSections(bool force)
        {
            for (var i = 0; i < _sections.Count; i++)
            {
                if (force)
                {
                    _sections[i].Rebuild();
                }
                else
                {
                    _sections[i].Sync();
                }
            }
        }

        // 外から(自動収集・「修正」・埋め込みの編集など)Data を書いたあと: 検証・行の状態・一覧をすべて最新にする。
        private void AfterDataWrite()
        {
            if (Target == null)
            {
                return;
            }

            RefreshAfterExternalWrite();
            SyncSections(false);
        }

        // ── 欄 ──

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            switch (e.Field)
            {
                case nameof(CanvasData.ElementEffects):
                    return CreateFxRows(e, card);
                case nameof(CanvasData.Buttons):
                    return CreateWireRows(e, card);
                case nameof(CanvasData.EmbeddedCanvases):
                    return CreateEmbeddedRows(e, card);
                case nameof(CanvasData.Navigation):
                    return CreateNavRows(e, card);
                default:
                    return base.CreateRow(e, card, isAdmin);
            }
        }

        // 一覧の欄の「そのもの」(要素の追加・削除・並べ替え・全フィールドの編集)。詳細モードでだけ出る重複欄。
        private UxRow RawListRow(FieldGuideEntry e, string label) =>
            new UxRow(So, DefaultSo, new[] { e }, label, "リストそのものを直接編集します(要素の追加・削除・並べ替え、すべてのフィールド)。", FieldTier.Advanced, true,
                GuidedField.MakeInput(So, e.Field), RowChanged) { IsDuplicate = true, NoBulkReset = true };

        private UxVisualRow SectionRow(FieldGuideEntry e, CanvasSectionView section, Func<string, bool> extraMatch)
        {
            _sections.Add(section);
            return new UxVisualRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, section, RowChanged)
            {
                NoFold = true,
                NoBulkReset = true,
                MaxIssues = 3,
                ExtraMatch = extraMatch,
            };
        }

        // ── 検証の行ごとの印 ──

        protected override void OnValidated(IReadOnlyList<ValidationResult> results)
        {
            _fxIssue.Clear();
            _wireIssue.Clear();
            _embedIssue.Clear();
            for (var i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (r.Severity == ValidationSeverity.Info || string.IsNullOrEmpty(r.Message))
                {
                    continue;
                }

                if (TryIndexAfter(r.Message, "ElementFx[", out var fi))
                {
                    _fxIssue[fi] = r.Message;
                }
                else if (TryIndexAfter(r.Message, "ButtonWire[", out var wi))
                {
                    _wireIssue[wi] = r.Message;
                }
                else if (TryIndexAfter(r.Message, "EmbeddedCanvases[", out var ei))
                {
                    _embedIssue[ei] = r.Message;
                }
            }

            ApplyLineFlags();
        }

        private readonly Dictionary<int, string> _fxIssue = new();
        private readonly Dictionary<int, string> _wireIssue = new();
        private readonly Dictionary<int, string> _embedIssue = new();
        private readonly Dictionary<int, Label> _fxLineFlags = new();
        private readonly Dictionary<int, Label> _wireLineFlags = new();
        private readonly Dictionary<int, Label> _embedLineFlags = new();

        private static bool TryIndexAfter(string message, string prefix, out int index)
        {
            index = -1;
            var start = message.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return false;
            }

            start += prefix.Length;
            var end = message.IndexOf(']', start);
            return end > start && int.TryParse(message.Substring(start, end - start), out index);
        }

        private static void ApplyFlags(Dictionary<int, Label> flags, Dictionary<int, string> issues)
        {
            foreach (var kv in flags)
            {
                var has = issues.TryGetValue(kv.Key, out var msg);
                kv.Value.Show(has);
                kv.Value.tooltip = has ? msg : string.Empty;
            }
        }

        private void ApplyLineFlags()
        {
            ApplyFlags(_fxLineFlags, _fxIssue);
            ApplyFlags(_wireLineFlags, _wireIssue);
            ApplyFlags(_embedLineFlags, _embedIssue);
        }

        private static Label NewFlag(Dictionary<int, Label> flags, int index, Dictionary<int, string> issues)
        {
            var flag = new Label("⚠");
            flag.AddToClassList("ux-lineflag");
            var has = issues.TryGetValue(index, out var msg);
            flag.Show(has);
            flag.tooltip = has ? msg : string.Empty;
            flags[index] = flag;
            return flag;
        }

        // ── 検索(要素・ボタン・埋め込みのパスにも一致) ──

        protected override void OnFiltersApplied(string query)
        {
            ApplyFxFilter(query);
            ApplyWireFilter(query);
            ApplyEmbedFilter(query);
        }

        private static bool ContainsIgnoreCase(string text, string query) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        // ── Validation(テスト・他から使う。旧エディターと同じ実行部) ──

        public static List<ValidationResult> CollectValidation(CanvasData target)
            => DDrive.Editor.Validation.DataValidationRunner.Run(target);
    }
}
