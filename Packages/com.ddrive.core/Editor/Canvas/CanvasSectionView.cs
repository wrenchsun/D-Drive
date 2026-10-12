using System;
using System.Collections.Generic;
using DDrive.Editor.Ux;
using UnityEngine.UIElements;

namespace DDrive.Editor.CanvasTool
{
    // 新 CanvasEditor の「一覧の欄」(要素の動き・配線・埋め込み・Navigation)の入れ物。UxVisualRow に載せる。
    // 既定は折りたたみ(Foldout。docs/1010 UX-4 / v1.4.1 の決定)。中身 = 1 度だけ作る上部(Static: ボタン列など)+
    // データが変わったら作り直す一覧(List)。データの署名(signature)が変わったときだけ作り直し、毎 150ms の Sync は署名の比較だけ(割り当てなし)。
    internal sealed class CanvasSectionView : VisualElement, IUxSyncable
    {
        private readonly Func<int> _signature;
        private readonly Action<VisualElement> _build;
        private readonly Func<string> _title;
        private int _last = int.MinValue;

        public Foldout Fold { get; }
        public VisualElement Static { get; }
        public VisualElement List { get; }

        public CanvasSectionView(string key, Dictionary<string, bool> openState, Func<string> title, Func<int> signature, Action<VisualElement> build)
        {
            _title = title;
            _signature = signature;
            _build = build;
            AddToClassList("ux-sec");
            Fold = new Foldout { value = openState.TryGetValue(key, out var open) && open };
            Fold.AddToClassList("ux-sec__fold");
            Fold.RegisterValueChangedCallback(e =>
            {
                if (e.target == Fold)
                {
                    openState[key] = e.newValue;
                }
            });
            Static = new VisualElement();
            Static.AddToClassList("ux-sec__static");
            Fold.Add(Static);
            List = new VisualElement();
            List.AddToClassList("ux-sec__list");
            Fold.Add(List);
            Add(Fold);
        }

        // 一覧を作り直す(署名も取り直す)。Static を作り終えたあとに 1 度呼ぶ。
        public void Rebuild()
        {
            _last = _signature();
            List.Clear();
            _build(List);
            Fold.text = _title();
        }

        public void Sync()
        {
            if (_signature() != _last)
            {
                Rebuild();
            }
        }

        public void Open()
        {
            if (!Fold.value)
            {
                Fold.value = true;
            }
        }
    }

    // データの署名(「変わったか」だけを割り当て無しで見る)。
    internal static class CanvasSig
    {
        public static int Add(int h, int v) => unchecked(h * 31 + v);

        public static int Add(int h, string s) => Add(h, s == null ? 0 : s.GetHashCode());

        public static int Add(int h, ulong v) => Add(h, v.GetHashCode());

        public static int Add(int h, float v) => Add(h, v.GetHashCode());
    }
}

namespace DDrive.Editor.CanvasTool
{
    // 一覧の 1 件(1 要素 = 1 行。docs/1010 D-10)。Make は行の見た目を作る(展開した詳細もその行の中だけ)。
    internal sealed class CanvasListItem
    {
        public string Key;
        public string Title;
        public string Chip;
        public int Kind = -1;         // 種類チップの絞り込み用(CanvasItemList の kinds の添字。-1 = 種類なし)
        public bool Configured;       // 「設定済み」か(絞り込み・件数用)
        public bool Summary;          // 件数に数えない要約の行(埋め込みの子の 1 行要約)
        public string Search = string.Empty; // 絞り込みに一致させる語(パス・要約)
        public System.Func<VisualElement> Make;
    }

    // 1 要素 1 行の一覧の入れ物: 一覧の上に絞り込み(検索・設定済み / 未設定・種類のチップ)と件数、本体(20 行を超えたら ListView で仮想化)。
    internal sealed class CanvasItemList : VisualElement
    {
        public const int VirtualizeAbove = 20;

        private readonly string _noun;
        private readonly string _doneWord;
        private readonly string[] _kinds;
        private readonly Label _count;
        private readonly Label _empty;
        private readonly VisualElement _host;
        private readonly Button[] _stateButtons = new Button[3];
        private readonly List<Button> _kindButtons = new List<Button>();
        private readonly TextField _search;
        private List<CanvasListItem> _items = new List<CanvasListItem>();
        private readonly List<CanvasListItem> _shown = new List<CanvasListItem>();
        private ListView _listView;
        private string _query = string.Empty;
        private string _external = string.Empty;
        private int _state;
        private int _kind = -1;

        public int TotalCount => _items.Count;
        public int ShownCount => _shown.Count;

        public CanvasItemList(string noun, string doneWord, string[] kinds, string emptyText)
        {
            _noun = noun;
            _doneWord = doneWord;
            _kinds = kinds ?? new string[0];
            AddToClassList("ux-itemlist");

            var bar = new VisualElement();
            bar.AddToClassList("ux-itemlist__bar");
            _search = new TextField();
            _search.AddToClassList("ux-itemlist__search");
            _search.textEdition.placeholder = "パス・内容で絞り込み";
            _search.textEdition.hidePlaceholderOnFocus = true;
            _search.RegisterValueChangedCallback(e =>
            {
                _query = (e.newValue ?? string.Empty).Trim();
                Render();
            });
            bar.Add(_search);
            var names = new[] { "すべて", doneWord, "未設定" };
            for (var i = 0; i < 3; i++)
            {
                var s = i;
                _stateButtons[i] = Chip(names[i], () =>
                {
                    _state = s;
                    Render();
                });
                bar.Add(_stateButtons[i]);
            }

            for (var i = 0; i < _kinds.Length; i++)
            {
                var k = i;
                var b = Chip(_kinds[i], () =>
                {
                    _kind = _kind == k ? -1 : k;
                    Render();
                });
                _kindButtons.Add(b);
                bar.Add(b);
            }

            Add(bar);
            _count = new Label();
            _count.AddToClassList("ux-dim");
            _count.AddToClassList("ux-itemlist__count");
            Add(_count);
            _host = new VisualElement();
            _host.AddToClassList("ux-itemlist__host");
            Add(_host);
            _empty = new Label(emptyText);
            _empty.AddToClassList("ux-dim");
            _empty.AddToClassList("ux-note");
            Add(_empty);
        }

        private static Button Chip(string text, Action click)
        {
            var b = new Button(click) { text = text };
            b.AddToClassList("ux-filter");
            return b;
        }

        public void SetItems(List<CanvasListItem> items)
        {
            _items = items ?? new List<CanvasListItem>();
            Render();
        }

        // ウィンドウの検索語。一致する件があるときだけ絞る(一致が 0 件なら絞らない = 表の語だけに一致した検索で一覧が空にならない)。
        public void SetExternalQuery(string query)
        {
            query = query ?? string.Empty;
            if (query != _external)
            {
                _external = query;
                Render();
            }
        }

        public void ClearFilters()
        {
            _query = string.Empty;
            _state = 0;
            _kind = -1;
            _search.SetValueWithoutNotify(string.Empty);
        }

        public bool AnyMatch(string query)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (Has(_items[i], query))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Has(CanvasListItem item, string q) =>
            (item.Title != null && item.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
            || (item.Search != null && item.Search.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);

        public void Render()
        {
            var ext = !string.IsNullOrEmpty(_external) && AnyMatch(_external) ? _external : null;
            _shown.Clear();
            var done = 0;
            var total = _items.Count;
            for (var i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.Summary)
                {
                    total--;
                }
                else if (it.Configured)
                {
                    done++;
                }

                if (_state == 1 && !it.Configured || _state == 2 && it.Configured)
                {
                    continue;
                }

                if (_kind >= 0 && it.Kind != _kind)
                {
                    continue;
                }

                if (_query.Length > 0 && !Has(it, _query) || ext != null && !Has(it, ext))
                {
                    continue;
                }

                _shown.Add(it);
            }

            for (var i = 0; i < 3; i++)
            {
                _stateButtons[i].EnableInClassList("ux-filter--on", i == _state);
            }

            for (var i = 0; i < _kindButtons.Count; i++)
            {
                _kindButtons[i].EnableInClassList("ux-filter--on", i == _kind);
            }

            var text = $"{total} {_noun}中 {_doneWord} {done}";
            if (_shown.Count != _items.Count)
            {
                text += $"(表示 {_shown.Count})";
            }

            _count.text = text;
            _count.Show(_items.Count > 0);
            _empty.Show(_items.Count == 0);
            this.Q(className: "ux-itemlist__bar").Show(_items.Count > 0);

            _host.Clear();
            _listView = null;
            if (_shown.Count > VirtualizeAbove)
            {
                _listView = new ListView
                {
                    virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                    selectionType = SelectionType.None,
                    showAlternatingRowBackgrounds = AlternatingRowBackground.None,
                    itemsSource = _shown,
                    makeItem = () => new VisualElement(),
                    bindItem = (e, i) =>
                    {
                        e.Clear();
                        if (i >= 0 && i < _shown.Count)
                        {
                            e.Add(_shown[i].Make());
                        }
                    },
                };
                _listView.AddToClassList("ux-itemlist__lv");
                _host.Add(_listView);
            }
            else
            {
                for (var i = 0; i < _shown.Count; i++)
                {
                    _host.Add(_shown[i].Make());
                }
            }
        }
    }
}
