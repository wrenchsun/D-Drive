using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.Menu;
using DDrive.Foundation.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-6 / [09_editor_tools.md] §1 — 「未使用検出」。FindUnusedIds の一覧をチェックボックス付きで
    // 表示し、選択項目を一括 Archive する(削除はしない。[10_workflow.md] §3 の 2 段階運用)。
    public sealed class UnusedAssetsWindow : EditorWindow
    {
        private sealed class Row
        {
            public UnusedAssetId Info;
            public bool Selected;
            public bool Archived;
        }

        private readonly List<Row> _rows = new();
        private ListView _listView;
        private Label _statusLabel;

        [MenuItem(DDriveMenu.Root + "未使用アセット")]
        public static void Open()
        {
            var window = GetWindow<UnusedAssetsWindow>("未使用アセット");
            window.minSize = new Vector2(480, 320);
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(Reload) { text = "更新" });
            toolbar.Add(new ToolbarButton(() => SetAllSelected(true)) { text = "全選択" });
            toolbar.Add(new ToolbarButton(() => SetAllSelected(false)) { text = "選択解除" });
            toolbar.Add(new ToolbarButton(ArchiveSelected) { text = "選択項目を一括Archive" });
            toolbar.Add(new ToolbarButton(() => DependencyGraphService.RebuildAll()) { text = "依存関係グラフを再構築" });
            root.Add(toolbar);

            if (DependencyGraphService.CachedFileCount == 0)
            {
                root.Add(new HelpBox(
                    "依存関係グラフが未構築です(Library を消した直後・導入直後は空)。上の「依存関係グラフを再構築」を先に実行してください。",
                    HelpBoxMessageType.Warning));
            }

            _listView = new ListView
            {
                fixedItemHeight = 24,
                selectionType = SelectionType.Single,
                makeItem = MakeRow,
                bindItem = BindRow,
                itemsSource = _rows,
            };
            _listView.style.flexGrow = 1;
            _listView.itemsChosen += items =>
            {
                if (items.FirstOrDefault() is Row row)
                {
                    DependencyJumpService.RevealAt(row.Info.AssetPath, string.Empty);
                }
            };
            root.Add(_listView);

            _statusLabel = new Label();
            root.Add(_statusLabel);

            Reload();
        }

        private void Reload()
        {
            _rows.Clear();
            foreach (var info in DependencyGraphService.FindUnusedIds())
            {
                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(info.AssetPath);
                _rows.Add(new Row { Info = info, Archived = ArchiveTagService.IsArchived(asset) });
            }

            _rows.Sort((a, b) => string.CompareOrdinal(a.Info.AssetPath, b.Info.AssetPath));
            _listView?.RefreshItems();
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (_statusLabel == null)
            {
                return;
            }

            var selected = _rows.Count(r => r.Selected);
            _statusLabel.text = $"未使用 {_rows.Count} 件(選択中 {selected} 件)。ダブルクリックで対象を選択します。";
        }

        private void SetAllSelected(bool selected)
        {
            foreach (var row in _rows)
            {
                row.Selected = selected;
            }

            _listView?.RefreshItems();
            UpdateStatus();
        }

        private void ArchiveSelected()
        {
            var targets = _rows.Where(r => r.Selected).ToList();
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("未使用アセット", "チェックを入れた項目がありません。", "OK");
                return;
            }

            var archived = 0;
            foreach (var row in targets)
            {
                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(row.Info.AssetPath);
                if (asset == null)
                {
                    continue;
                }

                ArchiveTagService.SetArchived(asset, true);
                archived++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[DDrive] 未使用アセット: {archived} 件を Archive しました。");
            Reload();
        }

        private VisualElement MakeRow()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var toggle = new Toggle { name = "toggle" };
            toggle.style.width = 20;
            // 行は ListView によって使い回される(仮想化)ため、コールバックは Bind のたびに追加せず
            // ここで 1 回だけ登録し、対象は element.userData(BindRow が都度差し替える)から読む。
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (row.userData is Row current)
                {
                    current.Selected = evt.newValue;
                    UpdateStatus();
                }
            });
            row.Add(toggle);

            var pathLabel = new Label { name = "path" };
            pathLabel.style.flexGrow = 1;
            row.Add(pathLabel);

            var stateLabel = new Label { name = "state" };
            stateLabel.style.width = 90;
            stateLabel.style.opacity = 0.7f;
            row.Add(stateLabel);

            return row;
        }

        private void BindRow(VisualElement element, int index)
        {
            var row = _rows[index];
            element.userData = row;

            var toggle = element.Q<Toggle>("toggle");
            toggle.SetValueWithoutNotify(row.Selected);

            element.Q<Label>("path").text = $"[{row.Info.Type}] {row.Info.DisplayName} ({row.Info.AssetPath})";
            element.Q<Label>("state").text = row.Archived ? "Archived 済み" : string.Empty;
        }
    }
}
