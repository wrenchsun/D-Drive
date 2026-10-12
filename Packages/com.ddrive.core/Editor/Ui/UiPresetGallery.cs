using System;
using System.Collections.Generic;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Manual;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ui
{
    // プリセットギャラリー(docs/1010 UX-12。旧 UiPresetGalleryWindow は Archive メニューへ退避)。新デザイン(UxTheme)の独立ウィンドウ。
    // カード = 名前・種類・曲線の形(静的スケッチ)・★(お気に入り)・▶(選択中のシーン要素で再生)・この要素に適用 など。
    // タブ(出現 / 常時 / 消滅 / 強調 / カタログ)・検索・★ お気に入りだけ・「独自プリセットとして登録」は旧と同じ機能(お気に入りは旧と同じ保存先を共有)。
    // 動くものをウィンドウ内に描かない方針(2026-09-10): カードは静的で、再生は開いているシーンの実要素を実 UiTweenManager で動かす(ADR-4)。
    // 新エディターの「雛形から始める」(UiTweenPresetPicker)と同じカード一覧・同じ書き込み(UiTweenPresetOps)を、大きく見る・Canvas の要素へ当てるための場所。
    internal sealed class UiPresetGallery : EditorWindow
    {
        private const string NoneChoice = "(なし)";
        private const int ListAbove = 20;
        private const float CardWidth = 160f;

        [SerializeField] private GalleryTab _tab = GalleryTab.出現;
        [SerializeField] private bool _favOnly;
        [SerializeField] private string _query = string.Empty;
        [SerializeField] private CanvasData _canvas;
        [SerializeField] private string _selectedElementPath = string.Empty;
        [SerializeField] private ElementFxPhase _applyPhase = ElementFxPhase.Appear;
        [SerializeField] private UiTweenData _source;

        private readonly IGalleryPrefsStore _prefs = new EditorPrefsGalleryStore();
        private readonly Dictionary<string, float[]> _sketchCache = new Dictionary<string, float[]>();
        private readonly List<Button> _tabButtons = new List<Button>();
        private readonly List<(string name, UiTweenData tween)> _catalogEntries = new List<(string, UiTweenData)>();
        private readonly UiTweenSnapshot _snapshot = new UiTweenSnapshot();

        private HashSet<string> _favourites = new HashSet<string>();
        private List<GalleryCard> _allCards = new List<GalleryCard>();
        private List<GalleryCard> _shown = new List<GalleryCard>();

        private VisualElement _root;
        private VisualElement _gridHost;
        private Label _count;
        private Label _hint;
        private Label _status;
        private TextField _search;
        private ObjectField _canvasField;
        private ObjectField _sourceField;
        private DropdownField _elementDropdown;
        private EnumField _phaseField;
        private UiPresetRegisterBox _register;
        private Button _stop;
        private int _perRow = 1;
        private bool _lastPro;
        private bool _geometryHooked;
        private IVisualElementScheduledItem _themeTick;

        private UiTweenManager _previewManager;
        private Handle<UiTweenMarker> _previewHandle = Handle<UiTweenMarker>.Invalid;
        private double _lastTime;
        private bool _active;

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "UI Tween · Preset Gallery")]
        public static void OpenFromMenu() => Open(Selection.activeObject as UiTweenData);

        // source を渡すと「編集中の Tween」(置き換え / 追加の先・独自プリセットとして登録の元)に入る。
        public static UiPresetGallery Open(UiTweenData source = null)
        {
            var w = GetWindow<UiPresetGallery>("UI Preset Gallery");
            w.minSize = new Vector2(500, 480);
            if (source != null)
            {
                w.SetSource(source);
            }

            return w;
        }

        // ── ライフサイクル ──

        private void OnEnable()
        {
            _favourites = UiPresetGalleryFavorites.Load(_prefs);
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            StopPreview();
        }

        private void CreateGUI()
        {
            _root = rootVisualElement;
            UxTheme.Apply(_root);
            _lastPro = EditorGUIUtility.isProSkin;

            var body = new ScrollView(ScrollViewMode.Vertical);
            body.AddToClassList("ux-scroll");
            _root.Add(body);
            var page = new VisualElement();
            page.AddToClassList("ux-page");
            body.Add(page);

            page.Add(BuildHero());
            page.Add(BuildCardsCard());
            page.Add(BuildTargetCard());
            page.Add(BuildRegisterCard());

            _status = new Label();
            _status.AddToClassList("ux-dim");
            _status.AddToClassList("ux-note");
            page.Add(_status);

            if (!_geometryHooked)
            {
                _geometryHooked = true;
                _root.RegisterCallback<GeometryChangedEvent>(e => OnResized());
            }

            _themeTick?.Pause();
            _themeTick = _root.schedule.Execute(() =>
            {
                if (EditorGUIUtility.isProSkin != _lastPro)
                {
                    // Light / Dark が切り替わった: クラスを付け替え、Painter の色を読み直すため作り直す。
                    _lastPro = EditorGUIUtility.isProSkin;
                    _root.Clear();
                    _tabButtons.Clear();
                    CreateGUI();
                }
            }).Every(500);

            Reload();
            RebuildElementChoices();
            Render();
        }

        private void OnDestroy() => StopPreview();

        // ── 見出し(タブ・検索・お気に入り) ──

        private VisualElement BuildHero()
        {
            var hero = new VisualElement();
            hero.AddToClassList("ux-hero");

            var top = new VisualElement();
            top.AddToClassList("ux-hero__top");
            var info = new VisualElement();
            info.AddToClassList("ux-hero__info");
            info.AddToClassList("ux-ut-gh");
            var title = new Label("UI Preset Gallery");
            title.AddToClassList("ux-hero__title");
            info.Add(title);
            _hint = new Label();
            _hint.AddToClassList("ux-dim");
            _hint.AddToClassList("ux-hero__id");
            info.Add(_hint);
            top.Add(info);

            var help = new Button(() => ManualLauncher.OpenPage("ui-tween", "gallery")) { text = "?", tooltip = "マニュアルを開く" };
            help.AddToClassList("ux-btn");
            help.AddToClassList("ux-btn--icon");
            top.Add(help);
            hero.Add(top);

            var tabs = new VisualElement();
            tabs.AddToClassList("ux-itemlist__bar");
            foreach (GalleryTab tab in Enum.GetValues(typeof(GalleryTab)))
            {
                var captured = tab;
                var b = new Button(() =>
                {
                    _tab = captured;
                    _favOnly = false;
                    Render();
                }) { text = tab.ToString() };
                b.AddToClassList("ux-filter");
                _tabButtons.Add(b);
                tabs.Add(b);
            }

            var fav = new Button(() =>
            {
                _favOnly = !_favOnly;
                Render();
            }) { text = "★ お気に入り", tooltip = "お気に入りに入れたプリセットだけを全タブから集めて出す" };
            fav.AddToClassList("ux-filter");
            _tabButtons.Add(fav);
            tabs.Add(fav);

            _search = new TextField { value = _query };
            _search.AddToClassList("ux-itemlist__search");
            _search.textEdition.placeholder = "プリセット名で検索";
            _search.textEdition.hidePlaceholderOnFocus = true;
            _search.RegisterValueChangedCallback(e =>
            {
                _query = (e.newValue ?? string.Empty).Trim();
                Render();
            });
            tabs.Add(_search);
            hero.Add(tabs);

            var actions = new VisualElement();
            actions.AddToClassList("ux-toolsline");
            _stop = new Button(StopPreview) { text = "■ 再生を止める", tooltip = "シーン上で再生中のプリセットを止めて、元の見た目に戻す" };
            _stop.AddToClassList("ux-btn");
            actions.Add(_stop);
            var openEditor = new Button(() => UiTweenEditor.Open(_source)) { text = "UI Tween Editor で開く", tooltip = "編集中の Tween を UI Tween Editor で開く" };
            openEditor.AddToClassList("ux-btn");
            actions.Add(openEditor);
            hero.Add(actions);
            return hero;
        }

        // ── カード一覧 ──

        private VisualElement BuildCardsCard()
        {
            var card = new UxCard("プリセット", "カードの曲線は形だけ(静的)。カードの ▶ で、シーンで選んでいる UI 要素を実際に動かします", UxCard.Help("ui-tween", "gallery"));
            _count = new Label();
            _count.AddToClassList("ux-dim");
            _count.AddToClassList("ux-itemlist__count");
            card.Body.Add(_count);
            _gridHost = new VisualElement();
            _gridHost.AddToClassList("ux-itemlist__host");
            card.Body.Add(_gridHost);
            return card;
        }

        private void Reload()
        {
            _catalogEntries.Clear();
            _catalogEntries.AddRange(UiPresetCatalogUtility.Collect());
            _sketchCache.Clear(); // カタログを編集(登録・上書き)しても同名カードの曲線キャッシュが古いままにならないように
            _allCards = UiPresetGalleryFilter.BuildBuiltinCards();
            _allCards.AddRange(UiPresetGalleryFilter.BuildCatalogCards(_catalogEntries));
        }

        internal int ShownCount => _shown.Count;

        internal GalleryTab Tab => _tab;

        internal bool FavouritesOnly => _favOnly;

        internal string Status => _status != null ? _status.text : string.Empty;

        internal bool IsPreviewing => _active;

        internal UiTweenData Source => _source;

        internal UiPresetRegisterBox RegisterBox => _register;

        internal bool UsesListView => _gridHost != null && _gridHost.Q<ListView>() != null;

        internal bool IsFavourite(string name) => _favourites.Contains(name);

        internal IReadOnlyList<GalleryCard> ShownCards => _shown;

        internal void SetSearch(string query)
        {
            _query = query ?? string.Empty;
            _search?.SetValueWithoutNotify(_query);
            Render();
        }

        internal void SelectTab(GalleryTab tab)
        {
            _tab = tab;
            _favOnly = false;
            Render();
        }

        internal void SetFavouritesOnly(bool on)
        {
            _favOnly = on;
            Render();
        }

        internal void ToggleFavouriteForTest(string name) => ToggleFavourite(name);

        internal void SetCanvas(CanvasData canvas)
        {
            _canvas = canvas;
            _canvasField?.SetValueWithoutNotify(canvas);
            RebuildElementChoices();
            Render();
        }

        internal void SetSource(UiTweenData source)
        {
            _source = source;
            _sourceField?.SetValueWithoutNotify(source);
            _register?.SetSource(source);
            Render();
        }

        internal bool TryGetCard(string name, out GalleryCard card)
        {
            for (var i = 0; i < _allCards.Count; i++)
            {
                if (_allCards[i].Name == name)
                {
                    card = _allCards[i];
                    return true;
                }
            }

            card = default;
            return false;
        }

        private List<GalleryCard> Visible()
        {
            if (!_favOnly)
            {
                return UiPresetGalleryFilter.Filter(_allCards, _tab, _query, _favourites);
            }

            var all = new List<GalleryCard>();
            foreach (GalleryTab tab in Enum.GetValues(typeof(GalleryTab)))
            {
                all.AddRange(UiPresetGalleryFilter.Filter(_allCards, tab, _query, null));
            }

            return all.FindAll(c => _favourites.Contains(c.Name));
        }

        private void OnResized()
        {
            var perRow = Mathf.Max(1, Mathf.FloorToInt((position.width - 64f) / (CardWidth + 8f)));
            if (perRow != _perRow && _shown.Count > ListAbove)
            {
                _perRow = perRow;
                Render();
            }

            _perRow = perRow;
        }

        private void Render()
        {
            if (_gridHost == null)
            {
                return;
            }

            var tabs = Enum.GetValues(typeof(GalleryTab));
            for (var i = 0; i < _tabButtons.Count; i++)
            {
                var on = i < tabs.Length ? !_favOnly && (int)tabs.GetValue(i) == (int)_tab : _favOnly;
                _tabButtons[i].EnableInClassList("ux-filter--on", on);
            }

            _shown = Visible();
            var favCount = 0;
            for (var i = 0; i < _allCards.Count; i++)
            {
                if (_favourites.Contains(_allCards[i].Name))
                {
                    favCount++;
                }
            }

            _hint.text = $"組み込み {_allCards.Count - _catalogEntries.Count} 種 + カタログ {_catalogEntries.Count} 件 ・ お気に入り {favCount}"
                + (_source != null ? $" ・ 編集中の Tween: {_source.name}" : string.Empty);
            _count.text = $"{_shown.Count} 件" + (_favOnly ? "(お気に入り)" : $"({_tab})") + (string.IsNullOrEmpty(_query) ? string.Empty : $" ・ 「{_query}」で絞り込み中");
            _stop?.SetEnabled(_active);

            _gridHost.Clear();
            if (_shown.Count == 0)
            {
                var empty = new Label(_favOnly ? "お気に入りはまだありません(カードの ☆ を押すと入ります)" : "該当するプリセットがありません");
                empty.AddToClassList("ux-dim");
                empty.AddToClassList("ux-note");
                _gridHost.Add(empty);
                return;
            }

            if (_shown.Count <= ListAbove)
            {
                var grid = new VisualElement();
                grid.AddToClassList("ux-ut-cards");
                for (var i = 0; i < _shown.Count; i++)
                {
                    grid.Add(BuildCard(_shown[i]));
                }

                _gridHost.Add(grid);
                return;
            }

            // 20 件を超える一覧(カタログが多いとき)は ListView(仮想化)。1 行 = カードを幅に入るだけ並べたもの。
            var rows = new List<int>();
            _perRow = Mathf.Max(1, Mathf.FloorToInt((position.width - 64f) / (CardWidth + 8f)));
            for (var i = 0; i < _shown.Count; i += _perRow)
            {
                rows.Add(i);
            }

            var lv = new ListView
            {
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                fixedItemHeight = 168f,
                selectionType = SelectionType.None,
                showAlternatingRowBackgrounds = AlternatingRowBackground.None,
                itemsSource = rows,
                makeItem = () =>
                {
                    var r = new VisualElement();
                    r.AddToClassList("ux-ut-cards");
                    r.AddToClassList("ux-ut-cardrow");
                    return r;
                },
                bindItem = (e, i) =>
                {
                    e.Clear();
                    var start = rows[i];
                    for (var k = start; k < Mathf.Min(_shown.Count, start + _perRow); k++)
                    {
                        e.Add(BuildCard(_shown[k]));
                    }
                },
            };
            lv.AddToClassList("ux-itemlist__lv");
            _gridHost.Add(lv);
        }

        private VisualElement BuildCard(GalleryCard card)
        {
            var box = new VisualElement();
            box.AddToClassList("ux-ut-gcard");

            var head = new VisualElement();
            head.AddToClassList("ux-ut-gcard__head");
            var name = new Label(card.Name) { tooltip = card.Name };
            name.AddToClassList("ux-ut-gcard__name");
            head.Add(name);
            var play = new Button(() => PlayOnSelection(card)) { text = "▶", tooltip = "シーンで選んでいる UI 要素(RectTransform)を、このプリセットで実際に動かす。終わる・止めると元に戻る" };
            play.AddToClassList("ux-btn");
            play.AddToClassList("ux-btn--icon");
            head.Add(play);
            var starred = _favourites.Contains(card.Name);
            var star = new Button(() => ToggleFavourite(card.Name)) { text = starred ? "★" : "☆", tooltip = starred ? "お気に入りから外す" : "お気に入りに入れる(UI Tween Editor の雛形と共通)" };
            star.AddToClassList("ux-btn");
            star.AddToClassList("ux-btn--icon");
            star.EnableInClassList("ux-btn--on", starred);
            head.Add(star);
            box.Add(head);

            var sub = new VisualElement();
            sub.AddToClassList("ux-ut-gcard__sub");
            var chip = new Label(card.Tab.ToString());
            chip.AddToClassList("ux-chip");
            chip.AddToClassList("ux-kind--" + (int)card.Tab);
            sub.Add(chip);
            var trackCount = card.Preset != UiPreset.None ? string.Empty : $"Track {(card.CatalogTween != null && card.CatalogTween.Tracks != null ? card.CatalogTween.Tracks.Length : 0)} 本";
            var note = new Label(trackCount);
            note.AddToClassList("ux-dim");
            sub.Add(note);
            box.Add(sub);

            if (!_sketchCache.TryGetValue(card.Name, out var samples))
            {
                samples = UiTweenPresetOps.ShapeSamples(card);
                _sketchCache[card.Name] = samples;
            }

            var sketch = new CurveSketch();
            sketch.SetSamples(samples);
            box.Add(sketch);

            var line = new VisualElement();
            line.AddToClassList("ux-ut-gcard__btns");
            line.Add(Small("要素へ", "「適用先」の Canvas の要素 / フェーズに、このプリセットを割り当てる", () => ApplyToElement(card)));
            if (card.Preset != UiPreset.None)
            {
                line.Add(Small("全ボタンへ", "Canvas 内のすべてのボタンにこのプリセットを割り当てる", () => ApplyBulkToButtons(card)));
            }

            if (_source != null)
            {
                line.Add(Small("置換", "編集中の Tween の Tracks を、このプリセットに置き換える(Undo 可)", () => ApplyToSource(card, false)));
                line.Add(Small("追加", "編集中の Tween の Tracks の末尾に、このプリセットを足す(8 本まで)", () => ApplyToSource(card, true)));
            }

            box.Add(line);
            return box;
        }

        private static Button Small(string text, string tooltip, Action click)
        {
            var b = new Button(click) { text = text, tooltip = tooltip };
            b.AddToClassList("ux-btn");
            b.AddToClassList("ux-btn--small");
            return b;
        }

        private void ToggleFavourite(string name)
        {
            if (!_favourites.Add(name))
            {
                _favourites.Remove(name);
            }

            UiPresetGalleryFavorites.Save(_prefs, _favourites);
            Render();
        }

        // ── 適用先(CanvasData + 要素パス)と編集中の Tween ──

        private VisualElement BuildTargetCard()
        {
            var card = new UxCard("適用先", "カードの「要素へ」「全ボタンへ」の宛先(Canvas の要素)と、「置換」「追加」の先(編集中の Tween)", UxCard.Help("ui-tween", "gallery"));

            _sourceField = new ObjectField("編集中の Tween") { objectType = typeof(UiTweenData), tooltip = "カードの「置換」「追加」の先。独自プリセットとして登録するときの元にもなる" };
            _sourceField.AddToClassList("ux-ut-field");
            _sourceField.SetValueWithoutNotify(_source);
            _sourceField.RegisterValueChangedCallback(e =>
            {
                _source = e.newValue as UiTweenData;
                _register?.SetSource(_source);
                Render();
            });
            card.Body.Add(_sourceField);

            _canvasField = new ObjectField("CanvasData") { objectType = typeof(CanvasData) };
            _canvasField.AddToClassList("ux-ut-field");
            _canvasField.SetValueWithoutNotify(_canvas);
            _canvasField.RegisterValueChangedCallback(e =>
            {
                _canvas = e.newValue as CanvasData;
                RebuildElementChoices();
                Reload();
                Render();
            });
            card.Body.Add(_canvasField);

            _elementDropdown = new DropdownField("要素", new List<string> { NoneChoice }, 0);
            _elementDropdown.AddToClassList("ux-ut-field");
            // "(ルート)" を選ぶとその表示文字列がそのままパスとして使われないよう、ルート = 空文字に戻す。
            _elementDropdown.RegisterValueChangedCallback(e => _selectedElementPath =
                e.newValue == NoneChoice || e.newValue == UiTweenEditor.RootElementLabel ? string.Empty : e.newValue);
            card.Body.Add(_elementDropdown);

            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            line.Add(Small("選択中のシーン要素のパスを使う", "シーンで選んでいる UI 要素の、Canvas のルートからのパスを「要素」にする(埋め込み Canvas の要素なら子の CanvasData に切り替える)", UseSceneSelectionAsElement));
            line.Add(Small("この設定を他の要素へコピー", "「要素」の ElementFx の設定(Appear / Idle / Disappear)を、ほかの要素にもコピーする", CopyToOtherElements));
            card.Body.Add(line);

            _phaseField = new EnumField("適用フェーズ", _applyPhase);
            _phaseField.AddToClassList("ux-ut-field");
            _phaseField.RegisterValueChangedCallback(e => _applyPhase = (ElementFxPhase)e.newValue);
            card.Body.Add(_phaseField);
            return card;
        }

        private VisualElement BuildRegisterCard()
        {
            var card = new UxCard("独自プリセットとして登録", "「編集中の Tween」を、選んだカタログに名前付きで登録する(同名なら上書き)。登録するとカタログのタブに出る", UxCard.Help("ui-tween", "gallery"));
            _register = new UiPresetRegisterBox(() =>
            {
                Reload();
                Render();
                Say("登録しました");
            });
            _register.SetSource(_source);
            card.Body.Add(_register);
            return card;
        }

        private void Say(string text)
        {
            if (_status != null)
            {
                _status.text = text;
            }
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
                    choices.Add(string.IsNullOrEmpty(fx.ElementPath) ? UiTweenEditor.RootElementLabel : fx.ElementPath);
                }
            }

            _elementDropdown.choices = choices;
            var current = string.IsNullOrEmpty(_selectedElementPath) ? NoneChoice : _selectedElementPath;
            _elementDropdown.SetValueWithoutNotify(choices.Contains(current) ? current : NoneChoice);
        }

        // 「選択中のシーン要素」から CanvasData のルート(Prefab インスタンス)を基準にした相対パスを求める。Prefab アセットは対象にしない。
        private void UseSceneSelectionAsElement()
        {
            var selected = Selection.activeTransform;
            if (selected == null || EditorUtility.IsPersistent(selected.gameObject))
            {
                Say("シーン上のオブジェクトを選択してください(Prefab アセットは不可)");
                return;
            }

            // Canvas のルート(プレビュー実体 / プレハブステージのルート / Prefab インスタンス)を探してそこからのパスにする(見つからなければ最上位)。
            var canvasRoot = CanvasEmbeddedEditing.FindCanvasRoot(selected, _canvas != null ? _canvas.Prefab : null) ?? selected.root;
            var path = TransformPath.GetRelative(canvasRoot, selected);
            var note = string.Empty;

            // 埋め込み Canvas の配下の要素なら、子の CanvasData に子ルート基準のパスで割り当てる。
            if (_canvas != null && _canvas.EmbeddedCanvases != null && _canvas.EmbeddedCanvases.Length > 0)
            {
                var owner = CanvasEmbeddedEditing.ResolveOwner(_canvas, path, CanvasEmbeddedEditing.CanvasLookup.Build());
                if (owner.Data != null && owner.Data != _canvas)
                {
                    _canvas = owner.Data;
                    _canvasField?.SetValueWithoutNotify(owner.Data);
                    path = owner.Path;
                    note = $"(埋め込み Canvas '{owner.Data.name}' の要素のため、適用先を子の CanvasData に切り替えました)";
                }
            }

            _selectedElementPath = path;
            RebuildElementChoices();
            Say($"要素パスを '{_selectedElementPath}' に設定しました{note}(手動確認してください)");
        }

        // ── アクション ──

        private void ApplyToElement(GalleryCard card)
        {
            if (_canvas == null)
            {
                Say("CanvasData を選択してください");
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
                Say("この Tween は Id が未採番のため割り当てできません(AssetBrowser で採番してください)");
                return;
            }

            Say($"'{card.Name}' を要素 '{_selectedElementPath}' の {_applyPhase} に適用しました");
        }

        private void ApplyBulkToButtons(GalleryCard card)
        {
            if (_canvas == null || _canvas.Prefab == null)
            {
                Say("CanvasData(Prefab 設定済み)を選択してください");
                return;
            }

            var merged = CanvasElementFxCollector.ApplyPresetToButtons(_canvas.Prefab, _canvas.ElementEffects, card.Preset);
            Undo.RecordObject(_canvas, "Preset Gallery: Canvas 内一括適用");
            _canvas.ElementEffects = merged;
            EditorUtility.SetDirty(_canvas);
            Say($"Canvas 内の全ボタンへ '{card.Name}' を適用しました");
        }

        private void CopyToOtherElements()
        {
            if (_canvas == null || _canvas.ElementEffects == null)
            {
                Say("CanvasData を選択してください");
                return;
            }

            var index = Array.FindIndex(_canvas.ElementEffects, e => e.ElementPath == _selectedElementPath);
            if (index < 0)
            {
                Say("選択中の要素の ElementFx 行が見つかりません(先に「この要素に適用」してください)");
                return;
            }

            Undo.RecordObject(_canvas, "Preset Gallery: 設定を他の要素へコピー");
            var rows = _canvas.ElementEffects;
            CanvasElementFxCollector.CopyPhases(ref rows, index);
            _canvas.ElementEffects = rows;
            EditorUtility.SetDirty(_canvas);
            Say("設定を他の要素へコピーしました");
        }

        // 編集中の Tween へ置き換え / 追加(新エディターの「雛形」と同じ UiTweenPresetOps)。
        internal string ApplyToSource(GalleryCard card, bool append)
        {
            if (_source == null)
            {
                return Said("「編集中の Tween」を指定してください");
            }

            var target = Selection.activeTransform as RectTransform;
            if (!UiTweenPresetOps.TryResolve(card, target, out var tracks, out var label))
            {
                return Said("このプリセットは Track を作れませんでした");
            }

            if (append)
            {
                var added = UiTweenPresetOps.Append(_source, tracks);
                return Said(added <= 0
                    ? $"Track が上限({UiTweenManager.MaxTracksPerTween} 本)に達しているため追加できません"
                    : $"{label} から Track を {added} 本、'{_source.name}' に追加しました");
            }

            UiTweenPresetOps.Replace(_source, tracks);
            return Said($"{label} で '{_source.name}' の Tracks を置き換えました");
        }

        private string Said(string text)
        {
            Say(text);
            return text;
        }

        // ── 再生(シーンで選んでいる実要素。Prefab アセットへは絶対に適用しない) ──

        internal string PlayOnSelection(GalleryCard card)
        {
            var target = Selection.activeTransform as RectTransform;
            if (target == null && Selection.activeGameObject != null)
            {
                target = Selection.activeGameObject.GetComponent<RectTransform>();
            }

            return PlayOn(card, target);
        }

        internal string PlayOn(GalleryCard card, RectTransform target)
        {
            if (target == null || EditorUtility.IsPersistent(target.gameObject))
            {
                return Said("シーン上の RectTransform を選択してください(Prefab アセットは不可)");
            }

            StopPreview(); // 前回の再生対象が残っていれば先に元へ戻す
            _previewManager ??= new UiTweenManager(EditorAnchorRegistry.Build());
            _snapshot.Take(target);

            if (card.Preset != UiPreset.None)
            {
                var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
                var count = UiPresetFactory.Build(new UiPresetRef { Preset = card.Preset }, target, buffer);
                _previewHandle = count > 0 ? _previewManager.PlayTracks(buffer, count, target) : Handle<UiTweenMarker>.Invalid;
            }
            else if (card.CatalogTween != null)
            {
                _previewHandle = _previewManager.PlayData(card.CatalogTween, target);
            }

            _lastTime = EditorApplication.timeSinceStartup;
            _active = _previewManager.IsPlaying(_previewHandle);
            if (!_active)
            {
                _snapshot.Restore();
                return Said($"'{card.Name}' は再生する Track がありません");
            }

            _stop?.SetEnabled(true);
            return Said($"'{card.Name}' を '{target.name}' で再生中");
        }

        // 即座に停止し(演出の終端値へジャンプさせない)、再生前の姿勢へ戻す。UiTweenManager が追加した CanvasGroup(元々無かった場合のみ)も取り除く。
        internal void StopPreview()
        {
            if (_previewManager != null)
            {
                _previewManager.Stop(_previewHandle, complete: false);
            }

            _previewHandle = Handle<UiTweenMarker>.Invalid;
            _active = false;
            _snapshot.Restore();
            _stop?.SetEnabled(false);
        }

        private void OnEditorUpdate()
        {
            if (_previewManager == null)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - _lastTime);
            _lastTime = now;
            if (!_active)
            {
                return;
            }

            if (dt > 0f && dt < 1f)
            {
                _previewManager.Tick(dt);
            }

            if (!_previewManager.IsPlaying(_previewHandle))
            {
                _active = false;
                _snapshot.Restore();
                _stop?.SetEnabled(false);
                Say("再生が終わりました(再生前の状態に戻しました)");
            }
        }
    }
}
