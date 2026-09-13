using System;
using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Identity;

namespace DDrive.Editor.Spec
{
    // [27_spec_sheet.md] §3.1/§3.2/§4.3 — CSV(生の行)を SpecAssetRow/SpecTuningRow に変換し、
    // 「衝突」(§4.3 の表: 同じ種別+識別子が2行 / 識別子が PascalCase でない / 未知の種別)を検出する。
    // AssetDatabase に依存しない純ロジック(CSV 文字列を直接注入してテストする、5-13 の要件)。
    public static class SpecSheetParser
    {
        // 「アセット」タブの列順([27] §3.1): 種別,カテゴリ,識別子,表示名,状態,担当,仕様,備考
        public static SpecParseResult<SpecAssetRow> ParseAssetSheet(string csv)
        {
            var result = new SpecParseResult<SpecAssetRow>();
            var allRows = SpecCsv.Parse(csv);
            var seenKeys = new Dictionary<string, int>(StringComparer.Ordinal);

            // 1 行目は見出し行として無条件にスキップする([27] §3.1)。
            for (var i = 1; i < allRows.Count; i++)
            {
                var row = allRows[i];
                var rowNumber = i + 1;
                if (SpecCsv.IsIgnoredRow(row))
                {
                    continue;
                }

                var typeCell = Cell(row, 0);
                var category = Cell(row, 1);
                var identifier = Cell(row, 2);
                var displayName = Cell(row, 3);
                var status = Cell(row, 4);
                var assignee = Cell(row, 5);
                var specLink = Cell(row, 6);
                var note = Cell(row, 7);

                if (string.IsNullOrEmpty(typeCell) || string.IsNullOrEmpty(identifier))
                {
                    result.Issues.Add(new SpecIssue(rowNumber, "種別・識別子は必須です(空欄)。"));
                    continue;
                }

                if (!Enum.TryParse<AssetType>(typeCell, ignoreCase: true, out var assetType)
                    || assetType == AssetType.None
                    || !Enum.IsDefined(typeof(AssetType), assetType))
                {
                    result.Issues.Add(new SpecIssue(rowNumber, $"種別 '{typeCell}' が AssetType に見つかりません。"));
                    continue;
                }

                if (!AssetNamingService.IsValidIdentifier(identifier))
                {
                    result.Issues.Add(new SpecIssue(rowNumber, $"識別子 '{identifier}' は PascalCase(先頭大文字・英数字のみ)にしてください。"));
                    continue;
                }

                var key = assetType + "::" + identifier;
                if (seenKeys.TryGetValue(key, out var firstRowNumber))
                {
                    result.Issues.Add(new SpecIssue(rowNumber, $"種別+識別子が {firstRowNumber} 行目と重複しています。"));
                    continue;
                }

                seenKeys[key] = rowNumber;

                result.Rows.Add(new SpecAssetRow
                {
                    RowNumber = rowNumber,
                    Type = assetType,
                    Category = category,
                    Identifier = identifier,
                    DisplayName = displayName,
                    Status = status,
                    Assignee = assignee,
                    SpecLink = specLink,
                    Note = note,
                });
            }

            return result;
        }

        // 「調整値」タブの列順([27] §3.2): キー,値,型,最小,最大,単位,説明
        public static SpecParseResult<SpecTuningRow> ParseTuningSheet(string csv)
        {
            var result = new SpecParseResult<SpecTuningRow>();
            var allRows = SpecCsv.Parse(csv);
            var seenKeys = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var i = 1; i < allRows.Count; i++)
            {
                var row = allRows[i];
                var rowNumber = i + 1;
                if (SpecCsv.IsIgnoredRow(row))
                {
                    continue;
                }

                var key = Cell(row, 0);
                var value = Cell(row, 1);
                var type = Cell(row, 2);

                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value) || string.IsNullOrEmpty(type))
                {
                    result.Issues.Add(new SpecIssue(rowNumber, "キー・値・型は必須です(空欄)。"));
                    continue;
                }

                if (seenKeys.TryGetValue(key, out var firstRowNumber))
                {
                    result.Issues.Add(new SpecIssue(rowNumber, $"キー '{key}' が {firstRowNumber} 行目と重複しています。"));
                    continue;
                }

                seenKeys[key] = rowNumber;

                result.Rows.Add(new SpecTuningRow
                {
                    RowNumber = rowNumber,
                    Key = key,
                    RawValue = value,
                    RawType = type,
                    RawMin = Cell(row, 3),
                    RawMax = Cell(row, 4),
                    Unit = Cell(row, 5),
                    Description = Cell(row, 6),
                });
            }

            return result;
        }

        private static string Cell(string[] row, int index)
            => index < row.Length && row[index] != null ? row[index].Trim() : string.Empty;
    }
}
