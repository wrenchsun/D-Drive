using System;
using System.Collections.Generic;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ui;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.CanvasTool
{
    // [07_canvas_prefab.md] Part A の最小エディタ(4-1)。編集は SerializedObject バインド(Undo 対応)、
    // プレビューは実 UiManager で OpenData した実体を開いているシーン / プレハブステージの DontSave
    // ルートに置いて SceneView / Game View で確認する(ADR-4: Editor 専用の再生経路を作らない。
    // PrefabEditorWindow / MaterialEditorWindow と同じ設計。owner instruction 2026-09-10: EditorWindow 内部には描画しない)。
    // ノードグラフ(NavigationGraphView)は静的な編集用ダイアグラムであり実 UI を描画するものではないため許容する。
    // ゲームパッド入力シミュレーション(4-3)は「確認用シーンを開く」で実際に開いた UiManager インスタンスに対して
    // MoveFocus/MoveFocusFrom を叩くだけで、ウィンドウ内で UI を再現描画することはしない(owner instruction 2026-09-10)。
    // 2026-09-12: 「確認用シーンを開く」は CanvasPreviewSceneSetup で専用の空シーン(CanvasPreviewScene)に
    // 切り替えたうえで、その場で OpenData まで行う(ユーザー自身の Canvas 等と重ならずに確認できる。以前は
    // 「切り替え」と「配置」を別ボタンにしていたが、切り替えたら必ず置きたいだけなので統合した)。
    // Disappear の見た目は UI Tween Editor 側(実要素をプレビュー対象に自動割り当てして再生できる)で確認する。
    [DDrive.Editor.Inspector.DataEditor(typeof(CanvasData), "Canvas Editor で開く")]
    public sealed partial class CanvasEditorWindow : EditorWindow
    {
        private const string NoneChoice = "なし";
        // (レビュー対応 2026-09-14) 生の new GameObject をやめ EditorPreviewRoots で生成し、OnEnable で同名の残骸を掃除する。
        private const string PreviewRootName = "[D-Drive] Canvas Preview";
        // (レビュー対応 2026-09-14) "(ルート)" の直書きが 5 か所あったので UI Tween Editor と共有の定数にする。
        private const string RootElementLabel = UiTweenEditorWindow.RootElementLabel;

        private CanvasData _target;
        private bool _lockTarget;

        // 2026-10-03(Canvas の埋め込み): _target が別の Canvas(親)に埋め込まれた子として編集されているときの親の連なり
        // (外側 → 内側。各 Link.RootPath = その Data の Prefab ルートから、次の(内側の)Canvas のルートまでのパス)。
        // 空 = 単独で編集中。プレビュー再生・「選択」は、この連なりのどれかの Prefab(プレハブモード / 確認用プレビュー)で
        // 子のパスを親ルート基準へ変換して行う。
        private List<CanvasEmbeddedEditing.Link> _ancestors = new();
        private CanvasEmbeddedEditing.CanvasLookup _lookup;
        private CanvasData _previewData; // 確認用プレビューで OpenData した CanvasData(_target か、その親の連なりの最外側)
        private const string FollowSelectionPrefKey = "DDrive.CanvasEditor.FollowSelection";
        private bool _followSelection = true;
        private string _fxFilter = string.Empty;
        private string _highlightPath;
        private int _selfSelectedId; // このウィンドウが選択した GameObject(選択に追従で自分の選択に反応しないため)
        private VisualElement _contextRow;
        private Button _backButton;
        private Label _contextLabel;
        private Foldout _embeddedFoldout;
        private VisualElement _embeddedContainer;
        private readonly Dictionary<string, Foldout> _fxBoxByPath = new();
        private UiManager _manager;
        private AssetRegistry _registry;
        private PoolService _pool;
        private UiTweenManager _tweenManager;
        private double _lastEditorTime;
        private UiPreset _bulkPreset;

        private GameObject _previewRoot;
        private Handle<CanvasMarker> _previewHandle = Handle<CanvasMarker>.Invalid;

        private ScrollView _root;
        private ObjectField _targetField;
        private VisualElement _inspectorContainer;
        private Foldout _elementFxFoldout;
        private VisualElement _elementFxContainer;
        private Label _statusLabel;
        private VisualElement _validationFoldout;

        // 4-3: ノードグラフ(NavigationGraphView)とパッド操作シミュレーション。
        private Foldout _graphFoldout;
        private NavigationGraphView _graphView;
        private VisualElement _unreachableContainer;
        private VisualElement _padRow;
        private Label _focusLabel;
        private string _simFocusPath;

        private readonly List<(string label, UiTweenData tween)> _catalogChoices = new();

        // ElementFx 各行の直接再生(Appear/Idle/Disappear)。UI Tween Editor を開かず Canvas Editor 内で
        // 完結できるようにする(2026-09-12 ユーザー要望)。Key は (ElementPath, Phase ラベル)。
        private readonly Dictionary<(string path, string phase), Handle<UiTweenMarker>> _phasePreviewHandles = new();

        // ElementFx 各要素の Foldout 開閉状態(ElementPath がキー、デフォルトは折りたたみ)。
        // RebuildElementFxAssignments は行の変更のたびに全 Foldout を作り直すため、ここに残しておかないと
        // 別の行を編集しただけで開いていた行まで畳まれてしまう。
        private readonly Dictionary<string, bool> _elementFxExpanded = new();
        private readonly List<PhaseRowWidgets> _phaseRowWidgets = new();
        private readonly TweenTrack[] _presetPlayScratch = new TweenTrack[UiTweenManager.MaxTracksPerTween];

        // 2026-09-29: ElementFx の再生前の状態(初期状態)の控え。再生のたびにここへ戻してから再生する
        // (連打しても位置がずれない)。_previewStates = 確認用シーンのプレビュー実体(実体を作り直すまで保持)、
        // _stageStates = プレハブモードのステージ内の実体(プレハブに値を残さないよう、再生が終わったら戻して捨てる)。
        private const string SelectOnPlayPrefKey = "DDrive.CanvasEditor.SelectOnPlay";
        private readonly ElementFxStateSnapshot _previewStates = new();
        private readonly ElementFxStateSnapshot _stageStates = new();
        private readonly List<RectTransform> _targetScratch = new();

        // 2026-10-06(U-29b): 「Idle を流す」(プレハブモードで Idle が割り当てられた全要素の Idle を流し続ける)。
        // トグルの状態はウィンドウのセッション内だけ(保存しない。開き直すとオフ)。実体は CanvasIdleFlow(流す前の値を控えて必ず戻す)。
        private CanvasIdleFlow _idleFlow;
        private bool _idleFlowOn;
        private bool _idleHeldByPhase;          // 行の ▶ 再生(Appear / Disappear 等の個別再生)の間は Idle を止めて、終わったら再開する
        private int _idleSignature;             // 今流している内容(ステージ + 割り当て)の署名。変わったら作り直す
        private bool _idleBuilt;
        private double _lastIdlePoll;
        private double _lastIdleRepaint;
        private int _idleSelectionActiveId;
        private int _idleSelectionCount;
        private readonly List<CanvasIdleFlow.Entry> _idleEntries = new();
        private Toggle _idleFlowToggle;
        private Label _idleFlowHint;
        private const double IdlePollInterval = 0.25;
        private const double IdleRepaintInterval = 1.0 / 30.0;

        // (レビュー対応 2026-09-14) FindUiTweenData が ▶ のたびに(「▶ 全〜」では要素数ぶん)AssetDatabase を
        // 全走査していた。Id → アセットの対応を 1 回の走査でまとめて作り、ヒットしなかったときだけ作り直す。
        private readonly Dictionary<ulong, UiTweenData> _tweenLookup = new();

        private sealed class PhaseRowWidgets
        {
            public string ElementPath;
            public string Phase;
            public Button PlayButton;
            public Button PauseButton;
            public Button StopButton;
            public Label StatusLabel;
        }

        [MenuItem(DDriveMenu.Editors + "Canvas")]
        public static void OpenFromMenu() => Open(Selection.activeObject as CanvasData);

        public static void Open(CanvasData target)
        {
            var window = GetWindow<CanvasEditorWindow>("Canvas Editor");
            window.minSize = new Vector2(380, 320);
            if (target != null)
            {
                window.SetTarget(target);
            }
        }

        private void OnEnable()
        {
            _registry = EditorAnchorRegistry.Build();
            _pool = new PoolService();
            _tweenManager = new UiTweenManager(_registry);
            _manager = new UiManager(_pool, _registry, tweens: _tweenManager);
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            _lastEditorTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;
            PrefabStage.prefabSaving += OnPrefabSaving;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            _idleFlow = new CanvasIdleFlow(_tweenManager, FindUiTweenData);

            // (レビュー対応 2026-09-14) 前回閉じ損ねた・ドメインリロードで参照を失ったプレビュールートの残骸を消す。
            EditorPreviewRoots.DestroyAll(PreviewRootName);
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
            PrefabStage.prefabSaving -= OnPrefabSaving;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= OnEditorUpdate;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            StopIdleFlow(); // 流していた Idle の値をプレハブに残さない(ドメインリロード・ウィンドウを閉じる前にも通る)
            _idleFlow = null;
            ReleaseStageStates(); // プレハブモードで再生した値をプレハブに残さない(ドメインリロード前にも通る)
            RemovePreview();
            // (レビュー対応 2026-09-14) 閉じても "[D-Drive] UI Root"(レイヤー 5 枚 + プールに戻った Canvas 実体)が
            // 次にシーンを閉じるまで残っていた。RemovePreview(StopAll 済み)の後に UiManager 自身のルートを破棄する
            // (この後 _manager / _pool ごと捨てるので、破棄済みのプール実体が再利用されることは無い)。
            _manager?.DestroyEditorRoot();
            _manager = null;
            _tweenManager = null;
            _pool = null;
            _registry = null;
        }

        // ADR-4 / owner instruction 2026-09-10: 確認用シーンのプレビューは実 UiManager + UiTweenManager を
        // EditorApplication.update から駆動する(ウィンドウ内には何も描画しない)。ElementFx(4-9)の
        // Appear/Idle/Disappear は UiTweenManager.Tick が進めないと一切動かないため、UiTweenEditorWindow と
        // 同じ手順でここでも Tick する。
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

        private void OnSelectionChange()
        {
            // 流している Idle のうち、選択した要素(とその祖先)の分だけ止めて元の値へ戻す(U-29b。編集と値が混ざらないように)。
            if (_idleFlowOn && _idleFlow != null && _idleFlow.IsActive)
            {
                _idleFlow.SuspendFor(Selection.transforms);
                UpdateIdleFlowUi();
            }

            if (!_lockTarget && Selection.activeObject is CanvasData data && data != _target)
            {
                SetTarget(data);
                return;
            }

            if (_followSelection && !_lockTarget)
            {
                FollowSceneSelection();
            }
        }

        // プロジェクトの変更(CanvasData の追加・削除・Prefab の差し替え)があったら、子 CanvasData の引き当て表を作り直す。
        private void OnProjectChange()
        {
            _lookup = null;
            if (_root != null && _target != null)
            {
                RebuildEmbeddedSection();
            }
        }

        private CanvasEmbeddedEditing.CanvasLookup Lookup => _lookup ??= CanvasEmbeddedEditing.CanvasLookup.Build();

        // 編集対象を含む「Prefab を表示している側」の CanvasData(親の連なりの最外側、無ければ _target)。
        private CanvasData ViewData => _ancestors.Count > 0 ? _ancestors[0].Data : _target;

        // 4-3: NavNode の編集(SetLink/ClearLink 等)は Undo.RecordObject で包んでいるため、Undo/Redo が
        // 走ったらグラフを作り直す(SerializedObject 側は Bind 済みなので自動で追従する)。
        // (レビュー対応 2026-09-14) グラフだけ作り直していたため、Undo で ElementFx の行が減ると古い行 UI の
        // ▶ / プリセット選択が範囲外の index を引いていた。ElementFx 割当と Validation も作り直す。
        // 2026-10-06(docs/43 16-2 / 16-24): 埋め込み Canvas 欄(登録済みの行・RootPath 欄・子 CanvasData 欄・入れ子 Prefab から
        // の検出結果)も作り直す。以前は作り直しておらず、Undo / Redo のあとデータは戻っても欄の表示が古いままだった。
        // 親の連なりのヘッダー(UpdateContextRow)・ElementFx 一覧のグループ分け(RebuildElementFxAssignments が
        // EmbeddedCanvases から作る)・Validation もここで現在のデータから描き直す。編集対象(_target)自体は変えない。
        private void OnUndoRedoPerformed() => RefreshAfterUndoRedo();

        // テストから直接呼べるよう public(Undo イベントを経由せず、再構築だけを検証する)。
        public void RefreshAfterUndoRedo()
        {
            if (_root == null)
            {
                return;
            }

            UpdateContextRow();
            RebuildGraph();
            RebuildEmbeddedSection();
            RebuildElementFxAssignments();
            RebuildButtonWires();
            RefreshValidation();
        }

        private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene previous, UnityEngine.SceneManagement.Scene current)
        {
            // (レビュー対応 2026-09-14) 以前は参照を捨てるだけだったため、UiManager の _stack / _instances に
            // 実体を失った CanvasInstance が残り、Idle 等の Tween も走り続けていた。StopAll で Manager 側の状態を
            // 片付け(破棄済みの実体には各所の null 判定で触れない)、UI Root も破棄して次の OpenData で
            // 新しいシーンに作り直させる(DontSave のルートがシーン外に取り残されても確実に消えるように)。
            RemovePreview();
            _manager?.DestroyEditorRoot();
        }

        public void CreateGUI()
        {
            _root = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rootVisualElement.Add(_root);

            var toolbar = new Toolbar();
            _targetField = new ObjectField("対象") { objectType = typeof(CanvasData), allowSceneObjects = false, style = { flexGrow = 1 } };
            _targetField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is CanvasData data)
                {
                    SetTarget(data);
                }
                else if (evt.newValue != null)
                {
                    _targetField.SetValueWithoutNotify(_target);
                }
            });
            toolbar.Add(_targetField);
            var lockToggle = new ToolbarToggle { text = "🔒", tooltip = "選択に追従しない" };
            lockToggle.RegisterValueChangedCallback(evt =>
            {
                _lockTarget = evt.newValue;
                UpdateFollowLockUi();
            });
            toolbar.Add(lockToggle);
            toolbar.Add(DDrive.Editor.Inspector.NewAssetToolbarButton.CreateToolbarButton(typeof(CanvasEditorWindow)));
            // U-21([39_usability_fixes_2026-09-17.md]): Canvas Editor には要素(RectTransform)を移動する手段が
            // 無かった。プレビュー実体(「確認用シーンを開く」で置く物)は OpenData が Prefab リンク無しで
            // 生成するため、そこを動かしても Prefab には反映されない。ModelEditorWindow.OpenPrefab /
            // VfxEditorWindow.OpenPrefab と同じ導線で Prefab 自身をプレハブモードで開き、Unity 標準の
            // Move/Rotate/Rect ツールで編集できるようにする(Undo はプレハブ編集の標準機構にそのまま乗る)。
            toolbar.Add(new ToolbarButton(OpenPrefab) { text = "Prefab を開く(要素の移動)", tooltip = "CanvasData.Prefab をプレハブモードで開く。要素を選択して Unity 標準の Move/Rotate/Rect ツールで移動・回転・リサイズできる(Ctrl+Z で戻せる)" });
            _root.Add(toolbar);

            // 2026-10-03(Canvas の埋め込み): 親の中から子へ切り替えたときだけ出る「← 親へ戻る」。
            _contextRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 4, display = DisplayStyle.None } };
            _backButton = new Button(BackToParent) { text = "← 親へ戻る" };
            _contextRow.Add(_backButton);
            _contextLabel = new Label { style = { marginLeft = 6, opacity = 0.8f } };
            _contextRow.Add(_contextLabel);
            _root.Add(_contextRow);

            _followSelection = EditorPrefs.GetBool(FollowSelectionPrefKey, true);
            var followToggle = new Toggle("選択に追従")
            {
                value = _followSelection,
                tooltip = "Hierarchy / プレハブステージ / 確認用プレビューで選んだ GameObject に合わせて編集対象を切り替える(埋め込み Canvas の配下なら子の CanvasData、それ以外は親)。入力中のフィールドがあるあいだは切り替えない。設定はエディタに保存される",
            };
            followToggle.RegisterValueChangedCallback(evt =>
            {
                _followSelection = evt.newValue;
                EditorPrefs.SetBool(FollowSelectionPrefKey, evt.newValue);
            });
            // 2026-10-06(docs/43 16-7): 🔒 が ON の間は「選択に追従」が効かない(動作は `_followSelection && !_lockTarget` のまま)。
            // 2 つの関係が画面から分かるよう、🔒 が ON の間はチェックを灰色にして理由を出す(チェックの値は保持する)。
            _followToggle = followToggle;
            _followLockHint = new Label("🔒 がオンの間は追従しません")
            {
                style = { whiteSpace = WhiteSpace.Normal, flexShrink = 1, marginLeft = 4, opacity = 0.8f, display = DisplayStyle.None },
            };
            var followRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center } };
            followRow.Add(followToggle);
            followRow.Add(_followLockHint);
            _root.Add(followRow);
            UpdateFollowLockUi();

            var navButtons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            navButtons.Add(new Button(() => CollectSelectables()) { text = "Selectable を自動収集", tooltip = "Prefab 内の Selectable から Navigation を作る(既存の行は保持する)" });
            _root.Add(navButtons);

            var fxButtons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            fxButtons.Add(new Button(() => CollectElementFx()) { text = "要素を自動収集(Image / UiButton / パネル)", tooltip = "Prefab 内の Graphic/UiInteractable から ElementFx の行を作る(既存の行は保持する)" });
            _root.Add(fxButtons);

            var bulkRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4, alignItems = Align.Center } };
            var presetField = new EnumField("プリセット", _bulkPreset) { style = { flexGrow = 1f } };
            presetField.RegisterValueChangedCallback(evt => _bulkPreset = (UiPreset)evt.newValue);
            bulkRow.Add(presetField);
            bulkRow.Add(new Button(ApplyBulkPresetToButtons) { text = "一括適用: 全ボタンに反映", tooltip = "Prefab 内の全 UiButton の AppearPreset にこのプリセットを設定する" });
            _root.Add(bulkRow);

            _embeddedFoldout = new Foldout { text = "埋め込み Canvas(入れ子の子 Canvas)", value = true, style = { marginTop = 8 } };
            _root.Add(_embeddedFoldout);
            _embeddedContainer = new VisualElement();
            _embeddedFoldout.Add(_embeddedContainer);

            _elementFxFoldout = new Foldout { text = "ElementFx 割当(Appear / Idle / Disappear)", value = true, style = { marginTop = 8 } };
            _root.Add(_elementFxFoldout);

            var fxFilterField = new ToolbarSearchField { style = { marginBottom = 4 } };
            fxFilterField.tooltip = "ElementFx の一覧を要素のパスで絞り込む";
            fxFilterField.RegisterValueChangedCallback(evt =>
            {
                _fxFilter = evt.newValue ?? string.Empty;
                RebuildElementFxAssignments();
            });
            _elementFxFoldout.Add(fxFilterField);
            _elementFxFoldout.Add(new HelpBox("各要素の行でプリセット・プロジェクト独自カタログ([Catalog] 名前)・UiTweenData 直接指定のいずれかを選べます。", HelpBoxMessageType.Info));

            // 2026-09-12 ユーザー要望: 登録済みの ElementFx を 1 行ずつ「▶ 再生」するのは数が多いと手間なので、
            // 同じ区間(Appear/Idle/Disappear)を全要素まとめて再生できるようにする(各要素は自分に割り当てられた
            // プリセット/直接指定をそれぞれ再生する。実 UiTweenManager 経由で ADR-4 のまま)。
            var batchPlayRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };
            batchPlayRow.Add(new Button(() => PlayAllPhasePreview("Appear")) { text = "▶ 全 Appear", tooltip = "登録済みの全要素の Appear を、それぞれに割り当てられた演出でまとめて再生する" });
            batchPlayRow.Add(new Button(() => PlayAllPhasePreview("Idle")) { text = "▶ 全 Idle", tooltip = "登録済みの全要素の Idle をまとめて再生する" });
            batchPlayRow.Add(new Button(() => PlayAllPhasePreview("Disappear")) { text = "▶ 全 Disappear", tooltip = "登録済みの全要素の Disappear をまとめて再生する" });
            batchPlayRow.Add(new Button(StopAllPhasePreview) { text = "■ 全て停止", tooltip = "再生中の ElementFx プレビューをまとめて止め、再生前の状態(初期位置など)へ戻す" });
            _elementFxFoldout.Add(batchPlayRow);

            // 2026-10-06(U-29b): プレハブモードでも Idle を流し続ける(確認用プレビューでは元から流れる)。
            _idleFlowToggle = new Toggle("Idle を流す(プレハブモード)")
            {
                value = _idleFlowOn,
                tooltip = "プレハブモードで、Idle が割り当てられた全要素(埋め込みの子の分を含む)の Idle を流し続ける。止める・保存の直前・プレハブモードを閉じる・対象の切り替え・Play Mode に入るときは、流す前の値へ戻す(Prefab に途中の値は残らない)。設定は保存されない(開き直すとオフ)",
            };
            _idleFlowToggle.RegisterValueChangedCallback(evt => SetIdleFlow(evt.newValue));
            _idleFlowHint = new Label { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.8f, marginLeft = 4, marginBottom = 4 } };
            _elementFxFoldout.Add(_idleFlowToggle);
            _elementFxFoldout.Add(_idleFlowHint);
            UpdateIdleFlowUi();

            // 2026-09-29: 行ごとの ▶ を押したとき、その要素を Selection にして Inspector / Hierarchy で
            // どの要素か分かるようにする(既定 ON。全 Appear 等のまとめ再生では選択を変えない)。
            var selectOnPlayToggle = new Toggle("▶ 再生時にその要素を選択")
            {
                value = EditorPrefs.GetBool(SelectOnPlayPrefKey, true),
                tooltip = "行ごとの ▶ 再生を押したとき、再生する要素(プレビュー実体 / プレハブモードの実体)を選択状態にする。設定はエディタに保存される",
            };
            selectOnPlayToggle.RegisterValueChangedCallback(evt => EditorPrefs.SetBool(SelectOnPlayPrefKey, evt.newValue));
            _elementFxFoldout.Add(selectOnPlayToggle);

            _elementFxContainer = new VisualElement();
            _elementFxFoldout.Add(_elementFxContainer);

            BuildButtonWireSection();

            var previewButtons = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 4, marginBottom = 4 } }; // [09] §7.1
            // 2026-09-12 レビュー対応: 「確認用シーンを開く」と「ここに配置」を分けていたが、専用シーンに
            // 切り替えたら必ず置きたいだけなので手間なだけだった(ユーザー指摘)。1 ボタンに統合する
            // (専用シーンへ切り替え → その場で OpenData まで行う)。「Disappear を再生」も撤去: Disappear の
            // 見た目確認は UI Tween Editor 側(実要素をプレビュー対象に自動割り当てして▶再生できる。2026-09-12
            // 追加)でできるようになったため、Canvas Editor に専用ボタンを残す必要がなくなった。
            previewButtons.Add(PreviewPlacementButton.Create(
                "確認用シーンを開く",
                "EventSystem だけを置いた空の専用シーン(CanvasPreviewScene)に切り替え、そのまま実 UiManager で OpenData する(Selectable / 要素の自動収集も併せて実行する)。自分の Canvas 等と重ならずに確認できる。既に確認用シーンを開いているときはシーンを開き直さず、表示だけやり直す(Appear / Idle をやり直す)",
                OpenPreviewSceneAndPlace));
            previewButtons.Add(new Button(RemovePreview) { text = "閉じる", tooltip = "演出を待たず即座に片付ける(StopAll)" });
            _root.Add(previewButtons);

            _statusLabel = new Label { style = { marginLeft = 4, marginBottom = 4 } };
            _root.Add(_statusLabel);

            BuildNavigationGraphSection();

            _inspectorContainer = new VisualElement();
            _root.Add(_inspectorContainer);

            var validationHeader = new Label("Validation") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } };
            _root.Add(validationHeader);
            _validationFoldout = new VisualElement();
            _root.Add(_validationFoldout);

            if (_target != null)
            {
                SetTarget(_target);
            }
            else if (Selection.activeObject is CanvasData data)
            {
                SetTarget(data);
            }
        }

        private void SetTarget(CanvasData target)
        {
            if (target != _target)
            {
                _ancestors.Clear(); // 別の Canvas を選び直したら親の文脈は捨てる(同じ対象の再構築では保つ)
            }

            ApplyTarget(target);
        }

        // 親の連なり(ancestors。外側 → 内側)付きで子 Canvas を編集対象にする(「この Canvas を編集」・選択に追従・戻る)。
        private void SetTargetIn(CanvasData target, List<CanvasEmbeddedEditing.Link> ancestors)
        {
            _ancestors = ancestors != null ? new List<CanvasEmbeddedEditing.Link>(ancestors) : new List<CanvasEmbeddedEditing.Link>();
            _highlightPath = null;
            ApplyTarget(target);
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

        private Toggle _followToggle;
        private Label _followLockHint;

        private void UpdateFollowLockUi()
        {
            _followToggle?.SetEnabled(!_lockTarget);
            if (_followLockHint != null)
            {
                _followLockHint.style.display = _lockTarget ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void UpdateContextRow()
        {
            if (_contextRow == null)
            {
                return;
            }

            if (_ancestors.Count == 0 || _target == null)
            {
                _contextRow.style.display = DisplayStyle.None;
                return;
            }

            _contextRow.style.display = DisplayStyle.Flex;
            var parent = _ancestors[_ancestors.Count - 1].Data;
            _backButton.text = $"← {NameOf(parent)} へ戻る";
            var names = new System.Text.StringBuilder();
            for (var i = 0; i < _ancestors.Count; i++)
            {
                names.Append(NameOf(_ancestors[i].Data)).Append(" > ");
            }

            names.Append(NameOf(_target));
            _contextLabel.text = $"埋め込みとして編集中: {names}";
        }

        private static string NameOf(CanvasData data)
            => data == null ? "(なし)" : string.IsNullOrEmpty(data.DisplayName) ? data.name : data.DisplayName;

        // 入力途中(遅延確定)の欄の値を、切り替え前の対象に確定させる(レビュー PC-R-05)。フォーカスを外すと遅延確定の欄が確定し、
        // そのコールバックは作ったときの対象(owner)に書く。対象を切り替える直前に呼ぶ。
        private void FlushPendingInput()
        {
            if (rootVisualElement?.panel?.focusController?.focusedElement is VisualElement focused)
            {
                focused.Blur();
            }
        }

        private void ApplyTarget(CanvasData target)
        {
            if (target != _target)
            {
                FlushPendingInput();
                // ElementFx 直接再生の Handle は (ElementPath, Phase) 文字列だけがキーなので、別の CanvasData に
                // 切り替えると偶然同じパスの行が「再生中」と誤判定されうる。対象を切り替えたら破棄しておく
                // (再生自体はプレビューの実体ごと RemovePreview 側で止まるので、ここは辞書のクリアのみでよい)。
                StopIdleFlow(); // 対象を切り替えたら流していた Idle を戻す(必要なら次の更新で新しい対象の分を流し直す)
                ReleaseStageStates();
                _phasePreviewHandles.Clear();
            }

            _target = target;
            if (_root == null)
            {
                return;
            }

            _targetField.SetValueWithoutNotify(target);
            _inspectorContainer.Clear();
            if (target == null)
            {
                _statusLabel.text = "CanvasData を選択してください";
                _validationFoldout?.Clear();
                _elementFxContainer?.Clear();
                _buttonWireContainer?.Clear();
                _embeddedContainer?.Clear();
                UpdateContextRow();
                _phaseRowWidgets.Clear(); // 消した行の UI を毎フレーム更新し続けないように(レビュー対応 2026-09-14)
                return;
            }

            var so = new SerializedObject(target);
            _inspectorContainer.Add(new InspectorElement(so));
            _inspectorContainer.Bind(so);

            _statusLabel.text = _manager != null && _manager.IsOpen(_previewHandle) ? "プレビュー表示中" : "「確認用シーンを開く」で確認できます";
            UpdateContextRow();
            RefreshValidation();
            RebuildEmbeddedSection();
            RebuildElementFxAssignments();
            RebuildButtonWires();
            _simFocusPath = target.FirstSelected;
            RebuildGraph();
        }

        // 戻り値: データを書き換えたか。
        // (レビュー対応 2026-09-14) PlacePreview(▶ で暗黙に呼ばれる)が毎回これを実行し、差分が無くても Undo を
        // 2 件積んでアセットを Dirty にしていた。CollectMerged の結果が既存と同じ(キーの並びが一致)なら何も書かない。
        private bool CollectSelectables()
        {
            if (_target == null || _target.Prefab == null)
            {
                _statusLabel.text = "Prefab を設定してください";
                return false;
            }

            var merged = CanvasNavigationCollector.CollectMerged(_target.Prefab, _target.Navigation);
            if (SameNavigationKeys(_target.Navigation, merged))
            {
                _statusLabel.text = $"Selectable は収集済みです({merged.Length} 件)";
                return false;
            }

            Undo.RecordObject(_target, "Collect Selectables");
            _target.Navigation = merged;
            EditorUtility.SetDirty(_target);

            SetTarget(_target); // Inspector / Validation / グラフを再構築
            _statusLabel.text = $"Selectable を {merged.Length} 件収集しました";
            return true;
        }

        // U-21: CanvasData.Prefab をプレハブモードで開く(ModelEditorWindow.OpenPrefab / VfxEditorWindow.OpenPrefab
        // と同じ導線)。プレビュー実体はここで片付ける(プレハブモードへ切り替わるとプレビュー実体の所属ステージが
        // 不定になり、閉じ忘れの残骸になりやすいため)。
        private void OpenPrefab()
        {
            if (_target == null || _target.Prefab == null)
            {
                Debug.LogWarning("[DDrive] 対象 CanvasData に Prefab がありません。");
                return;
            }

            RemovePreview();
            AssetDatabase.OpenAsset(_target.Prefab);
        }

        // U-21: ElementFx の各行から、その要素(RectTransform)を選んで実際に動かせるようにする。プレビュー実体
        // (「確認用シーンを開く」で置く物、OpenData 生成、Prefab リンク無し)を動かしても Prefab には反映されない
        // ため、必ず Prefab 自身をプレハブモードで開いてから選択する。プレハブモードは通常のシーン編集と同じ
        // Undo 機構に乗るため、Move/Rotate/Rect ツールでの移動はそのまま Ctrl+Z で戻せる(CLAUDE.md §0-5 は
        // 「エディタがコードで書き換えるとき」の規約であり、ここはユーザー自身が Unity 標準ツールで動かす
        // 経路なので該当しない)。
        private void SelectElementForMove(string elementPath)
        {
            if (_target == null || _target.Prefab == null)
            {
                _statusLabel.text = "Prefab を設定してください";
                return;
            }

            // 2026-10-03(Canvas の埋め込み): 対象自身の Prefab、または対象を埋め込んでいる親の Prefab のステージが既に開いて
            // いればそのまま使う(子のプレハブモードから親へ、親から子へ勝手に切り替えない)。どちらも開いていなければ対象自身の Prefab を開く。
            var stage = GetTargetStage(out var stagePrefix);
            if (stage == null)
            {
                RemovePreview();
                AssetDatabase.OpenAsset(_target.Prefab);
                stage = GetTargetStage(out stagePrefix);
            }

            if (stage == null || stage.prefabContentsRoot == null)
            {
                _statusLabel.text = "Prefab を開けませんでした";
                return;
            }

            var rootTransform = stage.prefabContentsRoot.transform;
            var viewPath = EmbeddedPaths.Combine(stagePrefix, elementPath);
            var found = string.IsNullOrEmpty(viewPath) ? rootTransform : rootTransform.Find(viewPath);
            var label = string.IsNullOrEmpty(elementPath) ? RootElementLabel : elementPath;
            if (found == null)
            {
                _statusLabel.text = $"要素が見つかりません({label})";
                return;
            }

            SelectGameObject(found.gameObject);
            PreviewPlacement.Focus(found.gameObject);
            _statusLabel.text = $"'{label}' を選択しました。SceneView の移動/回転/リサイズツールで編集できます(Ctrl+Z で戻せます)";
        }

        // 4-9: Graphic(Image 等)/UiInteractable(UiButton 等)/パネル(RectTransform+Graphic)を持つパスを
        // ElementEffects に追加する(既存の行・値は保持する)。戻り値: データを書き換えたか(レビュー対応 2026-09-14)。
        private bool CollectElementFx()
        {
            if (_target == null || _target.Prefab == null)
            {
                _statusLabel.text = "Prefab を設定してください";
                return false;
            }

            // 登録済みの埋め込みルートの配下は子の CanvasData の担当なので集めない(既に親にある行は消えず残る)。
            var merged = CanvasElementFxCollector.CollectMerged(_target.Prefab, _target.ElementEffects, CanvasEmbeddedEditing.RegisteredRoots(_target));
            if (SameElementFxKeys(_target.ElementEffects, merged))
            {
                _statusLabel.text = $"ElementFx は収集済みです({merged.Length} 件)";
                return false;
            }

            Undo.RecordObject(_target, "Collect ElementFx");
            _target.ElementEffects = merged;
            EditorUtility.SetDirty(_target);

            SetTarget(_target);
            _statusLabel.text = $"ElementFx を {merged.Length} 件収集しました";
            return true;
        }

        // CollectMerged は既存の行をそのままの値・順序で残し、無いキーだけ末尾に足す(重複キーは 1 行にまとまる)。
        // よって「件数とキーの並びが一致」なら内容も同一で、書き換える必要が無い(レビュー対応 2026-09-14)。
        private static bool SameNavigationKeys(NavNode[] existing, NavNode[] merged)
        {
            var count = existing?.Length ?? 0;
            if (count != merged.Length)
            {
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                if (!string.Equals(existing[i].Element ?? string.Empty, merged[i].Element ?? string.Empty, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameElementFxKeys(ElementFx[] existing, ElementFx[] merged)
        {
            var count = existing?.Length ?? 0;
            if (count != merged.Length)
            {
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                if (!string.Equals(existing[i].ElementPath ?? string.Empty, merged[i].ElementPath ?? string.Empty, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        // 4-10: 各 ElementFx 行の Appear/Idle/Disappear を「なし / 組み込みプリセット / プロジェクトの
        // UiPresetCatalog / UiTweenData 直接指定」から選べる UI(PopupField + ObjectField)。
        // 選択の解決優先順位はランタイム側([15] B-5 実装メモ)と同じ id > Preset。
        private readonly Dictionary<string, bool> _fxGroupExpanded = new();

        private void RebuildElementFxAssignments()
        {
            if (_elementFxContainer == null)
            {
                return;
            }

            _elementFxContainer.Clear();
            // (レビュー対応 2026-09-14) 早期 return の前に消す(以前は行が 0 になっても古い行の UI を毎フレーム更新していた)。
            _phaseRowWidgets.Clear();
            _fxBoxByPath.Clear();
            var hasRows = _target != null && _target.ElementEffects != null && _target.ElementEffects.Length > 0;
            var hasEmbeds = _target != null && _target.EmbeddedCanvases != null && _target.EmbeddedCanvases.Length > 0;
            if (!hasRows && !hasEmbeds)
            {
                _elementFxContainer.Add(new Label("ElementFx がありません(上の「要素を自動収集」で追加してください)") { style = { opacity = 0.7f } });
                return;
            }

            _catalogChoices.Clear();
            foreach (var (name, tween) in UiPresetCatalogUtility.Collect())
            {
                _catalogChoices.Add(($"[Catalog] {name}", tween));
            }

            // 2026-10-03(Canvas の埋め込み): 「親の要素」と「埋め込み: 子ごと」に分けて表示する(埋め込みが無ければ従来の平らな一覧)。
            var groups = CanvasEmbeddedEditing.BuildGroups(_target, Lookup, _fxFilter);
            if (groups.Embeds.Count == 0)
            {
                if (groups.ParentRows.Count == 0)
                {
                    _elementFxContainer.Add(new Label("絞り込みに一致する行がありません") { style = { opacity = 0.7f } });
                    return;
                }

                foreach (var index in groups.ParentRows)
                {
                    _elementFxContainer.Add(BuildFxRow(index, null));
                }

                return;
            }

            var parentKey = "parent";
            var parentFoldout = new Foldout { text = $"{NameOf(_target)} の要素({groups.ParentRows.Count})", value = !_fxGroupExpanded.TryGetValue(parentKey, out var parentOpen) || parentOpen, style = { marginTop = 4 } };
            parentFoldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == parentFoldout)
                {
                    _fxGroupExpanded[parentKey] = evt.newValue;
                }
            });
            foreach (var index in groups.ParentRows)
            {
                parentFoldout.Add(BuildFxRow(index, null));
            }

            foreach (var g in groups.Embeds)
            {
                if (g.OverrideRows.Count == 0)
                {
                    continue;
                }

                parentFoldout.Add(new Label($"↳ 親での上書き: {NameOf(g.Child)}({g.RootPath})。この行は子の CanvasData の同じ要素の設定より優先されます")
                {
                    style = { unityFontStyleAndWeight = FontStyle.Bold, whiteSpace = WhiteSpace.Normal, marginTop = 4, marginBottom = 2 },
                });
                var cleanupGroup = g;
                parentFoldout.Add(new Button(() => CleanUpOverrides(cleanupGroup))
                {
                    text = "上書きをまとめて整理…",
                    tooltip = "この埋め込みの配下を指す親の行のうち、中身が既定のままの行(自動収集されただけの行)を取り除く。設定が入っている行があれば、取り除く / 残す / キャンセルを 1 回確認する。Ctrl+Z 1 回で戻せる",
                });
                foreach (var index in g.OverrideRows)
                {
                    parentFoldout.Add(BuildFxRow(index, "[親での上書き] "));
                }
            }

            _elementFxContainer.Add(parentFoldout);

            foreach (var g in groups.Embeds)
            {
                _elementFxContainer.Add(BuildEmbedGroup(g));
            }
        }

        // 埋め込みごとのグループ: 子の CanvasData の ElementFx 行を読み取り表示し、「この Canvas を編集」で編集対象を子へ切り替える。
        private VisualElement BuildEmbedGroup(CanvasEmbeddedEditing.EmbedGroup g)
        {
            var key = "embed:" + g.RootPath;
            var title = g.Child != null ? $"埋め込み: {NameOf(g.Child)}({g.RootPath})" : $"埋め込み: (未解決)({g.RootPath})";
            var foldout = new Foldout { text = title, value = !_fxGroupExpanded.TryGetValue(key, out var open) || open, style = { marginTop = 4 } };
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == foldout)
                {
                    _fxGroupExpanded[key] = evt.newValue;
                }
            });

            if (g.Child == null)
            {
                foldout.Add(new HelpBox("子の CanvasData が未設定、または見つかりません。上の「埋め込み Canvas」で指定してください。", HelpBoxMessageType.Warning));
                return foldout;
            }

            var index = g.EmbedIndex;
            foldout.Add(new Button(() => EditEmbedded(index))
            {
                text = "この Canvas を編集",
                tooltip = "編集対象をこの子の CanvasData に切り替える(「← 親へ戻る」で戻れる)。▶ 再生・「選択」は親の Prefab(プレハブモード / 確認用プレビュー)の中の埋め込み実体で動く",
            });
            foldout.Add(new Label($"子の ElementFx: {g.ChildRows.Count} 行(読み取り表示。編集は「この Canvas を編集」で)") { style = { opacity = 0.7f, marginTop = 2 } });
            foreach (var row in g.ChildRows)
            {
                foldout.Add(new Label($"・{(string.IsNullOrEmpty(row.ElementPath) ? RootElementLabel : row.ElementPath)}   {row.Summary}{(row.OverriddenByParent ? "   [親で上書き]" : string.Empty)}")
                {
                    style = { whiteSpace = WhiteSpace.Normal, opacity = row.OverriddenByParent ? 0.5f : 0.85f },
                });
            }

            return foldout;
        }

        private void EditEmbedded(int embedIndex)
        {
            if (_target == null || _target.EmbeddedCanvases == null || embedIndex < 0 || embedIndex >= _target.EmbeddedCanvases.Length)
            {
                return;
            }

            var embed = _target.EmbeddedCanvases[embedIndex];
            var child = Lookup.Find(embed.Canvas);
            if (child == null)
            {
                _statusLabel.text = "子の CanvasData が見つかりません";
                return;
            }

            var chain = new List<CanvasEmbeddedEditing.Link>(_ancestors) { new CanvasEmbeddedEditing.Link(_target, embed.RootPath) };
            SetTargetIn(child, chain);
        }

        // ElementFx 1 行ぶんの編集 UI(Foldout)。index = _target.ElementEffects の添字。
        private VisualElement BuildFxRow(int index, string labelPrefix)
        {
            // (レビュー PC-R-05) 各欄のコールバックは、作ったときの対象(owner)に書く。選択に追従して _target が切り替わったあとに
            // 古い行の UI から確定されても、別の CanvasData には書かない。
            var owner = _target;
            var fx = owner.ElementEffects[index];
            // (レビュー対応 2026-09-14) ElementPath が null の行で Dictionary<string, bool> が ArgumentNullException になっていた。
            var elementPath = fx.ElementPath ?? string.Empty;

            // 2026-09-12 ユーザー要望: 要素数が多いと縦に長くなりすぎるので、各要素を折りたためるようにする
            // (デフォルトは折りたたみ)。展開状態は ElementPath をキーに保持し、ドロップダウン変更などで
            // 再構築が起きても(その行自身の変更でなければ)開閉が飛ばないようにする。
            var expanded = _elementFxExpanded.TryGetValue(elementPath, out var wasExpanded) && wasExpanded;
            var isHighlight = _highlightPath != null && _highlightPath == elementPath;
            var box = new Foldout { text = (labelPrefix ?? string.Empty) + (string.IsNullOrEmpty(elementPath) ? RootElementLabel : elementPath), value = expanded || isHighlight, style = { marginBottom = 6 } };
            if (isHighlight)
            {
                ApplyHighlight(box, true);
            }

            _fxBoxByPath[elementPath] = box;
            box.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == box)
                {
                    _elementFxExpanded[elementPath] = evt.newValue;
                }
            });

            // (レビュー対応 2026-09-14) getter/setter は GetFx/UpdateFx 経由で範囲チェックする(Undo で行が減った後に
            // 古い行 UI から呼ばれても例外にしない)。
            // U-21: この要素を選んで Prefab 上で移動・回転・リサイズできるようにする(選択のみ。実際の
            // 移動は Unity 標準の Move/Rect ツールで行う。ウィンドウ内に描画しない方針は維持する)。
            var focusRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 4 } };
            focusRow.Add(new Button(() => SelectElement(elementPath, focus: false))
            {
                text = "選択",
                tooltip = "この要素を選択して Inspector に出す(プレハブモード中はステージ内、確認用シーンの表示中はプレビュー実体、どちらも無ければ Prefab アセット内の要素を Ping)",
            });
            focusRow.Add(new Button(() => SelectElement(elementPath, focus: true))
            {
                text = "フォーカス",
                tooltip = "この要素を選択し、SceneView のカメラをその要素の矩形へ寄せる(表示中の実体が無いときは選択のみ)",
            });
            focusRow.Add(new Button(() => SelectElementForMove(elementPath)) { text = "選択して移動(Prefab を開く)" });
            box.Add(focusRow);

            box.Add(BuildPhaseRow(
                "Appear",
                elementPath,
                () => GetFx(owner, index).AppearPreset,
                v => UpdateFx(owner, index, e => { e.AppearPreset = v; return e; }),
                () => GetFx(owner, index).Appear,
                v => UpdateFx(owner, index, e => { e.Appear = v; return e; })));

            box.Add(BuildPhaseRow(
                "Idle",
                elementPath,
                () => GetFx(owner, index).IdlePreset,
                v => UpdateFx(owner, index, e => { e.IdlePreset = v; return e; }),
                () => GetFx(owner, index).Idle,
                v => UpdateFx(owner, index, e => { e.Idle = v; return e; })));

            box.Add(BuildPhaseRow(
                "Disappear",
                elementPath,
                () => GetFx(owner, index).DisappearPreset,
                v => UpdateFx(owner, index, e => { e.DisappearPreset = v; return e; }),
                () => GetFx(owner, index).Disappear,
                v => UpdateFx(owner, index, e => { e.Disappear = v; return e; })));

            box.Add(new Button(() => CopyRowToOthers(index)) { text = "この要素の設定を他の要素へコピー", style = { marginTop = 4 } });
            return box;
        }

        private static void ApplyHighlight(VisualElement box, bool on)
        {
            box.style.borderLeftWidth = on ? 3 : 0;
            box.style.borderLeftColor = on ? new Color(0.25f, 0.6f, 1f) : Color.clear;
            box.style.paddingLeft = on ? 4 : 0;
        }

        // 選択した要素の行を強調して展開し、一覧の該当位置までスクロールする。行が無ければステータスで知らせる。
        private void HighlightRow(string path)
        {
            path ??= string.Empty;
            _highlightPath = path;
            foreach (var kv in _fxBoxByPath)
            {
                ApplyHighlight(kv.Value, false);
            }

            if (!_fxBoxByPath.TryGetValue(path, out var box))
            {
                _statusLabel.text = $"'{(string.IsNullOrEmpty(path) ? RootElementLabel : path)}' の ElementFx 行はまだありません(「要素を自動収集」で追加できます)";
                return;
            }

            ApplyHighlight(box, true);
            box.value = true;
            _elementFxExpanded[path] = true;
            box.schedule.Execute(() => _root?.ScrollTo(box)).StartingIn(50);
        }

        // ── 埋め込み Canvas セクション(登録・候補の検出・削除) ──

        private void RebuildEmbeddedSection()
        {
            if (_embeddedContainer == null)
            {
                return;
            }

            _embeddedContainer.Clear();
            if (_target == null)
            {
                return;
            }

            _embeddedContainer.Add(new HelpBox(
                "親 Prefab の中に子 Canvas の Prefab を入れ子で入れたら「埋め込みとして登録」します。子の ElementFx・ボタン配線は子の CanvasData に 1 か所で持ち(「この Canvas を編集」)、親を Open したときも効きます。親の CanvasData に同じ要素の行があれば親が優先されます。",
                HelpBoxMessageType.Info));

            var rows = _target.EmbeddedCanvases;
            if (rows != null)
            {
                for (var i = 0; i < rows.Length; i++)
                {
                    _embeddedContainer.Add(BuildEmbeddedRow(i));
                }
            }

            if (_target.Prefab != null)
            {
                var candidates = CanvasEmbeddedEditing.DetectCandidates(_target.Prefab, Lookup.All, rows);
                var unregistered = new List<CanvasEmbeddedEditing.Candidate>();
                foreach (var c in candidates)
                {
                    if (!c.Registered)
                    {
                        unregistered.Add(c);
                    }
                }

                if (unregistered.Count > 0)
                {
                    _embeddedContainer.Add(new Label("入れ子 Prefab から検出(未登録)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 } });
                    foreach (var c in unregistered)
                    {
                        var candidate = c;
                        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center } };
                        row.Add(new Label($"{candidate.RootPath} = {NameOf(candidate.Canvas)}") { style = { flexGrow = 1f } });
                        row.Add(new Button(() => RegisterEmbedded(candidate.RootPath, candidate.Canvas))
                        {
                            text = "埋め込みとして登録",
                            tooltip = "この入れ子 Prefab を埋め込み Canvas として EmbeddedCanvases に追加する(子の ElementFx・配線は子の CanvasData のものが親の中でも効く)",
                        });
                        _embeddedContainer.Add(row);
                    }
                }
            }

            _embeddedContainer.Add(new Button(() =>
            {
                CanvasEmbeddedEditing.AddEmpty(_target);
                RefreshAfterEmbeddedEdit();
            })
            { text = "+ 手動で追加", style = { marginTop = 4 } });
        }

        private VisualElement BuildEmbeddedRow(int index)
        {
            // (レビュー PC-R-05) 各欄のコールバックは、作ったときの対象(owner)に書く(選択に追従して _target が切り替わったあとに
            // 遅延確定の欄が確定されても、別の CanvasData には書かない)。
            var owner = _target;
            var embed = owner.EmbeddedCanvases[index];
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginTop = 2 } };

            var pathField = new TextField { value = embed.RootPath ?? string.Empty, isDelayed = true, style = { width = 180 }, tooltip = "親 Prefab ルートからの相対パス(入れ子になっている子 Canvas のルート)。Enter か欄外クリックで確定" };
            pathField.RegisterValueChangedCallback(evt =>
            {
                if (owner == null || owner.EmbeddedCanvases == null || index >= owner.EmbeddedCanvases.Length)
                {
                    return;
                }

                // 確定時に正規化する(`\` → `/`、先頭・末尾の `/` を除く。レビュー PC-R-09)。
                // 2026-10-06(U-29a): 新しい配下の「親での上書き」の行を整理する(キャンセルしたら変更しない)。1 つの Undo グループ。
                var newPath = (evt.newValue ?? string.Empty).Replace('\\', '/').Trim('/');
                var current = owner.EmbeddedCanvases[index];
                if (!CanvasEmbeddedEditing.ChangeEmbedWithCleanup(owner, index, newPath, Lookup.Find(current.Canvas), out var cleanup))
                {
                    if (cleanup.Cancelled)
                    {
                        pathField.SetValueWithoutNotify(evt.previousValue);
                        _statusLabel.text = "RootPath の変更をキャンセルしました(何も変更していません)";
                    }

                    return;
                }

                if (owner == _target)
                {
                    RefreshAfterEmbeddedEdit();
                    var detail = cleanup.Describe();
                    if (!string.IsNullOrEmpty(detail))
                    {
                        _statusLabel.text = detail;
                    }
                }
            });
            row.Add(pathField);

            var child = Lookup.Find(embed.Canvas);
            var canvasField = new ObjectField { objectType = typeof(CanvasData), allowSceneObjects = false, style = { width = 180 } };
            canvasField.SetValueWithoutNotify(child);
            canvasField.RegisterValueChangedCallback(evt =>
            {
                if (owner == null || owner.EmbeddedCanvases == null || index >= owner.EmbeddedCanvases.Length)
                {
                    return;
                }

                var picked = evt.newValue as CanvasData;
                if (picked != null && picked.Id == 0)
                {
                    _statusLabel.text = "その CanvasData には Id がありません";
                    canvasField.SetValueWithoutNotify(evt.previousValue);
                    return;
                }

                // 2026-10-06(U-29a): 子を選び直したら、その配下の「親での上書き」の行を整理する(キャンセルしたら変更しない)。
                if (!CanvasEmbeddedEditing.ChangeEmbedWithCleanup(owner, index, owner.EmbeddedCanvases[index].RootPath, picked, out var cleanup))
                {
                    if (cleanup.Cancelled)
                    {
                        canvasField.SetValueWithoutNotify(evt.previousValue);
                        _statusLabel.text = "子 CanvasData の変更をキャンセルしました(何も変更していません)";
                    }

                    return;
                }

                if (owner == _target)
                {
                    RefreshAfterEmbeddedEdit();
                    var detail = cleanup.Describe();
                    if (!string.IsNullOrEmpty(detail))
                    {
                        _statusLabel.text = detail;
                    }
                }
            });
            row.Add(canvasField);

            var editButton = new Button(() => EditEmbedded(index)) { text = "この Canvas を編集", tooltip = "編集対象をこの子の CanvasData に切り替える(「← 親へ戻る」で戻れる)" };
            editButton.SetEnabled(child != null);
            row.Add(editButton);
            row.Add(new Button(() =>
            {
                if (CanvasEmbeddedEditing.RemoveAt(_target, index))
                {
                    RefreshAfterEmbeddedEdit();
                }
            })
            { text = "削除", tooltip = "この埋め込みの登録を外す(親の ElementFx の行は消えない。子の設定が親の中で効かなくなる)" });
            return row;
        }

        // 2026-10-06(U-29a): 登録と同時に、その配下を指す親の ElementFx の行を整理する(既定のままの行は確認なしで取り除き、
        // 設定のある行は 1 回確認。1 つの Undo グループ)。キャンセルしたら登録しない。
        private void RegisterEmbedded(string rootPath, CanvasData child)
        {
            if (CanvasEmbeddedEditing.RegisterWithCleanup(_target, rootPath, child, out var cleanup))
            {
                RefreshAfterEmbeddedEdit();
                _statusLabel.text = JoinStatus($"'{rootPath}' を埋め込み Canvas(子 = {NameOf(child)})として登録しました", cleanup);
            }
            else if (cleanup.Cancelled)
            {
                _statusLabel.text = $"'{rootPath}' の登録をキャンセルしました(何も変更していません)";
            }
        }

        private static string JoinStatus(string head, CanvasEmbeddedEditing.CleanupResult cleanup)
        {
            var detail = cleanup.Describe();
            return string.IsNullOrEmpty(detail) ? head : head + "。" + detail;
        }

        // 「親での上書き」グループの「上書きをまとめて整理…」: 登録済みの埋め込みについて、同じ整理(既定のままの行は取り除き、
        // 設定のある行は確認)をする。以前に登録した / 自動収集のあとで登録したデータを直すためのもの。
        private void CleanUpOverrides(CanvasEmbeddedEditing.EmbedGroup g)
        {
            var owner = _target;
            if (owner == null || g.Child == null)
            {
                return;
            }

            var cleanup = CanvasEmbeddedEditing.CleanUpOverrides(owner, g.RootPath, g.Child);
            if (owner != _target)
            {
                return;
            }

            if (cleanup.Cancelled)
            {
                _statusLabel.text = $"'{g.RootPath}' の整理をキャンセルしました(何も変更していません)";
                return;
            }

            RefreshAfterEmbeddedEdit();
            var detail = cleanup.Describe();
            _statusLabel.text = string.IsNullOrEmpty(detail) ? $"'{g.RootPath}' の配下に整理する行はありませんでした" : detail;
        }

        private void RefreshAfterEmbeddedEdit()
        {
            if (_target == null)
            {
                return;
            }

            _inspectorContainer?.Q<InspectorElement>()?.Bind(new SerializedObject(_target));
            RefreshValidation();
            RebuildEmbeddedSection();
            RebuildElementFxAssignments();
            RebuildButtonWires();
        }

        // ── 選択に追従(Hierarchy / プレハブステージ / 確認用プレビューで選んだ GameObject → 編集対象) ──

        private void FollowSceneSelection()
        {
            var go = Selection.activeGameObject;
            if (go != null && go.GetInstanceID() == _selfSelectedId)
            {
                return; // このウィンドウ自身が選んだもの(「選択」「▶ 再生」など)には反応しない
            }

            _selfSelectedId = 0;
            if (go == null || _target == null || EditorUtility.IsPersistent(go) || IsEditingText())
            {
                return;
            }

            if (!TryFindViewForSelection(go, out var view, out var viewRoot))
            {
                return;
            }

            var rel = TransformPath.GetRelative(viewRoot, go.transform);
            var owner = CanvasEmbeddedEditing.ResolveOwner(view, rel, Lookup);
            if (owner.Data == null)
            {
                return;
            }

            if (owner.Data != _target)
            {
                // 既存の文脈の中の祖先へ戻るだけなら、文脈(その外側)は保つ。
                var index = _ancestors.FindIndex(l => l.Data == owner.Data);
                SetTargetIn(owner.Data, index >= 0 ? _ancestors.GetRange(0, index) : owner.Ancestors);
            }

            HighlightRow(owner.Path);
        }

        // フォーカスのある入力欄(テキスト・数値)がこのウィンドウにあるあいだは切り替えない(入力中の値を失わない)。
        // このウィンドウ以外(Hierarchy など)にフォーカスがあるときは入力中ではない。
        private bool IsEditingText()
        {
            if (focusedWindow != this)
            {
                return false;
            }

            var focused = rootVisualElement.panel?.focusController?.focusedElement as VisualElement;
            return CanvasEmbeddedEditing.IsTextInputElement(focused);
        }

        // 選んだ GameObject が「どの CanvasData の Prefab を表示している実体」の中にあるか。
        // プレハブステージ(その Prefab を持つ CanvasData)か、確認用プレビューの実体。
        private bool TryFindViewForSelection(GameObject go, out CanvasData view, out Transform viewRoot)
        {
            view = null;
            viewRoot = null;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null && stage.IsPartOfPrefabContents(go))
            {
                view = FindCanvasByPrefabPath(stage.assetPath);
                viewRoot = stage.prefabContentsRoot.transform;
                return view != null;
            }

            if (_manager != null && _manager.IsOpen(_previewHandle) && _previewData != null)
            {
                var rootGo = _manager.GetGameObject(_previewHandle);
                if (rootGo != null && go.transform.IsChildOf(rootGo.transform))
                {
                    view = _previewData;
                    viewRoot = rootGo.transform;
                    return true;
                }
            }

            return false;
        }

        private CanvasData FindCanvasByPrefabPath(string assetPath)
        {
            if (_target != null && _target.Prefab != null && AssetDatabase.GetAssetPath(_target.Prefab) == assetPath)
            {
                return _target;
            }

            for (var i = _ancestors.Count - 1; i >= 0; i--)
            {
                var a = _ancestors[i].Data;
                if (a != null && a.Prefab != null && AssetDatabase.GetAssetPath(a.Prefab) == assetPath)
                {
                    return a;
                }
            }

            return Lookup.FindByPrefabPath(assetPath);
        }

        private void SelectGameObject(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            _selfSelectedId = go.GetInstanceID();
            Selection.activeGameObject = go;
        }

        // 範囲外(Undo で行が減った等)なら default を返す(レビュー対応 2026-09-14)。
        private static ElementFx GetFx(CanvasData owner, int index)
            => owner != null && owner.ElementEffects != null && index >= 0 && index < owner.ElementEffects.Length
                ? owner.ElementEffects[index]
                : default;

        // 範囲外なら何もしない(レビュー対応 2026-09-14)。Undo.RecordObject / SetDirty は呼び出し側が行う。
        private static void UpdateFx(CanvasData owner, int index, Func<ElementFx, ElementFx> mutate)
        {
            if (owner == null || owner.ElementEffects == null || index < 0 || index >= owner.ElementEffects.Length)
            {
                return;
            }

            owner.ElementEffects[index] = mutate(owner.ElementEffects[index]);
        }

        private VisualElement BuildPhaseRow(
            string label, string elementPath,
            System.Func<UiPresetRef> getPreset, System.Action<UiPresetRef> setPreset,
            System.Func<AssetId<UiTweenMarker>> getId, System.Action<AssetId<UiTweenMarker>> setId)
        {
            var owner = _target; // (レビュー PC-R-05) 確定時に _target が切り替わっていても、作ったときの対象に書く
            var container = new VisualElement();

            // [09] §7.1 — ラベル + PopupField + ObjectField(160px 固定) + 「✎ Tween Editor」ボタンを
            // 横一列に詰め込むため、幅 500px では折り返し(flexWrap)が無いとボタンが見切れる(2026-09-17, U-27)。
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginTop = 2 } };
            row.Add(new Label(label) { style = { width = 70 } });

            var choices = new List<string> { NoneChoice };
            foreach (var name in System.Enum.GetNames(typeof(UiPreset)))
            {
                if (name == nameof(UiPreset.None))
                {
                    continue;
                }

                choices.Add(name);
            }

            foreach (var (choiceLabel, _) in _catalogChoices)
            {
                choices.Add(choiceLabel);
            }

            var currentId = getId();
            var currentPreset = getPreset();
            var currentLabel = NoneChoice;
            if (currentId.IsValid)
            {
                var match = _catalogChoices.Find(c => c.tween != null && c.tween.Id == currentId.Value);
                currentLabel = match.tween != null ? match.label : $"(Id 0x{currentId.Value:X})";
                if (!choices.Contains(currentLabel))
                {
                    choices.Add(currentLabel);
                }
            }
            else if (currentPreset.Preset != UiPreset.None)
            {
                currentLabel = currentPreset.Preset.ToString();
            }

            var startIndex = choices.IndexOf(currentLabel);
            var popup = new PopupField<string>(choices, startIndex >= 0 ? startIndex : 0) { style = { flexGrow = 1f } };
            popup.RegisterValueChangedCallback(evt =>
            {
                if (owner == null)
                {
                    return;
                }

                Undo.RecordObject(owner, "ElementFx: 割当変更");
                var value = evt.newValue;
                if (value == NoneChoice)
                {
                    setPreset(default);
                    setId(default);
                }
                else
                {
                    var match = _catalogChoices.Find(c => c.label == value);
                    if (match.tween != null)
                    {
                        setId(new AssetId<UiTweenMarker>(match.tween.Id, AssetType.UiTween));
                        setPreset(default);
                    }
                    else if (System.Enum.TryParse<UiPreset>(value, out var preset))
                    {
                        setPreset(new UiPresetRef { Preset = preset });
                        setId(default);
                    }
                }

                EditorUtility.SetDirty(owner);
                if (owner == _target)
                {
                    RebuildElementFxAssignments();
                }
            });
            row.Add(popup);

            var objectField = new ObjectField { objectType = typeof(UiTweenData), style = { width = 160 } };
            objectField.SetValueWithoutNotify(currentId.IsValid ? FindUiTweenData(currentId.Value) : null);
            objectField.RegisterValueChangedCallback(evt =>
            {
                if (owner == null)
                {
                    return;
                }

                Undo.RecordObject(owner, "ElementFx: Tween 直接指定");
                if (evt.newValue is UiTweenData tween && tween.Id != 0)
                {
                    setId(new AssetId<UiTweenMarker>(tween.Id, AssetType.UiTween));
                    setPreset(default);
                }
                else
                {
                    setId(default);
                }

                EditorUtility.SetDirty(owner);
                if (owner == _target)
                {
                    RebuildElementFxAssignments();
                }
            });
            row.Add(objectField);

            // 4-10 レビュー対応(2026-09-12): Track の細かい編集(カーブ一覧・スプライン・Validation)は
            // UI Tween Editor 側の設備をそのまま使う(埋め込みで二重管理しない方針)。直接指定(Id)がある
            // ときだけ開ける。プリセット指定だけのときは実体の UiTweenData が無いので押せない。
            // 2026-09-12: 「確認用シーンに配置」の無関係な仮画像でしか試せない、という声を受け、
            // 開いたときにこの行が担当している実要素(elementPath)を自動でプレビュー対象にする。
            // プレビューがまだ開いていなければ先に開く(閉じているのに押しても失敗しないように)。
            var editButton = new Button(() =>
            {
                var tween = currentId.IsValid ? FindUiTweenData(currentId.Value) : null;
                if (tween == null)
                {
                    return;
                }

                if (_manager != null && !IsPreviewUsable())
                {
                    PlacePreview();
                }

                GameObject collectRoot = null;
                RectTransform elementTarget = null;
                if (TryGetPreviewPrefix(out var previewPrefix))
                {
                    collectRoot = _manager.GetGameObject(_previewHandle);
                    elementTarget = _manager.GetComponent<RectTransform>(_previewHandle, EmbeddedPaths.Combine(previewPrefix, elementPath));
                }

                var elementLabel = string.IsNullOrEmpty(elementPath) ? RootElementLabel : elementPath;
                UiTweenEditorWindow.Open(tween, collectRoot, elementTarget, elementLabel);
            })
            {
                text = "✎ Tween Editor",
                tooltip = "UI Tween Editor で開く(この要素を自動でプレビュー対象にして Track を編集・確認)。直接指定(UiTweenData)があるときだけ押せる",
            };
            editButton.SetEnabled(currentId.IsValid);
            row.Add(editButton);

            container.Add(row);
            container.Add(BuildPhasePlaybackRow(label, elementPath, getPreset, getId));

            return container;
        }

        // 2026-09-12 ユーザー要望: ElementFx の Appear/Idle/Disappear を UI Tween Editor を開かずに
        // Canvas Editor 内でそのまま再生・一時停止・停止できるように(プリセット指定でも直接指定でも可)。
        // 実要素(elementPath)を対象に実 UiTweenManager で再生するので、見た目は本番と同じになる(ADR-4)。
        private VisualElement BuildPhasePlaybackRow(
            string phase, string elementPath,
            System.Func<UiPresetRef> getPreset, System.Func<AssetId<UiTweenMarker>> getId)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 2 } };
            row.Add(new Label(string.Empty) { style = { width = 70 } });

            var widgets = new PhaseRowWidgets { ElementPath = elementPath, Phase = phase };

            widgets.PlayButton = new Button(() => PlayPhasePreview(elementPath, phase, getPreset(), getId(), selectPlayed: true))
            {
                text = "▶ 再生",
                tooltip = "この Appear/Idle/Disappear を、確認用シーンの実要素に対して再生する(未表示なら自動で「確認用シーンを開く」。対象の Prefab をプレハブモードで開いているときはステージ内の要素で再生し、終わると元へ戻す)。押すたびに再生前の状態へ戻してから再生する",
            };
            row.Add(widgets.PlayButton);

            widgets.PauseButton = new Button(() => TogglePausePhasePreview(elementPath, phase))
            {
                text = "⏸ 一時停止",
                tooltip = "その場で一時停止 / 再開する",
            };
            row.Add(widgets.PauseButton);

            widgets.StopButton = new Button(() => StopPhasePreviewAndReset(elementPath, phase))
            {
                text = "■ 停止",
                tooltip = "止めて、再生前の状態(初期位置など)へ戻す",
            };
            row.Add(widgets.StopButton);

            widgets.StatusLabel = new Label(string.Empty) { style = { marginLeft = 4, opacity = 0.8f } };
            row.Add(widgets.StatusLabel);

            _phaseRowWidgets.Add(widgets);
            UpdatePhaseRowWidgets(widgets);

            return row;
        }

        // プレハブモードで、このウィンドウの対象 CanvasData の Prefab(または、対象を埋め込んでいる親の Prefab)を
        // 開いているときのステージ(それ以外は null)。開いているのが無関係な Prefab のときは対象外(プレビュー実体側で再生する)。
        // prefix = そのステージのルートから見た対象 CanvasData のルートのパス(対象自身の Prefab なら空文字)。
        // 2026-10-03(Canvas の埋め込み): 以前は「対象 CanvasData の Prefab と完全一致」だけだった。
        private PrefabStage GetTargetStage() => GetTargetStage(out _, out _);

        private PrefabStage GetTargetStage(out string prefix) => GetTargetStage(out prefix, out _);

        // owner = そのステージの Prefab を持つ CanvasData(対象自身か、対象を埋め込んでいる親)。
        private PrefabStage GetTargetStage(out string prefix, out CanvasData owner)
        {
            prefix = string.Empty;
            owner = null;
            if (_target == null)
            {
                return null;
            }

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.prefabContentsRoot == null)
            {
                return null;
            }

            if (_target.Prefab != null && stage.assetPath == AssetDatabase.GetAssetPath(_target.Prefab))
            {
                owner = _target;
                return stage;
            }

            // 親の連なりを内側から見る(子のプレハブモードから親へ勝手に戻さない。親のプレハブモードでも再生できる)。
            for (var i = _ancestors.Count - 1; i >= 0; i--)
            {
                var ancestor = _ancestors[i].Data;
                if (ancestor != null && ancestor.Prefab != null && stage.assetPath == AssetDatabase.GetAssetPath(ancestor.Prefab))
                {
                    prefix = CanvasEmbeddedEditing.ToAncestorPath(_ancestors, i, string.Empty);
                    owner = ancestor;
                    return stage;
                }
            }

            return null;
        }

        private static RectTransform FindInStage(PrefabStage stage, string elementPath)
        {
            var root = stage.prefabContentsRoot.transform;
            var found = string.IsNullOrEmpty(elementPath) ? root : root.Find(elementPath);
            return found as RectTransform;
        }

        // 確認用プレビューが開いていて、その実体が対象(または対象を埋め込んでいる親)の Prefab なら true。
        // prefix = プレビュー実体のルートから見た対象 CanvasData のルートのパス。
        private bool TryGetPreviewPrefix(out string prefix)
        {
            prefix = string.Empty;
            if (_manager == null || !_manager.IsOpen(_previewHandle) || _previewData == null)
            {
                return false;
            }

            if (_previewData == _target)
            {
                return true;
            }

            for (var i = _ancestors.Count - 1; i >= 0; i--)
            {
                if (_ancestors[i].Data == _previewData)
                {
                    prefix = CanvasEmbeddedEditing.ToAncestorPath(_ancestors, i, string.Empty);
                    return true;
                }
            }

            return false;
        }

        private bool IsPreviewUsable() => TryGetPreviewPrefix(out _);

        // 今「表示されている」再生対象(プレハブモードならステージ内、そうでなければ確認用シーンのプレビュー実体)。
        // 表示されていなければ null。states には対応する初期状態の控えを返す。プレビューの新規配置はしない。
        // elementPath は対象 CanvasData のルート基準(親の中に埋め込まれているときは、親の実体の中の位置へ変換して探す)。
        private RectTransform FindPlaybackTarget(string elementPath, out ElementFxStateSnapshot states)
        {
            var stage = GetTargetStage(out var stagePrefix);
            if (stage != null)
            {
                states = _stageStates;
                return FindInStage(stage, EmbeddedPaths.Combine(stagePrefix, elementPath));
            }

            states = _previewStates;
            return TryGetPreviewPrefix(out var previewPrefix)
                ? _manager.GetComponent<RectTransform>(_previewHandle, EmbeddedPaths.Combine(previewPrefix, elementPath))
                : null;
        }

        // 戻り値: 実際に再生を開始したか(レビュー対応 2026-09-14。「▶ 全〜」が失敗も再生件数に数えていた)。
        // 2026-09-29: プレハブモードで対象の Prefab を開いているときは、ステージ内の実体を実 UiTweenManager で再生する
        // (プレビュー実体を置き直さない)。再生前の値を控え、再生のたびに・止めるとき・終わったとき・プレハブモードを
        // 閉じる / 保存するときに元へ戻す(Undo には積まない。プレハブに値を残さない)。
        private bool PlayPhasePreview(string elementPath, string phase, UiPresetRef preset, AssetId<UiTweenMarker> id, bool selectPlayed = false)
        {
            if (_target == null || _tweenManager == null)
            {
                return false;
            }

            elementPath ??= string.Empty; // 行 UI 側のキー(null → 空文字に正規化済み)と揃える(レビュー対応 2026-09-14)

            var stage = GetTargetStage();
            if (stage == null && _manager != null && !IsPreviewUsable())
            {
                PlacePreview();
            }

            // 「Idle を流す」がオンなら、この再生の間は Idle を止めて元の値へ戻す(終わったら UpdateIdleFlow が再開する)。
            // Idle の途中の値を再生前の状態として控えてしまわないよう、控える前に止める。
            if (stage != null)
            {
                HoldIdleFlowForPhasePreview();
            }

            var elementTarget = FindPlaybackTarget(elementPath, out var states);
            if (elementTarget == null)
            {
                if (stage == null && !IsPreviewUsable())
                {
                    return false;
                }

                _statusLabel.text = $"{phase}: 要素が見つかりません({(string.IsNullOrEmpty(elementPath) ? RootElementLabel : elementPath)})";
                return false;
            }

            // 初期状態(最初に再生する前の値)を控える。既に控えてあれば上書きしない。
            states.Capture(elementTarget);

            StopPhasePreview(elementPath, phase);
            // (レビュー対応 2026-09-14) 同じ要素で UiManager 自身の ElementFx(開いた直後の Appear や自動の Idle ループ)が
            // 走っていると同じプロパティを取り合うため、この要素の Tween を全て止めてから再生する
            // (_tweenManager はこのウィンドウ専用のプレビュー用インスタンスなので、止めてよいのはプレビューの Tween だけ)。
            // 2026-09-29: U-23 の StopAll(complete:true) は Disappear(SlideOut 等)の終端値が残って次の SlideIn の基準
            // (UiPresetFactory.Build が読む現在位置)になり、連打でずれていた。止めたあとで必ず初期状態へ戻してから
            // 組み立てることで、何度押しても 1 回目と同じ再生になる。
            _tweenManager.StopAll(elementTarget);
            states.Restore(elementTarget);

            Handle<UiTweenMarker> handle;
            if (id.IsValid)
            {
                var tween = FindUiTweenData(id.Value);
                if (tween == null)
                {
                    return false;
                }

                handle = _tweenManager.PlayData(tween, elementTarget);
            }
            else if (preset.Preset != UiPreset.None)
            {
                var count = UiPresetFactory.Build(in preset, elementTarget, _presetPlayScratch);
                if (count <= 0)
                {
                    return false;
                }

                handle = _tweenManager.PlayTracks(_presetPlayScratch, count, elementTarget);
            }
            else
            {
                return false;
            }

            _phasePreviewHandles[(elementPath, phase)] = handle;

            if (selectPlayed)
            {
                SelectForPlayback(elementTarget);
                if (stage != null)
                {
                    _statusLabel.text = $"{phase}: プレハブモードの要素で再生中(終わると元の値へ戻します)";
                }
            }

            return true;
        }

        private void SelectForPlayback(RectTransform target)
        {
            if (target != null && EditorPrefs.GetBool(SelectOnPlayPrefKey, true))
            {
                SelectGameObject(target.gameObject);
            }
        }

        // 「選択」「フォーカス」ボタンの実体。今表示している実体(プレハブモード → プレビュー実体 → Prefab アセット内の順)を
        // Selection にして Inspector に出す。focus=true なら SceneView も寄せる(アセット内の要素は SceneView に無いので Ping のみ)。
        private void SelectElement(string elementPath, bool focus)
        {
            if (_target == null)
            {
                return;
            }

            elementPath ??= string.Empty;
            var label = string.IsNullOrEmpty(elementPath) ? RootElementLabel : elementPath;
            var stage = GetTargetStage();
            var previewOpen = IsPreviewUsable();

            if (stage != null || previewOpen)
            {
                var displayed = FindPlaybackTarget(elementPath, out _);
                if (displayed == null)
                {
                    _statusLabel.text = $"要素が見つかりません({label})";
                    return;
                }

                SelectGameObject(displayed.gameObject);
                if (focus)
                {
                    PreviewPlacement.FocusRect(displayed);
                }

                _statusLabel.text = focus ? $"'{label}' を選択して SceneView を寄せました" : $"'{label}' を選択しました";
                return;
            }

            // 表示中の実体が無い: Prefab アセット内の該当要素を選択 + Ping。
            if (_target.Prefab == null)
            {
                _statusLabel.text = "Prefab を設定してください";
                return;
            }

            var assetRoot = _target.Prefab.transform;
            var inAsset = string.IsNullOrEmpty(elementPath) ? assetRoot : assetRoot.Find(elementPath);
            if (inAsset == null)
            {
                _statusLabel.text = $"要素が見つかりません({label})";
                return;
            }

            Selection.activeObject = inAsset.gameObject;
            EditorGUIUtility.PingObject(inAsset.gameObject);
            _statusLabel.text = focus
                ? $"'{label}' は表示中の実体が無いため Prefab アセットを選択しました(SceneView へ寄せるには「確認用シーンを開く」かプレハブモードで表示してください)"
                : $"'{label}' を Prefab アセット内で選択しました";
        }

        // プレハブモードで再生した値を元へ戻して控えを捨てる(実行中のトゥイーンも止める)。
        private void ReleaseStageStates()
        {
            if (_stageStates.Count == 0)
            {
                return;
            }

            if (_tweenManager != null)
            {
                _stageStates.CollectTargets(_targetScratch);
                for (var i = 0; i < _targetScratch.Count; i++)
                {
                    _tweenManager.StopAll(_targetScratch[i]);
                }

                _targetScratch.Clear();
            }

            _stageStates.RestoreAllAndClear();
        }

        // 再生が全部終わった(停止した)ら、プレハブモードの値を元へ戻す(プレハブに値を残さない)。
        private void ReleaseStageStatesIfIdle()
        {
            if (_stageStates.Count > 0 && !AnyPhasePreviewActive())
            {
                ReleaseStageStates();
            }
        }

        // 行の ▶ 再生がまだ動いているか(終わった Handle はここで掃除する)。
        private bool AnyPhasePreviewActive()
        {
            if (_phasePreviewHandles.Count == 0)
            {
                return false;
            }

            if (_tweenManager == null)
            {
                _phasePreviewHandles.Clear();
                return false;
            }

            _handleScratch.Clear();
            foreach (var kv in _phasePreviewHandles)
            {
                if (!_tweenManager.IsPlaying(kv.Value))
                {
                    _handleScratch.Add(kv.Key);
                }
            }

            for (var i = 0; i < _handleScratch.Count; i++)
            {
                _phasePreviewHandles.Remove(_handleScratch[i]);
            }

            return _phasePreviewHandles.Count > 0;
        }

        private readonly List<(string path, string phase)> _handleScratch = new();

        private void OnPrefabStageClosing(PrefabStage stage)
        {
            StopIdleFlow(); // プレハブモードを閉じる前に Idle の値を戻す(閉じる時の保存確認にも途中の値を残さない)
            ReleaseStageStates();
            _phasePreviewHandles.Clear();
        }

        private void OnPrefabSaving(GameObject prefabRoot)
        {
            // 保存の直前に Idle・再生の途中の値を元へ戻す(Prefab に途中の値を書かない)。保存後は UpdateIdleFlow が作り直して再開する。
            StopIdleFlow();
            ReleaseStageStates();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                SetIdleFlow(false); // Play Mode に入る前に Idle の値を戻し、トグルもオフにする
            }
        }

        // ── 「Idle を流す」(U-29b) ──
        //
        // 後始末の経路(どの契機でも CanvasIdleFlow.Stop() = 流す前の値へ戻してから止める):
        //   トグルをオフ / 保存の直前(prefabSaving。保存後は更新で自動再開)/ プレハブモードを閉じる(prefabStageClosing)/
        //   編集対象の切り替え(ApplyTarget)/ Play Mode に入る(ExitingEditMode。トグルもオフ)/ ウィンドウを閉じる・ドメインリロード(OnDisable)/
        //   対象の Prefab のステージでなくなった(更新の確認で検出)。
        // 編集とぶつからない対処: 選択した要素(とその祖先)の Idle だけ止めて元の値へ戻し、選択が外れたら取り直して再開する(OnSelectionChange)。
        // 行の ▶ 再生中は Idle 全体を止めて、終わったら再開する。

        private bool IdleFlowActive => _idleFlowOn && _idleFlow != null && _idleFlow.IsActive;

        // テストから呼べるよう public(ウィンドウを画面に出さず、トグルと同じ経路を検証する)。
        public void SetIdleFlow(bool on)
        {
            _idleFlowOn = on;
            _idleFlowToggle?.SetValueWithoutNotify(on);
            if (!on)
            {
                _idleHeldByPhase = false;
                StopIdleFlow();
                UpdateIdleFlowUi();
                return;
            }

            PollIdleFlow();
        }

        private void StopIdleFlow()
        {
            _idleFlow?.Stop();
            _idleBuilt = false;
        }

        private void HoldIdleFlowForPhasePreview()
        {
            if (!_idleFlowOn)
            {
                return;
            }

            StopIdleFlow();
            _idleHeldByPhase = true;
        }

        private void UpdateIdleFlow(double now)
        {
            if (_idleFlow == null)
            {
                return;
            }

            if (now - _lastIdlePoll >= IdlePollInterval)
            {
                _lastIdlePoll = now;
                if (_idleFlowOn)
                {
                    PollIdleFlow();
                }
                else
                {
                    UpdateIdleFlowUi();
                }
            }

            // 選択の変化は OnSelectionChange に加えて毎フレーム確認する(OnSelectionChange はウィンドウが非アクティブなときや
            // コード経由の選択変更で遅れることがあるため。active と本数だけの軽い比較)。
            if (_idleFlowOn && _idleFlow.IsActive)
            {
                var activeId = Selection.activeInstanceID;
                var count = Selection.count;
                if (activeId != _idleSelectionActiveId || count != _idleSelectionCount)
                {
                    _idleSelectionActiveId = activeId;
                    _idleSelectionCount = count;
                    _idleFlow.SuspendFor(Selection.transforms);
                    UpdateIdleFlowUi();
                }
            }

            // 再描画は必要最小限: 実際に動いている Idle があるときだけ、30Hz まで。
            if (_idleFlowOn && _idleFlow.HasRunning && now - _lastIdleRepaint >= IdleRepaintInterval)
            {
                _lastIdleRepaint = now;
                SceneView.RepaintAll();
            }
        }

        // いまの表示先(プレハブモードのステージ)と割り当てに合わせて Idle を流す / 流し直す / 止める。
        // 署名(ステージ + 割り当ての内容)が変わらない限り流し直さない(絞り込み入力などでアニメが途切れないように)。
        private void PollIdleFlow()
        {
            if (_idleFlow == null)
            {
                return;
            }

            var stage = GetTargetStage(out _, out var owner);
            if (stage == null || owner == null || stage.prefabContentsRoot == null)
            {
                if (_idleBuilt)
                {
                    StopIdleFlow(); // 対象の Prefab のステージでなくなった
                }

                UpdateIdleFlowUi();
                return;
            }

            if (_idleHeldByPhase)
            {
                if (AnyPhasePreviewActive() || _stageStates.Count > 0)
                {
                    UpdateIdleFlowUi();
                    return;
                }

                _idleHeldByPhase = false; // 個別再生が終わって元の値へ戻った: 再開
            }

            CanvasIdleFlow.CollectEntries(owner, Lookup, _idleEntries);
            var signature = CanvasIdleFlow.Signature(stage.GetInstanceID(), _idleEntries);
            if (_idleBuilt && signature == _idleSignature && !_idleFlow.HasDestroyedTargets)
            {
                UpdateIdleFlowUi();
                return;
            }

            _idleFlow.Stop();
            _idleFlow.Start(stage.prefabContentsRoot.transform, _idleEntries);
            _idleFlow.SuspendFor(Selection.transforms);
            _idleSignature = signature;
            _idleBuilt = true;
            UpdateIdleFlowUi();
        }

        private void UpdateIdleFlowUi()
        {
            if (_idleFlowToggle == null || _idleFlowHint == null)
            {
                return;
            }

            var stage = _target != null ? GetTargetStage() : null;
            var previewOnly = _target != null && stage == null && IsPreviewUsable();
            _idleFlowToggle.SetEnabled(!previewOnly);

            string text;
            if (_target == null)
            {
                text = string.Empty;
            }
            else if (previewOnly)
            {
                text = "確認用プレビューでは Idle は常に流れています(このトグルはプレハブモード用)";
            }
            else if (stage == null)
            {
                text = "プレハブモードで対象の Prefab(または対象を埋め込んでいる親)を開いているときに流せます";
            }
            else if (!_idleFlowOn)
            {
                text = "オンにすると、このプレハブモードの中で Idle が割り当てられた全要素の Idle を流します(止めると元の値へ戻ります)";
            }
            else if (_idleHeldByPhase)
            {
                text = "行の ▶ 再生が終わるまで Idle を止めています(終わったら再開します)";
            }
            else
            {
                var count = _idleFlow != null ? _idleFlow.Count : 0;
                var suspended = _idleFlow != null ? _idleFlow.SuspendedCount : 0;
                text = count == 0
                    ? "Idle が割り当てられた要素がありません"
                    : suspended > 0
                        ? $"● {count} 件の要素で Idle を流しています(選択した要素 {suspended} 件は止めて元の値にしています。選択を外すと再開します)"
                        : $"● {count} 件の要素で Idle を流しています(要素を選択すると、その要素だけ止まります)";
            }

            if (_idleFlowHint.text != text)
            {
                _idleFlowHint.text = text;
            }
        }

        // 行の「■ 停止」: 止めて再生前の状態へ戻す。
        private void StopPhasePreviewAndReset(string elementPath, string phase)
        {
            elementPath ??= string.Empty;
            StopPhasePreview(elementPath, phase);
            var target = FindPlaybackTarget(elementPath, out var states);
            if (target != null)
            {
                _tweenManager?.StopAll(target);
                states.Restore(target);
            }
        }

        // 実体を新しく置いた直後(まだ Tick が進む前)の値を初期状態として控える。
        private void CapturePreviewBaselines()
        {
            _previewStates.Clear();
            if (!TryGetPreviewPrefix(out var prefix))
            {
                return;
            }

            _previewStates.Capture(_manager.GetComponent<RectTransform>(_previewHandle));
            if (_target?.ElementEffects == null)
            {
                return;
            }

            foreach (var fx in _target.ElementEffects)
            {
                _previewStates.Capture(_manager.GetComponent<RectTransform>(_previewHandle, EmbeddedPaths.Combine(prefix, fx.ElementPath)));
            }
        }

        private void TogglePausePhasePreview(string elementPath, string phase)
        {
            if (_tweenManager == null || !_phasePreviewHandles.TryGetValue((elementPath, phase), out var handle) || !_tweenManager.IsPlaying(handle))
            {
                return;
            }

            _tweenManager.SetPaused(handle, !_tweenManager.IsPaused(handle));
        }

        private void StopPhasePreview(string elementPath, string phase)
        {
            if (_tweenManager != null && _phasePreviewHandles.TryGetValue((elementPath, phase), out var handle))
            {
                _tweenManager.Stop(handle);
            }

            _phasePreviewHandles.Remove((elementPath, phase));
        }

        // 2026-09-12 ユーザー要望: 登録済みの ElementFx を同じ区間(Appear/Idle/Disappear)でまとめて再生する。
        // 各要素は自分に割り当てられたプリセット/直接指定をそれぞれ再生する(何も割り当てが無い要素はスキップ)。
        private void PlayAllPhasePreview(string phase)
        {
            if (_target == null || _target.ElementEffects == null || _target.ElementEffects.Length == 0)
            {
                return;
            }

            // プレハブモードで対象の Prefab を開いているときはステージ内の実体で再生する(プレビューを置かない)。
            var stageMode = GetTargetStage() != null;
            if (!stageMode && _manager != null && !IsPreviewUsable())
            {
                PlacePreview();
            }

            // (レビュー対応 2026-09-14) 開けなかったときに要素ごとに PlacePreview(RemovePreview/OpenData)を繰り返していた。
            // 1 回で打ち切る。
            if (!stageMode && !IsPreviewUsable())
            {
                _statusLabel.text = $"{phase}: プレビューを開けませんでした";
                return;
            }

            var played = 0;
            var assigned = 0;
            foreach (var fx in _target.ElementEffects)
            {
                var (preset, id) = GetPhaseValue(fx, phase);
                if (!id.IsValid && preset.Preset == UiPreset.None)
                {
                    continue;
                }

                assigned++;
                if (PlayPhasePreview(fx.ElementPath, phase, preset, id))
                {
                    played++;
                }
            }

            _statusLabel.text = assigned == 0
                ? $"{phase} が割り当てられた要素がありません"
                : played == assigned
                    ? $"{phase} を {played} 件まとめて再生しました"
                    : $"{phase} を {played} / {assigned} 件再生しました(要素または Tween が見つからない行はスキップ)";
        }

        private static (UiPresetRef preset, AssetId<UiTweenMarker> id) GetPhaseValue(ElementFx fx, string phase) => phase switch
        {
            "Appear" => (fx.AppearPreset, fx.Appear),
            "Idle" => (fx.IdlePreset, fx.Idle),
            "Disappear" => (fx.DisappearPreset, fx.Disappear),
            _ => (default, default),
        };

        private void StopAllPhasePreview()
        {
            if (_tweenManager != null)
            {
                foreach (var handle in _phasePreviewHandles.Values)
                {
                    _tweenManager.Stop(handle);
                }
            }

            _phasePreviewHandles.Clear();

            // 止めた要素を再生前の状態へ戻す(プレビュー実体は控えを残し、プレハブステージは戻して捨てる)。
            if (_tweenManager != null)
            {
                _previewStates.CollectTargets(_targetScratch);
                for (var i = 0; i < _targetScratch.Count; i++)
                {
                    _tweenManager.StopAll(_targetScratch[i]);
                }

                _targetScratch.Clear();
            }

            _previewStates.RestoreAll();
            ReleaseStageStates();
            _statusLabel.text = "すべて停止しました(再生前の状態へ戻しました)";
        }

        // OnEditorUpdate から毎フレーム呼ぶ(AnimEditorWindow の状態ラベル更新と同じ方針)。行数分だけ
        // 軽い問い合わせ(IsPlaying/IsPaused、いずれも Dictionary 参照)をするだけなので許容範囲。
        private void RefreshPhaseRowStatuses()
        {
            for (var i = 0; i < _phaseRowWidgets.Count; i++)
            {
                UpdatePhaseRowWidgets(_phaseRowWidgets[i]);
            }
        }

        private void UpdatePhaseRowWidgets(PhaseRowWidgets widgets)
        {
            var key = (widgets.ElementPath, widgets.Phase);
            var handle = Handle<UiTweenMarker>.Invalid;
            var playing = _tweenManager != null && _phasePreviewHandles.TryGetValue(key, out handle) && _tweenManager.IsPlaying(handle);
            if (!playing)
            {
                _phasePreviewHandles.Remove(key);
            }

            var paused = playing && _tweenManager.IsPaused(handle);
            widgets.PauseButton.SetEnabled(playing);
            widgets.PauseButton.text = paused ? "▶ 再開" : "⏸ 一時停止";
            widgets.StopButton.SetEnabled(playing);

            var status = !playing ? "■ 停止中" : paused ? "⏸ 一時停止" : "● 再生中";
            if (widgets.StatusLabel.text != status)
            {
                widgets.StatusLabel.text = status;
            }
        }

        // (レビュー対応 2026-09-14) キャッシュ(_tweenLookup)に生きた一致があればそれを返し、無ければ 1 回だけ全走査して
        // 作り直す(アセットの削除・Id の変更は「キャッシュの値が null / Id 不一致」で検出される)。
        private UiTweenData FindUiTweenData(ulong id)
        {
            if (id == 0)
            {
                return null;
            }

            if (_tweenLookup.TryGetValue(id, out var cached) && cached != null && cached.Id == id)
            {
                return cached;
            }

            _tweenLookup.Clear();
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(UiTweenData)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UiTweenData>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.Id != 0)
                {
                    _tweenLookup.TryAdd(asset.Id, asset); // 同じ Id が複数あるときは従来どおり最初に見つかったもの
                }
            }

            return _tweenLookup.TryGetValue(id, out cached) ? cached : null;
        }

        // 4-10:「この要素の設定を他の要素へコピー」。実体は CanvasElementFxCollector.CopyPhases(配列操作のみ切り出し済み)。
        private void CopyRowToOthers(int index)
        {
            if (_target == null || _target.ElementEffects == null)
            {
                return;
            }

            Undo.RecordObject(_target, "ElementFx: 設定を他の要素へコピー");
            var rows = _target.ElementEffects;
            CanvasElementFxCollector.CopyPhases(ref rows, index);
            _target.ElementEffects = rows;
            EditorUtility.SetDirty(_target);
            RebuildElementFxAssignments();
            _statusLabel.text = "設定を他の要素へコピーしました";
        }

        // 4-9: Prefab 内の全 UiButton へ、選択中のプリセットを AppearPreset として一括設定する(行が無ければ追加する)。
        private void ApplyBulkPresetToButtons()
        {
            if (_target == null || _target.Prefab == null)
            {
                _statusLabel.text = "Prefab を設定してください";
                return;
            }

            var merged = CanvasElementFxCollector.ApplyPresetToButtons(_target.Prefab, _target.ElementEffects, _bulkPreset, CanvasEmbeddedEditing.RegisteredRoots(_target));

            Undo.RecordObject(_target, "Apply Preset To Buttons");
            _target.ElementEffects = merged;
            EditorUtility.SetDirty(_target);

            SetTarget(_target);
            _statusLabel.text = $"全ボタンの AppearPreset に {_bulkPreset} を設定しました";
        }

        // ── ナビゲーションのノードグラフ + パッド操作シミュレーション(4-3) ──

        private void BuildNavigationGraphSection()
        {
            _graphFoldout = new Foldout { text = "Navigation グラフ", value = true, style = { marginTop = 8 } };
            _root.Add(_graphFoldout);

            var toolRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };
            toolRow.Add(new Button(() => { _graphView.ClearManualLayout(); RebuildGraph(); }) { text = "自動レイアウトを更新", tooltip = "ドラッグで動かしたノード位置・追加した Reroute point を破棄し、Prefab のレイアウトどおりに戻す" });
            toolRow.Add(new Button(DetectUnreachable) { text = "到達不能を検出" });
            toolRow.Add(new Button(ReportUnlinked) { text = "未配線を自動リンク" });
            _graphFoldout.Add(toolRow);

            _graphFoldout.Add(new Label("パン: 中ドラッグ / Alt+左ドラッグ　ズーム: ホイール　リンク作成: ポートからドラッグ　配線を切る: Ctrl+左ドラッグでなぞる　Reroute point 追加: 配線をダブルクリック(移動はドラッグ、削除は右クリック)") { style = { opacity = 0.7f, whiteSpace = WhiteSpace.Normal, marginBottom = 2 } });

            var heightSlider = new SliderInt("表示高さ", 320, 900) { value = 320, style = { marginBottom = 4 } };
            _graphView = new NavigationGraphView { style = { height = 320, minHeight = 320 } };
            heightSlider.RegisterValueChangedCallback(evt => _graphView.style.height = evt.newValue);
            _graphFoldout.Add(heightSlider);
            _graphFoldout.Add(_graphView);

            _graphView.OnSetLink = (from, dir, to) => ApplyNavEdit(() => NavigationGraph.SetLink(_target, from, dir, to), "リンクを設定");
            _graphView.OnClearLink = (from, dir) => ApplyNavEdit(() => NavigationGraph.ClearLink(_target, from, dir), "リンクを削除");
            _graphView.OnClearAllLinks = from => ApplyNavEdit(() => NavigationGraph.ClearAllLinks(_target, from), "リンクを全て削除");
            _graphView.OnSetFirstSelected = path => ApplyNavEdit(() => _target.FirstSelected = path, "FirstSelected を設定");
            _graphView.OnCutLinks = edges => ApplyNavEdit(() =>
            {
                foreach (var edge in edges)
                {
                    NavigationGraph.ClearLink(_target, edge.From, edge.Direction);
                }
            }, "配線を切る");
            // ドラッグで動かしたノード位置・Reroute point はユーザー要望により例外的に CanvasData へ保存する
            // (ランタイムの動作には使わないエディタ表示専用データ。[07_canvas_prefab.md] 2026-09-12 追記)。
            // グラフ形状は変わらないので ApplyNavEdit(RebuildGraph を伴う)は使わず、Undo + SetDirty だけ行う。
            _graphView.OnLayoutChanged = () =>
            {
                if (_target == null)
                {
                    return;
                }

                Undo.RecordObject(_target, "グラフレイアウトを変更");
                _target.NavigationNodeLayout = _graphView.ExportNodeLayout();
                _target.NavigationEdgeWaypoints = _graphView.ExportEdgeWaypoints();
                EditorUtility.SetDirty(_target);
            };

            _unreachableContainer = new VisualElement { style = { marginTop = 4 } };
            _graphFoldout.Add(_unreachableContainer);

            _graphFoldout.Add(new Label("パッド操作シミュレーション") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            _padRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            _padRow.Add(new Button(() => SimulateMove(Vector2.up)) { text = "▲" });
            _padRow.Add(new Button(() => SimulateMove(Vector2.down)) { text = "▼" });
            _padRow.Add(new Button(() => SimulateMove(Vector2.left)) { text = "◀" });
            _padRow.Add(new Button(() => SimulateMove(Vector2.right)) { text = "▶" });
            _padRow.Add(new Button(SimulateSubmit) { text = "決定" });
            _graphFoldout.Add(_padRow);

            _focusLabel = new Label("フォーカス: (未確認)") { style = { marginTop = 2 } };
            _graphFoldout.Add(_focusLabel);

            UpdatePadEnabled();
        }

        // Undo.RecordObject + SetDirty + serializedObject.Update() で包んでからグラフ/検証を再構築する。
        private void ApplyNavEdit(Action mutate, string undoLabel)
        {
            if (_target == null)
            {
                return;
            }

            Undo.RecordObject(_target, undoLabel);
            mutate();
            EditorUtility.SetDirty(_target);
            _inspectorContainer.Q<InspectorElement>()?.Bind(new SerializedObject(_target));
            RefreshValidation();
            RebuildGraph();
        }

        private void RebuildGraph()
        {
            if (_graphView == null)
            {
                return;
            }

            // 保存済みのノード位置/Reroute point を読み込む(Undo/Redo で配列側が変わった場合もここで追従する)。
            _graphView.LoadLayout(_target?.NavigationNodeLayout, _target?.NavigationEdgeWaypoints);

            if (_target == null || _target.Prefab == null)
            {
                _graphView.SetGraph(new NavigationGraph(), string.Empty, null);
                _unreachableContainer?.Clear();
                UpdatePadEnabled();
                return;
            }

            var graph = NavigationGraph.Build(_target, _target.Prefab);
            _graphView.SetGraph(graph, _target.FirstSelected, _target.Prefab);
            _graphView.SetFocusedPath(_simFocusPath);
            UpdateFocusLabel();
            UpdatePadEnabled();
        }

        private void DetectUnreachable()
        {
            if (_unreachableContainer == null)
            {
                return;
            }

            _unreachableContainer.Clear();
            if (_target == null || _target.Prefab == null)
            {
                return;
            }

            var unreachable = NavigationGraph.Build(_target, _target.Prefab).Unreachable(_target.FirstSelected);
            if (unreachable.Count == 0)
            {
                _unreachableContainer.Add(new Label("到達不能な要素はありません。") { style = { opacity = 0.7f } });
                return;
            }

            _unreachableContainer.Add(new HelpBox($"到達不能な要素が {unreachable.Count} 件あります(赤枠のノード):", HelpBoxMessageType.Warning));
            foreach (var path in unreachable)
            {
                _unreachableContainer.Add(new Label("・" + (string.IsNullOrEmpty(path) ? RootElementLabel : path)));
            }
        }

        // 「未配線を自動リンク」: Navigation に登録されているが 4 方向とも空(Unity の自動ナビゲーションのまま)の
        // 要素を一覧するだけで、データは書き換えない(Automatic のままにする方針を守る)。
        private void ReportUnlinked()
        {
            if (_unreachableContainer == null || _target == null)
            {
                return;
            }

            _unreachableContainer.Clear();
            var unlinked = new List<string>();
            if (_target.Navigation != null)
            {
                foreach (var node in _target.Navigation)
                {
                    if (string.IsNullOrEmpty(node.Up) && string.IsNullOrEmpty(node.Down) && string.IsNullOrEmpty(node.Left) && string.IsNullOrEmpty(node.Right))
                    {
                        unlinked.Add(node.Element);
                    }
                }
            }

            if (unlinked.Count == 0)
            {
                _unreachableContainer.Add(new Label("未配線(Automatic のまま)の要素はありません。") { style = { opacity = 0.7f } });
                return;
            }

            _unreachableContainer.Add(new HelpBox($"未配線(Automatic のまま)の要素が {unlinked.Count} 件あります。Unity の自動ナビゲーションのまま動作します:", HelpBoxMessageType.Info));
            foreach (var path in unlinked)
            {
                _unreachableContainer.Add(new Label("・" + (string.IsNullOrEmpty(path) ? RootElementLabel : path)));
            }
        }

        private void UpdatePadEnabled()
        {
            var enabled = _target != null && _manager != null && _manager.IsOpen(_previewHandle) && _previewData == _target;
            if (_padRow != null)
            {
                _padRow.SetEnabled(enabled);
            }
        }

        private void SimulateMove(Vector2 dir)
        {
            if (_target == null || _manager == null || !_manager.IsOpen(_previewHandle))
            {
                return;
            }

            if (string.IsNullOrEmpty(_simFocusPath))
            {
                _simFocusPath = _target.FirstSelected;
            }

            if (UnityEngine.EventSystems.EventSystem.current != null && _manager.MoveFocus(dir))
            {
                var go = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
                if (go != null)
                {
                    _simFocusPath = ComputeRelativePath(go.transform);
                }
            }
            else
            {
                _manager.MoveFocusFrom(_previewHandle, _simFocusPath, dir, out var next);
                _simFocusPath = next;
            }

            _graphView.SetFocusedPath(_simFocusPath);
            UpdateFocusLabel();
        }

        private void SimulateSubmit()
        {
            if (_target == null || _manager == null || !_manager.IsOpen(_previewHandle) || string.IsNullOrEmpty(_simFocusPath))
            {
                return;
            }

            var button = _manager.GetComponent<UiButton>(_previewHandle, _simFocusPath);
            if (button != null)
            {
                button.SimulateClick();
                return;
            }

            var uguiButton = _manager.GetComponent<UnityEngine.UI.Selectable>(_previewHandle, _simFocusPath);
            (uguiButton as UnityEngine.EventSystems.ISubmitHandler)?.OnSubmit(new UnityEngine.EventSystems.BaseEventData(UnityEngine.EventSystems.EventSystem.current));
        }

        private string ComputeRelativePath(Transform target)
        {
            var rootGo = _manager.GetGameObject(_previewHandle);
            if (rootGo == null)
            {
                return _simFocusPath;
            }

            return TransformPath.GetRelative(rootGo.transform, target); // 共通ヘルパーへ集約(レビュー対応 2026-09-14)
        }

        private void UpdateFocusLabel()
        {
            if (_focusLabel == null)
            {
                return;
            }

            _focusLabel.text = string.IsNullOrEmpty(_simFocusPath)
                ? "フォーカス: (未確認)"
                : $"フォーカス: {_simFocusPath}";
        }

        private void EnsurePreviewRoot()
        {
            if (_previewRoot != null)
            {
                return;
            }

            _previewRoot = EditorPreviewRoots.CreateRoot(PreviewRootName); // DontSave(レビュー対応 2026-09-14)
            StageUtility.PlaceGameObjectInCurrentStage(_previewRoot);
            _pool.SetInstanceParent(_previewRoot.transform);
        }

        // 2026-09-12: 「確認用シーンを開く」ボタンの実体。専用シーンへの切り替えと配置を 1 手で行う
        // (切り替えた直後に必ず置きたいだけなので、2 ボタンに分ける意味が無かった)。
        // (レビュー対応 2026-09-14) 表示中に押すと、シーンの読み直しで実体だけが消え UiManager の _stack / _instances に
        // 古い CanvasInstance が残っていた。先に RemovePreview で Manager 側を片付ける。また既に確認用シーンを
        // 開いているときはシーンを開き直さない(保存確認ダイアログや読み直しが無駄なため。表示だけやり直す)。
        // 2026-09-17(U-5): 「既に確認用シーンを開いているなら開き直さない」判定は
        // CanvasPreviewSceneSetup.TryOpenOrCreate に集約した。ここは mode の分岐だけを見る。
        private void OpenPreviewSceneAndPlace(PreviewPlaceMode mode)
        {
            RemovePreview();
            if (!PreviewPlacement.PrepareScene(mode, CanvasPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            if (PreviewPlacement.IsPersistent(mode))
            {
                if (_target == null)
                {
                    _statusLabel.text = "CanvasData を選択してください";
                    return;
                }

                // 本配置は UiManager が追跡しない実体(Prefab リンク付き)にする。
                var placed = PreviewPlacement.PlacePrefabPersistent(_target.Prefab, Vector3.zero, Quaternion.identity);
                _statusLabel.text = placed != null
                    ? "このシーンに本配置しました(シーンを保存すると残ります。プレビュー表示ではありません)"
                    : "本配置に失敗しました(CanvasData に Prefab がありません)";
                return;
            }

            PlacePreview();
        }

        private void PlacePreview()
        {
            if (_target == null)
            {
                _statusLabel.text = "CanvasData を選択してください";
                return;
            }

            RemovePreview();
            EnsurePreviewRoot();
            EditorAnchorRegistry.Refresh(_registry);

            // 2026-10-03(Canvas の埋め込み): 子を編集中は、親(連なりの最外側)を Open する。UiManager が親の中の
            // 埋め込み実体に子の ElementFx・配線を適用するので、本番と同じ見え方で子のパスの再生・選択ができる。
            var viewData = ViewData;
            _previewHandle = _manager.OpenData(viewData);
            _previewData = _manager.IsOpen(_previewHandle) ? viewData : null;
            _statusLabel.text = _manager.IsOpen(_previewHandle) ? "プレビュー表示中" : "表示に失敗しました";
            _simFocusPath = _target.FirstSelected;
            UpdatePadEnabled();
            UpdateFocusLabel();
            _graphView?.SetFocusedPath(_simFocusPath);

            if (_manager.IsOpen(_previewHandle))
            {
                // パッド操作シミュレーション/ノードグラフ/ElementFx 割当がすぐ使えるよう、プレビューを開いたら
                // Selectable と要素(Image/UiButton/パネル)を両方自動収集する(どちらも既存の行を保持する
                // CollectMerged 経由なので、何度押しても安全)。
                // (レビュー対応 2026-09-14) 差分が無ければ書き込まない(Undo を積まない・Dirty にしない)。
                var changed = CollectSelectables();
                changed |= CollectElementFx();
                CapturePreviewBaselines();
                if (!changed)
                {
                    _statusLabel.text = "プレビュー表示中";
                }

                // U-5(2026-09-17): 配置場所が SceneView のカメラから遠いと何も映らないため、必ず寄せる。
                PreviewPlacement.Focus(_previewRoot);
            }

            SceneView.RepaintAll();
        }

        private void RemovePreview()
        {
            // (レビュー対応 2026-09-14) 以前は _phasePreviewHandles をクリアするだけで止めていなかった。Canvas 実体は
            // UI Root のレイヤー下に残り、プールに戻っても SetActive(false) されるだけなので、Idle ループ等が非表示の
            // 要素上で走り続け、開き直した後の Appear とぶつかっていた。
            // 1) 行ごとの直接再生を止める → 2) UiManager.StopAll(自身の ElementFx は Appear を終端値で完了させて閉じる)
            // → 3) 残りをこのウィンドウ専用の _tweenManager ごと止める、の順(2 を先にすると行の再生の途中値が残るため)。
            if (_tweenManager != null)
            {
                foreach (var handle in _phasePreviewHandles.Values)
                {
                    _tweenManager.Stop(handle);
                }
            }

            _phasePreviewHandles.Clear();

            // 実体を失った(シーン切り替え等)インスタンスも片付けるため、IsOpen に関係なく呼ぶ(開いていなければ no-op)。
            _manager?.StopAll(DDrive.Foundation.Manager.StopReason.Manual);
            _tweenManager?.StopAll(DDrive.Foundation.Manager.StopReason.Manual);

            _previewHandle = Handle<CanvasMarker>.Invalid;
            _previewData = null;
            _previewStates.Clear(); // 実体ごと破棄するので戻さず捨てる

            if (_previewRoot != null)
            {
                DestroyImmediate(_previewRoot);
            }

            _previewRoot = null;

            if (_statusLabel != null && _target != null)
            {
                _statusLabel.text = "「確認用シーンを開く」で確認できます";
            }

            _simFocusPath = null;
            UpdatePadEnabled();
            UpdateFocusLabel();
            _graphView?.SetFocusedPath(null);

            SceneView.RepaintAll();
        }

        // ── Validation ──

        private void RefreshValidation()
        {
            if (_validationFoldout == null)
            {
                return;
            }

            _validationFoldout.Clear();
            if (_target == null)
            {
                return;
            }

            var any = false;
            var validationContext = new ValidationContext(new List<AssetDataBase> { _target });
            var results = new List<ValidationResult>(new CanvasDataValidator().Validate(_target, validationContext));
            results.AddRange(new CanvasEmbeddedValidator().Validate(_target, validationContext));
            foreach (var result in results)
            {
                any = true;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                row.Add(new HelpBox(result.Message, ToBoxType(result.Severity)) { style = { flexGrow = 1 } });
                if (result.FixAction != null)
                {
                    var fixAction = result.FixAction;
                    row.Add(new Button(() =>
                    {
                        // Codex レビュー対応(2026-09-11): Undo.RecordObject 無しで fixAction が _target を
                        // 書き換えていたため、Ctrl+Z で元に戻せなかった([CLAUDE.md] #5)。
                        Undo.RecordObject(_target, "Canvas Validation 修正");
                        fixAction();
                        EditorUtility.SetDirty(_target);
                        RefreshValidation();
                    })
                    { text = "修正" });
                }

                _validationFoldout.Add(row);
            }

            if (!any)
            {
                _validationFoldout.Add(new Label("Validation に問題はありません。") { style = { opacity = 0.6f } });
            }
        }

        private static HelpBoxMessageType ToBoxType(ValidationSeverity severity) => severity switch
        {
            ValidationSeverity.Error => HelpBoxMessageType.Error,
            ValidationSeverity.Warning => HelpBoxMessageType.Warning,
            _ => HelpBoxMessageType.Info,
        };
    }
}
