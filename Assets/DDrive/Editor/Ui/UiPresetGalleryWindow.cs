using System;
using System.Collections.Generic;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace DDrive.Editor.Ui
{
    // [15_ui_interaction.md] B-3.5「エディタ: プリセットギャラリー」(4-12)。
    // owner instruction 2026-09-10: ウィンドウ内には「動く」ものを描画しない。カードは静的カード
    // (名前・カテゴリ・64 サンプルの静的イージング曲線スケッチ)のみで、実際に動く見た目確認は
    // 「選択中のシーン要素で再生」が開いているシーンの実オブジェクトを実 UiTweenManager で
    // EditorApplication.update から駆動して見せる(ADR-4。UiTweenEditorWindow / CanvasEditorWindow と同じ設計)。
    public sealed class UiPresetGalleryWindow : EditorWindow
    {
        private const string NoneChoice = "(なし)";

        private GalleryTab _tab = GalleryTab.出現;
        private string _query = string.Empty;
        private HashSet<string> _favourites = new();
        private readonly IGalleryPrefsStore _prefsStore = new EditorPrefsGalleryStore();

        private CanvasData _canvas;
        private string _selectedElementPath = string.Empty;
        private ElementFxPhase _applyPhase = ElementFxPhase.Appear;

        private VisualElement _tabsRow;
        private VisualElement _gridContainer;
        private ObjectField _canvasField;
        private DropdownField _elementDropdown;
        private EnumField _phaseField;
        private Label _statusLabel;

        // 独自プリセット登録(実体は UiPresetCatalogEditing.Register)。
        private ObjectField _registerCatalogField;
        private ObjectField _registerTweenField;
        private TextField _registerNameField;
        private TextField _registerCategoryField;

        private UiTweenManager _previewManager;
        private DDrive.Foundation.Handle.Handle<UiTweenMarker> _previewHandle;
        private double _lastEditorTime;

        private readonly List<(string name, UiTweenData tween)> _catalogEntries = new();
        private List<GalleryCard> _allCards = new();

        [MenuItem(DDriveMenu.Editors + "UI Tween · Preset Gallery")]
        public static void OpenFromMenu() => GetWindow<UiPresetGalleryWindow>("Preset Gallery").minSize = new Vector2(520, 480);

        private void OnEnable()
        {
            _favourites = UiPresetGalleryFavorites.Load(_prefsStore);
            EditorApplication.update += OnEditorUpdate;
            _lastEditorTime = EditorApplication.timeSinceStartup;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void CreateGUI()
        {
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);
            scrollView.style.paddingLeft = 6;
            scrollView.style.paddingRight = 6;
            scrollView.style.paddingTop = 6;

            scrollView.Add(new HelpBox(
                "カードは静的表示のみです(方針 2026-09-10)。実際に動く確認は「選択中のシーン要素で再生」で開いているシーン上に反映します。",
                HelpBoxMessageType.Info));

            BuildTargetSection(scrollView);
            BuildTabsAndSearch(scrollView);

            _gridContainer = new VisualElement { style = { marginTop = 6 } };
            scrollView.Add(_gridContainer);

            BuildRegisterSection(scrollView);

            _statusLabel = new Label { style = { marginTop = 6, opacity = 0.8f } };
            scrollView.Add(_statusLabel);

            RebuildCatalogChoices();
            RebuildElementChoices();
            RebuildGrid();
        }

        private void OnDestroy()
        {
            StopPreview();
        }

        // ── ターゲット選択(CanvasData + 要素パス) ──

        private void BuildTargetSection(VisualElement root)
        {
            root.Add(new Label("適用先") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6 } });

            _canvasField = new ObjectField("CanvasData") { objectType = typeof(CanvasData) };
            _canvasField.RegisterValueChangedCallback(evt =>
            {
                _canvas = evt.newValue as CanvasData;
                RebuildCatalogChoices();
                RebuildElementChoices();
                RebuildGrid();
            });
            root.Add(_canvasField);

            _elementDropdown = new DropdownField("要素", new List<string> { NoneChoice }, 0);
            _elementDropdown.RegisterValueChangedCallback(evt => _selectedElementPath = evt.newValue == NoneChoice ? string.Empty : evt.newValue);
            root.Add(_elementDropdown);

            var selectionRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 2 } };
            selectionRow.Add(new Button(UseSceneSelectionAsElement) { text = "選択中のシーン要素のパスを使う" });
            root.Add(selectionRow);

            _phaseField = new EnumField("適用フェーズ", _applyPhase);
            _phaseField.RegisterValueChangedCallback(evt => _applyPhase = (ElementFxPhase)evt.newValue);
            root.Add(_phaseField);
        }

        // 「選択中のシーン要素」から CanvasData のルート(Prefab インスタンス)を基準にした相対パスを求める。
        // Canvas の Prefab インスタンスが見つからない/対象が Prefab アセットの場合は何もしない(DontSave-safe)。
        private void UseSceneSelectionAsElement()
        {
            var selected = Selection.activeTransform;
            if (selected == null || EditorUtility.IsPersistent(selected.gameObject))
            {
                _statusLabel.text = "シーン上のオブジェクトを選択してください(Prefab アセットは不可)";
                return;
            }

            var root = selected.root;
            var names = new List<string>();
            var cur = selected;
            while (cur != null && cur != root)
            {
                names.Add(cur.name);
                cur = cur.parent;
            }

            names.Reverse();
            _selectedElementPath = string.Join("/", names);
            _statusLabel.text = $"要素パスを '{_selectedElementPath}' に設定しました(手動確認してください)";
        }

        private void RebuildElementChoices()
        {
            if (_elementDropdown == null)
            {
                return;
            }

            var choices = new List<string> { NoneChoice };
            if (_canvas != null && _canvas.Prefab != null)
            {
                var merged = CanvasElementFxCollector.CollectMerged(_canvas.Prefab, _canvas.ElementEffects);
                foreach (var fx in merged)
                {
                    choices.Add(string.IsNullOrEmpty(fx.ElementPath) ? "(ルート)" : fx.ElementPath);
                }
            }

            _elementDropdown.choices = choices;
            _elementDropdown.SetValueWithoutNotify(choices.Contains(_selectedElementPath) ? _selectedElementPath : NoneChoice);
        }

        // ── タブ / 検索 ──

        private void BuildTabsAndSearch(VisualElement root)
        {
            _tabsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8 } };
            root.Add(_tabsRow);
            foreach (GalleryTab tab in Enum.GetValues(typeof(GalleryTab)))
            {
                var captured = tab;
                var btn = new Button(() => { _tab = captured; RebuildGrid(); }) { text = tab.ToString(), style = { flexGrow = 1f } };
                _tabsRow.Add(btn);
            }

            var searchField = new ToolbarSearchField { style = { marginTop = 4 } };
            searchField.RegisterValueChangedCallback(evt =>
            {
                _query = evt.newValue;
                RebuildGrid();
            });
            root.Add(searchField);
        }

        // ── 登録セクション(独自プリセット登録) ──

        private void BuildRegisterSection(VisualElement root)
        {
            root.Add(new Label("独自プリセットとして登録") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10 } });
            root.Add(new HelpBox("選択中の UiTweenData を、選んだ UiPresetCatalog へ名前付きで登録(同名なら上書き)します。", HelpBoxMessageType.Info));

            _registerCatalogField = new ObjectField("登録先カタログ") { objectType = typeof(UiPresetCatalog) };
            root.Add(_registerCatalogField);

            _registerTweenField = new ObjectField("元になる UiTweenData") { objectType = typeof(UiTweenData) };
            root.Add(_registerTweenField);

            _registerNameField = new TextField("表示名");
            root.Add(_registerNameField);

            _registerCategoryField = new TextField("カテゴリ(任意)");
            root.Add(_registerCategoryField);

            root.Add(new Button(RegisterCustomPreset) { text = "登録する" });
        }

        private void RegisterCustomPreset()
        {
            var catalog = _registerCatalogField.value as UiPresetCatalog;
            var tween = _registerTweenField.value as UiTweenData;
            var name = _registerNameField.value;
            if (catalog == null || tween == null || string.IsNullOrWhiteSpace(name))
            {
                _statusLabel.text = "カタログ・UiTweenData・表示名を指定してください";
                return;
            }

            UiPresetCatalogEditing.Register(catalog, name, tween, _registerCategoryField.value);
            _statusLabel.text = $"'{name}' を {catalog.name} に登録しました";
            RebuildCatalogChoices();
            RebuildGrid();
        }

        // ── カタログ収集 / カード一覧 ──

        private void RebuildCatalogChoices()
        {
            _catalogEntries.Clear();
            _catalogEntries.AddRange(UiPresetCatalogUtility.Collect());

            _allCards = UiPresetGalleryFilter.BuildBuiltinCards();
            _allCards.AddRange(UiPresetGalleryFilter.BuildCatalogCards(_catalogEntries));
        }

        private void RebuildGrid()
        {
            if (_gridContainer == null)
            {
                return;
            }

            _gridContainer.Clear();
            var filtered = UiPresetGalleryFilter.Filter(_allCards, _tab, _query, _favourites);
            if (filtered.Count == 0)
            {
                _gridContainer.Add(new Label("該当するプリセットがありません") { style = { opacity = 0.7f } });
                return;
            }

            var grid = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            _gridContainer.Add(grid);
            foreach (var card in filtered)
            {
                grid.Add(BuildCard(card));
            }
        }

        private VisualElement BuildCard(GalleryCard card)
        {
            var box = new Box { style = { width = 180, marginRight = 6, marginBottom = 6, paddingLeft = 4, paddingRight = 4, paddingTop = 4, paddingBottom = 4 } };

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween } };
            header.Add(new Label(card.Name) { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1f } });
            var star = new Button(() => ToggleFavourite(card.Name)) { text = _favourites.Contains(card.Name) ? "★" : "☆", style = { width = 24 } };
            header.Add(star);
            box.Add(header);

            box.Add(new Label(card.Tab.ToString()) { style = { opacity = 0.6f, fontSize = 10 } });

            var curve = new IMGUIContainer(() => DrawCurveSketch(GUILayoutUtility.GetRect(160, 50), card)) { style = { height = 54 } };
            box.Add(curve);

            box.Add(new Button(() => ApplyToElement(card)) { text = "この要素に適用" });
            if (card.Preset != UiPreset.None)
            {
                box.Add(new Button(() => ApplyBulkToButtons(card)) { text = "Canvas 内一括適用(全ボタン)" });
            }

            box.Add(new Button(() => PlayOnSelection(card)) { text = "選択中のシーン要素で再生" });
            box.Add(new Button(() => CopyToOtherElements()) { text = "この設定を他の要素へコピー" });

            return box;
        }

        private void ToggleFavourite(string name)
        {
            if (!_favourites.Add(name))
            {
                _favourites.Remove(name);
            }

            UiPresetGalleryFavorites.Save(_prefsStore, _favourites);
            RebuildGrid();
        }

        // 64 サンプルの静的イージング曲線スケッチ(UiTweenEditorWindow.DrawCurvePreview と同じ考え方)。
        // 実際の値域(位置/スケール等)ではなく、Motion(Parametric/Curve)の形そのものを 0..1 で描く。
        private static void DrawCurveSketch(Rect rect, in GalleryCard card)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));

            ValueDef motion;
            if (card.Preset != UiPreset.None)
            {
                var scratch = CreateScratchRect();
                var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
                var count = UiPresetFactory.Build(new UiPresetRef { Preset = card.Preset }, scratch, buffer);
                motion = count > 0 ? buffer[0].Motion : default;
                UnityEngine.Object.DestroyImmediate(scratch.gameObject);
            }
            else if (card.CatalogTween != null && card.CatalogTween.Tracks != null && card.CatalogTween.Tracks.Length > 0)
            {
                motion = card.CatalogTween.Tracks[0].Motion;
            }
            else
            {
                return;
            }

            const int samples = 64;
            var points = new Vector3[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)(samples - 1);
                var shape = SampleShape(motion, t);
                points[i] = new Vector3(rect.x + t * rect.width, rect.yMax - Mathf.Clamp01(shape) * rect.height, 0f);
            }

            Handles.BeginGUI();
            var prev = Handles.color;
            Handles.color = new Color(0.4f, 0.85f, 1f);
            Handles.DrawAAPolyLine(2f, points);
            Handles.color = prev;
            Handles.EndGUI();
        }

        // UiTweenManager.EvaluateShape と同じ考え方(Constant=1=即時反映)。曲線の「形」だけを見せるため
        // From/To は使わない(値域は要素ごとに異なるため)。
        private static float SampleShape(in ValueDef motion, float t)
        {
            switch (motion.Mode)
            {
                case ValueMode.Parametric:
                    return motion.Parametric.Evaluate(t);
                case ValueMode.Curve:
                    return motion.Curve != null ? motion.Curve.Evaluate(t) : t;
                default:
                    return 1f;
            }
        }

        private static RectTransform CreateScratchRect()
        {
            var go = new GameObject("ScratchRect", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(200f, 80f);
            return rt;
        }

        // ── アクション ──

        private void ApplyToElement(GalleryCard card)
        {
            if (_canvas == null)
            {
                _statusLabel.text = "CanvasData を選択してください";
                return;
            }

            if (card.Preset != UiPreset.None)
            {
                ElementFxAssignment.SetPreset(_canvas, _selectedElementPath, _applyPhase, new UiPresetRef { Preset = card.Preset });
            }
            else if (card.CatalogTween != null && card.CatalogTween.Id != 0)
            {
                ElementFxAssignment.SetTween(_canvas, _selectedElementPath, _applyPhase, new AssetId<UiTweenMarker>(card.CatalogTween.Id, AssetType.UiTween));
            }
            else
            {
                _statusLabel.text = "この Tween は Id が未採番のため割り当てできません(AssetBrowser で採番してください)";
                return;
            }

            _statusLabel.text = $"'{card.Name}' を要素 '{_selectedElementPath}' の {_applyPhase} に適用しました";
        }

        private void ApplyBulkToButtons(GalleryCard card)
        {
            if (_canvas == null || _canvas.Prefab == null)
            {
                _statusLabel.text = "CanvasData(Prefab 設定済み)を選択してください";
                return;
            }

            var merged = CanvasElementFxCollector.ApplyPresetToButtons(_canvas.Prefab, _canvas.ElementEffects, card.Preset);
            Undo.RecordObject(_canvas, "Preset Gallery: Canvas 内一括適用");
            _canvas.ElementEffects = merged;
            EditorUtility.SetDirty(_canvas);
            _statusLabel.text = $"Canvas 内の全ボタンへ '{card.Name}' を適用しました";
        }

        private void CopyToOtherElements()
        {
            if (_canvas == null || _canvas.ElementEffects == null)
            {
                _statusLabel.text = "CanvasData を選択してください";
                return;
            }

            var index = Array.FindIndex(_canvas.ElementEffects, e => e.ElementPath == _selectedElementPath);
            if (index < 0)
            {
                _statusLabel.text = "選択中の要素の ElementFx 行が見つかりません(先に「この要素に適用」してください)";
                return;
            }

            Undo.RecordObject(_canvas, "Preset Gallery: 設定を他の要素へコピー");
            var rows = _canvas.ElementEffects;
            CanvasElementFxCollector.CopyPhases(ref rows, index);
            _canvas.ElementEffects = rows;
            EditorUtility.SetDirty(_canvas);
            _statusLabel.text = "設定を他の要素へコピーしました";
        }

        // owner instruction 2026-09-10: 再生は必ずシーン上の実オブジェクト(DontSave 含む)を対象にし、
        // Prefab アセットへは絶対に適用しない。
        private void PlayOnSelection(GalleryCard card)
        {
            var target = Selection.activeTransform as RectTransform;
            if (target == null)
            {
                target = (Selection.activeGameObject != null) ? Selection.activeGameObject.GetComponent<RectTransform>() : null;
            }

            if (target == null || EditorUtility.IsPersistent(target.gameObject))
            {
                _statusLabel.text = "シーン上の RectTransform を選択してください(Prefab アセットは不可)";
                return;
            }

            StopPreview();
            _previewManager ??= new UiTweenManager(EditorAnchorRegistry.Build());

            if (card.Preset != UiPreset.None)
            {
                var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
                var count = UiPresetFactory.Build(new UiPresetRef { Preset = card.Preset }, target, buffer);
                _previewHandle = count > 0 ? _previewManager.PlayTracks(buffer, count, target) : DDrive.Foundation.Handle.Handle<UiTweenMarker>.Invalid;
            }
            else if (card.CatalogTween != null)
            {
                _previewHandle = _previewManager.PlayData(card.CatalogTween, target);
            }

            _lastEditorTime = EditorApplication.timeSinceStartup;
            _statusLabel.text = $"'{card.Name}' を '{target.name}' で再生中";
        }

        private void StopPreview()
        {
            if (_previewManager != null)
            {
                _previewManager.Stop(_previewHandle);
            }

            _previewHandle = DDrive.Foundation.Handle.Handle<UiTweenMarker>.Invalid;
        }

        private void OnEditorUpdate()
        {
            if (_previewManager == null)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - _lastEditorTime);
            _lastEditorTime = now;
            if (dt > 0f && dt < 1f)
            {
                _previewManager.Tick(dt);
            }
        }
    }
}
