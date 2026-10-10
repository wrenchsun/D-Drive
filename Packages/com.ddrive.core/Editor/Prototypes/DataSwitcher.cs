using System;
using System.Collections.Generic;
using System.Linq;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.EditorPrototypes
{
    // ウィンドウ内で別の VfxData へ切り替える部品(◀ [名前 ▼] ▶)。
    // ドロップダウンは AdvancedDropdown(検索付き。先頭に「最近開いた」、以降は Category 別)。◀ ▶ は同じ Category 内を表示名順に移る。
    // 🔒 固定とは独立(固定中でも切り替えられる)。
    internal sealed class DataSwitcher : VisualElement
    {
        private const string RecentKey = "DDrive.PrototypeE.Recent";
        private const int RecentMax = 5;

        private readonly Func<VfxData> _current;
        private readonly Action<VfxData> _set;
        private readonly Button _prev;
        private readonly Button _next;
        private readonly Button _drop;

        public DataSwitcher(Func<VfxData> current, Action<VfxData> set)
        {
            _current = current;
            _set = set;
            AddToClassList("pe-switch");

            _prev = new Button(() => Step(-1)) { text = "◀" };
            _prev.AddToClassList("pd-btn");
            _prev.AddToClassList("pe-switch__step");
            Add(_prev);

            _drop = new Button(OpenDropdown);
            _drop.AddToClassList("pd-btn");
            _drop.AddToClassList("pe-switch__drop");
            Add(_drop);

            _next = new Button(() => Step(1)) { text = "▶" };
            _next.AddToClassList("pd-btn");
            _next.AddToClassList("pe-switch__step");
            Add(_next);
            Refresh();
        }

        public void Refresh()
        {
            var cur = _current();
            var text = cur == null ? "Data を選ぶ ▾" : Name(cur) + " ▾";
            if (_drop.text != text)
            {
                _drop.text = text;
            }

            _drop.tooltip = "別の VfxData に切り替える(検索できます)";
            var siblings = Siblings(cur);
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
            var siblings = Siblings(cur);
            var idx = siblings.IndexOf(cur) + dir;
            if (idx >= 0 && idx < siblings.Count)
            {
                Select(siblings[idx]);
            }
        }

        private void Select(VfxData d)
        {
            if (d == null)
            {
                return;
            }

            Record(d);
            _set(d);
        }

        private void OpenDropdown()
        {
            var dd = new SwitchDropdown(new AdvancedDropdownState(), CollectAll(), LoadRecent(), _current(), Select);
            dd.Show(_drop.worldBound);
        }

        // ── 一覧 ──

        public static string Name(VfxData d) => string.IsNullOrEmpty(d.DisplayName) ? d.name : d.DisplayName;

        public static string CategoryOf(VfxData d) => string.IsNullOrEmpty(d.Category) ? "未分類" : d.Category;

        public static List<VfxData> CollectAll()
        {
            var list = new List<VfxData>();
            foreach (var guid in AssetDatabase.FindAssets("t:VfxData"))
            {
                var d = AssetDatabase.LoadAssetAtPath<VfxData>(AssetDatabase.GUIDToAssetPath(guid));
                if (d != null)
                {
                    list.Add(d);
                }
            }

            return list.OrderBy(CategoryOf, StringComparer.Ordinal).ThenBy(Name, StringComparer.Ordinal).ToList();
        }

        private static List<VfxData> Siblings(VfxData cur)
        {
            if (cur == null)
            {
                return new List<VfxData>();
            }

            var cat = CategoryOf(cur);
            return CollectAll().Where(d => CategoryOf(d) == cat).ToList();
        }

        // ── 最近開いた Data(SessionState) ──

        public static void Record(VfxData d)
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

            var ids = SessionState.GetString(RecentKey, string.Empty).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            ids.Remove(guid);
            ids.Insert(0, guid);
            if (ids.Count > RecentMax)
            {
                ids.RemoveRange(RecentMax, ids.Count - RecentMax);
            }

            SessionState.SetString(RecentKey, string.Join("|", ids));
        }

        public static List<VfxData> LoadRecent()
        {
            var result = new List<VfxData>();
            foreach (var guid in SessionState.GetString(RecentKey, string.Empty).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var d = AssetDatabase.LoadAssetAtPath<VfxData>(AssetDatabase.GUIDToAssetPath(guid));
                if (d != null)
                {
                    result.Add(d);
                }
            }

            return result;
        }

        private sealed class Item : AdvancedDropdownItem
        {
            public readonly VfxData Data;

            public Item(string name, VfxData data) : base(name)
            {
                Data = data;
            }
        }

        private sealed class SwitchDropdown : AdvancedDropdown
        {
            private readonly List<VfxData> _all;
            private readonly List<VfxData> _recent;
            private readonly VfxData _current;
            private readonly Action<VfxData> _pick;

            public SwitchDropdown(AdvancedDropdownState state, List<VfxData> all, List<VfxData> recent, VfxData current, Action<VfxData> pick) : base(state)
            {
                _all = all;
                _recent = recent;
                _current = current;
                _pick = pick;
                minimumSize = new Vector2(280, 320);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("VfxData");
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
