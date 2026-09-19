using System.Collections.Generic;
using DDrive.Foundation.Identity;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-6 — 依存関係ツリー UI 用のノード。DependencyTreeBuilder が組み立てる。
    public sealed class DependencyTreeNode
    {
        public string Label;
        public string AssetPath; // 解決できなかった場合は空
        public AssetType Type;
        public ulong Id;
        public bool IsCycle;
        public bool IsUnresolved; // (Type, Id) を持つ Data が見つからない(削除済み・欠落)
        public readonly List<DependencyTreeNode> Children = new();
    }

    // 選択アセットが参照する ID を FindReferencesIn で再帰展開する([09] §1 の「依存関係ツリー」)。
    // 循環は経路(現在たどっている親の連なり)で検出して打ち切る。ダイヤモンド形の共有参照(循環でない)は
    // 複数回展開してよい(実際のツリー UI として自然な見え方になるため)。
    public static class DependencyTreeBuilder
    {
        private const int MaxDepth = 32;

        public static DependencyTreeNode Build(string rootPath, string rootLabel)
        {
            var root = new DependencyTreeNode { Label = rootLabel, AssetPath = rootPath };
            var pathStack = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { rootPath };
            Expand(root, rootPath, pathStack, 0);
            return root;
        }

        private static void Expand(DependencyTreeNode node, string sourcePath, HashSet<string> ancestry, int depth)
        {
            if (depth >= MaxDepth || string.IsNullOrEmpty(sourcePath))
            {
                return;
            }

            foreach (var edge in DependencyGraphService.FindReferencesIn(sourcePath))
            {
                var found = DependencyAssetResolver.Find(edge.TargetType, edge.TargetId);
                var child = new DependencyTreeNode
                {
                    Type = edge.TargetType,
                    Id = edge.TargetId,
                    AssetPath = found.Path,
                    Label = found.IsValid
                        ? DependencyAssetResolver.DisplayNameOrFileName(found.Asset, found.Path)
                        : $"<{edge.TargetType} #{edge.TargetId:X}が見つかりません>",
                    IsUnresolved = !found.IsValid,
                };

                node.Children.Add(child);

                if (!found.IsValid)
                {
                    continue;
                }

                if (ancestry.Contains(found.Path))
                {
                    child.IsCycle = true;
                    continue;
                }

                ancestry.Add(found.Path);
                Expand(child, found.Path, ancestry, depth + 1);
                ancestry.Remove(found.Path);
            }
        }
    }
}
