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
    public sealed class CanvasEditorWindow : EditorWindow
    {
        private const string NoneChoice = "なし";
        // (レビュー対応 2026-09-14) 生の new GameObject をやめ EditorPreviewRoots で生成し、OnEnable で同名の残骸を掃除する。
        private const string PreviewRootName = "[D-Drive] Canvas Preview";
        // (レビュー対応 2026-09-14) "(ルート)" の直書きが 5 か所あったので UI Tween Editor と共有の定数にする。
        private const string RootElementLabel = UiTweenEditorWindow.RootElementLabel;

        private CanvasData _target;
        private bool _lockTarget;
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

            // (レビュー対応 2026-09-14) 前回閉じ損ねた・ドメインリロードで参照を失ったプレビュールートの残骸を消す。
            EditorPreviewRoots.DestroyAll(PreviewRootName);
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            EditorApplication.update -= OnEditorUpdate;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
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
        }

        private void OnSelectionChange()
        {
            if (!_lockTarget && Selection.activeObject is CanvasData data && data != _target)
            {
                SetTarget(data);
            }
        }

        // 4-3: NavNode の編集(SetLink/ClearLink 等)は Undo.RecordObject で包んでいるため、Undo/Redo が
        // 走ったらグラフを作り直す(SerializedObject 側は Bind 済みなので自動で追従する)。
        // (レビュー対応 2026-09-14) グラフだけ作り直していたため、Undo で ElementFx の行が減ると古い行 UI の
        // ▶ / プリセット選択が範囲外の index を引いていた。ElementFx 割当と Validation も作り直す。
        private void OnUndoRedoPerformed()
        {
            RebuildGraph();
            RebuildElementFxAssignments();
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
            lockToggle.RegisterValueChangedCallback(evt => _lockTarget = evt.newValue);
            toolbar.Add(lockToggle);
            toolbar.Add(DDrive.Editor.Inspector.NewAssetToolbarButton.CreateToolbarButton(typeof(CanvasEditorWindow)));
            _root.Add(toolbar);

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

            _elementFxFoldout = new Foldout { text = "ElementFx 割当(Appear / Idle / Disappear)", value = true, style = { marginTop = 8 } };
            _root.Add(_elementFxFoldout);
            _elementFxFoldout.Add(new HelpBox("各要素の行でプリセット・プロジェクト独自カタログ([Catalog] 名前)・UiTweenData 直接指定のいずれかを選べます。", HelpBoxMessageType.Info));

            // 2026-09-12 ユーザー要望: 登録済みの ElementFx を 1 行ずつ「▶ 再生」するのは数が多いと手間なので、
            // 同じ区間(Appear/Idle/Disappear)を全要素まとめて再生できるようにする(各要素は自分に割り当てられた
            // プリセット/直接指定をそれぞれ再生する。実 UiTweenManager 経由で ADR-4 のまま)。
            var batchPlayRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };
            batchPlayRow.Add(new Button(() => PlayAllPhasePreview("Appear")) { text = "▶ 全 Appear", tooltip = "登録済みの全要素の Appear を、それぞれに割り当てられた演出でまとめて再生する" });
            batchPlayRow.Add(new Button(() => PlayAllPhasePreview("Idle")) { text = "▶ 全 Idle", tooltip = "登録済みの全要素の Idle をまとめて再生する" });
            batchPlayRow.Add(new Button(() => PlayAllPhasePreview("Disappear")) { text = "▶ 全 Disappear", tooltip = "登録済みの全要素の Disappear をまとめて再生する" });
            batchPlayRow.Add(new Button(StopAllPhasePreview) { text = "■ 全て停止", tooltip = "再生中の ElementFx プレビューをまとめて止める(最終状態には進めない)" });
            _elementFxFoldout.Add(batchPlayRow);

            _elementFxContainer = new VisualElement();
            _elementFxFoldout.Add(_elementFxContainer);

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
                // ElementFx 直接再生の Handle は (ElementPath, Phase) 文字列だけがキーなので、別の CanvasData に
                // 切り替えると偶然同じパスの行が「再生中」と誤判定されうる。対象を切り替えたら破棄しておく
                // (再生自体はプレビューの実体ごと RemovePreview 側で止まるので、ここは辞書のクリアのみでよい)。
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
                _phaseRowWidgets.Clear(); // 消した行の UI を毎フレーム更新し続けないように(レビュー対応 2026-09-14)
                return;
            }

            var so = new SerializedObject(target);
            _inspectorContainer.Add(new InspectorElement(so));
            _inspectorContainer.Bind(so);

            _statusLabel.text = _manager != null && _manager.IsOpen(_previewHandle) ? "プレビュー表示中" : "「確認用シーンを開く」で確認できます";
            RefreshValidation();
            RebuildElementFxAssignments();
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

        // 4-9: Graphic(Image 等)/UiInteractable(UiButton 等)/パネル(RectTransform+Graphic)を持つパスを
        // ElementEffects に追加する(既存の行・値は保持する)。戻り値: データを書き換えたか(レビュー対応 2026-09-14)。
        private bool CollectElementFx()
        {
            if (_target == null || _target.Prefab == null)
            {
                _statusLabel.text = "Prefab を設定してください";
                return false;
            }

            var merged = CanvasElementFxCollector.CollectMerged(_target.Prefab, _target.ElementEffects);
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
        private void RebuildElementFxAssignments()
        {
            if (_elementFxContainer == null)
            {
                return;
            }

            _elementFxContainer.Clear();
            // (レビュー対応 2026-09-14) 早期 return の前に消す(以前は行が 0 になっても古い行の UI を毎フレーム更新していた)。
            _phaseRowWidgets.Clear();
            if (_target == null || _target.ElementEffects == null || _target.ElementEffects.Length == 0)
            {
                _elementFxContainer.Add(new Label("ElementFx がありません(上の「要素を自動収集」で追加してください)") { style = { opacity = 0.7f } });
                return;
            }

            _catalogChoices.Clear();
            foreach (var (name, tween) in UiPresetCatalogUtility.Collect())
            {
                _catalogChoices.Add(($"[Catalog] {name}", tween));
            }

            for (var i = 0; i < _target.ElementEffects.Length; i++)
            {
                var index = i;
                var fx = _target.ElementEffects[i];
                // (レビュー対応 2026-09-14) ElementPath が null の行で Dictionary<string, bool> が ArgumentNullException になっていた。
                var elementPath = fx.ElementPath ?? string.Empty;

                // 2026-09-12 ユーザー要望: 要素数が多いと縦に長くなりすぎるので、各要素を折りたためるようにする
                // (デフォルトは折りたたみ)。展開状態は ElementPath をキーに保持し、ドロップダウン変更などで
                // 再構築が起きても(その行自身の変更でなければ)開閉が飛ばないようにする。
                var expanded = _elementFxExpanded.TryGetValue(elementPath, out var wasExpanded) && wasExpanded;
                var box = new Foldout { text = string.IsNullOrEmpty(elementPath) ? RootElementLabel : elementPath, value = expanded, style = { marginBottom = 6 } };
                box.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target == box)
                    {
                        _elementFxExpanded[elementPath] = evt.newValue;
                    }
                });

                // (レビュー対応 2026-09-14) getter/setter は GetFx/UpdateFx 経由で範囲チェックする(Undo で行が減った後に
                // 古い行 UI から呼ばれても例外にしない)。
                box.Add(BuildPhaseRow(
                    "Appear",
                    elementPath,
                    () => GetFx(index).AppearPreset,
                    v => UpdateFx(index, e => { e.AppearPreset = v; return e; }),
                    () => GetFx(index).Appear,
                    v => UpdateFx(index, e => { e.Appear = v; return e; })));

                box.Add(BuildPhaseRow(
                    "Idle",
                    elementPath,
                    () => GetFx(index).IdlePreset,
                    v => UpdateFx(index, e => { e.IdlePreset = v; return e; }),
                    () => GetFx(index).Idle,
                    v => UpdateFx(index, e => { e.Idle = v; return e; })));

                box.Add(BuildPhaseRow(
                    "Disappear",
                    elementPath,
                    () => GetFx(index).DisappearPreset,
                    v => UpdateFx(index, e => { e.DisappearPreset = v; return e; }),
                    () => GetFx(index).Disappear,
                    v => UpdateFx(index, e => { e.Disappear = v; return e; })));

                box.Add(new Button(() => CopyRowToOthers(index)) { text = "この要素の設定を他の要素へコピー", style = { marginTop = 4 } });

                _elementFxContainer.Add(box);
            }
        }

        // 範囲外(Undo で行が減った等)なら default を返す(レビュー対応 2026-09-14)。
        private ElementFx GetFx(int index)
            => _target != null && _target.ElementEffects != null && index >= 0 && index < _target.ElementEffects.Length
                ? _target.ElementEffects[index]
                : default;

        // 範囲外なら何もしない(レビュー対応 2026-09-14)。Undo.RecordObject / SetDirty は呼び出し側が行う。
        private void UpdateFx(int index, Func<ElementFx, ElementFx> mutate)
        {
            if (_target == null || _target.ElementEffects == null || index < 0 || index >= _target.ElementEffects.Length)
            {
                return;
            }

            _target.ElementEffects[index] = mutate(_target.ElementEffects[index]);
        }

        private VisualElement BuildPhaseRow(
            string label, string elementPath,
            System.Func<UiPresetRef> getPreset, System.Action<UiPresetRef> setPreset,
            System.Func<AssetId<UiTweenMarker>> getId, System.Action<AssetId<UiTweenMarker>> setId)
        {
            var container = new VisualElement();

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 2 } };
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
                if (_target == null)
                {
                    return;
                }

                Undo.RecordObject(_target, "ElementFx: 割当変更");
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

                EditorUtility.SetDirty(_target);
                RebuildElementFxAssignments();
            });
            row.Add(popup);

            var objectField = new ObjectField { objectType = typeof(UiTweenData), style = { width = 160 } };
            objectField.SetValueWithoutNotify(currentId.IsValid ? FindUiTweenData(currentId.Value) : null);
            objectField.RegisterValueChangedCallback(evt =>
            {
                if (_target == null)
                {
                    return;
                }

                Undo.RecordObject(_target, "ElementFx: Tween 直接指定");
                if (evt.newValue is UiTweenData tween && tween.Id != 0)
                {
                    setId(new AssetId<UiTweenMarker>(tween.Id, AssetType.UiTween));
                    setPreset(default);
                }
                else
                {
                    setId(default);
                }

                EditorUtility.SetDirty(_target);
                RebuildElementFxAssignments();
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

                if (_manager != null && !_manager.IsOpen(_previewHandle))
                {
                    PlacePreview();
                }

                GameObject collectRoot = null;
                RectTransform elementTarget = null;
                if (_manager != null && _manager.IsOpen(_previewHandle))
                {
                    collectRoot = _manager.GetGameObject(_previewHandle);
                    elementTarget = _manager.GetComponent<RectTransform>(_previewHandle, elementPath);
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

            widgets.PlayButton = new Button(() => PlayPhasePreview(elementPath, phase, getPreset(), getId()))
            {
                text = "▶ 再生",
                tooltip = "この Appear/Idle/Disappear を、確認用シーンの実要素に対して再生する(未表示なら自動で「確認用シーンを開く」)",
            };
            row.Add(widgets.PlayButton);

            widgets.PauseButton = new Button(() => TogglePausePhasePreview(elementPath, phase))
            {
                text = "⏸ 一時停止",
                tooltip = "その場で一時停止 / 再開する",
            };
            row.Add(widgets.PauseButton);

            widgets.StopButton = new Button(() => StopPhasePreview(elementPath, phase))
            {
                text = "■ 停止",
                tooltip = "途中で止める(最終状態には進めない)",
            };
            row.Add(widgets.StopButton);

            widgets.StatusLabel = new Label(string.Empty) { style = { marginLeft = 4, opacity = 0.8f } };
            row.Add(widgets.StatusLabel);

            _phaseRowWidgets.Add(widgets);
            UpdatePhaseRowWidgets(widgets);

            return row;
        }

        // 戻り値: 実際に再生を開始したか(レビュー対応 2026-09-14。「▶ 全〜」が失敗も再生件数に数えていた)。
        private bool PlayPhasePreview(string elementPath, string phase, UiPresetRef preset, AssetId<UiTweenMarker> id)
        {
            if (_target == null || _tweenManager == null)
            {
                return false;
            }

            elementPath ??= string.Empty; // 行 UI 側のキー(null → 空文字に正規化済み)と揃える(レビュー対応 2026-09-14)

            if (_manager != null && !_manager.IsOpen(_previewHandle))
            {
                PlacePreview();
            }

            if (_manager == null || !_manager.IsOpen(_previewHandle))
            {
                return false;
            }

            var elementTarget = _manager.GetComponent<RectTransform>(_previewHandle, elementPath);
            if (elementTarget == null)
            {
                _statusLabel.text = $"{phase}: 要素が見つかりません({(string.IsNullOrEmpty(elementPath) ? RootElementLabel : elementPath)})";
                return false;
            }

            StopPhasePreview(elementPath, phase);
            // (レビュー対応 2026-09-14) 同じ要素で UiManager 自身の ElementFx(開いた直後の Appear や自動の Idle ループ)が
            // 走っていると同じプロパティを取り合うため、この要素の Tween を全て止めてから再生する
            // (_tweenManager はこのウィンドウ専用のプレビュー用インスタンスなので、止めてよいのはプレビューの Tween だけ)。
            _tweenManager.StopAll(elementTarget);

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
            return true;
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

            if (_manager != null && !_manager.IsOpen(_previewHandle))
            {
                PlacePreview();
            }

            // (レビュー対応 2026-09-14) 開けなかったときに要素ごとに PlacePreview(RemovePreview/OpenData)を繰り返していた。
            // 1 回で打ち切る。
            if (_manager == null || !_manager.IsOpen(_previewHandle))
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
            _statusLabel.text = "すべて停止しました";
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

            var merged = CanvasElementFxCollector.ApplyPresetToButtons(_target.Prefab, _target.ElementEffects, _bulkPreset);

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
            var enabled = _target != null && _manager != null && _manager.IsOpen(_previewHandle);
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

            _previewHandle = _manager.OpenData(_target);
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
            foreach (var result in new CanvasDataValidator().Validate(_target, new ValidationContext(new List<AssetDataBase> { _target })))
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
