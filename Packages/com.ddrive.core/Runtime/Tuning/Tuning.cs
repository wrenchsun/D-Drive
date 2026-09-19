using System;
using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Runtime.Tuning
{
    // デザイナー/プログラマー向けの薄い静的ファサード(Audio.cs / Options.cs と同じ設計、ADR#3)。
    // 生成された TUNING.キー定数(Assets/Generated/Tuning.g.cs、DDrive.Editor.Codegen.TuningCodegen)と
    // 組み合わせて Tuning.GetFloat(TUNING.InfluenceFanBase) のように読む([27_spec_sheet.md] §3.2)。
    // 例外で止めない: 未登録キーは警告を 1 回だけ出し、既定値(呼び出し側が渡した defaultValue)を返す。
    public static class Tuning
    {
        private static TuningTable _table;
        private static readonly HashSet<string> WarnedKeys = new();

        // P5 レビュー対応(2026-09-14): GetBool/GetString が型不一致(entry.Type が要求と違う)を検出しない
        // 問題への対応。「未登録キー」の警告(WarnedKeys)とは別に 1 キー 1 回だけ警告する。
        private static readonly HashSet<string> WarnedTypeMismatchKeys = new();

        public static void Bind(TuningTable table)
        {
            _table = table;
            _table?.RebuildIndex();
            WarnedKeys.Clear();
            WarnedTypeMismatchKeys.Clear();
            WarnedTableKeys.Clear();
            WarnedTableTypeMismatchKeys.Clear();
        }

        public static bool IsBound => _table != null;

        public static float GetFloat(string key, float defaultValue = 0f)
        {
            if (TryFindEntry(key, out var entry))
            {
                if (entry.Type != TuningValueType.Float && entry.Type != TuningValueType.Int)
                {
                    WarnTypeMismatchOnce(key, entry.Type, nameof(GetFloat));
                }

                return entry.Type == TuningValueType.Float ? entry.ValueFloat : entry.ValueInt;
            }

            return defaultValue;
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            if (TryFindEntry(key, out var entry))
            {
                if (entry.Type != TuningValueType.Float && entry.Type != TuningValueType.Int)
                {
                    WarnTypeMismatchOnce(key, entry.Type, nameof(GetInt));
                }

                return entry.Type == TuningValueType.Int ? entry.ValueInt : (int)entry.ValueFloat;
            }

            return defaultValue;
        }

        public static bool GetBool(string key, bool defaultValue = false)
        {
            if (TryFindEntry(key, out var entry))
            {
                if (entry.Type != TuningValueType.Bool)
                {
                    WarnTypeMismatchOnce(key, entry.Type, nameof(GetBool));
                }

                return entry.ValueBool;
            }

            return defaultValue;
        }

        public static string GetString(string key, string defaultValue = "")
        {
            if (TryFindEntry(key, out var entry))
            {
                if (entry.Type != TuningValueType.String)
                {
                    WarnTypeMismatchOnce(key, entry.Type, nameof(GetString));
                }

                return entry.ValueString;
            }

            return defaultValue;
        }

        // W-10(2026-09-14) 追加: valueType=Enum の調整値を読む。値そのものは ValueString に入る
        // (TuningTable.cs の TuningEntry.EnumOptions 参照)。
        public static string GetEnum(string key, string defaultValue = "")
        {
            if (TryFindEntry(key, out var entry))
            {
                if (entry.Type != TuningValueType.Enum)
                {
                    WarnTypeMismatchOnce(key, entry.Type, nameof(GetEnum));
                }

                return entry.ValueString;
            }

            return defaultValue;
        }

        private static bool TryFindEntry(string key, out TuningEntry entry)
        {
            if (_table != null && _table.TryFindIndex(key, out var index))
            {
                entry = _table.Entries[index];
                return true;
            }

            entry = default;
            if (WarnedKeys.Add(key))
            {
                // P5 レビュー対応(2026-09-14): 整理項目 — 未登録キー警告(実行時に毎フレーム呼ばれうる
                // 定常経路)を開発ビルド/エディタ限定にする(製品ビルドでログ汚染・コスト増を避ける)。
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning($"[DDrive] Tuning: キー '{key}' が TuningTable に見つかりません。既定値を使います。");
#endif
            }

            return false;
        }

        private static void WarnTypeMismatchOnce(string key, TuningValueType actualType, string calledFrom)
        {
            if (!WarnedTypeMismatchKeys.Add(key))
            {
                return;
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.LogWarning($"[DDrive] Tuning: キー '{key}' は {actualType} 型ですが {calledFrom} で読まれました。想定と異なる値が返る可能性があります。");
#endif
        }

        // ── W-10(2026-09-14) 追加: テーブル型調整値の読み取り([32_spec_web.md] §5.3) ──
        // 定常経路制約([12_review.md] §3: LINQ・クロージャ・boxing 禁止)を守るため、行/列の
        // 検索は for ループの線形探索にする(テーブルの行・列数は数十件程度が前提。索引が必要な
        // 規模になった場合は Bind() 時に構築する方式へ変更する。GetFloat/GetInt 等と同じ
        // 「未登録は警告1回+既定値」方式)。

        // 2026-09-17 レビュー対応(P2-4) — 以前は "table/row/column" を毎回文字列連結してから
        // HashSet.Add に渡していたため、Tick から未登録キーを読むと毎フレーム string alloc が出ていた
        // (スカラー版 WarnedKeys は key をそのまま使って 0 alloc なのと非対称だった)。
        // ValueTuple のキーなら boxing 無し・連結無しで判定できる([12_review.md] §3 定常経路 alloc 禁止)。
        private static readonly HashSet<(string Table, string Row, string Column)> WarnedTableKeys = new();
        private static readonly HashSet<(string Table, string Row, string Column)> WarnedTableTypeMismatchKeys = new();

        public static float GetTableFloat(string tableKey, string rowId, string columnKey, float defaultValue = 0f)
        {
            if (TryFindCell(tableKey, rowId, columnKey, out var column, out var cell))
            {
                if (column.Type != TuningValueType.Float && column.Type != TuningValueType.Int)
                {
                    WarnTableTypeMismatchOnce(tableKey, rowId, columnKey, column.Type, nameof(GetTableFloat));
                }

                return column.Type == TuningValueType.Float ? cell.F : cell.I;
            }

            return defaultValue;
        }

        public static int GetTableInt(string tableKey, string rowId, string columnKey, int defaultValue = 0)
        {
            if (TryFindCell(tableKey, rowId, columnKey, out var column, out var cell))
            {
                if (column.Type != TuningValueType.Float && column.Type != TuningValueType.Int)
                {
                    WarnTableTypeMismatchOnce(tableKey, rowId, columnKey, column.Type, nameof(GetTableInt));
                }

                return column.Type == TuningValueType.Int ? cell.I : (int)cell.F;
            }

            return defaultValue;
        }

        public static bool GetTableBool(string tableKey, string rowId, string columnKey, bool defaultValue = false)
        {
            if (TryFindCell(tableKey, rowId, columnKey, out var column, out var cell))
            {
                if (column.Type != TuningValueType.Bool)
                {
                    WarnTableTypeMismatchOnce(tableKey, rowId, columnKey, column.Type, nameof(GetTableBool));
                }

                return cell.B;
            }

            return defaultValue;
        }

        public static string GetTableString(string tableKey, string rowId, string columnKey, string defaultValue = "")
        {
            if (TryFindCell(tableKey, rowId, columnKey, out var column, out var cell))
            {
                if (column.Type != TuningValueType.String && column.Type != TuningValueType.Enum)
                {
                    WarnTableTypeMismatchOnce(tableKey, rowId, columnKey, column.Type, nameof(GetTableString));
                }

                return cell.S;
            }

            return defaultValue;
        }

        private static bool TryFindCell(string tableKey, string rowId, string columnKey, out TuningTableColumn column, out TuningCellValue cell)
        {
            column = default;
            cell = default;

            if (_table == null || !_table.TryFindTableIndex(tableKey, out var tableIndex))
            {
                WarnTableMissingOnce(tableKey, rowId, columnKey);
                return false;
            }

            var table = _table.Tables[tableIndex];

            var columnFound = false;
            for (var i = 0; i < table.Columns.Length; i++)
            {
                if (string.Equals(table.Columns[i].Key, columnKey, StringComparison.Ordinal))
                {
                    column = table.Columns[i];
                    columnFound = true;
                    break;
                }
            }

            if (!columnFound)
            {
                WarnTableMissingOnce(tableKey, rowId, columnKey);
                return false;
            }

            for (var r = 0; r < table.Rows.Length; r++)
            {
                if (!string.Equals(table.Rows[r].RowId, rowId, StringComparison.Ordinal))
                {
                    continue;
                }

                var cells = table.Rows[r].Cells;
                for (var c = 0; c < cells.Length; c++)
                {
                    if (string.Equals(cells[c].ColumnKey, columnKey, StringComparison.Ordinal))
                    {
                        cell = cells[c];
                        return true;
                    }
                }

                break;
            }

            WarnTableMissingOnce(tableKey, rowId, columnKey);
            return false;
        }

        private static void WarnTableMissingOnce(string tableKey, string rowId, string columnKey)
        {
            if (!WarnedTableKeys.Add((tableKey, rowId, columnKey)))
            {
                return;
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.LogWarning($"[DDrive] Tuning: テーブル '{tableKey}' の行 '{rowId}' 列 '{columnKey}' が見つかりません。既定値を使います。");
#endif
        }

        private static void WarnTableTypeMismatchOnce(string tableKey, string rowId, string columnKey, TuningValueType actualType, string calledFrom)
        {
            if (!WarnedTableTypeMismatchKeys.Add((tableKey, rowId, columnKey)))
            {
                return;
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.LogWarning($"[DDrive] Tuning: テーブル '{tableKey}' の行 '{rowId}' 列 '{columnKey}' は {actualType} 型ですが {calledFrom} で読まれました。想定と異なる値が返る可能性があります。");
#endif
        }
    }
}
