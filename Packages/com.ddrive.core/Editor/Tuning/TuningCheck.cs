using System;
using System.Collections.Generic;
using DDrive.Runtime.Tuning;

namespace DDrive.Editor.Tuning
{
    // Tuning エディターの「検証」(docs/1010 UX-15)。UnityEditor に依存しない純関数(EditMode テストで GUI を介さず検証する)。
    // TuningTable そのものの形と値だけを見る軽い検査で、仕様書スナップショットとの突き合わせ(SpecDiffValidator = 調整値が仕様書の範囲外)は
    // Validation > Run All 側。ここは「いま開いている TuningTable の中で矛盾していないか」を編集中に見せるためのもの。
    //   エラー: 値が範囲(Min ≠ Max)の外 / 数値が NaN・∞ / テーブルのセルが列の範囲の外
    //   警告 : キーが空 / キーが重複(後勝ち)/ Enum の値が選択肢に無い・選択肢が空 / テーブルのキー・行 ID の重複
    public static class TuningCheck
    {
        public enum Level
        {
            Error,
            Warning,
        }

        public readonly struct Issue
        {
            public Issue(Level level, string key, string message)
            {
                Level = level;
                Key = key;
                Message = message;
            }

            public Level Level { get; }

            // 該当のキー(または テーブルのキー)。クリックで検索に入れる。空 = 特定のキーに結びつかない。
            public string Key { get; }

            public string Message { get; }
        }

        public static List<Issue> Run(TuningTable table)
        {
            var issues = new List<Issue>();
            if (table == null)
            {
                return issues;
            }

            CheckEntries(table.Entries, issues);
            CheckTables(table.Tables, issues);
            return issues;
        }

        public static int Count(List<Issue> issues, Level level)
        {
            var n = 0;
            for (var i = 0; i < issues.Count; i++)
            {
                if (issues[i].Level == level)
                {
                    n++;
                }
            }

            return n;
        }

        private static void CheckEntries(TuningEntry[] entries, List<Issue> issues)
        {
            if (entries == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var reported = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (string.IsNullOrEmpty(e.Key))
                {
                    issues.Add(new Issue(Level.Warning, string.Empty, $"{i + 1} 番目のキーが空です(読み出されません)"));
                    continue;
                }

                if (!seen.Add(e.Key) && reported.Add(e.Key))
                {
                    issues.Add(new Issue(Level.Warning, e.Key, $"キー '{e.Key}' が重複しています(後ろのものが使われます)"));
                }

                switch (e.Type)
                {
                    case TuningValueType.Float:
                        if (float.IsNaN(e.ValueFloat) || float.IsInfinity(e.ValueFloat))
                        {
                            issues.Add(new Issue(Level.Error, e.Key, $"'{e.Key}' の値が数値ではありません"));
                        }
                        else if (e.Min != e.Max && (e.ValueFloat < e.Min || e.ValueFloat > e.Max))
                        {
                            issues.Add(new Issue(Level.Error, e.Key, $"'{e.Key}' の値 {e.ValueFloat:0.###} が範囲 [{e.Min:0.###}, {e.Max:0.###}] の外です"));
                        }

                        break;

                    case TuningValueType.Int:
                        if (e.Min != e.Max && (e.ValueInt < e.Min || e.ValueInt > e.Max))
                        {
                            issues.Add(new Issue(Level.Error, e.Key, $"'{e.Key}' の値 {e.ValueInt} が範囲 [{e.Min:0.###}, {e.Max:0.###}] の外です"));
                        }

                        break;

                    case TuningValueType.Enum:
                        CheckEnum(e.Key, e.EnumOptions, e.ValueString, issues);
                        break;
                }
            }
        }

        private static void CheckEnum(string label, string[] options, string value, List<Issue> issues)
        {
            if (options == null || options.Length == 0)
            {
                issues.Add(new Issue(Level.Warning, label, $"'{label}' は選択肢型ですが選択肢がありません"));
                return;
            }

            if (Array.IndexOf(options, value ?? string.Empty) < 0)
            {
                issues.Add(new Issue(Level.Warning, label, $"'{label}' の値 '{value}' が選択肢にありません"));
            }
        }

        private static void CheckTables(TuningTableEntry[] tables, List<Issue> issues)
        {
            if (tables == null)
            {
                return;
            }

            var seenTables = new HashSet<string>(StringComparer.Ordinal);
            for (var t = 0; t < tables.Length; t++)
            {
                var table = tables[t];
                var key = table.Key ?? string.Empty;
                if (string.IsNullOrEmpty(key))
                {
                    issues.Add(new Issue(Level.Warning, string.Empty, $"{t + 1} 番目のテーブルのキーが空です"));
                    continue;
                }

                if (!seenTables.Add(key))
                {
                    issues.Add(new Issue(Level.Warning, key, $"テーブル '{key}' のキーが重複しています"));
                }

                var columns = table.Columns ?? Array.Empty<TuningTableColumn>();
                var rows = table.Rows ?? Array.Empty<TuningTableRow>();
                var seenRows = new HashSet<string>(StringComparer.Ordinal);
                for (var r = 0; r < rows.Length; r++)
                {
                    var row = rows[r];
                    if (!seenRows.Add(row.RowId ?? string.Empty))
                    {
                        issues.Add(new Issue(Level.Warning, key, $"テーブル '{key}' の行 ID '{row.RowId}' が重複しています"));
                    }

                    var cells = row.Cells ?? Array.Empty<TuningCellValue>();
                    for (var c = 0; c < columns.Length; c++)
                    {
                        var column = columns[c];
                        TuningCellValue? cell = null;
                        for (var i = 0; i < cells.Length; i++)
                        {
                            if (string.Equals(cells[i].ColumnKey, column.Key, StringComparison.Ordinal))
                            {
                                cell = cells[i];
                                break;
                            }
                        }

                        if (cell == null)
                        {
                            continue;
                        }

                        var where = $"{key} / {row.RowId} / {column.Key}";
                        switch (column.Type)
                        {
                            case TuningValueType.Float:
                                if (column.Min != column.Max && (cell.Value.F < column.Min || cell.Value.F > column.Max))
                                {
                                    issues.Add(new Issue(Level.Error, key, $"{where} の値 {cell.Value.F:0.###} が範囲 [{column.Min:0.###}, {column.Max:0.###}] の外です"));
                                }

                                break;
                            case TuningValueType.Int:
                                if (column.Min != column.Max && (cell.Value.I < column.Min || cell.Value.I > column.Max))
                                {
                                    issues.Add(new Issue(Level.Error, key, $"{where} の値 {cell.Value.I} が範囲 [{column.Min:0.###}, {column.Max:0.###}] の外です"));
                                }

                                break;
                            case TuningValueType.Enum:
                                if (column.EnumOptions != null && column.EnumOptions.Length > 0 && Array.IndexOf(column.EnumOptions, cell.Value.S ?? string.Empty) < 0)
                                {
                                    issues.Add(new Issue(Level.Warning, key, $"{where} の値 '{cell.Value.S}' が選択肢にありません"));
                                }

                                break;
                        }
                    }
                }
            }
        }
    }
}
