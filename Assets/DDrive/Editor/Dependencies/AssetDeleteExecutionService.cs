using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using UnityEditor;

namespace DDrive.Editor.Dependencies
{
    // [削除の確認画面(2026-09-14、UE の Delete Assets 相当)] — AssetDeleteWindow が選んだ操作を実行する。
    // ウィンドウ(UI)から実行ロジックを分離してあるので、EditMode テストは EditorWindow を開かずに
    // このクラスだけを呼んで検証できる(CLAUDE.md §0-9 の「新規 EditorWindow は ScrollView ルート必須」等の
    // UI 側の制約と、削除の実処理を混ぜないため)。
    public enum DeleteAction
    {
        Cancel,
        ArchiveOnly,
        ForceDelete,
        ReplaceThenDelete,
    }

    public sealed class DeleteExecutionRequest
    {
        public List<DeleteTarget> PrimaryTargets = new();

        // §3「一緒に削除」で選んだ依存先(解決できたものだけをここに詰める)。
        public List<DeleteTarget> CascadeTargets = new();

        public DeleteAction Action;

        // ReplaceThenDelete のときのみ使う(Primary 1件につき最大1プラン。差し替え先を選ばなかった対象は
        // 元から外部参照が無ければそのまま削除される)。
        public List<ReplacementPlan> ReplacementPlans = new();

        public bool ScanCodeReferences = true;
    }

    public sealed class DeleteExecutionResult
    {
        public sealed class PerAssetResult
        {
            public DeleteTarget Target;
            public bool Deleted;
            public bool ArchivedOnly;
            public string CodeReferenceWarning;

            // 2026-09-17(docs/41_phase6_review_2026-09-17.md P2-7) 追加 —
            // コード参照のヒット(ファイル:行)。**削除前**に取らなければならない:
            // `MoveAssetToTrash` 済みの `AssetDataBase` は fake null になるため、結果画面で
            // `r.Target.Asset != null` を条件に取り直していた旧実装ではヒット一覧が絶対に出なかった
            // (docs/09 §10 の「クリックでエディタを開く」が機能していなかった)。
            // 走査していない / ヒット無しのときは null。
            public List<CodeReferenceScan.Hit> CodeReferenceHits;
        }

        // 2026-09-17([41] P2-8) 追加 — 結果画面の文言を実際に選ばれた操作で出し分けるために持つ
        // (`Deleted=false` が一律「参照が残っているため削除せず…」だったため、ユーザーが明示的に
        // 「アーカイブのみ」を選んだ場合にも同じ文言が出ていた)。
        public DeleteAction Action;

        public readonly List<PerAssetResult> Results = new();
        public readonly List<string> ChangedDataPaths = new(); // 参照差し替えで書き換わった Data
        public readonly List<string> ChangedPrefabPaths = new(); // 同 Prefab
        public readonly List<DependencyReference> RemainingSceneUsages = new(); // 手動で直す一覧(ジャンプ可能)
        public readonly List<DependencyReference> SkippedCascadeStillUsed = new(); // 「一緒に削除」を選んだが結局使われていて削除しなかったもの
    }

    public static class AssetDeleteExecutionService
    {
        public static DeleteExecutionResult Execute(DeleteExecutionRequest request)
        {
            var result = new DeleteExecutionResult();
            if (request == null || request.Action == DeleteAction.Cancel)
            {
                return result;
            }

            result.Action = request.Action;

            switch (request.Action)
            {
                case DeleteAction.ArchiveOnly:
                    ExecuteArchiveOnly(request, result);
                    break;

                case DeleteAction.ForceDelete:
                    ExecuteForceDelete(request, result);
                    break;

                case DeleteAction.ReplaceThenDelete:
                    ExecuteReplaceThenDelete(request, result);
                    break;
            }

            AssetDatabase.SaveAssets();
            return result;
        }

        private static void ExecuteArchiveOnly(DeleteExecutionRequest request, DeleteExecutionResult result)
        {
            foreach (var t in AllTargets(request))
            {
                ArchiveTagService.SetArchived(t.Asset, true);
                result.Results.Add(new DeleteExecutionResult.PerAssetResult { Target = t, ArchivedOnly = true });
            }
        }

        private static void ExecuteForceDelete(DeleteExecutionRequest request, DeleteExecutionResult result)
        {
            foreach (var t in AllTargets(request))
            {
                var perAsset = new DeleteExecutionResult.PerAssetResult { Target = t, Deleted = true };
                CollectCodeReferences(request, t, perAsset);
                ArchiveTagService.SetArchived(t.Asset, true);
                SafeDeleteService.PerformDelete(t.Asset, t.Path);
                result.Results.Add(perAsset);
            }
        }

        private static void ExecuteReplaceThenDelete(DeleteExecutionRequest request, DeleteExecutionResult result)
        {
            var replaceResult = ReferenceReplaceService.Replace(request.ReplacementPlans);
            result.ChangedDataPaths.AddRange(replaceResult.ChangedDataPaths);
            result.ChangedPrefabPaths.AddRange(replaceResult.ChangedPrefabPaths);
            result.RemainingSceneUsages.AddRange(replaceResult.RemainingSceneUsages);

            var primaryPaths = new HashSet<string>(request.PrimaryTargets.Select(t => t.Path), System.StringComparer.OrdinalIgnoreCase);
            var deletedPaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var t in request.PrimaryTargets)
            {
                // 差し替え後も外部(削除対象以外)から使われている場所が残っているか(Scene 参照は自動で
                // 差し替えないため、Scene から使われていれば必ずここに残る)。
                var stillUsedExternally = DependencyGraphService.FindUsages(t.Type, t.Asset.Id)
                    .Any(u => !primaryPaths.Contains(u.SourcePath));

                if (stillUsedExternally)
                {
                    // [設計方針] Scene 参照が残っている間は削除を保留し、Archive だけ行う。
                    ArchiveTagService.SetArchived(t.Asset, true);
                    result.Results.Add(new DeleteExecutionResult.PerAssetResult { Target = t, ArchivedOnly = true });
                    continue;
                }

                var perAsset = new DeleteExecutionResult.PerAssetResult { Target = t, Deleted = true };
                CollectCodeReferences(request, t, perAsset);
                ArchiveTagService.SetArchived(t.Asset, true);
                SafeDeleteService.PerformDelete(t.Asset, t.Path);
                deletedPaths.Add(t.Path);
                result.Results.Add(perAsset);
            }

            // 「一緒に削除」対象は、実際に削除された Primary(と他の一緒に削除対象)以外から使われていなければ削除する。
            // 差し替え直後に決めるのではなく実行結果(deletedPaths)を見て判定するので、Scene 参照で保留された
            // Primary に依存していた場合は安全側(削除しない)に倒れる。
            var cascadePaths = new HashSet<string>(request.CascadeTargets.Select(c => c.Path), System.StringComparer.OrdinalIgnoreCase);
            foreach (var c in request.CascadeTargets)
            {
                var stillUsed = DependencyGraphService.FindUsages(c.Type, c.Asset.Id)
                    .Any(u => !deletedPaths.Contains(u.SourcePath) && !cascadePaths.Contains(u.SourcePath));

                if (stillUsed)
                {
                    result.SkippedCascadeStillUsed.AddRange(DependencyGraphService.FindUsages(c.Type, c.Asset.Id));
                    continue;
                }

                var perAsset = new DeleteExecutionResult.PerAssetResult { Target = c, Deleted = true };
                CollectCodeReferences(request, c, perAsset);
                ArchiveTagService.SetArchived(c.Asset, true);
                SafeDeleteService.PerformDelete(c.Asset, c.Path);
                result.Results.Add(perAsset);
            }
        }

        // 2026-09-17([41] P2-7) — 警告文言とヒット一覧(ファイル:行)を**削除の前に**まとめて取る。
        // 削除後に取り直すと `MoveAssetToTrash` 済みのオブジェクトが fake null になり何も取れない。
        private static void CollectCodeReferences(DeleteExecutionRequest request, DeleteTarget target, DeleteExecutionResult.PerAssetResult into)
        {
            if (!request.ScanCodeReferences || target.Asset == null)
            {
                return;
            }

            into.CodeReferenceWarning = CodeReferenceScan.FindPossibleReferences(target.Asset, target.Path);
            if (!string.IsNullOrEmpty(into.CodeReferenceWarning))
            {
                // 走査(キャッシュ済みのファイル内容)を共有するので、警告が出たときだけ行番号を数える。
                into.CodeReferenceHits = CodeReferenceScan.FindPossibleReferenceHits(target.Asset, target.Path);
            }
        }

        private static IEnumerable<DeleteTarget> AllTargets(DeleteExecutionRequest request)
        {
            foreach (var t in request.PrimaryTargets)
            {
                yield return t;
            }

            foreach (var t in request.CascadeTargets)
            {
                yield return t;
            }
        }
    }
}
