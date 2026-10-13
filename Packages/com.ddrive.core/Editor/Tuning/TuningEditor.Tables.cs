using System;
using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Tuning;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Tuning
{
    // Tables タブ(TuningTable.Tables = テーブル型の調整値)。1 テーブル = 1 カード、列 × 行のグリッド(行 = 1 行、セルは列の型の部品)。
    // 編集できるのはセルの値だけ(列・行の追加削除は仕様書が正本。docs/32 §5.3)。値の変更は Undo + SetDirty、Play 中は索引を作り直して読み出し側へ即時に反映する。
    // 検索: テーブルのキー・列のキーが当たればそのテーブルを全行、行 ID が当たれば当たった行だけ出す。行が 20 を超えるテーブルは ListView(行の仮想化)。
    internal sealed partial class TuningEditor
    {
        private const int VirtualizeRowsAbove = 20;

        private struct CellSnap
        {
            public float F;
            public int I;
            public bool B;
            public string S;
        }

        private Dictionary<long, CellSnap> _cellBase = new Dictionary<long, CellSnap>();
        private int _cellMods;
        private int _tablesShown;
        private string _tablesSig = string.Empty;
        private long _cellGroupKey = -1;
        private int _cellGroup;
        private double _cellTime;

        private static long CellKey(int t, int r, int c) => ((long)t << 40) | ((long)r << 20) | (long)c;

        // 開いたときのセルの値(「変更」の色と数の基準)。
        private void BuildCellBaseline()
        {
            _cellBase = new Dictionary<long, CellSnap>();
            _cellMods = 0;
            var tables = _target != null ? _target.Tables : null;
            for (var t = 0; tables != null && t < tables.Length; t++)
            {
                var rows = tables[t].Rows;
                for (var r = 0; rows != null && r < rows.Length; r++)
                {
                    var cells = rows[r].Cells;
                    for (var c = 0; cells != null && c < cells.Length; c++)
                    {
                        _cellBase[CellKey(t, r, c)] = new CellSnap { F = cells[c].F, I = cells[c].I, B = cells[c].B, S = cells[c].S ?? string.Empty };
                    }
                }
            }
        }

        private bool IsCellModified(int t, int r, int c)
        {
            var cell = _target.Tables[t].Rows[r].Cells[c];
            return _cellBase.TryGetValue(CellKey(t, r, c), out var b)
                && (!Mathf.Approximately(cell.F, b.F) || cell.I != b.I || cell.B != b.B || (cell.S ?? string.Empty) != b.S);
        }

        private void RecountCellMods()
        {
            var n = 0;
            var tables = _target != null ? _target.Tables : null;
            for (var t = 0; tables != null && t < tables.Length; t++)
            {
                var rows = tables[t].Rows;
                for (var r = 0; rows != null && r < rows.Length; r++)
                {
                    var cells = rows[r].Cells;
                    for (var c = 0; cells != null && c < cells.Length; c++)
                    {
                        if (IsCellModified(t, r, c))
                        {
                            n++;
                        }
                    }
                }
            }

            _cellMods = n;
        }

        // テーブルの構成と値の署名(外部の変更 = 仕様書同期・別の窓の編集を見つける)。自分の編集のあとは作り直さないよう署名を更新する。
        private string TablesSignature()
        {
            var h = new System.Text.StringBuilder();
            var tables = _target != null ? _target.Tables : null;
            var value = 17;
            for (var t = 0; tables != null && t < tables.Length; t++)
            {
                var tb = tables[t];
                h.Append(tb.Key).Append('/').Append(tb.Columns != null ? tb.Columns.Length : 0).Append('/').Append(tb.Rows != null ? tb.Rows.Length : 0).Append(';');
                var rows = tb.Rows;
                for (var r = 0; rows != null && r < rows.Length; r++)
                {
                    h.Append(rows[r].RowId).Append(',');
                    var cells = rows[r].Cells;
                    for (var c = 0; cells != null && c < cells.Length; c++)
                    {
                        value = value * 31 + cells[c].F.GetHashCode();
                        value = value * 31 + cells[c].I;
                        value = value * 31 + (cells[c].B ? 1 : 0);
                        value = value * 31 + (cells[c].S != null ? cells[c].S.GetHashCode() : 0);
                    }
                }
            }

            return h.Append('#').Append(value).ToString();
        }

        private void SyncTablesIfChanged()
        {
            if (_target == null || _tablesHost == null)
            {
                return;
            }

            var sig = TablesSignature();
            if (sig != _tablesSig)
            {
                RebuildTables();
            }

            RecountCellMods();
        }

        // Tables タブの中身を作り直す(検索・Undo・外部の変更のとき)。
        private void RebuildTables()
        {
            if (_tablesHost == null || _target == null)
            {
                return;
            }

            _tablesHost.Clear();
            _tablesShown = 0;
            var tables = _target.Tables ?? Array.Empty<TuningTableEntry>();
            var q = Query;
            if (tables.Length == 0)
            {
                _tablesHost.Add(Note("Tables がありません(「仕様書と同期」で作成されます)。"));
            }

            for (var t = 0; t < tables.Length; t++)
            {
                var rows = VisibleRows(tables[t], q);
                if (rows == null)
                {
                    continue;
                }

                _tablesShown++;
                _tablesHost.Add(BuildTableCard(t, rows));
            }

            if (tables.Length > 0 && _tablesShown == 0)
            {
                _tablesHost.Add(Note("検索条件に一致する Tables がありません。"));
            }

            _tablesSig = TablesSignature();
            RecountCellMods();
            UpdateHit();
        }

        private static Label Note(string text)
        {
            var l = new Label(text);
            l.AddToClassList("ux-dim");
            l.AddToClassList("ux-tt-emptyrow");
            return l;
        }

        // 検索に当たる行の添字(null = このテーブルは出さない)。
        private static List<int> VisibleRows(TuningTableEntry table, string q)
        {
            var rows = table.Rows ?? Array.Empty<TuningTableRow>();
            var all = new List<int>(rows.Length);
            for (var r = 0; r < rows.Length; r++)
            {
                all.Add(r);
            }

            if (string.IsNullOrEmpty(q) || Contains(table.Key, q))
            {
                return all;
            }

            var columns = table.Columns ?? Array.Empty<TuningTableColumn>();
            for (var c = 0; c < columns.Length; c++)
            {
                if (Contains(columns[c].Key, q))
                {
                    return all;
                }
            }

            var hit = new List<int>();
            for (var r = 0; r < rows.Length; r++)
            {
                if (Contains(rows[r].RowId, q))
                {
                    hit.Add(r);
                }
            }

            return hit.Count > 0 ? hit : null;
        }

        private static bool Contains(string text, string q) => text != null && text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        private UxCard BuildTableCard(int t, List<int> rowIndices)
        {
            var table = _target.Tables[t];
            var columns = table.Columns ?? Array.Empty<TuningTableColumn>();
            var rowCount = table.Rows != null ? table.Rows.Length : 0;
            var title = string.IsNullOrEmpty(table.Key) ? "(キー未設定)" : table.Key;
            var desc = $"列 {columns.Length} × 行 {rowCount}" + (rowIndices.Count != rowCount ? $"(検索で {rowIndices.Count} 行を表示)" : string.Empty);
            var card = new UxCard(title, desc, UxCard.Help(ManualPage, "tuning-tables"));
            card.AddToClassList("ux-tt-tablecard");

            var grid = new VisualElement();
            grid.AddToClassList("ux-tt-grid");

            var head = new VisualElement();
            head.AddToClassList("ux-tt-row");
            head.AddToClassList("ux-tt-head");
            var corner = new Label("行 ID");
            corner.AddToClassList("ux-dim");
            corner.AddToClassList("ux-tt-rowid");
            head.Add(corner);
            for (var c = 0; c < columns.Length; c++)
            {
                var name = new Label(string.IsNullOrEmpty(columns[c].Unit) ? columns[c].Key : columns[c].Key + " (" + columns[c].Unit + ")");
                name.AddToClassList("ux-tt-colname");
                name.tooltip = columns[c].Key + " : " + columns[c].Type + (columns[c].Min != columns[c].Max ? $"  範囲 [{columns[c].Min:0.###}, {columns[c].Max:0.###}]" : string.Empty);
                head.Add(name);
            }

            grid.Add(head);

            if (rowIndices.Count > VirtualizeRowsAbove)
            {
                var tableIndex = t;
                var lv = new ListView
                {
                    virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                    fixedItemHeight = 28,
                    selectionType = SelectionType.None,
                    showAlternatingRowBackgrounds = AlternatingRowBackground.None,
                    itemsSource = rowIndices,
                    makeItem = () => new VisualElement(),
                    bindItem = (e, i) =>
                    {
                        e.Clear();
                        if (i >= 0 && i < rowIndices.Count)
                        {
                            e.Add(BuildRow(tableIndex, rowIndices[i]));
                        }
                    },
                };
                lv.AddToClassList("ux-tt-lv");
                grid.Add(lv);
            }
            else
            {
                for (var i = 0; i < rowIndices.Count; i++)
                {
                    grid.Add(BuildRow(t, rowIndices[i]));
                }
            }

            if (rowIndices.Count == 0)
            {
                grid.Add(Note("このテーブルに行がありません。"));
            }

            card.Body.Add(grid);
            return card;
        }

        private VisualElement BuildRow(int t, int r)
        {
            var table = _target.Tables[t];
            var columns = table.Columns ?? Array.Empty<TuningTableColumn>();
            var row = table.Rows[r];
            var line = new VisualElement();
            line.AddToClassList("ux-tt-row");
            var id = new Label(row.RowId);
            id.AddToClassList("ux-tt-rowid");
            id.tooltip = row.RowId;
            line.Add(id);
            for (var c = 0; c < columns.Length; c++)
            {
                var cellIndex = FindCellIndex(row, columns[c].Key);
                if (cellIndex < 0)
                {
                    var none = new Label("-");
                    none.AddToClassList("ux-dim");
                    none.AddToClassList("ux-tt-cell");
                    none.AddToClassList("ux-tt-cell--none");
                    none.tooltip = "この行にはセルがありません";
                    line.Add(none);
                    continue;
                }

                line.Add(BuildCell(t, r, cellIndex, columns[c]));
            }

            return line;
        }

        private static int FindCellIndex(TuningTableRow row, string columnKey)
        {
            if (row.Cells == null)
            {
                return -1;
            }

            for (var i = 0; i < row.Cells.Length; i++)
            {
                if (string.Equals(row.Cells[i].ColumnKey, columnKey, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private VisualElement BuildCell(int t, int r, int c, TuningTableColumn column)
        {
            var cell = _target.Tables[t].Rows[r].Cells[c];
            var ranged = column.Min != column.Max;
            VisualElement field;
            switch (column.Type)
            {
                case TuningValueType.Float:
                {
                    var f = new FloatField { value = cell.F };
                    f.RegisterValueChangedCallback(e =>
                    {
                        var v = ranged ? Mathf.Clamp(e.newValue, column.Min, column.Max) : e.newValue;
                        if (!Mathf.Approximately(v, e.newValue))
                        {
                            f.SetValueWithoutNotify(v);
                        }

                        WriteCell(t, r, c, () => _target.Tables[t].Rows[r].Cells[c].F = v, f);
                    });
                    field = f;
                    break;
                }

                case TuningValueType.Int:
                {
                    var f = new IntegerField { value = cell.I };
                    f.RegisterValueChangedCallback(e =>
                    {
                        var v = ranged ? Mathf.Clamp(e.newValue, Mathf.RoundToInt(column.Min), Mathf.RoundToInt(column.Max)) : e.newValue;
                        if (v != e.newValue)
                        {
                            f.SetValueWithoutNotify(v);
                        }

                        WriteCell(t, r, c, () => _target.Tables[t].Rows[r].Cells[c].I = v, f);
                    });
                    field = f;
                    break;
                }

                case TuningValueType.Bool:
                {
                    var f = new Toggle { value = cell.B };
                    f.RegisterValueChangedCallback(e => WriteCell(t, r, c, () => _target.Tables[t].Rows[r].Cells[c].B = e.newValue, f));
                    field = f;
                    break;
                }

                case TuningValueType.Enum:
                {
                    var f = new DropdownField(new List<string>(column.EnumOptions ?? new string[0]), -1);
                    f.SetValueWithoutNotify(cell.S ?? string.Empty);
                    f.RegisterValueChangedCallback(e => WriteCell(t, r, c, () => _target.Tables[t].Rows[r].Cells[c].S = e.newValue ?? string.Empty, f));
                    field = f;
                    break;
                }

                default:
                {
                    var f = new TextField { value = cell.S ?? string.Empty };
                    f.RegisterValueChangedCallback(e => WriteCell(t, r, c, () => _target.Tables[t].Rows[r].Cells[c].S = e.newValue ?? string.Empty, f));
                    field = f;
                    break;
                }
            }

            field.AddToClassList("ux-tt-cell");
            field.tooltip = column.Key + (string.IsNullOrEmpty(column.Unit) ? string.Empty : " (" + column.Unit + ")");
            field.EnableInClassList("ux-tt-cell--mod", IsCellModified(t, r, c));
            return field;
        }

        // セルの値の書き込み(Undo + SetDirty。1.2 秒以内の同じセルの連続操作は 1 回の Undo にまとめる。Play 中は索引を作り直して即時に反映)。
        private void WriteCell(int t, int r, int c, Action apply, VisualElement field)
        {
            if (_target == null || t >= _target.Tables.Length)
            {
                return;
            }

            var key = CellKey(t, r, c);
            var now = EditorApplication.timeSinceStartup;
            if (key == _cellGroupKey && now - _cellTime < 1.2)
            {
                Undo.RecordObject(_target, "Tuning の値を変更");
                Undo.CollapseUndoOperations(_cellGroup);
            }
            else
            {
                Undo.IncrementCurrentGroup();
                _cellGroup = Undo.GetCurrentGroup();
                Undo.RecordObject(_target, "Tuning の値を変更");
                _cellGroupKey = key;
            }

            _cellTime = now;
            apply();
            EditorUtility.SetDirty(_target);
            _so?.Update();
            if (Application.isPlaying)
            {
                _target.RebuildIndex();
                NotePending(_target.Tables[t].Key + "/" + _target.Tables[t].Rows[r].RowId + "/" + c);
            }

            field.EnableInClassList("ux-tt-cell--mod", IsCellModified(t, r, c));
            _tablesSig = TablesSignature();
            RecountCellMods();
            _autoEditTime = EditorApplication.timeSinceStartup;
            _issueSig = null;
            RefreshAll();
        }
    }
}
