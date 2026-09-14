using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;

namespace DDrive.Editor.Dependencies
{
    // [削除の確認画面(2026-09-14、UE の Delete Assets 相当)] — 削除しようとしている対象 1 件。
    // AssetBrowserWindow の行(Row)が既に Asset/Type/Path を持っているので、そのままここへ詰め替えるだけでよい。
    public readonly struct DeleteTarget
    {
        public readonly AssetDataBase Asset;
        public readonly AssetType Type;
        public readonly string Path;

        public DeleteTarget(AssetDataBase asset, AssetType type, string path)
        {
            Asset = asset;
            Type = type;
            Path = path;
        }
    }

    // 参照元ファイルの種別(表示のグルーピング用。DependencyReference.SourcePath の拡張子から判定)。
    public enum ReferenceFileKind
    {
        Unknown,
        Data,
        Prefab,
        Scene,
    }

    // 「このアセットを使っている場所」1 件。削除対象どうしの参照(まとめて消すなら問題ない)かどうかを
    // 区別して見せるため IsFromDeleteTarget を持つ([画面案] §2)。
    public readonly struct ClassifiedReference
    {
        public readonly DependencyReference Reference;
        public readonly ReferenceFileKind Kind;
        public readonly bool IsFromDeleteTarget;

        public ClassifiedReference(DependencyReference reference, ReferenceFileKind kind, bool isFromDeleteTarget)
        {
            Reference = reference;
            Kind = kind;
            IsFromDeleteTarget = isFromDeleteTarget;
        }

        public static ReferenceFileKind ClassifyPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return ReferenceFileKind.Unknown;
            }

            if (path.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase))
            {
                return ReferenceFileKind.Scene;
            }

            if (path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
            {
                return ReferenceFileKind.Prefab;
            }

            if (path.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
            {
                return ReferenceFileKind.Data;
            }

            return ReferenceFileKind.Unknown;
        }
    }

    // 「削除対象が使っているもの」1 件([画面案] §3)。WouldBecomeUnused は「この削除を実行した後の仮想状態で
    // FindUnusedIds 相当を判定した」結果(=この依存先を今使っている場所が、削除対象自身を除いて他に無い)。
    public readonly struct DependencyCandidate
    {
        public readonly AssetType Type;
        public readonly ulong Id;
        public readonly string AssetPath; // 解決できなければ空
        public readonly string DisplayName;
        public readonly bool IsUnresolved;
        public readonly bool IsAlsoDeleteTarget;
        public readonly bool WouldBecomeUnused;

        public DependencyCandidate(AssetType type, ulong id, string assetPath, string displayName, bool isUnresolved, bool isAlsoDeleteTarget, bool wouldBecomeUnused)
        {
            Type = type;
            Id = id;
            AssetPath = assetPath;
            DisplayName = displayName;
            IsUnresolved = isUnresolved;
            IsAlsoDeleteTarget = isAlsoDeleteTarget;
            WouldBecomeUnused = wouldBecomeUnused;
        }
    }

    // Analyze() の結果一式。ウィンドウはこれをそのまま表示・分岐に使う(EditMode テストからも直接組み立てを検証できる)。
    public sealed class AssetDeleteAnalysis
    {
        public readonly List<DeleteTarget> Targets = new();

        // 削除対象を使っている場所。削除対象どうしの参照(IsFromDeleteTarget=true。まとめて消すなら問題ない)も
        // 同じリストに含め、UI 側で分けて表示する(件数バッジは「外部からの参照」だけを数える)。
        public readonly List<ClassifiedReference> Usages = new();

        // 削除対象が使っているもの(全対象分をまとめて重複排除。(Type,Id) が同じなら 1 件にまとめる)。
        public readonly List<DependencyCandidate> Dependencies = new();

        public int ExternalUsageCount
        {
            get
            {
                var count = 0;
                foreach (var u in Usages)
                {
                    if (!u.IsFromDeleteTarget)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public bool HasBlockingExternalUsages => ExternalUsageCount > 0;

        public bool HasExternalSceneUsages
        {
            get
            {
                foreach (var u in Usages)
                {
                    if (!u.IsFromDeleteTarget && u.Kind == ReferenceFileKind.Scene)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    public static class AssetDeleteAnalysisService
    {
        public static AssetDeleteAnalysis Analyze(IReadOnlyList<DeleteTarget> targets)
        {
            var analysis = new AssetDeleteAnalysis();
            if (targets == null || targets.Count == 0)
            {
                return analysis;
            }

            analysis.Targets.AddRange(targets);

            var targetPaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var targetKeys = new HashSet<(AssetType, ulong)>();
            foreach (var t in targets)
            {
                if (!string.IsNullOrEmpty(t.Path))
                {
                    targetPaths.Add(t.Path);
                }

                targetKeys.Add((t.Type, t.Asset != null ? t.Asset.Id : 0));
            }

            // §2: 参照元一覧(削除対象どうしの参照も含めて集め、IsFromDeleteTarget で区別する)。
            foreach (var t in targets)
            {
                if (t.Asset == null)
                {
                    continue;
                }

                foreach (var usage in DependencyGraphService.FindUsages(t.Type, t.Asset.Id))
                {
                    var kind = ClassifiedReference.ClassifyPath(usage.SourcePath);
                    var fromTarget = targetPaths.Contains(usage.SourcePath);
                    analysis.Usages.Add(new ClassifiedReference(usage, kind, fromTarget));
                }
            }

            // §3: 依存先一覧((Type,Id) で重複排除)。
            var seenDependency = new HashSet<(AssetType, ulong)>();
            foreach (var t in targets)
            {
                if (string.IsNullOrEmpty(t.Path))
                {
                    continue;
                }

                foreach (var edge in DependencyGraphService.FindReferencesIn(t.Path))
                {
                    var key = (edge.TargetType, edge.TargetId);
                    if (!seenDependency.Add(key))
                    {
                        continue;
                    }

                    var found = DependencyAssetResolver.Find(edge.TargetType, edge.TargetId);
                    var isAlsoTarget = targetKeys.Contains(key);

                    var wouldBecomeUnused = false;
                    if (!isAlsoTarget)
                    {
                        wouldBecomeUnused = true;
                        foreach (var usage in DependencyGraphService.FindUsages(edge.TargetType, edge.TargetId))
                        {
                            if (!targetPaths.Contains(usage.SourcePath))
                            {
                                wouldBecomeUnused = false;
                                break;
                            }
                        }
                    }

                    analysis.Dependencies.Add(new DependencyCandidate(
                        edge.TargetType,
                        edge.TargetId,
                        found.IsValid ? found.Path : string.Empty,
                        found.IsValid ? DependencyAssetResolver.DisplayNameOrFileName(found.Asset, found.Path) : $"<{edge.TargetType} #{edge.TargetId:X}が見つかりません>",
                        !found.IsValid,
                        isAlsoTarget,
                        wouldBecomeUnused));
                }
            }

            return analysis;
        }
    }
}
