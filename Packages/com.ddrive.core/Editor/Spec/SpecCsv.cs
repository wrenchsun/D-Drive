using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DDrive.Editor.Spec
{
    // [27_spec_sheet.md] §4.1 — CSV パース(引用符・カンマ・改行入りセルに対応)と gviz URL の組み立て。
    // AssetDatabase / UnityEditor に依存しない純ロジックとしてテスト可能に保つ(CSV 文字列を直接注入できる)。
    public static class SpecCsv
    {
        private static readonly Regex SpreadsheetIdPattern = new(@"/d/([a-zA-Z0-9-_]+)", RegexOptions.Compiled);

        // フルの編集用 URL("https://docs.google.com/spreadsheets/d/<ID>/edit#gid=...")からも
        // ID だけの入力からも同じ gviz CSV URL を組み立てる([27] §4.1)。
        public static string BuildGvizCsvUrl(string spreadsheetUrlOrId, string sheetName)
        {
            if (string.IsNullOrEmpty(spreadsheetUrlOrId))
            {
                return null;
            }

            var id = spreadsheetUrlOrId;
            var match = SpreadsheetIdPattern.Match(spreadsheetUrlOrId);
            if (match.Success)
            {
                id = match.Groups[1].Value;
            }

            var sheet = string.IsNullOrEmpty(sheetName) ? string.Empty : System.Uri.EscapeDataString(sheetName);
            return $"https://docs.google.com/spreadsheets/d/{id}/gviz/tq?tqx=out:csv&sheet={sheet}";
        }

        // 引用符("...")・カンマ・改行入りセル(RFC4180 相当)に対応した最小限の CSV パーサ。
        // 行頭が '#' の行、完全に空の行は呼び出し側([27] §3.1/§3.2)で無視する(ここでは生の行を返すだけ)。
        public static List<string[]> Parse(string csv)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(csv))
            {
                return rows;
            }

            var fields = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;
            var i = 0;
            var length = csv.Length;

            void EndField()
            {
                fields.Add(current.ToString());
                current.Length = 0;
            }

            void EndRow()
            {
                EndField();
                rows.Add(fields.ToArray());
                fields.Clear();
            }

            while (i < length)
            {
                var c = csv[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < length && csv[i + 1] == '"')
                        {
                            current.Append('"');
                            i += 2;
                            continue;
                        }

                        inQuotes = false;
                        i++;
                        continue;
                    }

                    current.Append(c);
                    i++;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        i++;
                        break;
                    case ',':
                        EndField();
                        i++;
                        break;
                    case '\r':
                        i++;
                        break;
                    case '\n':
                        EndRow();
                        i++;
                        break;
                    default:
                        current.Append(c);
                        i++;
                        break;
                }
            }

            // 末尾に改行が無いファイルの最後の行も取り込む。
            if (current.Length > 0 || fields.Count > 0)
            {
                EndRow();
            }

            return rows;
        }

        // '#' 始まりの行・完全な空行を無視する([27] §3.1)。1 列目が空かどうかだけでなく、
        // 全列が空文字のときも空行とみなす。
        public static bool IsIgnoredRow(string[] row)
        {
            if (row == null || row.Length == 0)
            {
                return true;
            }

            var allEmpty = true;
            foreach (var cell in row)
            {
                if (!string.IsNullOrWhiteSpace(cell))
                {
                    allEmpty = false;
                    break;
                }
            }

            if (allEmpty)
            {
                return true;
            }

            return row[0] != null && row[0].TrimStart().StartsWith("#");
        }
    }
}
