using System.Collections.Generic;
using DDrive.Foundation.Identity;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-6 / [09_editor_tools.md] §1 — 「依存関係ツリー」。選択アセットが参照する ID を
    // FindReferencesIn で再帰展開する。循環は DependencyTreeBuilder が検出して打ち切り表示する。
    public sealed class DependencyTreeWindow : EditorWindow
    {
        private string _rootPath;
        private string _rootLabel;
        private TreeView _treeView;

        public static void Open(string assetPath, string label)
        {
            var window = CreateInstance<DependencyTreeWindow>();
            window.titleContent = new GUIContent($"依存ツリー: {label}");
            window._rootPath = assetPath;
            window._rootLabel = label;
            window.minSize = new Vector2(420, 300);
            window.Show();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(Reload) { text = "更新" });
            root.Add(toolbar);

            if (DependencyGraphService.CachedFileCount == 0)
            {
                root.Add(new HelpBox("依存関係グラフが未構築です。まず Tools > D-Drive > Generate > 依存関係グラフを再構築 を実行してください。", HelpBoxMessageType.Warning));
            }

            _treeView = new TreeView
            {
                fixedItemHeight = 22,
                makeItem = () => new Label(),
                bindItem = BindItem,
            };
            _treeView.style.flexGrow = 1;
            root.Add(_treeView);

            Reload();
        }

        private void Reload()
        {
            var rootNode = DependencyTreeBuilder.Build(_rootPath, _rootLabel);
            var items = new List<TreeViewItemData<DependencyTreeNode>>();

            var nextId = 0;
            items.Add(BuildItem(rootNode, ref nextId));

            _treeView.SetRootItems(items);
            _treeView.Rebuild();
            _treeView.ExpandAll();
        }

        private static TreeViewItemData<DependencyTreeNode> BuildItem(DependencyTreeNode node, ref int nextId)
        {
            var id = nextId++;
            var children = new List<TreeViewItemData<DependencyTreeNode>>(node.Children.Count);
            foreach (var child in node.Children)
            {
                children.Add(BuildItem(child, ref nextId));
            }

            return new TreeViewItemData<DependencyTreeNode>(id, node, children);
        }

        private void BindItem(VisualElement element, int index)
        {
            var node = _treeView.GetItemDataForIndex<DependencyTreeNode>(index);
            var label = (Label)element;

            if (node.IsCycle)
            {
                label.text = $"{node.Label} (循環参照。ここで打ち切り)";
                label.style.color = new StyleColor(new Color(0.9f, 0.5f, 0.2f));
            }
            else if (node.IsUnresolved)
            {
                label.text = node.Label;
                label.style.color = new StyleColor(new Color(0.9f, 0.3f, 0.3f));
            }
            else
            {
                label.text = node.Label;
                label.style.color = StyleKeyword.Null;
            }
        }
    }
}
