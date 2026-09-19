using System.Collections.Generic;
using DDrive.Editor.Dependencies;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Loading;

namespace DDrive.Editor.Preload
{
    // [11_tasks.md] 5-7 / [10_workflow.md] §5 — 「このシーンで参照される ID」を依存関係グラフ(5-5/5-6)から
    // 再帰的に集計する。DependencyTreeBuilder(5-6, Editor/Dependencies/DependencyTreeNode.cs)と同じ
    // 「祖先パスの集合で循環を検出して打ち切る」方式をそのまま流用し、ツリーではなくフラットな重複無し
    // 集合(PreloadEntry の Id で dedup)を作る点だけが異なる。
    //
    // 見つからない参照先(削除済み・Addressables 未登録等)も PreloadEntry としては採用する
    // (DisplayName に "見つかりません" と焼き込むだけ。ランタイムの ScenePreload 側で Address 解決に
    // 失敗した時点で改めて警告+スキップされるため、ここで除外する必要はない)。ただし見つからない
    // 参照先はそれ以上辿れないため展開はしない。
    public static class ScenePreloadAggregator
    {
        private const int MaxDepth = 32;

        public static List<PreloadEntry> Aggregate(string rootPath)
        {
            var result = new Dictionary<ulong, PreloadEntry>();
            if (string.IsNullOrEmpty(rootPath))
            {
                return new List<PreloadEntry>();
            }

            var ancestry = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { rootPath };
            Collect(rootPath, ancestry, 0, result);

            var list = new List<PreloadEntry>(result.Values);
            list.Sort((a, b) => a.Id.CompareTo(b.Id)); // ID 昇順(10_workflow.md §4 の追記型運用と同じ考え方でコンフリクトを減らす)
            return list;
        }

        private static void Collect(string sourcePath, HashSet<string> ancestry, int depth, Dictionary<ulong, PreloadEntry> result)
        {
            if (depth >= MaxDepth || string.IsNullOrEmpty(sourcePath))
            {
                return;
            }

            foreach (var edge in DependencyGraphService.FindReferencesIn(sourcePath))
            {
                if (!result.ContainsKey(edge.TargetId))
                {
                    var found = DependencyAssetResolver.Find(edge.TargetType, edge.TargetId);
                    var displayName = found.IsValid
                        ? DependencyAssetResolver.DisplayNameOrFileName(found.Asset, found.Path)
                        : $"<{edge.TargetType} #{edge.TargetId:X} が見つかりません>";
                    result[edge.TargetId] = new PreloadEntry(edge.TargetType, edge.TargetId, displayName);
                }

                var resolved = DependencyAssetResolver.Find(edge.TargetType, edge.TargetId);
                if (!resolved.IsValid || ancestry.Contains(resolved.Path))
                {
                    continue; // 未解決、または循環(打ち切り)
                }

                ancestry.Add(resolved.Path);
                Collect(resolved.Path, ancestry, depth + 1, result);
                ancestry.Remove(resolved.Path);
            }
        }
    }
}
