using System;
using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ui
{
    // 「独自プリセットとして登録」(選んだ UiPresetCatalog へ名前付きで登録。同名は上書き)。UiPresetGallery と UiTweenEditor の「雛形」行が共有する。
    // 実体は UiPresetCatalogEditing.Register(旧 UiPresetGalleryWindow と同じ)。
    internal sealed class UiPresetRegisterBox : VisualElement
    {
        private readonly ObjectField _catalog;
        private readonly ObjectField _tween;
        private readonly TextField _name;
        private readonly TextField _category;
        private readonly Label _status;
        private readonly Action _registered;

        public UiPresetRegisterBox(Action registered)
        {
            _registered = registered;
            AddToClassList("ux-ut-register");

            var note = new Label("選んだ UiTweenData を、選んだ UiPresetCatalog へ名前付きで登録します(同名なら上書き)。登録するとギャラリーの「カタログ」タブと「雛形」に出ます。");
            note.AddToClassList("ux-dim");
            note.AddToClassList("ux-note");
            Add(note);

            _catalog = new ObjectField("登録先カタログ") { objectType = typeof(UiPresetCatalog) };
            _catalog.AddToClassList("ux-ut-field");
            Add(_catalog);
            _tween = new ObjectField("元になる UiTweenData") { objectType = typeof(UiTweenData) };
            _tween.AddToClassList("ux-ut-field");
            Add(_tween);
            _name = new TextField("表示名");
            _name.AddToClassList("ux-ut-field");
            Add(_name);
            _category = new TextField("カテゴリ(任意)");
            _category.AddToClassList("ux-ut-field");
            Add(_category);

            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            var register = new Button(() => Register()) { text = "登録する", tooltip = "元の UiTweenData をカタログに名前付きで登録する(同名なら上書き)" };
            register.AddToClassList("ux-btn");
            register.AddToClassList("ux-btn--primary");
            line.Add(register);
            Add(line);

            _status = new Label();
            _status.AddToClassList("ux-dim");
            _status.AddToClassList("ux-note");
            Add(_status);

            // 登録先は 1 つしか無ければ最初から入れておく(迷わず登録できるように)。
            var guids = AssetDatabase.FindAssets("t:" + nameof(UiPresetCatalog));
            if (guids.Length == 1)
            {
                _catalog.SetValueWithoutNotify(AssetDatabase.LoadAssetAtPath<UiPresetCatalog>(AssetDatabase.GUIDToAssetPath(guids[0])));
            }
        }

        public UiPresetCatalog Catalog => _catalog.value as UiPresetCatalog;

        public UiTweenData Source => _tween.value as UiTweenData;

        public string DisplayName => _name.value;

        // 元になる Tween を入れる(表示名が空なら Tween の名前を入れる)。
        public void SetSource(UiTweenData source)
        {
            if (source == null)
            {
                return;
            }

            _tween.SetValueWithoutNotify(source);
            if (string.IsNullOrEmpty(_name.value))
            {
                _name.SetValueWithoutNotify(string.IsNullOrEmpty(source.DisplayName) ? source.name : source.DisplayName);
            }
        }

        public void SetCatalog(UiPresetCatalog catalog) => _catalog.SetValueWithoutNotify(catalog);

        public void SetName(string name) => _name.SetValueWithoutNotify(name);

        public string Register()
        {
            var catalog = _catalog.value as UiPresetCatalog;
            var tween = _tween.value as UiTweenData;
            var name = _name.value;
            if (catalog == null || tween == null || string.IsNullOrWhiteSpace(name))
            {
                return Say("カタログ・UiTweenData・表示名を指定してください");
            }

            UiPresetCatalogEditing.Register(catalog, name, tween, _category.value);
            Say($"'{name}' を {catalog.name} に登録しました");
            _registered?.Invoke();
            return _status.text;
        }

        private string Say(string text)
        {
            _status.text = text;
            return text;
        }
    }

    // 新 UiTweenEditor の「雛形から始める」の中身。タブ(出現 / 常時 / 消滅 / 強調 / カタログ / ★)・検索・ボタンの一覧から 1 つ選び、
    // 「置き換え」(Tracks を丸ごと入れ替え)か「＋ 追加」(末尾に足す)で適用する。ギャラリー(UiPresetGallery)は同じカード一覧・お気に入りを大きく見る別ウィンドウ。
    // 適用の書き込みは UiTweenPresetOps(旧 UiTweenEditorWindow の「プリセットから Tracks を生成」「＋ プリセットを追加」と同じ規則)。
    internal sealed class UiTweenPresetPicker : VisualElement
    {
        private const int MaxChips = 60;

        private readonly Func<UiTweenData> _target;
        private readonly Func<RectTransform> _previewTarget;
        private readonly Action<string> _say;
        private readonly Action _changed;
        private readonly Action<UiTweenData> _openGallery;
        private readonly IGalleryPrefsStore _prefs = new EditorPrefsGalleryStore();
        private readonly List<Button> _tabButtons = new List<Button>();
        private readonly VisualElement _grid = new VisualElement();
        private readonly Label _note = new Label();
        private readonly Button _replace;
        private readonly Button _append;
        private readonly Button _fav;
        private readonly UiPresetRegisterBox _register;
        private readonly Foldout _registerFold;

        private HashSet<string> _favourites;
        private List<GalleryCard> _cards = new List<GalleryCard>();
        private GalleryTab _tab = GalleryTab.出現;
        private bool _favOnly;
        private string _query = string.Empty;
        private string _selected;

        public UiTweenPresetPicker(Func<UiTweenData> target, Func<RectTransform> previewTarget, Action<string> say, Action changed, Action<UiTweenData> openGallery)
        {
            _target = target;
            _previewTarget = previewTarget;
            _say = say;
            _changed = changed;
            _openGallery = openGallery;
            _favourites = UiPresetGalleryFavorites.Load(_prefs);
            AddToClassList("ux-ut-presets");

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

            var favTab = new Button(() =>
            {
                _favOnly = !_favOnly;
                Render();
            }) { text = "★ お気に入り", tooltip = "お気に入りに入れたプリセットだけを全タブから集めて出す" };
            favTab.AddToClassList("ux-filter");
            _tabButtons.Add(favTab);
            tabs.Add(favTab);

            var search = new TextField();
            search.AddToClassList("ux-itemlist__search");
            search.textEdition.placeholder = "プリセット名で検索";
            search.textEdition.hidePlaceholderOnFocus = true;
            search.RegisterValueChangedCallback(e =>
            {
                _query = (e.newValue ?? string.Empty).Trim();
                Render();
            });
            tabs.Add(search);
            Add(tabs);

            _grid.AddToClassList("ux-ut-grid");
            Add(_grid);

            _note.AddToClassList("ux-dim");
            _note.AddToClassList("ux-note");
            Add(_note);

            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            _replace = new Button(() => Apply(false)) { text = "置き換え", tooltip = "いまの Tracks を丸ごとこのプリセットに置き換える(Undo で戻せます)" };
            _replace.AddToClassList("ux-btn");
            _replace.AddToClassList("ux-btn--primary");
            line.Add(_replace);
            _append = new Button(() => Apply(true)) { text = "＋ 追加", tooltip = "いまの Tracks は残したまま、このプリセットの Track を末尾に足す(スライドイン + フェードインのような組み合わせに。8 本まで)" };
            _append.AddToClassList("ux-btn");
            line.Add(_append);
            _fav = new Button(ToggleFavourite) { text = "☆ お気に入り", tooltip = "選んだプリセットをお気に入りに入れる / 外す(ギャラリーと共通)" };
            _fav.AddToClassList("ux-btn");
            line.Add(_fav);
            var gallery = new Button(() => _openGallery?.Invoke(_target?.Invoke())) { text = "ギャラリーを開く", tooltip = "カードの一覧(曲線の形・お気に入り・シーン上での再生・Canvas の要素への適用)を別ウィンドウで見る" };
            gallery.AddToClassList("ux-btn");
            line.Add(gallery);
            Add(line);

            _registerFold = new Foldout { text = "プリセットとして保存(いまの Tween をカタログに登録)", value = false };
            _registerFold.AddToClassList("ux-ut-regfold");
            _register = new UiPresetRegisterBox(() =>
            {
                Reload();
                Render();
            });
            _registerFold.Add(_register);
            Add(_registerFold);

            Reload();
            Render();
        }

        public UiPresetRegisterBox Register => _register;

        public GalleryTab Tab => _tab;

        public string Selected => _selected;

        public int ShownCount { get; private set; }

        // カタログを読み直す(登録したあと)。
        public void Reload()
        {
            _cards = UiPresetGalleryFilter.BuildBuiltinCards();
            _cards.AddRange(UiPresetGalleryFilter.BuildCatalogCards(UiPresetCatalogUtility.Collect()));
        }

        public void SetSource(UiTweenData source) => _register.SetSource(source);

        public void Select(string name)
        {
            _selected = name;
            Render();
        }

        public void SelectTab(GalleryTab tab)
        {
            _tab = tab;
            _favOnly = false;
            Render();
        }

        private bool TryGetSelected(out GalleryCard card)
        {
            for (var i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].Name == _selected)
                {
                    card = _cards[i];
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
                return UiPresetGalleryFilter.Filter(_cards, _tab, _query, _favourites);
            }

            var all = new List<GalleryCard>();
            foreach (GalleryTab tab in Enum.GetValues(typeof(GalleryTab)))
            {
                all.AddRange(UiPresetGalleryFilter.Filter(_cards, tab, _query, null));
            }

            return all.FindAll(c => _favourites.Contains(c.Name));
        }

        private void Render()
        {
            var tabs = Enum.GetValues(typeof(GalleryTab));
            for (var i = 0; i < _tabButtons.Count; i++)
            {
                var on = i < tabs.Length ? !_favOnly && (int)tabs.GetValue(i) == (int)_tab : _favOnly;
                _tabButtons[i].EnableInClassList("ux-filter--on", on);
            }

            _grid.Clear();
            var shown = Visible();
            ShownCount = shown.Count;
            if (shown.Count == 0)
            {
                var empty = new Label(_favOnly ? "お気に入りはまだありません(プリセットを選んで「☆ お気に入り」)" : "該当するプリセットがありません");
                empty.AddToClassList("ux-dim");
                empty.AddToClassList("ux-note");
                _grid.Add(empty);
            }

            var count = Mathf.Min(shown.Count, MaxChips);
            for (var i = 0; i < count; i++)
            {
                var card = shown[i];
                var b = new Button(() =>
                {
                    _selected = card.Name;
                    Render();
                }) { text = (_favourites.Contains(card.Name) ? "★ " : string.Empty) + card.Name, tooltip = DescribeCard(card) };
                b.AddToClassList("ux-filter");
                b.AddToClassList("ux-ut-chip");
                b.EnableInClassList("ux-filter--on", card.Name == _selected);
                _grid.Add(b);
            }

            var has = TryGetSelected(out var selected);
            var hasTarget = _target?.Invoke() != null;
            _replace.SetEnabled(has && hasTarget);
            _append.SetEnabled(has && hasTarget);
            _fav.SetEnabled(has);
            _fav.text = has && _favourites.Contains(selected.Name) ? "★ お気に入りを外す" : "☆ お気に入り";
            _note.text = has
                ? $"選択中: {selected.Name}({selected.Tab}){DescribeCard(selected, true)}"
                : (shown.Count > MaxChips ? $"{shown.Count} 件中 {MaxChips} 件を表示(検索で絞り込めます)。プリセットを 1 つ選んでください" : "プリセットを 1 つ選んでください(曲線の形はギャラリーで見られます)");
        }

        private static string DescribeCard(in GalleryCard card, bool brief = false)
        {
            if (card.Preset != UiPreset.None)
            {
                return brief ? string.Empty : $"{card.Name}({card.Tab})";
            }

            var n = card.CatalogTween != null && card.CatalogTween.Tracks != null ? card.CatalogTween.Tracks.Length : 0;
            return brief ? $"・Track {n} 本" : $"{card.Name}(カタログ・Track {n} 本)";
        }

        private void ToggleFavourite()
        {
            if (!TryGetSelected(out var card))
            {
                return;
            }

            if (!_favourites.Add(card.Name))
            {
                _favourites.Remove(card.Name);
            }

            UiPresetGalleryFavorites.Save(_prefs, _favourites);
            Render();
        }

        // 置き換え / 追加。成功したら changed を呼ぶ。メッセージを返す(テスト・状態表示用)。
        public string Apply(bool append)
        {
            var data = _target?.Invoke();
            if (data == null || !TryGetSelected(out var card))
            {
                return Say("対象の Data とプリセットを選んでください");
            }

            if (!UiTweenPresetOps.TryResolve(card, _previewTarget?.Invoke(), out var tracks, out var label))
            {
                return Say(card.CatalogTween != null ? "カタログの Tween に Tracks がありません" : "このプリセットは Track を作れませんでした");
            }

            if (append)
            {
                var added = UiTweenPresetOps.Append(data, tracks);
                if (added <= 0)
                {
                    return Say($"Track が上限({UiTweenManager.MaxTracksPerTween} 本)に達しているため追加できません");
                }

                _changed?.Invoke();
                return Say(added < tracks.Length
                    ? $"{label} から Track を {added} 本追加しました(上限のため一部省略)"
                    : $"{label} から Track を {added} 本追加しました");
            }

            UiTweenPresetOps.Replace(data, tracks);
            _changed?.Invoke();
            return Say($"{label} から Tracks を生成しました");
        }

        private string Say(string text)
        {
            _say?.Invoke(text);
            return text;
        }
    }
}
