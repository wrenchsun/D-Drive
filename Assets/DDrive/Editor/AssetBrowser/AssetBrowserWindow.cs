using System;
using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.AssetBrowser
{
    // [09_editor_tools.md] §1 — AssetBrowser 骨格(1-5)。
    // 本チケットの範囲: 横断一覧(仮想化 ListView) / インクリメンタル検索 / 種別フィルタ /
    // 新規作成(意味情報のみ入力) / AudioClip の D&D 登録 / 選択で Inspector 連動。
    // 使用箇所検索・依存ツリー(5-5, 5-6)、プレビューペイン(1-6)、お気に入り/最近は後続チケット。
    public sealed class AssetBrowserWindow : EditorWindow
    {
        private sealed class Row
        {
            public AssetDataBase Asset;
            public AssetType Type;
            public string Path;
        }

        private readonly List<Row> _allRows = new();
        private readonly List<Row> _visibleRows = new();

        private ToolbarSearchField _searchField;
        private DropdownField _typeFilter;
        private ListView _listView;
        private Label _statusLabel;
        private PreviewService _previewService;
        private AudioPreviewPane _previewPane;

        [MenuItem(DDriveMenu.Root + "Asset Browser")]
        public static void Open()
        {
            var window = GetWindow<AssetBrowserWindow>("Asset Browser");
            window.minSize = new Vector2(420, 300);
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var toolbar = new Toolbar();

            _searchField = new ToolbarSearchField();
            _searchField.style.flexGrow = 1f;
            _searchField.RegisterValueChangedCallback(_ => ApplyFilter());
            toolbar.Add(_searchField);

            var typeChoices = new List<string> { "All" };
            typeChoices.AddRange(Enum.GetNames(typeof(AssetType)).Where(n => n != nameof(AssetType.None)));
            _typeFilter = new DropdownField(typeChoices, 0);
            _typeFilter.RegisterValueChangedCallback(_ => ApplyFilter());
            toolbar.Add(_typeFilter);

            toolbar.Add(new ToolbarButton(() => NewAssetDialog.Open()) { text = "新規" });
            toolbar.Add(new ToolbarButton(Refresh) { text = "更新" });

            root.Add(toolbar);

            _listView = new ListView
            {
                fixedItemHeight = 22,
                selectionType = SelectionType.Single,
                makeItem = MakeRowElement,
                bindItem = BindRowElement,
                itemsSource = _visibleRows,
            };
            _listView.style.flexGrow = 1f;
            _listView.selectionChanged += OnSelectionChanged;
            _listView.itemsChosen += OnItemsChosen;
            root.Add(_listView);

            _statusLabel = new Label();
            _statusLabel.style.paddingLeft = 6;
            _statusLabel.style.paddingBottom = 2;
            root.Add(_statusLabel);

            _previewService ??= new PreviewService();
            _previewPane = new AudioPreviewPane(_previewService);
            root.Add(_previewPane);

            SetupDragAndDrop(root);
            Refresh();
        }

        private void OnDisable()
        {
            _previewService?.Dispose();
            _previewService = null;
        }

        private static VisualElement MakeRowElement()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            var typeLabel = new Label { name = "type" };
            typeLabel.style.width = 70;
            typeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(typeLabel);

            var nameLabel = new Label { name = "name" };
            nameLabel.style.flexGrow = 1f;
            row.Add(nameLabel);

            var categoryLabel = new Label { name = "category" };
            categoryLabel.style.width = 140;
            categoryLabel.style.opacity = 0.6f;
            row.Add(categoryLabel);

            return row;
        }

        private void BindRowElement(VisualElement element, int index)
        {
            var row = _visibleRows[index];
            element.Q<Label>("type").text = row.Type.ToString();
            element.Q<Label>("name").text = row.Asset != null && !string.IsNullOrEmpty(row.Asset.DisplayName)
                ? row.Asset.DisplayName
                : System.IO.Path.GetFileNameWithoutExtension(row.Path);
            element.Q<Label>("category").text = row.Asset != null ? row.Asset.Category : string.Empty;
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            if (selection.FirstOrDefault() is Row row && row.Asset != null)
            {
                Selection.activeObject = row.Asset;
                _previewPane?.Bind(row.Asset);
            }
            else
            {
                _previewPane?.Bind(null);
            }
        }

        private void OnItemsChosen(IEnumerable<object> items)
        {
            if (items.FirstOrDefault() is Row row && row.Asset != null)
            {
                EditorGUIUtility.PingObject(row.Asset);
            }
        }

        public void Refresh()
        {
            _allRows.Clear();

            var definitionByDataType = Inspectors.AssetIdLookup.GetAllDefinitions()
                .Where(d => d.dataType.Namespace?.Contains("Tests") != true)
                .ToDictionary(d => d.dataType, d => d.assetType);

            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(AssetDataBase)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(path);
                if (asset == null || !definitionByDataType.TryGetValue(asset.GetType(), out var assetType))
                {
                    continue;
                }

                _allRows.Add(new Row { Asset = asset, Type = assetType, Path = path });
            }

            _allRows.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            _visibleRows.Clear();

            var search = _searchField?.value ?? string.Empty;
            var typeName = _typeFilter?.value ?? "All";

            foreach (var row in _allRows)
            {
                if (typeName != "All" && row.Type.ToString() != typeName)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(search) && !MatchesSearch(row, search))
                {
                    continue;
                }

                _visibleRows.Add(row);
            }

            _listView?.RefreshItems();

            if (_statusLabel != null)
            {
                _statusLabel.text = $"{_visibleRows.Count} / {_allRows.Count} 件";
            }
        }

        // 横断検索: 表示名 / ファイル名 / カテゴリ / タグ / 説明 / 作成者([09] §1)。
        private static bool MatchesSearch(Row row, string search)
        {
            bool Contains(string value)
                => !string.IsNullOrEmpty(value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

            if (Contains(row.Asset.DisplayName) || Contains(row.Path) ||
                Contains(row.Asset.Category) || Contains(row.Asset.Description) || Contains(row.Asset.Author))
            {
                return true;
            }

            if (row.Asset.Tags != null)
            {
                foreach (var tag in row.Asset.Tags)
                {
                    if (Contains(tag))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // AudioClip をウィンドウへ D&D → 種別自動判定(SE)で新規作成ダイアログを開く。
        // 他種別(Prefab→Vfx 等)の判定は該当種別の実装チケットで追加する。
        private void SetupDragAndDrop(VisualElement root)
        {
            root.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (DraggedAudioClips().Length > 0)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    evt.StopPropagation();
                }
            });

            root.RegisterCallback<DragPerformEvent>(evt =>
            {
                var clips = DraggedAudioClips();
                if (clips.Length == 0)
                {
                    return;
                }

                DragAndDrop.AcceptDrag();
                NewAssetDialog.Open(clips);
                evt.StopPropagation();
            });
        }

        private static AudioClip[] DraggedAudioClips()
            => DragAndDrop.objectReferences.OfType<AudioClip>().ToArray();
    }
}
