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

        // Codex レビュー対応(2026-09-11): 「選択中のシーン要素で再生」はユーザーの実オブジェクトを直接
        // 動かすため、再生前の姿勢を保存しておき Stop 時に必ず元へ戻す(Undo だけに頼らない。ドメインリロード
        // や別要素への切り替えでも復元漏れが起きないようにするため)。CanvasGroup を UiTweenManager が
        // 内部で追加した場合は「元々無かった」ことも記録し、Stop 時に取り除く(Undo.RegisterCreatedObjectUndo
        // で Ctrl+Z 側の経路も確保する)。
        private RectTransform _previewTarget;
        private Vector2 _previewSnapAnchoredPosition;
        private Vector3 _previewSnapLocalScale;
        private Vector3 _previewSnapLocalEuler;
        private bool _previewHadCanvasGroup;
        private float _previewSnapAlpha;

        private readonly List<(string name, UiTweenData tween)> _catalogEntries = new();
        private List<GalleryCard> _allCards = new();

        // Codex レビュー対応(2026-09-11): DrawCurveSketch はカードの再描画(IMGUIContainer は毎 Repaint 呼ばれる)
        // のたびに `CreateScratchRect()` で GameObject を生成/破棄していた。曲線の「形」自体はカード名が
        // 同じなら毎回同じなので、64 サンプルを 1 回だけ計算してキャッシュする(Rect のサイズ変更には
        // 追従しなくてよい。points の x/y は描画時に現在の rect から計算し直す)。
        private readonly Dictionary<string, float[]> _sketchSampleCache = new();
        // DrawCurveSketch の作業バッファ(描画のたびに new Vector3[64] していた。レビュー対応 2026-09-14)。
        private Vector3[] _sketchPoints = Array.Empty<Vector3>();

        // U-9(2026-09-17): UI Tween Editor の「プリセットギャラリー」ボタンからも開けるようにしたため、
        // メニュー以外からの入口を Open() に切り出した。source を渡すと「独自プリセットとして登録」の
        // 「元になる UiTweenData」を埋めておく(いま編集中の Tween をそのままプリセット化する導線)。
        // minSize は [09] §7.1(横 500px 下限)に合わせて 520 → 500 にした。
        [MenuItem(DDriveMenu.Editors + "UI Tween · Preset Gallery")]
        public static void OpenFromMenu() => Open(Selection.activeObject as UiTweenData);

        public static UiPresetGalleryWindow Open(UiTweenData source = null)
        {
            var window = GetWindow<UiPresetGalleryWindow>("Preset Gallery");
            window.minSize = new Vector2(500, 480);

            if (source != null)
            {
                window._pendingRegisterSource = source;
                window.ApplyPendingRegisterSource();
            }

            return window;
        }

        // CreateGUI 前に Open(source) が呼ばれた場合に備えて保持しておく。
        private UiTweenData _pendingRegisterSource;

        private void ApplyPendingRegisterSource()
        {
            if (_pendingRegisterSource == null || _registerTweenField == null)
            {
                return;
            }

            _registerTweenField.value = _pendingRegisterSource;

            if (_registerNameField != null && string.IsNullOrEmpty(_registerNameField.value))
            {
                _registerNameField.value = string.IsNullOrEmpty(_pendingRegisterSource.DisplayName)
                    ? _pendingRegisterSource.name
                    : _pendingRegisterSource.DisplayName;
            }

            _pendingRegisterSource = null;
        }

        private void OnEnable()
        {
            _favourites = UiPresetGalleryFavorites.Load(_prefsStore);
            EditorApplication.update += OnEditorUpdate;
            _lastEditorTime = EditorApplication.timeSinceStartup;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            StopPreview();
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
            ApplyPendingRegisterSource();
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
            // (レビュー対応 2026-09-14) "(ルート)" を選ぶとその表示文字列がそのままパスとして使われていた。ルート = 空文字に戻す。
            _elementDropdown.RegisterValueChangedCallback(evt => _selectedElementPath =
                evt.newValue == NoneChoice || evt.newValue == UiTweenEditorWindow.RootElementLabel ? string.Empty : evt.newValue);
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

            _selectedElementPath = TransformPath.GetRelative(selected.root, selected); // 共通ヘルパーへ集約(レビュー対応 2026-09-14)
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
                    choices.Add(string.IsNullOrEmpty(fx.ElementPath) ? UiTweenEditorWindow.RootElementLabel : fx.ElementPath);
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
            // (レビュー対応 2026-09-14) カタログを編集(登録・上書き)しても同名カードの曲線キャッシュが古いままだった。
            _sketchSampleCache.Clear();

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

            var curve = new IMGUIContainer(() => DrawCurveSketch(GUILayoutUtility.GetRect(160, 50), in card)) { style = { height = 54 } };
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
        // サンプル自体はカード単位で 1 回だけ計算してキャッシュする(下記 BuildSketchSamples)。
        private void DrawCurveSketch(Rect rect, in GalleryCard card)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));

            if (!_sketchSampleCache.TryGetValue(card.Name, out var samples))
            {
                samples = BuildSketchSamples(card);
                _sketchSampleCache[card.Name] = samples;
            }

            if (samples == null)
            {
                return;
            }

            if (_sketchPoints.Length != samples.Length)
            {
                _sketchPoints = new Vector3[samples.Length];
            }

            var points = _sketchPoints;
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)(samples.Length - 1);
                points[i] = new Vector3(rect.x + t * rect.width, rect.yMax - Mathf.Clamp01(samples[i]) * rect.height, 0f);
            }

            Handles.BeginGUI();
            var prev = Handles.color;
            Handles.color = new Color(0.4f, 0.85f, 1f);
            Handles.DrawAAPolyLine(2f, points);
            Handles.color = prev;
            Handles.EndGUI();
        }

        // card.Name をキーにキャッシュされる「形」だけのサンプル配列(GameObject の生成/破棄はここで 1 回だけ)。
        private static float[] BuildSketchSamples(in GalleryCard card)
        {
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
                return null;
            }

            const int samples = 64;
            var result = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)(samples - 1);
                result[i] = SampleShape(motion, t);
            }

            return result;
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

            StopPreview(); // 前回の再生対象が残っていれば先に元へ戻す
            _previewManager ??= new UiTweenManager(EditorAnchorRegistry.Build());

            // 再生前の姿勢を保存(Stop 時に必ずここへ戻す)。
            _previewTarget = target;
            _previewSnapAnchoredPosition = target.anchoredPosition;
            _previewSnapLocalScale = target.localScale;
            _previewSnapLocalEuler = target.localEulerAngles;
            var existingGroup = target.GetComponent<CanvasGroup>();
            _previewHadCanvasGroup = existingGroup != null;
            _previewSnapAlpha = existingGroup != null ? existingGroup.alpha : 1f;

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

            // UiTweenManager.ResolveComponents が CanvasGroup/Rigidbody 等を必要に応じて追加することがある。
            // 元々無かったのに追加された場合は Ctrl+Z でも消せるよう Undo に登録しておく(実削除は StopPreview)。
            if (!_previewHadCanvasGroup)
            {
                var addedGroup = target.GetComponent<CanvasGroup>();
                if (addedGroup != null)
                {
                    Undo.RegisterCreatedObjectUndo(addedGroup, "Preset Gallery Preview: CanvasGroup");
                }
            }

            _lastEditorTime = EditorApplication.timeSinceStartup;
            _statusLabel.text = $"'{card.Name}' を '{target.name}' で再生中";
        }

        // complete=false で即座に停止し(演出の終端値へジャンプさせない)、再生前の姿勢へ戻す。
        // UiTweenManager が追加した CanvasGroup(元々無かった場合のみ)もここで取り除く。
        private void StopPreview()
        {
            if (_previewManager != null)
            {
                _previewManager.Stop(_previewHandle, complete: false);
            }

            _previewHandle = DDrive.Foundation.Handle.Handle<UiTweenMarker>.Invalid;

            if (_previewTarget != null)
            {
                _previewTarget.anchoredPosition = _previewSnapAnchoredPosition;
                _previewTarget.localScale = _previewSnapLocalScale;
                _previewTarget.localEulerAngles = _previewSnapLocalEuler;

                var group = _previewTarget.GetComponent<CanvasGroup>();
                if (group != null)
                {
                    if (_previewHadCanvasGroup)
                    {
                        group.alpha = _previewSnapAlpha;
                    }
                    else
                    {
                        Undo.DestroyObjectImmediate(group);
                    }
                }
            }

            _previewTarget = null;
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
