using System;
using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.Dependencies;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Spec;
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
    // 使用箇所検索・依存ツリー・未使用検出・安全な削除(5-6、2026-09-14): 行の右クリックメニューから。
    // プレビューペイン(1-6)、お気に入り/最近は後続チケット。
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
        // [27_spec_sheet.md] §4.2 / 5-13 — 起動時自動取得(SpecAutoSync)が差分を見つけたときの通知バッジ。
        private ToolbarButton _specBadge;

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
            // 5-6: 未使用検出は専用ウィンドウ([09] §1)。使用箇所検索・依存ツリー・削除は行の右クリックメニューから。
            toolbar.Add(new ToolbarButton(UnusedAssetsWindow.Open) { text = "未使用..." });

            _specBadge = new ToolbarButton(() => SpecSyncWindow.Open()) { text = string.Empty };
            _specBadge.style.display = DisplayStyle.None;
            toolbar.Add(_specBadge);

            root.Add(toolbar);

            _listView = new ListView
            {
                fixedItemHeight = 22,
                // 削除の確認画面(2026-09-14)が複数選択に対応するため Multiple に変更。
                // 単一選択のときの挙動(Inspector連動・ダブルクリックでエディタを開く等)は変わらない。
                selectionType = SelectionType.Multiple,
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

            SpecCache.Updated += RefreshSpecBadge;
            RefreshSpecBadge();
        }

        private void OnDisable()
        {
            SpecCache.Updated -= RefreshSpecBadge;
            _previewService?.Dispose();
            _previewService = null;
        }

        private void RefreshSpecBadge()
        {
            if (_specBadge == null)
            {
                return;
            }

            var count = SpecCache.PendingChangeCount;
            if (count > 0)
            {
                _specBadge.text = $"仕様書に変更 {count} 件";
                _specBadge.style.display = DisplayStyle.Flex;
            }
            else
            {
                _specBadge.style.display = DisplayStyle.None;
            }
        }

        private const float RowIconSize = 18f;

        private VisualElement MakeRowElement()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            var icon = new Image { name = "icon", scaleMode = ScaleMode.ScaleToFit };
            icon.style.width = RowIconSize;
            icon.style.height = RowIconSize;
            icon.style.marginRight = 4;
            icon.style.flexShrink = 0;
            row.Add(icon);

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

            // [11_tasks.md] 6-3 — 保存フックが記録した更新者・更新日時(今の値だけ)。
            // 現状の ListView は単一列の仮想化リストで、ソート可能な複数列ヘッダーは持っていない
            // (MultiColumnListView への切り替えが必要)。並べ替えは次回に回し、[09_editor_tools.md] §4 に記録する。
            var authorLabel = new Label { name = "author" };
            authorLabel.style.width = 70;
            authorLabel.style.opacity = 0.6f;
            row.Add(authorLabel);

            var updatedAtLabel = new Label { name = "updatedAt" };
            updatedAtLabel.style.width = 110;
            updatedAtLabel.style.opacity = 0.6f;
            row.Add(updatedAtLabel);

            // 5-6: 右クリックメニュー(使用箇所検索 / 依存ツリー / Archive / 安全な削除)。行は ListView に
            // よって使い回されるため、対象は毎回 element.userData(BindRowElement が差し替える)から読む。
            row.AddManipulator(new ContextualMenuManipulator(evt => PopulateRowContextMenu(evt, row)));

            return row;
        }

        private void BindRowElement(VisualElement element, int index)
        {
            var row = _visibleRows[index];
            element.userData = row;

            // 5-10: 行の先頭にアイコン(Data.Icon。未設定なら Unity の既定サムネイル/型アイコンにフォールバック)。
            element.Q<Image>("icon").image = row.Asset != null
                ? (row.Asset.Icon != null ? (Texture)row.Asset.Icon : AssetPreview.GetMiniThumbnail(row.Asset))
                : null;
            element.Q<Label>("type").text = row.Type.ToString();
            element.Q<Label>("name").text = row.Asset != null && !string.IsNullOrEmpty(row.Asset.DisplayName)
                ? row.Asset.DisplayName
                : System.IO.Path.GetFileNameWithoutExtension(row.Path);
            element.Q<Label>("category").text = row.Asset != null ? row.Asset.Category : string.Empty;
            element.Q<Label>("author").text = row.Asset != null ? row.Asset.Author : string.Empty;
            element.Q<Label>("updatedAt").text = row.Asset != null ? VersionStampGui.FormatForDisplay(row.Asset.UpdatedAt) : string.Empty;
        }

        private void PopulateRowContextMenu(ContextualMenuPopulateEvent evt, VisualElement rowElement)
        {
            if (rowElement.userData is not Row row || row.Asset == null)
            {
                return;
            }

            var displayName = !string.IsNullOrEmpty(row.Asset.DisplayName) ? row.Asset.DisplayName : row.Asset.name;

            // ダブルクリックと同じ経路([09] §1)。候補が複数ある種別(MaterialData 等)は
            // サブメニューで全候補(Order 昇順、Inspector の「エディターで開く」列と同じ順)を出す。
            var editorEntries = DataEditorRegistry.GetEntries(row.Asset.GetType());
            if (editorEntries.Count == 1)
            {
                var only = editorEntries[0];
                evt.menu.AppendAction("エディターで開く", _ => only.Open(row.Asset));
            }
            else if (editorEntries.Count > 1)
            {
                foreach (var entry in editorEntries)
                {
                    var captured = entry;
                    evt.menu.AppendAction($"エディターで開く/{captured.Label}", _ => captured.Open(row.Asset));
                }
            }

            evt.menu.AppendAction("使用箇所を表示", _ => UsagesWindow.Open(row.Type, row.Asset.Id, displayName));
            evt.menu.AppendAction("依存ツリーを表示", _ => DependencyTreeWindow.Open(row.Path, displayName));

            var archived = ArchiveTagService.IsArchived(row.Asset);
            evt.menu.AppendAction(archived ? "アーカイブを解除" : "アーカイブする", _ =>
            {
                ArchiveTagService.SetArchived(row.Asset, !archived);
                AssetDatabase.SaveAssets();
                Refresh();
            });

            evt.menu.AppendSeparator();
            evt.menu.AppendAction("削除...", _ => DeleteRows(row));
        }

        // [削除の確認画面(2026-09-14、UE の Delete Assets 相当)] — 右クリックした行が現在の選択に含まれていれば
        // 選択中の全行を対象にする(複数選択対応)。含まれていなければ右クリックした行だけを対象にする
        // (選択していない行を右クリックしたときに選択全部が対象になると驚かせてしまうため)。
        private void DeleteRows(Row clickedRow)
        {
            var selected = _listView?.selectedItems?.OfType<Row>().ToList() ?? new List<Row>();
            var rows = selected.Contains(clickedRow) && selected.Count > 1 ? selected : new List<Row> { clickedRow };

            var targets = rows
                .Where(r => r.Asset != null)
                .Select(r => new DeleteTarget(r.Asset, r.Type, r.Path))
                .ToList();

            if (targets.Count == 0)
            {
                return;
            }

            AssetDeleteWindow.Open(targets, Refresh);
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

        // ダブルクリック(または選択中に Enter。ListView.itemsChosen は両方を通す)。
        // [09_editor_tools.md] §1 — 対応する専用エディタがあれば主エディタ(DataEditorRegistry.OpenDefault、
        // Order 最小=Inspector の「エディターで開く」列の先頭と同じ)を開いて対象にする。
        // 対応エディタが無い種別は従来どおり Inspector で選択(Ping で一覧内の位置も分かるようにする)。
        private void OnItemsChosen(IEnumerable<object> items)
        {
            if (items.FirstOrDefault() is not Row row || row.Asset == null)
            {
                return;
            }

            if (DataEditorRegistry.OpenDefault(row.Asset))
            {
                return;
            }

            Selection.activeObject = row.Asset;
            EditorGUIUtility.PingObject(row.Asset);
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
