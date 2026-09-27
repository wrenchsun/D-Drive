using System;
using System.Collections.Generic;
using DDrive.Editor.Codegen;
using DDrive.Editor.Menu;
using DDrive.Editor.Spec;
using DDrive.Editor.Versioning;
using DDrive.Runtime.Loop;
using DDrive.Runtime.Tuning;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
// このファイル自身の名前空間 DDrive.Editor.Tuning と DDrive.Runtime.Tuning.Tuning(静的ファサード)が
// 同名衝突するため、別名のエイリアスで参照する("Tuning" という別名にすると、外側の名前空間
// DDrive.Editor のメンバー(= このファイルの名前空間 DDrive.Editor.Tuning 自身)が優先されてしまい
// 解決できない、CS0234)。
using RuntimeTuning = DDrive.Runtime.Tuning.Tuning;

namespace DDrive.Editor.Tuning
{
    // M-2a(2026-09-27。[11_tasks.md] M-2 チケット、[09_editor_tools.md] §「Tuning ウィンドウ」) —
    // MS2026 チームの要望「TuningTable が 46 キーになって Unity 既定の配列 Inspector では
    // 目的のキーに辿り着けない」への対応。データ形式は変えない(TuningTable に Category フィールドを
    // 足さない、Compat スナップショット無影響)。カテゴリ = キーの "<機能>/" 接頭辞(TuningCategoryGrouper、
    // 純関数として切り出し EditMode テストで検証)。
    //
    // ウィンドウ内で Tuning.Get*(ランタイム用の静的ファサード)は呼ばない([09] の規約どおり、Editor は
    // TuningTable を直接読み書きする)。保存は Undo.RecordObject + DDriveAssetSave.SaveDirty([09] §保存規約)。
    public sealed class TuningEditorWindow : EditorWindow
    {
        private const string TablesCategoryLabel = "Tables";

        [SerializeField] private TuningTable _table;

        private ObjectField _tableField;
        private VisualElement _categoryListContainer;
        private IMGUIContainer _rightPanel;
        private ToolbarButton _reloadButton;

        private string _searchQuery = string.Empty;
        private string _selectedCategory;
        private IReadOnlyList<TuningCategoryGrouper.Category> _categories = Array.Empty<TuningCategoryGrouper.Category>();

        [MenuItem(DDriveMenu.Editors + "Tuning（調整値）")]
        public static void OpenFromMenu() => Open();

        public static void Open(TuningTable table = null)
        {
            var window = GetWindow<TuningEditorWindow>("Tuning（調整値）");
            window.minSize = new Vector2(560, 420); // [09] §7.1: 横幅の下限を minSize で確保する
            if (table != null)
            {
                window.SetTable(table);
            }
        }

        // ── ライフサイクル ──

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            RebuildCategoryList();
            RefreshRightPanel();
        }

        private void OnEditorUpdate()
        {
            _reloadButton?.SetEnabled(EditorApplication.isPlaying && RuntimeTuning.IsBound);
        }

        // ── UI 構築 ──

        private void CreateGUI()
        {
            BuildToolbar(rootVisualElement);

            // [09_editor_tools.md] §6-7: 新規 EditorWindow はルートを ScrollView にする。
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);
            scrollView.style.paddingLeft = 6;
            scrollView.style.paddingRight = 6;
            scrollView.style.paddingTop = 6;

            var body = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1f } };
            scrollView.Add(body);

            _categoryListContainer = new VisualElement { style = { width = 170, marginRight = 8 } };
            body.Add(_categoryListContainer);

            _rightPanel = new IMGUIContainer(DrawRightPanel) { style = { flexGrow = 1f } };
            body.Add(_rightPanel);

            if (_table == null)
            {
                _table = ResolveDefaultTable();
            }

            SetTable(_table);
        }

        private void BuildToolbar(VisualElement root)
        {
            var toolbar = new Toolbar();

            _tableField = new ObjectField { objectType = typeof(TuningTable), style = { minWidth = 220 } };
            _tableField.RegisterValueChangedCallback(evt => SetTable(evt.newValue as TuningTable));
            toolbar.Add(_tableField);

            var searchField = new TextField { style = { minWidth = 150 } };
            searchField.tooltip = "キー・テーブル名で絞り込む";
            searchField.RegisterValueChangedCallback(evt =>
            {
                _searchQuery = evt.newValue ?? string.Empty;
                RefreshRightPanel();
            });
            toolbar.Add(searchField);

            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(new ToolbarButton(OnRegenerateKeysClicked) { text = "キー定数を再生成" });
            toolbar.Add(new ToolbarButton(SpecSyncWindow.Open) { text = "仕様書と同期" });

            _reloadButton = new ToolbarButton(OnReloadClicked)
            {
                text = "Play 中に再読込",
                tooltip = "Tuning.Rebind() を呼び、Reloaded を購読している側に新しい値を通知する(M-2c)",
            };
            _reloadButton.SetEnabled(EditorApplication.isPlaying && RuntimeTuning.IsBound);
            toolbar.Add(_reloadButton);

            root.Add(toolbar);
        }

        private void OnRegenerateKeysClicked()
        {
            var result = TuningCodegen.Regenerate(_table);
            Debug.Log($"[DDrive] Tuning keys regenerated: {result.TotalCount} entries.");
        }

        private void OnReloadClicked()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[DDrive] Tuning: 「Play 中に再読込」は Play Mode 中のみ使えます。");
                return;
            }

            RuntimeTuning.Rebind();
        }

        // ── 対象テーブル ──

        private static TuningTable ResolveDefaultTable()
        {
            var fromSettings = AssetDatabase.LoadAssetAtPath<TuningTable>(DDriveSpecSettings.DefaultTuningTablePath);
            if (fromSettings != null)
            {
                return fromSettings;
            }

            var bootstrap = UnityEngine.Object.FindFirstObjectByType<DDriveRuntimeBootstrap>();
            return bootstrap != null ? bootstrap.TuningTable : null;
        }

        public void SetTable(TuningTable table)
        {
            _table = table;
            _tableField?.SetValueWithoutNotify(_table);
            RebuildCategoryList();
            RefreshRightPanel();
        }

        // ── 左: カテゴリ一覧 ──

        private void RebuildCategoryList()
        {
            if (_categoryListContainer == null)
            {
                return; // CreateGUI 前
            }

            _categoryListContainer.Clear();

            _categories = _table != null
                ? TuningCategoryGrouper.Group(_table.Entries)
                : Array.Empty<TuningCategoryGrouper.Category>();

            foreach (var category in _categories)
            {
                AddCategoryButton(category.Name, category.Count);
            }

            var tableCount = _table != null && _table.Tables != null ? _table.Tables.Length : 0;
            AddCategoryButton(TablesCategoryLabel, tableCount);

            if (string.IsNullOrEmpty(_selectedCategory) || !CategoryExists(_selectedCategory))
            {
                _selectedCategory = _categories.Count > 0 ? _categories[0].Name : TablesCategoryLabel;
            }

            HighlightSelected();
        }

        private bool CategoryExists(string name)
        {
            if (string.Equals(name, TablesCategoryLabel, StringComparison.Ordinal))
            {
                return true;
            }

            foreach (var category in _categories)
            {
                if (string.Equals(category.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void AddCategoryButton(string name, int count)
        {
            var button = new Button(() =>
            {
                _selectedCategory = name;
                HighlightSelected();
                RefreshRightPanel();
            })
            {
                text = $"{name} ({count})",
                style = { unityTextAlign = TextAnchor.MiddleLeft, marginBottom = 1 },
            };
            button.userData = name;
            _categoryListContainer.Add(button);
        }

        private void HighlightSelected()
        {
            foreach (var child in _categoryListContainer.Children())
            {
                if (child is Button button)
                {
                    var isSelected = string.Equals((string)button.userData, _selectedCategory, StringComparison.Ordinal);
                    button.style.unityFontStyleAndWeight = isSelected ? FontStyle.Bold : FontStyle.Normal;
                }
            }
        }

        private TuningCategoryGrouper.Category? FindCategory(string name)
        {
            foreach (var category in _categories)
            {
                if (string.Equals(category.Name, name, StringComparison.Ordinal))
                {
                    return category;
                }
            }

            return null;
        }

        private void RefreshRightPanel() => _rightPanel?.MarkDirtyRepaint();

        // ── 右: キー一覧 / Tables グリッド(IMGUI) ──

        private void DrawRightPanel()
        {
            if (_table == null)
            {
                EditorGUILayout.HelpBox("対象の TuningTable を選択してください。", MessageType.Info);
                return;
            }

            if (string.Equals(_selectedCategory, TablesCategoryLabel, StringComparison.Ordinal))
            {
                DrawTables();
            }
            else
            {
                DrawEntries();
            }
        }

        private void DrawEntries()
        {
            var category = FindCategory(_selectedCategory);
            if (category == null)
            {
                EditorGUILayout.HelpBox("カテゴリを選択してください。", MessageType.Info);
                return;
            }

            var query = _searchQuery?.Trim();
            var any = false;

            foreach (var index in category.Value.EntryIndices)
            {
                var entry = _table.Entries[index];
                if (!string.IsNullOrEmpty(query) && (entry.Key ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                any = true;
                DrawEntryRow(index, entry);
            }

            if (!any)
            {
                EditorGUILayout.HelpBox(
                    string.IsNullOrEmpty(query) ? "このカテゴリにキーがありません。" : "検索条件に一致するキーがありません。",
                    MessageType.Info);
            }
        }

        private void DrawEntryRow(int index, TuningEntry entry)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(TuningCategoryGrouper.DisplayName(entry.Key), GUILayout.Width(160));

                EditorGUI.BeginChangeCheck();
                object newValue = null;

                switch (entry.Type)
                {
                    case TuningValueType.Float:
                        newValue = entry.Min != entry.Max
                            ? EditorGUILayout.Slider(entry.ValueFloat, entry.Min, entry.Max)
                            : (object)EditorGUILayout.FloatField(entry.ValueFloat);
                        break;

                    case TuningValueType.Int:
                        newValue = entry.Min != entry.Max
                            ? EditorGUILayout.IntSlider(entry.ValueInt, (int)entry.Min, (int)entry.Max)
                            : (object)EditorGUILayout.IntField(entry.ValueInt);
                        break;

                    case TuningValueType.Bool:
                        newValue = EditorGUILayout.Toggle(entry.ValueBool);
                        break;

                    case TuningValueType.String:
                        newValue = EditorGUILayout.TextField(entry.ValueString);
                        break;

                    case TuningValueType.Enum:
                        var options = entry.EnumOptions ?? Array.Empty<string>();
                        var selectedIndex = TuningEnumFieldLogic.ResolveSelectedIndex(options, entry.ValueString);
                        var newIndex = EditorGUILayout.Popup(selectedIndex, options);
                        newValue = TuningEnumFieldLogic.ResolveValueAt(options, newIndex);
                        break;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    ApplyEntryChange(index, entry.Type, newValue);
                }

                GUILayout.Label(entry.Unit ?? string.Empty, GUILayout.Width(50));
                GUILayout.Label(new GUIContent(entry.Description ?? string.Empty, entry.Description ?? string.Empty), GUILayout.MinWidth(120));
            }
        }

        private void ApplyEntryChange(int index, TuningValueType type, object newValue)
        {
            Undo.RecordObject(_table, "Tuning 値を変更");

            switch (type)
            {
                case TuningValueType.Float:
                    _table.Entries[index].ValueFloat = (float)newValue;
                    break;
                case TuningValueType.Int:
                    _table.Entries[index].ValueInt = (int)newValue;
                    break;
                case TuningValueType.Bool:
                    _table.Entries[index].ValueBool = (bool)newValue;
                    break;
                case TuningValueType.String:
                case TuningValueType.Enum:
                    _table.Entries[index].ValueString = (string)newValue;
                    break;
            }

            EditorUtility.SetDirty(_table);
            DDriveAssetSave.SaveDirty(_table);

            // Play 中は Tuning.Rebind()(M-2c)を明示的に押すまで消費側へは通知されない。
            // Edit Mode は次回の Bind() で索引が作り直されるため RebuildIndex() を呼ぶ必要はない([02] §14)。
            if (Application.isPlaying)
            {
                _table.RebuildIndex();
            }
        }

        // ── 右: Tables グリッド(値の編集のみ。列・行の追加削除は仕様書と同期に任せる、[09]) ──

        private void DrawTables()
        {
            if (_table.Tables == null || _table.Tables.Length == 0)
            {
                EditorGUILayout.HelpBox("Tables がありません(「仕様書と同期」で作成されます)。", MessageType.Info);
                return;
            }

            var query = _searchQuery?.Trim();
            var any = false;

            for (var t = 0; t < _table.Tables.Length; t++)
            {
                var table = _table.Tables[t];
                if (!string.IsNullOrEmpty(query) && (table.Key ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                any = true;
                EditorGUILayout.LabelField(table.Key, EditorStyles.boldLabel);
                DrawTableGrid(t, table);
                EditorGUILayout.Space(8);
            }

            if (!any)
            {
                EditorGUILayout.HelpBox("検索条件に一致する Tables がありません。", MessageType.Info);
            }
        }

        private void DrawTableGrid(int tableIndex, TuningTableEntry table)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(string.Empty, GUILayout.Width(120));
                foreach (var column in table.Columns)
                {
                    GUILayout.Label(column.Key, GUILayout.Width(90));
                }
            }

            for (var r = 0; r < table.Rows.Length; r++)
            {
                var row = table.Rows[r];
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(row.RowId, GUILayout.Width(120));

                    for (var c = 0; c < table.Columns.Length; c++)
                    {
                        var column = table.Columns[c];
                        var cellIndex = FindCellIndex(row, column.Key);
                        if (cellIndex < 0)
                        {
                            GUILayout.Label("-", GUILayout.Width(90));
                            continue;
                        }

                        DrawTableCell(tableIndex, r, cellIndex, column);
                    }
                }
            }
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

        private void DrawTableCell(int tableIndex, int rowIndex, int cellIndex, TuningTableColumn column)
        {
            var cell = _table.Tables[tableIndex].Rows[rowIndex].Cells[cellIndex];

            EditorGUI.BeginChangeCheck();
            object newValue = null;

            switch (column.Type)
            {
                case TuningValueType.Float:
                    newValue = column.Min != column.Max
                        ? EditorGUILayout.Slider(cell.F, column.Min, column.Max, GUILayout.Width(90))
                        : (object)EditorGUILayout.FloatField(cell.F, GUILayout.Width(90));
                    break;

                case TuningValueType.Int:
                    newValue = column.Min != column.Max
                        ? EditorGUILayout.IntSlider(cell.I, (int)column.Min, (int)column.Max, GUILayout.Width(90))
                        : (object)EditorGUILayout.IntField(cell.I, GUILayout.Width(90));
                    break;

                case TuningValueType.Bool:
                    newValue = EditorGUILayout.Toggle(cell.B, GUILayout.Width(90));
                    break;

                case TuningValueType.String:
                    newValue = EditorGUILayout.TextField(cell.S, GUILayout.Width(90));
                    break;

                case TuningValueType.Enum:
                    var options = column.EnumOptions ?? Array.Empty<string>();
                    var selectedIndex = TuningEnumFieldLogic.ResolveSelectedIndex(options, cell.S);
                    var newIndex = EditorGUILayout.Popup(selectedIndex, options, GUILayout.Width(90));
                    newValue = TuningEnumFieldLogic.ResolveValueAt(options, newIndex);
                    break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                ApplyTableCellChange(tableIndex, rowIndex, cellIndex, column.Type, newValue);
            }
        }

        private void ApplyTableCellChange(int tableIndex, int rowIndex, int cellIndex, TuningValueType type, object newValue)
        {
            Undo.RecordObject(_table, "Tuning 値を変更");

            switch (type)
            {
                case TuningValueType.Float:
                    _table.Tables[tableIndex].Rows[rowIndex].Cells[cellIndex].F = (float)newValue;
                    break;
                case TuningValueType.Int:
                    _table.Tables[tableIndex].Rows[rowIndex].Cells[cellIndex].I = (int)newValue;
                    break;
                case TuningValueType.Bool:
                    _table.Tables[tableIndex].Rows[rowIndex].Cells[cellIndex].B = (bool)newValue;
                    break;
                case TuningValueType.String:
                case TuningValueType.Enum:
                    _table.Tables[tableIndex].Rows[rowIndex].Cells[cellIndex].S = (string)newValue;
                    break;
            }

            EditorUtility.SetDirty(_table);
            DDriveAssetSave.SaveDirty(_table);

            if (Application.isPlaying)
            {
                _table.RebuildIndex();
            }
        }
    }
}
