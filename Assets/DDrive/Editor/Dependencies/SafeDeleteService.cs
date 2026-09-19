using System;
using System.Collections.Generic;
using System.Text;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEditor;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-6 — 安全な削除。AssetCreationService(作成)の逆操作を同じ層(Editor/AssetBrowser 相当)に
    // 対称に用意する([09] §1 の設計メモの指示どおり)。
    //
    // 手順: ①参照チェック(1件でもあれば一覧を出して中止) → ②(削除の一部として)Archived タグを付ける
    // (まだなら。[10_workflow.md] §3 の「Archived タグ→削除」の2段階運用に合わせ、削除そのものが2段階目になる) →
    // ③確認ダイアログ → ④確定でカタログ登録解除 + Addressables エントリ削除 + アイコン PNG ごと Data を
    // MoveAssetToTrash(OS のゴミ箱。復元可能) → ⑤依存グラフの差分更新。
    public static class SafeDeleteService
    {
        // テストから差し替え可能に(CLAUDE.md の指示: ダイアログはテストで差し替え可能にする)。
        // 情報ダイアログ(OK のみ): title, message, ok
        public static Action<string, string, string> InfoDialogOverride;

        // 確認ダイアログ: title, message, ok, cancel → 続行してよいか
        public static Func<string, string, string, string, bool> ConfirmDialogOverride;

        public enum DeleteOutcome
        {
            Deleted,
            BlockedByUsages,
            GraphNotBuilt,
            CancelledByUser,
        }

        public readonly struct DeleteReport
        {
            public readonly DeleteOutcome Outcome;
            public readonly IReadOnlyList<DependencyReference> BlockingUsages;

            public DeleteReport(DeleteOutcome outcome, IReadOnlyList<DependencyReference> blockingUsages)
            {
                Outcome = outcome;
                BlockingUsages = blockingUsages ?? Array.Empty<DependencyReference>();
            }
        }

        // requireGraphBuilt: 依存グラフが空(未構築)のまま削除させない([09] §10 の「Library を消した直後は空」
        // という既知の制約に対する安全策)。テストは自分で UpdatePaths を呼んで対象を索引に載せるため true のままでよい。
        // scanCodeReferences: コード側の grep チェック(重いので必要な呼び出し元だけ true にする)。
        public static DeleteReport TryDelete(AssetDataBase asset, AssetType type, bool requireGraphBuilt = true, bool scanCodeReferences = true)
        {
            if (asset == null)
            {
                return new DeleteReport(DeleteOutcome.CancelledByUser, null);
            }

            if (requireGraphBuilt && DependencyGraphService.CachedFileCount == 0)
            {
                ShowInfo(
                    "依存関係グラフが未構築です",
                    "参照の有無を確認できません。先に Tools > D-Drive > Generate > 依存関係グラフを再構築 を実行してから、もう一度削除してください。",
                    "OK");
                return new DeleteReport(DeleteOutcome.GraphNotBuilt, null);
            }

            var usages = DependencyGraphService.FindUsages(type, asset.Id);
            if (usages.Count > 0)
            {
                ShowInfo("削除できません(参照あり)", BuildUsagesMessage(asset, usages), "OK");
                return new DeleteReport(DeleteOutcome.BlockedByUsages, usages);
            }

            var assetPath = AssetDatabase.GetAssetPath(asset);

            // ②: 削除確定の前に(まだなら)Archived タグを付ける。ここは Undo 可能な通常の Data 変更であり、
            // 最終確認でキャンセルしても残ってよい(むしろ「未使用・削除候補」の印として有用)。
            ArchiveTagService.SetArchived(asset, true);

            string codeWarning = scanCodeReferences ? CodeReferenceScan.FindPossibleReferences(asset, assetPath) : null;
            var message = BuildConfirmMessage(asset, assetPath, codeWarning);

            if (!Confirm("アセットを削除しますか?", message, "削除する", "キャンセル"))
            {
                return new DeleteReport(DeleteOutcome.CancelledByUser, null);
            }

            PerformDelete(asset, assetPath);
            return new DeleteReport(DeleteOutcome.Deleted, null);
        }

        // internal(削除の確認画面、2026-09-14): AssetDeleteWindow が「差し替えてから削除」「強制削除」の
        // 実行段で、この低レベル操作(カタログ/Addressables 登録解除 + ゴミ箱移動 + 依存グラフ更新)だけを
        // 再利用する(確認ダイアログ・参照ブロック判定は AssetDeleteWindow 側の画面が担うため、
        // TryDelete の EditorUtility.DisplayDialog 経路は通さない)。
        internal static void PerformDelete(AssetDataBase asset, string assetPath)
        {
            var iconPath = asset.Icon != null ? AssetDatabase.GetAssetPath(asset.Icon) : null;

            // カタログ登録解除(AssetCreationService.RegisterToCatalog の逆操作)。
            var address = AddressablesSync.FindCatalogAddress(asset.Id, out var catalog, includeTestFolders: true);
            if (catalog != null && !string.IsNullOrEmpty(address))
            {
                Undo.RecordObject(catalog, "D-Drive: カタログ登録解除");
                catalog.Remove(asset.Id);
                EditorUtility.SetDirty(catalog);
            }

            // Addressables エントリ削除(Data 本体。AddressablesSync.EnsureEntry の逆操作)。
            AddressablesSync.RemoveEntry(asset);

            // [44_review_2026-09-19.md] P1-1: カタログ登録解除/Addressables 削除は「一括処理」相当の
            // 機械的なクリーンアップなので版数を進めない(削除対象自体はこの後消えるため関係ない)。
            DDriveAssetSave.SaveAllSuppressed();

            // アイコン PNG ごと Data を OS のゴミ箱へ(復元可能)。先にアイコン、次に Data 本体の順で消す
            // (Data を先に消すと asset.Icon 経由の参照が失われるため、iconPath は事前に確定させてある)。
            if (!string.IsNullOrEmpty(iconPath) && !string.Equals(iconPath, assetPath, StringComparison.Ordinal))
            {
                AssetDatabase.MoveAssetToTrash(iconPath);
            }

            AssetDatabase.MoveAssetToTrash(assetPath);

            DependencyGraphService.UpdatePaths(null, new[] { assetPath });
        }

        private static string BuildUsagesMessage(AssetDataBase asset, IReadOnlyList<DependencyReference> usages)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"'{DependencyAssetResolver.DisplayNameOrFileName(asset, AssetDatabase.GetAssetPath(asset))}' は次の {usages.Count} 箇所から参照されているため削除できません。");
            sb.AppendLine("先に参照を外すか、参照元を先に整理してください。");
            sb.AppendLine();

            var shown = Math.Min(usages.Count, 20);
            for (var i = 0; i < shown; i++)
            {
                var u = usages[i];
                var objectPart = string.IsNullOrEmpty(u.ObjectPath) ? string.Empty : $" / {u.ObjectPath}";
                sb.AppendLine($"- {u.SourcePath}{objectPart} ({u.ComponentType}.{u.PropertyPath})");
            }

            if (usages.Count > shown)
            {
                sb.AppendLine($"...他 {usages.Count - shown} 件");
            }

            return sb.ToString();
        }

        private static string BuildConfirmMessage(AssetDataBase asset, string assetPath, string codeWarning)
        {
            var sb = new StringBuilder();
            var name = DependencyAssetResolver.DisplayNameOrFileName(asset, assetPath);
            sb.AppendLine($"'{name}' ({assetPath}) を削除します。");
            sb.AppendLine("カタログ登録・Addressables エントリを外し、アイコン画像ごと OS のゴミ箱へ移動します(ゴミ箱からの復元は可能ですが、カタログ/Addressables 登録は自動では戻りません)。");
            // P5 レビュー対応(2026-09-14) 整理項目: このダイアログより前に Archived タグを付けている
            // (TryDelete の②)ため、ここでキャンセルしてもタグは残る(設計判断。「未使用・削除候補」の
            // 印として有用なため意図的に戻さない)。誤解が無いよう文言で明記する。
            sb.AppendLine("キャンセルしても、削除候補として付けた Archived タグは残ります。");

            if (!string.IsNullOrEmpty(codeWarning))
            {
                sb.AppendLine();
                sb.AppendLine("注意: " + codeWarning);
            }

            return sb.ToString();
        }

        private static void ShowInfo(string title, string message, string ok)
        {
            if (InfoDialogOverride != null)
            {
                InfoDialogOverride(title, message, ok);
            }
            else
            {
                EditorUtility.DisplayDialog(title, message, ok);
            }
        }

        private static bool Confirm(string title, string message, string ok, string cancel)
        {
            return ConfirmDialogOverride != null
                ? ConfirmDialogOverride(title, message, ok, cancel)
                : EditorUtility.DisplayDialog(title, message, ok, cancel);
        }
    }
}
