using System.Collections.Generic;
using DDrive.Foundation.Identity;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-6 / [09_editor_tools.md] §1 — 「使用箇所検索」。AssetBrowser の行コンテキスト
    // メニュー / Inspector の「使用箇所を表示」から開く。ダブルクリックでジャンプ(DependencyJumpService)。
    public sealed class UsagesWindow : EditorWindow
    {
        private readonly List<DependencyReference> _rows = new();
        private ListView _listView;
        private Label _statusLabel;
        private AssetType _type;
        private ulong _id;
        private string _title;

        public static void Open(AssetType type, ulong id, string title)
        {
            var window = CreateInstance<UsagesWindow>();
            window.titleContent = new GUIContent($"使用箇所: {title}");
            window._type = type;
            window._id = id;
            window._title = title;
            window.minSize = new Vector2(480, 260);
            window.Show();
        }

        private void CreateGUI()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rootVisualElement.Add(scroll);

            if (DependencyGraphService.CachedFileCount == 0)
            {
                var warning = new HelpBox(
                    "依存関係グラフが未構築です。Library を消した直後や導入直後は空のことがあります。",
                    HelpBoxMessageType.Warning);
                scroll.Add(warning);

                var rebuild = new Button(() =>
                {
                    DependencyGraphService.RebuildAll();
                    Reload();
                })
                { text = "依存関係グラフを再構築" };
                scroll.Add(rebuild);
            }

            _listView = new ListView
            {
                fixedItemHeight = 40,
                selectionType = SelectionType.Single,
                makeItem = MakeRow,
                bindItem = BindRow,
                itemsSource = _rows,
            };
            _listView.style.flexGrow = 1;
            _listView.itemsChosen += items =>
            {
                foreach (var item in items)
                {
                    if (item is DependencyReference reference)
                    {
                        DependencyJumpService.Reveal(reference);
                    }

                    break;
                }
            };
            scroll.Add(_listView);

            _statusLabel = new Label();
            scroll.Add(_statusLabel);

            Reload();
        }

        private void Reload()
        {
            _rows.Clear();
            _rows.AddRange(DependencyGraphService.FindUsages(_type, _id));
            _listView?.RefreshItems();

            if (_statusLabel != null)
            {
                _statusLabel.text = _rows.Count == 0
                    ? "どこからも参照されていません(未使用)。"
                    : $"{_rows.Count} 箇所から参照されています。ダブルクリックでジャンプします。";
            }
        }

        private static VisualElement MakeRow()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Column, paddingTop = 2, paddingBottom = 2 } };
            row.Add(new Label { name = "source", style = { unityFontStyleAndWeight = FontStyle.Bold } });
            row.Add(new Label { name = "detail", style = { opacity = 0.75f } });
            return row;
        }

        private void BindRow(VisualElement element, int index)
        {
            var reference = _rows[index];
            var objectPart = string.IsNullOrEmpty(reference.ObjectPath) ? string.Empty : $" / {reference.ObjectPath}";
            element.Q<Label>("source").text = $"{reference.SourcePath}{objectPart}";
            element.Q<Label>("detail").text = $"{reference.ComponentType}.{reference.PropertyPath}";
        }
    }
}
