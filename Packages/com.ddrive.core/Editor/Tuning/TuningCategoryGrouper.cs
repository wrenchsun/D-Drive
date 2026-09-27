using System;
using System.Collections.Generic;
using DDrive.Runtime.Tuning;

namespace DDrive.Editor.Tuning
{
    // M-2a(2026-09-27。[11_tasks.md] M-2 チケット、[09_editor_tools.md] §「Tuning ウィンドウ」) —
    // TuningEditorWindow のカテゴリ分類ロジックを UnityEditor API に依存しない純関数として切り出したもの
    // (EditMode テストで GUI を介さず検証するため)。キーの "<機能>/<名前>" 接頭辞([27_spec_sheet.md] §3.2)
    // でグループ化する。データに Category フィールドは足さない(既存 .asset 無変更、[42] §5.11-2 の
    // Compat スナップショットに影響しない)。
    public static class TuningCategoryGrouper
    {
        public const string Uncategorized = "(未分類)";

        public readonly struct Category
        {
            public Category(string name, IReadOnlyList<int> entryIndices)
            {
                Name = name;
                EntryIndices = entryIndices;
            }

            public string Name { get; }

            // 呼び出し元が渡した Entries 配列への添字。キーの昇順(Ordinal)で並ぶ。
            public IReadOnlyList<int> EntryIndices { get; }

            public int Count => EntryIndices.Count;
        }

        // 名前順(Ordinal)。"(未分類)" は常に最後に並べる([09] のモック画面と同じ並び)。
        public static IReadOnlyList<Category> Group(IReadOnlyList<TuningEntry> entries)
        {
            var byCategory = new Dictionary<string, List<int>>(StringComparer.Ordinal);

            if (entries != null)
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var category = ResolveCategory(entries[i].Key);
                    if (!byCategory.TryGetValue(category, out var list))
                    {
                        list = new List<int>();
                        byCategory[category] = list;
                    }

                    list.Add(i);
                }
            }

            foreach (var list in byCategory.Values)
            {
                list.Sort((a, b) => string.CompareOrdinal(entries[a].Key, entries[b].Key));
            }

            var names = new List<string>(byCategory.Keys);
            names.Sort((a, b) =>
            {
                if (string.Equals(a, Uncategorized, StringComparison.Ordinal) && string.Equals(b, Uncategorized, StringComparison.Ordinal))
                {
                    return 0;
                }

                if (string.Equals(a, Uncategorized, StringComparison.Ordinal))
                {
                    return 1;
                }

                if (string.Equals(b, Uncategorized, StringComparison.Ordinal))
                {
                    return -1;
                }

                return string.CompareOrdinal(a, b);
            });

            var result = new List<Category>(names.Count);
            foreach (var name in names)
            {
                result.Add(new Category(name, byCategory[name]));
            }

            return result;
        }

        // キーの "/" より前の部分がカテゴリ。"/" が無ければ(未分類)。
        public static string ResolveCategory(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return Uncategorized;
            }

            var slash = key.IndexOf('/');
            return slash > 0 ? key.Substring(0, slash) : Uncategorized;
        }

        // 表示名 = キーの "/" 以降([09] 「Tuning ウィンドウ」節)。
        public static string DisplayName(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return key ?? string.Empty;
            }

            var slash = key.IndexOf('/');
            return slash >= 0 && slash + 1 < key.Length ? key.Substring(slash + 1) : key;
        }
    }
}
