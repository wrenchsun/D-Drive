using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Inspectors;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Tuning;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Spec
{
    // [27_spec_sheet.md] §4.3/§3.2/§4.4 — 差分の適用(新規作成/既存アセットの上書き)と、
    // 調整値タブの TuningTable への取り込み、貼り付け補助(選択肢/既存アセットの TSV)。
    public static class SpecSyncService
    {
        // ── アセットタブ: 新規 → Placeholder 作成(既存の AssetCreationService をそのまま使う) ──

        public static AssetDataBase ApplyNew(SpecAssetChange change, string gameDataRoot)
        {
            var dataType = ResolveDataType(change.Row.Type);
            if (dataType == null)
            {
                Debug.LogWarning($"[DDrive] 仕様書同期: 種別 '{change.Row.Type}' に対応する Data 型を一意に決められないため、行 {change.Row.RowNumber} の自動作成をスキップしました(手動で作成してください)。");
                return null;
            }

            var row = change.Row;
            return AssetCreationService.Create(
                dataType, row.Type, row.DisplayName, row.Category, row.Identifier,
                configure: a => ApplyExtraFields(a, row),
                gameDataRoot: gameDataRoot);
        }

        // ── アセットタブ: 変更 → 上書きしてよい項目だけ反映(Undo.RecordObject + SetDirty) ──

        public static void ApplyChanged(SpecAssetChange change)
        {
            var asset = change.ExistingAsset;
            if (asset == null)
            {
                return;
            }

            Undo.RecordObject(asset, "仕様書と同期(変更を反映)");
            asset.DisplayName = change.Row.DisplayName;
            asset.Category = change.Row.Category;
            ApplyExtraFields(asset, change.Row);
            EditorUtility.SetDirty(asset);
        }

        // public: NewAssetDialog の「仕様書から選ぶ」(5-16)もここを呼ぶ。ダイアログ経由で作った結果と
        // 同期の「新規 → Placeholder 作成」の結果が食い違わないよう、状態タグ/Assignee/Description/SpecUrl の
        // 反映ロジックをコピペせずここ 1 箇所に保つ。
        public static void ApplyExtraFields(AssetDataBase asset, SpecAssetRow row)
        {
            asset.Tags = SpecStatusTag.WithStatus(asset.Tags, row.Status);
            asset.Assignee = row.Assignee;
            asset.Description = row.Note;
            // シート側が空のときは既存の仕様リンクを消さない(手で貼ったリンクを保護する。SpecDiffService と対称)。
            if (!string.IsNullOrEmpty(row.SpecLink))
            {
                asset.SpecUrl = row.SpecLink;
            }
        }

        // 1 つの AssetType に対して具象 Data 型が複数ある場合(例: ControlSkin = ButtonSkinData / SliderSkinData)は
        // どちらを作るべきか一意に決められないため、自動作成の対象外にする(docs/28 の「要判断」に記載)。
        private static Type ResolveDataType(AssetType assetType)
        {
            Type found = null;
            var count = 0;
            foreach (var (dataType, definedType) in AssetIdLookup.GetAllDefinitions())
            {
                if (definedType != assetType || (dataType.Namespace != null && dataType.Namespace.Contains("Tests")))
                {
                    continue;
                }

                found = dataType;
                count++;
            }

            return count == 1 ? found : null;
        }

        // ── 調整値タブ → TuningTable ──

        public static void ApplyTuning(SpecParseResult<SpecTuningRow> parsed, TuningTable table)
        {
            if (table == null)
            {
                return;
            }

            var entries = new List<TuningEntry>(parsed.Rows.Count);
            foreach (var row in parsed.Rows)
            {
                if (TryBuildEntry(row, out var entry))
                {
                    entries.Add(entry);
                }
            }

            Undo.RecordObject(table, "仕様書の調整値を同期");
            table.Entries = entries.ToArray();
            table.RebuildIndex();
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
        }

        private static bool TryBuildEntry(SpecTuningRow row, out TuningEntry entry)
        {
            entry = default;
            if (!Enum.TryParse<TuningValueType>(row.RawType, ignoreCase: true, out var valueType))
            {
                Debug.LogWarning($"[DDrive] 調整値 '{row.Key}'(行 {row.RowNumber}): 型 '{row.RawType}' は float/int/bool/string のいずれかにしてください。この行はスキップします。");
                return false;
            }

            entry.Key = row.Key;
            entry.Type = valueType;
            entry.Unit = row.Unit;
            entry.Description = row.Description;

            switch (valueType)
            {
                case TuningValueType.Float:
                    if (!TryParseFloat(row.RawValue, out entry.ValueFloat))
                    {
                        Debug.LogWarning($"[DDrive] 調整値 '{row.Key}'(行 {row.RowNumber}): 値 '{row.RawValue}' を float として解釈できません。この行はスキップします。");
                        return false;
                    }

                    break;

                case TuningValueType.Int:
                    if (!int.TryParse(row.RawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out entry.ValueInt))
                    {
                        Debug.LogWarning($"[DDrive] 調整値 '{row.Key}'(行 {row.RowNumber}): 値 '{row.RawValue}' を int として解釈できません。この行はスキップします。");
                        return false;
                    }

                    break;

                case TuningValueType.Bool:
                    if (!TryParseBool(row.RawValue, out entry.ValueBool))
                    {
                        Debug.LogWarning($"[DDrive] 調整値 '{row.Key}'(行 {row.RowNumber}): 値 '{row.RawValue}' を bool として解釈できません(true/false/1/0)。この行はスキップします。");
                        return false;
                    }

                    break;

                case TuningValueType.String:
                    entry.ValueString = row.RawValue;
                    break;

                case TuningValueType.Enum:
                    // W-10(案A): Enum は選択肢(RawEnumOptions)必須。値はその中に含まれること。
                    if (row.RawEnumOptions == null || row.RawEnumOptions.Length == 0)
                    {
                        Debug.LogWarning($"[DDrive] 調整値 '{row.Key}'(行 {row.RowNumber}): enum の選択肢が空です。この行はスキップします。");
                        return false;
                    }

                    if (Array.IndexOf(row.RawEnumOptions, row.RawValue) < 0)
                    {
                        Debug.LogWarning($"[DDrive] 調整値 '{row.Key}'(行 {row.RowNumber}): 値 '{row.RawValue}' が enum の選択肢に含まれません。この行はスキップします。");
                        return false;
                    }

                    entry.ValueString = row.RawValue;
                    entry.EnumOptions = row.RawEnumOptions;
                    break;
            }

            TryParseFloat(row.RawMin, out entry.Min);
            TryParseFloat(row.RawMax, out entry.Max);
            return true;
        }

        // ── 調整値タブ(テーブル型) → TuningTable.Tables(W-10、案A) ──
        // 既存の Entries(スカラー)には触れない(ApplyTuning とは独立に呼べる)。
        public static void ApplyTuningTable(SpecParseResult<SpecTuningTableRow> parsed, TuningTable table)
        {
            if (table == null)
            {
                return;
            }

            var entries = new List<TuningTableEntry>(parsed.Rows.Count);
            foreach (var row in parsed.Rows)
            {
                if (TryBuildTableEntry(row, out var entry))
                {
                    entries.Add(entry);
                }
            }

            Undo.RecordObject(table, "仕様書のテーブル調整値を同期");
            table.Tables = entries.ToArray();
            table.RebuildIndex();
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
        }

        private static bool TryBuildTableEntry(SpecTuningTableRow row, out TuningTableEntry entry)
        {
            entry = default;
            var raw = row.Raw;
            if (raw == null)
            {
                Debug.LogWarning($"[DDrive] 調整値テーブル '{row.Key}': 本体が空です。この行はスキップします。");
                return false;
            }

            var columnsRaw = raw["columns"] as JArray;
            if (columnsRaw == null)
            {
                Debug.LogWarning($"[DDrive] 調整値テーブル '{row.Key}': columns がありません。この行はスキップします。");
                return false;
            }

            var columns = new List<TuningTableColumn>(columnsRaw.Count);
            foreach (var columnToken in columnsRaw)
            {
                if (columnToken is not JObject columnObj)
                {
                    continue;
                }

                var columnKey = (string)columnObj["key"];
                if (string.IsNullOrEmpty(columnKey))
                {
                    Debug.LogWarning($"[DDrive] 調整値テーブル '{row.Key}': key の無い列があります。スキップします。");
                    continue;
                }

                if (!TryParseValueType((string)columnObj["valueType"], out var columnType))
                {
                    Debug.LogWarning($"[DDrive] 調整値テーブル '{row.Key}' の列 '{columnKey}': valueType '{columnObj["valueType"]}' を解釈できません。スキップします。");
                    continue;
                }

                columns.Add(new TuningTableColumn
                {
                    Key = columnKey,
                    Type = columnType,
                    Min = ToFloatOrZero(columnObj["min"]),
                    Max = ToFloatOrZero(columnObj["max"]),
                    Unit = (string)columnObj["unit"] ?? string.Empty,
                    EnumOptions = ToStringArray(columnObj["enumOptions"] as JArray),
                });
            }

            var rowsRaw = raw["rows"] as JArray ?? new JArray();
            var rows = new List<TuningTableRow>(rowsRaw.Count);
            foreach (var rowToken in rowsRaw)
            {
                if (rowToken is not JObject rowObj)
                {
                    continue;
                }

                var rowId = (string)rowObj["rowId"];
                if (string.IsNullOrEmpty(rowId))
                {
                    Debug.LogWarning($"[DDrive] 調整値テーブル '{row.Key}': rowId の無い行があります。スキップします。");
                    continue;
                }

                var cellsObj = rowObj["cells"] as JObject;
                var cells = new List<TuningCellValue>(columns.Count);
                foreach (var column in columns)
                {
                    var cellToken = cellsObj?[column.Key];
                    cells.Add(BuildCellValue(column, cellToken));
                }

                rows.Add(new TuningTableRow { RowId = rowId, Cells = cells.ToArray() });
            }

            entry = new TuningTableEntry
            {
                Key = row.Key,
                Columns = columns.ToArray(),
                Rows = rows.ToArray(),
            };
            return true;
        }

        private static TuningCellValue BuildCellValue(TuningTableColumn column, JToken cellToken)
        {
            var cell = new TuningCellValue { ColumnKey = column.Key };
            switch (column.Type)
            {
                case TuningValueType.Float:
                    cell.F = cellToken?.Type == JTokenType.Float || cellToken?.Type == JTokenType.Integer
                        ? cellToken.ToObject<float>()
                        : 0f;
                    break;
                case TuningValueType.Int:
                    cell.I = cellToken?.Type == JTokenType.Integer || cellToken?.Type == JTokenType.Float
                        ? cellToken.ToObject<int>()
                        : 0;
                    break;
                case TuningValueType.Bool:
                    cell.B = cellToken?.Type == JTokenType.Boolean && cellToken.ToObject<bool>();
                    break;
                case TuningValueType.String:
                case TuningValueType.Enum:
                    cell.S = cellToken?.Type == JTokenType.String ? cellToken.ToObject<string>() : string.Empty;
                    break;
            }

            return cell;
        }

        private static bool TryParseValueType(string raw, out TuningValueType type)
            => Enum.TryParse(raw, ignoreCase: true, out type) && Enum.IsDefined(typeof(TuningValueType), type);

        private static float ToFloatOrZero(JToken token)
            => token != null && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) ? token.ToObject<float>() : 0f;

        private static string[] ToStringArray(JArray array)
        {
            if (array == null || array.Count == 0)
            {
                return Array.Empty<string>();
            }

            var result = new string[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                result[i] = (string)array[i] ?? string.Empty;
            }

            return result;
        }

        private static bool TryParseFloat(string raw, out float value)
            => float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static bool TryParseBool(string raw, out bool value)
        {
            if (bool.TryParse(raw, out value))
            {
                return true;
            }

            if (raw == "1")
            {
                value = true;
                return true;
            }

            if (raw == "0")
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }

        // ── [27] §4.4: D-Drive → シートへ貼る補助(TSV) ──

        // 「選択肢をコピー」: 種別・状態・型の一覧(テンプレートの「_選択肢」タブと同じ列構成)。
        public static string BuildChoicesTsv()
        {
            var types = Enum.GetValues(typeof(AssetType)).Cast<AssetType>()
                .Where(t => t != AssetType.None)
                .Select(t => t.ToString())
                .ToArray();
            var states = new[] { "未着手", "仮", "本番", "保留" };
            var valueTypes = Enum.GetNames(typeof(TuningValueType)).Select(n => n.ToLowerInvariant()).ToArray();

            var sb = new StringBuilder();
            sb.Append("種別\t状態\t型\n");
            var rowCount = Math.Max(types.Length, Math.Max(states.Length, valueTypes.Length));
            for (var i = 0; i < rowCount; i++)
            {
                sb.Append(i < types.Length ? types[i] : string.Empty);
                sb.Append('\t');
                sb.Append(i < states.Length ? states[i] : string.Empty);
                sb.Append('\t');
                sb.Append(i < valueTypes.Length ? valueTypes[i] : string.Empty);
                sb.Append('\n');
            }

            return sb.ToString();
        }

        // 「既存アセットをコピー」: 既に D-Drive にあるアセットを「アセット」タブの形式でコピーする(運用開始時の初期入力用)。
        public static string BuildExistingAssetsTsv()
        {
            var sb = new StringBuilder();
            sb.Append("種別\tカテゴリ\t識別子\t表示名\t状態\t担当\t仕様\t備考\n");

            var index = SpecDiffService.BuildExistingIndex();
            var rows = new List<(string type, string category, string identifier, AssetDataBase asset)>();
            foreach (var kv in index)
            {
                var separator = kv.Key.IndexOf("::", StringComparison.Ordinal);
                if (separator < 0)
                {
                    continue;
                }

                rows.Add((kv.Key.Substring(0, separator), kv.Value.Category ?? string.Empty, kv.Key.Substring(separator + 2), kv.Value));
            }

            rows.Sort((a, b) => string.CompareOrdinal(a.type + a.identifier, b.type + b.identifier));

            foreach (var (type, category, identifier, asset) in rows)
            {
                sb.Append(type).Append('\t');
                sb.Append(category).Append('\t');
                sb.Append(identifier).Append('\t');
                sb.Append(asset.DisplayName ?? string.Empty).Append('\t');
                sb.Append(SpecStatusTag.GetCurrent(asset.Tags)).Append('\t');
                sb.Append(asset.Assignee ?? string.Empty).Append('\t');
                sb.Append(asset.SpecUrl ?? string.Empty).Append('\t');
                sb.Append(asset.Description ?? string.Empty).Append('\n');
            }

            return sb.ToString();
        }
    }
}
