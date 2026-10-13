using System;
using System.Collections.Generic;
using System.Text;
using DDrive.Editor.Codegen;
using DDrive.Editor.Menu;
using DDrive.Editor.Spec;
using DDrive.Editor.Ux;
using DDrive.Editor.Versioning;
using DDrive.Runtime.Loop;
using DDrive.Runtime.Tuning;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
// このファイル自身の名前空間 DDrive.Editor.Tuning と DDrive.Runtime.Tuning.Tuning(静的ファサード)が同名衝突するため、別名で参照する。
using RuntimeTuning = DDrive.Runtime.Tuning.Tuning;

namespace DDrive.Editor.Tuning
{
    // Tuning(調整値)のエディター(docs/1010 UX-15。旧 TuningEditorWindow〔IMGUI〕は Archive メニューへ退避)。
    // 対象の TuningTable は AssetDataBase 派生ではない(プロジェクト単位の設定)ので UxEditorWindow<TData> には載せられず、
    // SliderEditor / UiPresetGallery と同じく共通テーマ(UxTheme)と部品(UxCard / TuneKnobs / DDriveUx.uss)を当てた独立ウィンドウにしている。
    // 再生ボタンは無い(Tuning に「再生」は無い)。保存・自動保存・未保存の確認・検索・Play 中の再読込は旧と同じ意味で持つ。
    //   値 タブ   : 1 キー = 1 行の調整つまみ(TuneKnobs)。カテゴリ(キーの "<機能>/" 接頭辞)のチップで絞り込み、検索はキー名・説明・カテゴリ・単位
    //   Tables タブ: TuningTable.Tables(複数)を 1 テーブル 1 カード。セルの値だけ編集(列・行の追加削除は仕様書が正本)
    //   検証      : TuningCheck(範囲外・重複・選択肢に無い値など)を 1 件 1 行
    // 値の変更は Undo.RecordObject + SetDirty(1.2 秒以内の連続操作は 1 回の Undo にまとめる)。保存は DDriveAssetSave.SaveDirty(docs/1010 D-5)。
    // Edit Mode ではファサード Tuning.Get* を呼ばず TuningTable を直接読み書きする(docs/09)。Play 中は索引を作り直して読み出し側へ即時に反映し、
    // 起動時に値を読んで保持している機能への通知(Tuning.Rebind。M-2c)は「Play 中に再読込」で行う。
    internal sealed partial class TuningEditor : EditorWindow
    {
        private const string ManualPage = "spec-sync";
        private const string AutoSaveKey = "DDrive.Ux.Tuning.AutoSave";
        private const int IssueLimit = 40;

        private static bool SuppressDialogs => UxTestHooks.SuppressDialogs || Application.isBatchMode;

        [SerializeField] private TuningTable _target;
        [SerializeField] private int _tab;
        [SerializeField] private string _query = string.Empty;
        [SerializeField] private bool _lock;

        private VisualElement _root;
        private ScrollView _body;
        private VisualElement _footer;
        private VisualElement _notice;
        private Label _noticeText;
        private Button _btnReload;
        private SerializedObject _so;
        private TuneKnobs _knobs;
        private TuningTuneSource _source;
        private VisualElement _valuesHost;
        private VisualElement _tablesHost;
        private VisualElement _issueBody;
        private UxCard _issueCard;
        private Label _title;
        private Label _idLabel;
        private Label _chipKeys;
        private Label _chipCats;
        private Label _chipTables;
        private Label _chipMod;
        private Label _chipCheck;
        private Label _hit;
        private Label _barMod;
        private Button _btnSave;
        private Button _btnLock;
        private Button _btnTabValues;
        private Button _btnTabTables;
        private Toggle _autoToggle;
        private List<TuningCheck.Issue> _issues = new List<TuningCheck.Issue>();
        private string _issueSig = string.Empty;
        private readonly HashSet<string> _pending = new HashSet<string>(StringComparer.Ordinal);
        private int _tick;
        private int _autoUndoGroup = -1;
        private bool _autoWasDirty;
        private double _autoEditTime;

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "Tuning（調整値）")]
        public static void OpenFromMenu() => Open(Selection.activeObject as TuningTable);

        public static void Open(TuningTable table = null)
        {
            var w = GetWindow<TuningEditor>("Tuning（調整値）");
            w.minSize = new Vector2(500, 380); // docs/09 §7.1: 横幅 500px を minSize で回避しない
            if (table != null)
            {
                w.SetTarget(table);
            }
        }

        // 旧と同じ: 既定の対象 = 仕様書設定の TuningTable、無ければシーンの DDriveRuntimeBootstrap が持つもの。
        internal static TuningTable ResolveDefaultTable()
        {
            var fromSettings = AssetDatabase.LoadAssetAtPath<TuningTable>(DDriveSpecSettings.DefaultTuningTablePath);
            if (fromSettings != null)
            {
                return fromSettings;
            }

            var bootstrap = UnityEngine.Object.FindFirstObjectByType<DDriveRuntimeBootstrap>();
            return bootstrap != null ? bootstrap.TuningTable : null;
        }

        // ── ライフサイクル ──

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            _so?.Dispose();
            _so = null;
        }

        // ウィンドウを閉じるとき: 未保存なら聞く(自動保存 ON なら聞かずに保存)。ドメインリロードでは呼ばれない。
        private void OnDestroy() => ConfirmSaveBeforeLeave(_target, "ウィンドウを閉じます。");

        private void OnSelectionChange()
        {
            if (!_lock && Selection.activeObject is TuningTable sel && sel != _target)
            {
                SetTarget(sel);
            }
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode)
            {
                _pending.Clear();
            }
        }

        private void OnUndoRedo()
        {
            _autoEditTime = EditorApplication.timeSinceStartup;
            if (_target == null || _so == null)
            {
                return;
            }

            _so.Update();
            if (Application.isPlaying)
            {
                _target.RebuildIndex();
            }

            _knobs?.Sync();
            RebuildTables();
            RefreshAll();
        }

        private bool AutoSave
        {
            get => EditorPrefs.GetBool(AutoSaveKey, false);
            set => EditorPrefs.SetBool(AutoSaveKey, value);
        }

        // ── 画面の組み立て ──

        private void CreateGUI()
        {
            _root = rootVisualElement;
            UxTheme.Apply(_root);

            _body = new ScrollView(ScrollViewMode.Vertical);
            _body.AddToClassList("ux-scroll");
            _root.Add(_body);
            _notice = BuildNotice();
            _root.Add(_notice);
            _footer = BuildFooter();
            _root.Add(_footer);
            _root.schedule.Execute(Tick).Every(150);

            if (_target == null)
            {
                _target = Selection.activeObject as TuningTable ?? ResolveDefaultTable();
            }

            EnsureSo();
            RebuildBody();
        }

        private void EnsureSo()
        {
            if (_target == null)
            {
                _so?.Dispose();
                _so = null;
                return;
            }

            if (_so == null || _so.targetObject != _target)
            {
                _so?.Dispose();
                _so = new SerializedObject(_target);
            }
            else
            {
                _so.Update();
            }
        }

        public void SetTarget(TuningTable table, bool confirmSave = true)
        {
            if (confirmSave && _target != null && table != _target)
            {
                ConfirmSaveBeforeLeave(_target, "別の TuningTable に切り替えます。");
            }

            _target = table;
            _pending.Clear();
            EnsureSo();
            RebuildBody();
        }

        private void RebuildBody()
        {
            if (_body == null)
            {
                return;
            }

            _body.Clear();
            _knobs = null;
            _source = null;
            _issueBody = null;
            _issueCard = null;
            _valuesHost = _tablesHost = null;
            var page = new VisualElement();
            page.AddToClassList("ux-page");
            _body.Add(page);

            if (_target == null || _so == null)
            {
                _footer?.Show(false);
                _notice?.Show(false);
                page.Add(BuildEmpty());
                return;
            }

            _footer?.Show(true);
            _source = new TuningTuneSource(_target) { LiveEdited = OnLiveEdited };
            _knobs = new TuneKnobs(_source, _so, OnKnobEdited, OnKnobEdited);
            _knobs.SetQuery(Query);
            BuildCellBaseline();

            page.Add(BuildHero());

            _valuesHost = new VisualElement();
            _valuesHost.AddToClassList("ux-tt-values");
            var card = new UxCard("調整値", "1 キー = 1 行。カテゴリのチップで絞り込み、↺ で開いたときの値に戻せます。⚙ で説明・単位・範囲を編集", UxCard.Help(ManualPage, "tuning-values"));
            card.Body.Add(_knobs);
            _valuesHost.Add(card);
            page.Add(_valuesHost);

            _tablesHost = new VisualElement();
            page.Add(_tablesHost);
            RebuildTables();

            _issueCard = new UxCard("検証", "いま開いている TuningTable の中の矛盾を見ます(仕様書との突き合わせは Validation > Run All)", UxCard.Help(ManualPage, "tuning-check"));
            _issueBody = new VisualElement();
            _issueBody.AddToClassList("ux-issues");
            _issueCard.Body.Add(_issueBody);
            page.Add(_issueCard);

            _issueSig = string.Empty;
            ApplyTab();
            RunChecks();
            RefreshAll();
        }

        private string Query => (_query ?? string.Empty).Trim();

        // ── ヒーロー ──

        private VisualElement BuildHero()
        {
            var hero = new VisualElement();
            hero.AddToClassList("ux-hero");

            var top = new VisualElement();
            top.AddToClassList("ux-hero__top");

            var iconBox = new VisualElement { tooltip = "Project ウィンドウで表示" };
            iconBox.AddToClassList("ux-hero__icon");
            var img = new Image { image = EditorGUIUtility.IconContent("ScriptableObject Icon").image, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.AddToClassList("ux-hero__img");
            iconBox.Add(img);
            iconBox.RegisterCallback<ClickEvent>(e => PingTarget());
            top.Add(iconBox);

            var info = new VisualElement();
            info.AddToClassList("ux-hero__info");
            var picker = new ObjectField { objectType = typeof(TuningTable), allowSceneObjects = false, value = _target, tooltip = "編集する TuningTable を切り替える" };
            picker.AddToClassList("ux-tt-pick");
            picker.RegisterValueChangedCallback(e => SetTarget(e.newValue as TuningTable));
            info.Add(picker);
            _title = new Label();
            _title.AddToClassList("ux-hero__title");
            info.Add(_title);
            _idLabel = new Label();
            _idLabel.AddToClassList("ux-dim");
            _idLabel.AddToClassList("ux-hero__id");
            info.Add(_idLabel);

            var chips = new VisualElement();
            chips.AddToClassList("ux-hero__chips");
            _chipKeys = MakeChip(chips, null);
            _chipCats = MakeChip(chips, null);
            _chipTables = MakeChip(chips, null);
            _chipCheck = MakeChip(chips, "ux-chip--ok");
            _chipMod = MakeChip(chips, "ux-chip--mod");
            info.Add(chips);
            top.Add(info);
            top.Add(BuildSearch());
            hero.Add(top);

            var actions = new VisualElement();
            actions.AddToClassList("ux-hero__actions");
            actions.Add(MakeButton("Project", null, PingTarget, "この TuningTable を Project ウィンドウで表示する"));
            actions.Add(MakeButton("キー定数を再生成", null, () => RegenerateKeys(), "TUNING のキー定数(Tuning.g.cs)を作り直す(Generate > Regenerate Tuning Keys と同じ)"));
            actions.Add(MakeButton("仕様書と同期", null, SpecSyncWindow.Open, "仕様書と同期ウィンドウを開く(キー・値・Tables の取り込み)"));
            var spacer = new VisualElement();
            spacer.AddToClassList("ux-hero__spacer");
            actions.Add(spacer);
            _btnLock = MakeButton("🔒 固定", null, ToggleLock, "ON: Project ウィンドウの選択に追従しない");
            _btnLock.EnableInClassList("ux-btn--on", _lock);
            actions.Add(_btnLock);
            hero.Add(actions);

            var modes = new VisualElement();
            modes.AddToClassList("ux-hero__modes");
            var seg = new VisualElement();
            seg.AddToClassList("ux-seg");
            _btnTabValues = new Button(() => SetTab(0)) { tooltip = "キーごとの調整値(カテゴリで絞り込み)" };
            _btnTabValues.AddToClassList("ux-seg__btn");
            seg.Add(_btnTabValues);
            _btnTabTables = new Button(() => SetTab(1)) { tooltip = "表形式の調整値(列 × 行)" };
            _btnTabTables.AddToClassList("ux-seg__btn");
            seg.Add(_btnTabTables);
            modes.Add(seg);
            hero.Add(modes);

            _hit = new Label();
            _hit.AddToClassList("ux-dim");
            _hit.AddToClassList("ux-hit");
            hero.Add(_hit);
            return hero;
        }

        private static Label MakeChip(VisualElement parent, string cls)
        {
            var l = new Label();
            l.AddToClassList("ux-chip");
            if (cls != null)
            {
                l.AddToClassList(cls);
            }

            parent.Add(l);
            return l;
        }

        private static Button MakeButton(string text, string cls, Action click, string tooltip)
        {
            var b = new Button(click) { text = text, tooltip = tooltip };
            b.AddToClassList("ux-btn");
            if (cls != null)
            {
                b.AddToClassList(cls);
            }

            return b;
        }

        private VisualElement BuildSearch()
        {
            var box = new VisualElement { tooltip = "キー名・説明・カテゴリ・単位、Tables のキー・列・行 ID に一致するものだけ出す" };
            box.AddToClassList("ux-searchbox");
            var icon = new Image { image = EditorGUIUtility.IconContent("Search Icon").image, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            icon.AddToClassList("ux-searchbox__icon");
            box.Add(icon);
            var field = new TextField { value = _query };
            field.AddToClassList("ux-searchbox__field");
            field.textEdition.placeholder = "調整値を検索";
            field.textEdition.hidePlaceholderOnFocus = true;
            box.Add(field);
            field.RegisterCallback<FocusInEvent>(e => box.AddToClassList("ux-searchbox--focus"));
            field.RegisterCallback<FocusOutEvent>(e => box.RemoveFromClassList("ux-searchbox--focus"));
            var clear = new Button(() => field.value = string.Empty) { text = "×", tooltip = "検索を消す" };
            clear.AddToClassList("ux-searchbox__clear");
            clear.Show(!string.IsNullOrEmpty(_query));
            box.Add(clear);
            field.RegisterValueChangedCallback(e =>
            {
                _query = e.newValue ?? string.Empty;
                clear.Show(!string.IsNullOrEmpty(_query));
                _knobs?.SetQuery(Query);
                RebuildTables();
                RefreshAll();
            });
            return box;
        }

        private void SetTab(int tab)
        {
            _tab = tab;
            ApplyTab();
            RefreshAll();
        }

        private void ApplyTab()
        {
            _valuesHost?.Show(_tab == 0);
            _tablesHost?.Show(_tab == 1);
            _btnTabValues?.EnableInClassList("ux-seg__btn--on", _tab == 0);
            _btnTabTables?.EnableInClassList("ux-seg__btn--on", _tab == 1);
        }

        private void ToggleLock()
        {
            _lock = !_lock;
            _btnLock?.EnableInClassList("ux-btn--on", _lock);
        }

        private void PingTarget()
        {
            if (_target != null)
            {
                EditorGUIUtility.PingObject(_target);
            }
        }

        // 「キー定数を再生成」。outputPath が null なら既定の出力先(Assets/Generated/Tuning.g.cs 相当)。テストは一時パスを渡す。
        internal void RegenerateKeys(string outputPath = null)
        {
            if (_target == null)
            {
                return;
            }

            var result = outputPath == null ? TuningCodegen.Regenerate(_target) : TuningCodegen.Regenerate(_target, outputPath);
            var msg = $"Tuning のキー定数を再生成しました({result.TotalCount} キー)";
            Debug.Log($"[DDrive] Tuning keys regenerated: {result.TotalCount} entries.");
            ShowNotification(new GUIContent(msg), 2.0);
        }

        // ── 空状態 ──

        private VisualElement BuildEmpty()
        {
            var wrap = new VisualElement();
            wrap.AddToClassList("ux-empty");
            var zone = new VisualElement();
            zone.AddToClassList("ux-empty__zone");

            var icon = new VisualElement();
            icon.AddToClassList("ux-empty__icon");
            var img = new Image { image = EditorGUIUtility.IconContent("ScriptableObject Icon").image, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.AddToClassList("ux-empty__img");
            icon.Add(img);
            zone.Add(icon);

            var head = new Label("TuningTable を選んでください");
            head.AddToClassList("ux-empty__headline");
            zone.Add(head);
            var sub = new Label("Project ウィンドウで TuningTable を選ぶか、ここにドロップ");
            sub.AddToClassList("ux-dim");
            sub.AddToClassList("ux-empty__sub");
            zone.Add(sub);

            var picker = new ObjectField { objectType = typeof(TuningTable), allowSceneObjects = false };
            picker.AddToClassList("ux-empty__picker");
            picker.RegisterValueChangedCallback(e =>
            {
                if (e.newValue is TuningTable t)
                {
                    SetTarget(t);
                }
            });
            zone.Add(picker);

            var recent = new VisualElement();
            recent.AddToClassList("ux-empty__recent");
            var guids = AssetDatabase.FindAssets("t:" + nameof(TuningTable));
            var shown = 0;
            for (var i = 0; i < guids.Length && shown < 5; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var table = AssetDatabase.LoadAssetAtPath<TuningTable>(path);
                if (table == null)
                {
                    continue;
                }

                var t = table;
                var b = new Button(() => SetTarget(t)) { text = table.name, tooltip = path };
                b.AddToClassList("ux-btn");
                recent.Add(b);
                shown++;
            }

            if (shown > 0)
            {
                zone.Add(recent);
            }

            zone.RegisterCallback<DragEnterEvent>(e => zone.EnableInClassList("ux-empty__zone--hover", FindDragged() != null));
            zone.RegisterCallback<DragLeaveEvent>(e => zone.RemoveFromClassList("ux-empty__zone--hover"));
            zone.RegisterCallback<DragExitedEvent>(e => zone.RemoveFromClassList("ux-empty__zone--hover"));
            zone.RegisterCallback<DragUpdatedEvent>(e =>
            {
                DragAndDrop.visualMode = FindDragged() != null ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
                e.StopPropagation();
            });
            zone.RegisterCallback<DragPerformEvent>(e =>
            {
                var d = FindDragged();
                if (d == null)
                {
                    return;
                }

                DragAndDrop.AcceptDrag();
                zone.RemoveFromClassList("ux-empty__zone--hover");
                SetTarget(d);
                e.StopPropagation();
            });

            wrap.Add(zone);
            return wrap;
        }

        private static TuningTable FindDragged()
        {
            var objs = DragAndDrop.objectReferences;
            for (var i = 0; i < objs.Length; i++)
            {
                if (objs[i] is TuningTable t)
                {
                    return t;
                }
            }

            return null;
        }

        // ── Play 中の再読込の通知(M-2c) ──

        private VisualElement BuildNotice()
        {
            var bar = new VisualElement();
            bar.AddToClassList("ux-tt-notice");
            _noticeText = new Label();
            _noticeText.AddToClassList("ux-tt-notice__text");
            bar.Add(_noticeText);
            _btnReload = MakeButton("Play 中に再読込", "ux-btn--primary", OnReloadClicked, "Tuning.Rebind() を呼び、Tuning.Reloaded を購読している機能に新しい値を通知する(M-2c)");
            bar.Add(_btnReload);
            bar.Show(false);
            return bar;
        }

        private void OnReloadClicked()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[DDrive] Tuning: 「Play 中に再読込」は Play Mode 中のみ使えます。");
                return;
            }

            RuntimeTuning.Rebind();
            _pending.Clear();
            UpdateNotice();
        }

        // 再生中に値を書き換えた(索引の作り直しまで済んだ)。どの値かを覚えて「再読込が必要な変更」を数える。
        private void OnLiveEdited(string what)
        {
            NotePending(what);
            UpdateNotice();
        }

        private void NotePending(string what)
        {
            if (Application.isPlaying && !string.IsNullOrEmpty(what))
            {
                _pending.Add(what);
            }
        }

        private void UpdateNotice()
        {
            if (_notice == null)
            {
                return;
            }

            var playing = Application.isPlaying && _target != null;
            _notice.Show(playing);
            if (!playing)
            {
                return;
            }

            var bound = RuntimeTuning.IsBound;
            string text;
            if (!bound)
            {
                text = "Play 中ですが、Tuning が起動時に Bind されていません(再読込は使えません)。値は保存すると次回の Play から反映されます。";
            }
            else if (_pending.Count > 0)
            {
                text = $"Play 中に {_pending.Count} 件の値を変えました。読み出し側へはすぐ届いています。起動時に値を読んで保持している機能へは「Play 中に再読込」で通知してください。";
            }
            else
            {
                text = "Play 中です。値の変更は読み出し側へすぐ届きます。起動時に値を読んで保持している機能へは「Play 中に再読込」で通知できます。";
            }

            if (_noticeText.text != text)
            {
                _noticeText.text = text;
            }

            _notice.EnableInClassList("ux-tt-notice--attn", bound && _pending.Count > 0);
            _btnReload.SetEnabled(bound);
        }

        // ── 固定バー(保存) ──

        private VisualElement BuildFooter()
        {
            var bar = new VisualElement();
            bar.AddToClassList("ux-bar");
            bar.Show(false);
            var spacer = new VisualElement();
            spacer.AddToClassList("ux-bar__spacer");
            bar.Add(spacer);
            _barMod = new Label("変更なし");
            _barMod.AddToClassList("ux-chip");
            _barMod.AddToClassList("ux-bar__mod");
            bar.Add(_barMod);
            _autoToggle = new Toggle("自動保存") { value = AutoSave, tooltip = "ON: 編集が止まって 1 秒たつと自動で保存する(再生中でも)。保存のたびに版数が進む" };
            _autoToggle.AddToClassList("ux-bar__repeat");
            _autoToggle.RegisterValueChangedCallback(e => AutoSave = e.newValue);
            bar.Add(_autoToggle);
            _btnSave = MakeButton("保存", null, SaveTarget, "この TuningTable を保存する(版数が進む)。Ctrl+S(File > Save)でも保存されます");
            bar.Add(_btnSave);
            return bar;
        }

        // ── 保存 ──
        // デザイナーの編集の保存は SaveDirty(対象 1 個だけ。docs/1010 D-5)。

        internal void SaveTarget()
        {
            if (_target == null)
            {
                return;
            }

            EditorUtility.SetDirty(_target);
            DDriveAssetSave.SaveDirty(_target);
        }

        private void ConfirmSaveBeforeLeave(TuningTable table, string reason)
        {
            if (table == null || !AssetDatabase.Contains(table) || !EditorUtility.IsDirty(table))
            {
                return;
            }

            if (SuppressDialogs)
            {
                return;
            }

            if (AutoSave || EditorUtility.DisplayDialog("未保存の変更があります", $"{reason}\n'{table.name}' に未保存の変更があります。保存しますか?", "保存する", "保存しない"))
            {
                EditorUtility.SetDirty(table);
                DDriveAssetSave.SaveDirty(table);
            }
        }

        // 自動保存: 編集から 1 秒何も起きなければ SaveDirty。
        private void AutoSaveTick()
        {
            if (_target == null)
            {
                return;
            }

            var dirty = EditorUtility.IsDirty(_target);
            var group = Undo.GetCurrentGroup();
            var now = EditorApplication.timeSinceStartup;
            if (group != _autoUndoGroup || (dirty && !_autoWasDirty))
            {
                _autoEditTime = now;
            }

            _autoUndoGroup = group;
            _autoWasDirty = dirty;
            if (dirty && AutoSave && now - _autoEditTime >= 1.0 && !EditorApplication.isCompiling)
            {
                SaveTarget();
                _autoWasDirty = false;
            }
        }

        // ── 定期更新 ──

        private void OnKnobEdited()
        {
            _autoEditTime = EditorApplication.timeSinceStartup;
            RefreshChips();
            SyncFooter();
            _issueSig = null; // 次の確認で検証をやり直す
        }

        private void Tick()
        {
            if (UxTheme.IsStale(_root))
            {
                UxTheme.Apply(_root);
                RebuildBody();
                return;
            }

            AutoSaveTick();
            UpdateNotice();
            if (_target == null || _so == null)
            {
                return;
            }

            _so.Update();
            _knobs?.Sync();
            SyncFooter();
            RefreshChips();
            _tick++;
            if (_tick % 7 == 0)
            {
                SyncTablesIfChanged();
                RunChecks();
            }
        }

        private void SyncFooter()
        {
            var dirty = EditorUtility.IsDirty(_target);
            var saveText = dirty ? "保存 ●" : "保存";
            if (_btnSave != null && _btnSave.text != saveText)
            {
                _btnSave.text = saveText;
                _btnSave.tooltip = dirty
                    ? "未保存の変更があります。Ctrl+S(File > Save)でも保存されます"
                    : "この TuningTable を保存する(版数が進む)。Ctrl+S(File > Save)でも保存されます";
            }

            if (_autoToggle != null && _autoToggle.value != AutoSave)
            {
                _autoToggle.SetValueWithoutNotify(AutoSave);
            }
        }

        private void RefreshAll()
        {
            RefreshChips();
            UpdateNotice();
            SyncFooter();
        }

        // 件数・変更数・検索の一致数などの表示。
        private void RefreshChips()
        {
            if (_target == null || _knobs == null || _title == null)
            {
                return;
            }

            SetText(_title, _target.name);
            var path = AssetDatabase.GetAssetPath(_target);
            SetText(_idLabel, string.IsNullOrEmpty(path) ? "(アセットとして保存されていません)" : path);
            _idLabel.tooltip = path;

            var cats = _source != null ? _source.CategoryNames.Count : 0;
            var tables = _target.Tables != null ? _target.Tables.Length : 0;
            var keys = _knobs.KnobCount;
            SetText(_chipKeys, keys + " キー");
            SetText(_chipCats, (keys == 0 ? 0 : cats) + " カテゴリ");
            SetText(_chipTables, "Tables " + tables);
            SetText(_btnTabValues, "値(" + keys + ")");
            SetText(_btnTabTables, "Tables(" + tables + ")");

            var mods = _knobs.ModifiedCount + _cellMods;
            var modText = mods > 0 ? $"● 変更 {mods} 件" : "変更なし";
            SetText(_chipMod, modText);
            _chipMod.EnableInClassList("ux-chip--mod", mods > 0);
            _chipMod.tooltip = "このウィンドウで開いたときの値と違う数(↺ で戻せます)";
            if (_barMod != null)
            {
                SetText(_barMod, modText);
                _barMod.EnableInClassList("ux-chip--mod", mods > 0);
            }

            var errors = TuningCheck.Count(_issues, TuningCheck.Level.Error);
            var warnings = TuningCheck.Count(_issues, TuningCheck.Level.Warning);
            string status;
            string cls;
            if (errors > 0)
            {
                status = $"✕ エラー {errors}";
                cls = "ux-chip--err";
            }
            else if (warnings > 0)
            {
                status = $"⚠ 警告 {warnings}";
                cls = "ux-chip--warn";
            }
            else
            {
                status = "✓ 検証 OK";
                cls = "ux-chip--ok";
            }

            SetText(_chipCheck, status);
            _chipCheck.EnableInClassList("ux-chip--ok", cls == "ux-chip--ok");
            _chipCheck.EnableInClassList("ux-chip--warn", cls == "ux-chip--warn");
            _chipCheck.EnableInClassList("ux-chip--err", cls == "ux-chip--err");
            _chipCheck.tooltip = "いま開いている TuningTable の検証(下の「検証」に 1 件ずつ出ます)";

            UpdateHit();
        }

        private void UpdateHit()
        {
            if (_hit == null || _knobs == null)
            {
                return;
            }

            var q = Query;
            var text = string.Empty;
            if (q.Length > 0)
            {
                text = $"「{q}」に一致: キー {_knobs.ShownCount} 件 ・ Tables {_tablesShown} 件";
            }
            else if (_tab == 0 && _knobs.ShownCount != _knobs.KnobCount)
            {
                text = $"{_knobs.KnobCount} キー中 {_knobs.ShownCount} 件を表示中(カテゴリで絞り込み)";
            }

            SetText(_hit, text);
            _hit.Show(text.Length > 0);
        }

        private static void SetText(Label l, string text)
        {
            if (l != null && l.text != text)
            {
                l.text = text;
            }
        }

        private static void SetText(Button b, string text)
        {
            if (b != null && b.text != text)
            {
                b.text = text;
            }
        }

        // ── 検証 ──

        private void RunChecks()
        {
            if (_target == null || _issueBody == null)
            {
                return;
            }

            _issues = TuningCheck.Run(_target);
            var sb = new StringBuilder();
            for (var i = 0; i < _issues.Count; i++)
            {
                sb.Append((int)_issues[i].Level).Append(_issues[i].Message).Append('\n');
            }

            var sig = sb.ToString();
            if (sig == _issueSig)
            {
                return;
            }

            _issueSig = sig;
            _issueBody.Clear();
            if (_issues.Count == 0)
            {
                var ok = new Label("問題は見つかりませんでした");
                ok.AddToClassList("ux-dim");
                _issueBody.Add(ok);
            }

            for (var i = 0; i < _issues.Count && i < IssueLimit; i++)
            {
                _issueBody.Add(BuildIssueRow(_issues[i]));
            }

            if (_issues.Count > IssueLimit)
            {
                var more = new Label($"ほか {_issues.Count - IssueLimit} 件");
                more.AddToClassList("ux-dim");
                _issueBody.Add(more);
            }

            RefreshChips();
        }

        private VisualElement BuildIssueRow(TuningCheck.Issue issue)
        {
            var row = new VisualElement();
            row.AddToClassList("ux-issue");
            row.AddToClassList(issue.Level == TuningCheck.Level.Error ? "ux-issue--err" : "ux-issue--warn");
            var icon = new Label(issue.Level == TuningCheck.Level.Error ? "✕" : "⚠");
            icon.AddToClassList("ux-issue__icon");
            row.Add(icon);
            var msg = new Label(issue.Message);
            msg.AddToClassList("ux-issue__msg");
            row.Add(msg);
            if (!string.IsNullOrEmpty(issue.Key))
            {
                row.tooltip = "クリックでこのキーを検索に入れる";
                var key = issue.Key;
                row.RegisterCallback<ClickEvent>(e => JumpToKey(key));
            }

            return row;
        }

        // 検証の行から該当のキー(またはテーブル)へ。キーなら 値 タブ、テーブルのキーなら Tables タブ。
        private void JumpToKey(string key)
        {
            _query = key;
            var isTable = false;
            var tables = _target != null ? _target.Tables : null;
            for (var i = 0; tables != null && i < tables.Length; i++)
            {
                if (string.Equals(tables[i].Key, key, StringComparison.Ordinal))
                {
                    isTable = true;
                    break;
                }
            }

            _tab = isTable ? 1 : 0;
            RebuildBody();
            _body?.ScrollTo(_valuesHost ?? _tablesHost);
        }
    }
}
