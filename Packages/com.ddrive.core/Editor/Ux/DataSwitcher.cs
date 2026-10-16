using System;
using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ux
{
    // ウィンドウ内で別の Data へ切り替える部品(◀ [名前 ▼] ▶。試験版 E から移植。種別非依存で、Data の型名で一覧を引く)。
    // ドロップダウンは AdvancedDropdown(検索付き。先頭に「最近開いた」、以降は Category 別)。◀ ▶ は同じ Category 内を表示名順に移る。
    // 🔒 固定とは独立(固定中でも切り替えられる)。
    public sealed class DataSwitcher : VisualElement
    {
        private const string RecentKeyPrefix = "DDrive.Ux.Recent.";
        private const int RecentMax = 5;

        private readonly Type _dataType;
        private readonly Func<AssetDataBase> _current;
        private readonly Action<AssetDataBase> _set;
        private readonly Button _prev;
        private readonly Button _next;
        private readonly Button _drop;

        public DataSwitcher(Type dataType, Func<AssetDataBase> current, Action<AssetDataBase> set)
        {
            _dataType = dataType;
            _current = current;
            _set = set;
            AddToClassList("ux-switch");

            _prev = new Button(() => Step(-1)) { text = "◀" };
            _prev.AddToClassList("ux-btn");
            _prev.AddToClassList("ux-switch__step");
            Add(_prev);

            _drop = new Button(OpenDropdown);
            _drop.AddToClassList("ux-btn");
            _drop.AddToClassList("ux-switch__drop");
            Add(_drop);

            _next = new Button(() => Step(1)) { text = "▶" };
            _next.AddToClassList("ux-btn");
            _next.AddToClassList("ux-switch__step");
            Add(_next);
            Refresh();
        }

        // Refresh は 150ms ごとに呼ばれる。同じ種別の一覧(Category 順)は全ウィンドウで共有する静的キャッシュにし、
        // AssetDatabase の変更(インポート・削除・移動)があったときだけ捨てる(窓の数だけ全走査しない)。
        private static readonly Dictionary<Type, List<AssetDataBase>> SharedAll = new Dictionary<Type, List<AssetDataBase>>();

        [InitializeOnLoadMethod]
        private static void HookInvalidate()
        {
            EditorApplication.projectChanged -= InvalidateShared;
            EditorApplication.projectChanged += InvalidateShared;
        }

        private static int _version;

        internal static void InvalidateShared()
        {
            SharedAll.Clear();
            _version++;
        }

        private List<AssetDataBase> _siblingCache;
        private AssetDataBase _siblingFor;
        private int _siblingVersion = -1;
        private string _siblingCat;

        private static List<AssetDataBase> CachedAll(Type dataType)
        {
            if (!SharedAll.TryGetValue(dataType, out var all))
            {
                all = CollectAll(dataType);
                SharedAll[dataType] = all;
            }

            return all;
        }

        private List<AssetDataBase> CachedSiblings(AssetDataBase cur)
        {
            if (cur == null)
            {
                return new List<AssetDataBase>();
            }

            var cat = CategoryOf(cur);
            if (_siblingCache != null && _siblingFor == cur && _siblingVersion == _version && _siblingCat == cat)
            {
                return _siblingCache;
            }

            var all = CachedAll(_dataType);
            var list = new List<AssetDataBase>();
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] != null && CategoryOf(all[i]) == cat)
                {
                    list.Add(all[i]);
                }
            }

            _siblingCache = list;
            _siblingFor = cur;
            _siblingVersion = _version;
            _siblingCat = cat;
            return list;
        }

        public void Refresh()
        {
            var cur = _current();
            var text = cur == null ? "Data を選ぶ ▾" : Name(cur) + " ▾";
            if (_drop.text != text)
            {
                _drop.text = text;
            }

            _drop.tooltip = "別の " + _dataType.Name + " に切り替える(検索できます)";
            var siblings = CachedSiblings(cur);
            var idx = siblings.IndexOf(cur);
            var prev = idx > 0 ? siblings[idx - 1] : null;
            var next = idx >= 0 && idx < siblings.Count - 1 ? siblings[idx + 1] : null;
            _prev.SetEnabled(prev != null);
            _next.SetEnabled(next != null);
            _prev.tooltip = prev != null ? "前: " + Name(prev) : "前の Data はありません(同じカテゴリの先頭)";
            _next.tooltip = next != null ? "次: " + Name(next) : "次の Data はありません(同じカテゴリの末尾)";
        }

        private void Step(int dir)
        {
            var cur = _current();
            var siblings = Siblings(_dataType, cur);
            var idx = siblings.IndexOf(cur) + dir;
            if (idx >= 0 && idx < siblings.Count)
            {
                Select(siblings[idx]);
            }
        }

        private void Select(AssetDataBase d)
        {
            if (d == null)
            {
                return;
            }

            Record(_dataType, d);
            _set(d);
        }

        private void OpenDropdown()
        {
            var dd = new SwitchDropdown(new AdvancedDropdownState(), _dataType.Name, CollectAll(_dataType), LoadRecent(_dataType), _current(), Select);
            dd.Show(_drop.worldBound);
        }

        // ── 一覧 ──

        internal static string Name(AssetDataBase d) => string.IsNullOrEmpty(d.DisplayName) ? d.name : d.DisplayName;

        internal static string CategoryOf(AssetDataBase d) => string.IsNullOrEmpty(d.Category) ? "未分類" : d.Category;

        internal static List<AssetDataBase> CollectAll(Type dataType)
        {
            var list = new List<AssetDataBase>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + dataType.Name))
            {
                var d = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), dataType) as AssetDataBase;
                if (d != null)
                {
                    list.Add(d);
                }
            }

            return list.OrderBy(CategoryOf, StringComparer.Ordinal).ThenBy(Name, StringComparer.Ordinal).ToList();
        }

        private static List<AssetDataBase> Siblings(Type dataType, AssetDataBase cur)
        {
            if (cur == null)
            {
                return new List<AssetDataBase>();
            }

            var cat = CategoryOf(cur);
            return CollectAll(dataType).Where(d => CategoryOf(d) == cat).ToList();
        }

        // ── 最近開いた Data(SessionState) ──

        internal static void Record(Type dataType, AssetDataBase d)
        {
            if (d == null)
            {
                return;
            }

            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(d));
            if (string.IsNullOrEmpty(guid))
            {
                return;
            }

            var ids = SessionState.GetString(RecentKeyPrefix + dataType.Name, string.Empty).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            ids.Remove(guid);
            ids.Insert(0, guid);
            if (ids.Count > RecentMax)
            {
                ids.RemoveRange(RecentMax, ids.Count - RecentMax);
            }

            SessionState.SetString(RecentKeyPrefix + dataType.Name, string.Join("|", ids));
        }

        internal static List<AssetDataBase> LoadRecent(Type dataType)
        {
            var result = new List<AssetDataBase>();
            foreach (var guid in SessionState.GetString(RecentKeyPrefix + dataType.Name, string.Empty).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var d = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), dataType) as AssetDataBase;
                if (d != null)
                {
                    result.Add(d);
                }
            }

            return result;
        }

        private sealed class Item : AdvancedDropdownItem
        {
            public readonly AssetDataBase Data;

            public Item(string name, AssetDataBase data) : base(name)
            {
                Data = data;
            }
        }

        private sealed class SwitchDropdown : AdvancedDropdown
        {
            private readonly string _title;
            private readonly List<AssetDataBase> _all;
            private readonly List<AssetDataBase> _recent;
            private readonly AssetDataBase _current;
            private readonly Action<AssetDataBase> _pick;

            public SwitchDropdown(AdvancedDropdownState state, string title, List<AssetDataBase> all, List<AssetDataBase> recent, AssetDataBase current, Action<AssetDataBase> pick) : base(state)
            {
                _title = title;
                _all = all;
                _recent = recent;
                _current = current;
                _pick = pick;
                minimumSize = new Vector2(280, 320);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem(_title);
                if (_recent.Count > 0)
                {
                    var rec = new AdvancedDropdownItem("最近開いた");
                    foreach (var d in _recent)
                    {
                        rec.AddChild(new Item(Name(d), d));
                    }

                    root.AddChild(rec);
                }

                foreach (var group in _all.GroupBy(CategoryOf))
                {
                    var cat = new AdvancedDropdownItem(group.Key);
                    foreach (var d in group)
                    {
                        cat.AddChild(new Item(Name(d), d) { icon = d == _current ? EditorGUIUtility.FindTexture("d_FilterSelectedOnly") : null });
                    }

                    root.AddChild(cat);
                }

                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item is Item i && i.Data != null)
                {
                    _pick(i.Data);
                }
            }
        }
    }
}
