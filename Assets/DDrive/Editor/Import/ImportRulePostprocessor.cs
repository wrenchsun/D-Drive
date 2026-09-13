using System.Collections.Generic;
using DDrive.Editor.Menu;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Import
{
    // [11_tasks.md] 5-11 — SourceAssets/<種別>/<カテゴリ>/ への元ファイル配置を検知する入口。
    // MayaModelPostprocessor(3-7)と同じ形: インポート中はアセットを作れないため、
    // delayCall でインポート完了後に ImportRuleService.ProcessPaths をまとめて実行する。
    public sealed class ImportRulePostprocessor : AssetPostprocessor
    {
        // テスト / 一括インポート中の抑止(ImportRuleService.AutoImport とは別に、Postprocessor 自体を止める用)。
        public static bool Suppress;

        private static readonly List<string> Pending = new();

        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (Suppress || !ImportRuleService.AutoImport)
            {
                return;
            }

            // 元ファイル削除時は Data を消さない(§0 TL;DR / [11] 5-11 AC)ため deletedAssets は見ない。
            AddPending(importedAssets);
            AddPending(movedAssets);

            if (Pending.Count == 0)
            {
                return;
            }

            EditorApplication.delayCall -= Flush;
            EditorApplication.delayCall += Flush;
        }

        private static void AddPending(string[] paths)
        {
            if (paths == null)
            {
                return;
            }

            foreach (var path in paths)
            {
                if (!string.IsNullOrEmpty(path) && !Pending.Contains(path))
                {
                    Pending.Add(path);
                }
            }
        }

        private static void Flush()
        {
            var paths = Pending.ToArray();
            Pending.Clear();
            if (paths.Length == 0)
            {
                return;
            }

            var report = ImportRuleService.ProcessPaths(paths);
            if (report.Created > 0)
            {
                Debug.Log($"[DDrive] ImportRule: {report}");
            }
        }

        // 手動: AutoImport=OFF の期間中や、機能導入前から置かれていたファイル用に SourceAssets を丸ごと洗い直す。
        [MenuItem(DDriveMenu.Generate + "SourceAssets からインポートルールを再実行")]
        private static void RunScanAll()
        {
            var report = ImportRuleService.ScanAll();
            Debug.Log($"[DDrive] ImportRule 再実行: {report}");
        }
    }
}
