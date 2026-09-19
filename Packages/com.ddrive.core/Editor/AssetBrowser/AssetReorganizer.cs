using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.Menu;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.AssetBrowser
{
    // [01_architecture.md] §5 / [10_workflow.md] §3 — 「配置と名前はツールが維持する」の維持側。
    // GameData 配下の管理データを走査し、現在のカテゴリに合わせて
    //   1. フォルダ移動   (Audio/SE/Player/ のようなカテゴリ階層へ)
    //   2. 追従リネーム   (SE_Player_Slash のカテゴリセグメントを最新化)
    //   3. カタログ更新   (リネームで Address が変わった分を AddOrUpdate)
    // を行う。移動/リネームは GUID を変えないため ID 参照は壊れない(壊れないことがこの設計の前提)。
    // GameData 外のアセット(人間管理の実データ等)には一切触らない。
    public static class AssetReorganizer
    {
        public sealed class Result
        {
            public int Scanned;
            public int Moved;
            public int Renamed;
            public int Skipped;
            public readonly List<string> Warnings = new();
        }

        // トリム済み wav の付随ファイル探索上限(SeTrimApplier の命名規則 <base>_Trimmed_<i>.wav に対応)。
        private const int CompanionProbeLimit = 64;

        [MenuItem(DDriveMenu.Generate + "GameData をカテゴリ配置に整理")]
        public static void RunFromMenu()
        {
            var result = Reorganize(AssetCreationService.ResolveGameDataRoot(AssetCreationService.DefaultGameDataRoot));

            foreach (var warning in result.Warnings)
            {
                Debug.LogWarning($"[DDrive] {warning}");
            }

            Debug.Log($"[DDrive] 整理完了: 対象 {result.Scanned} 件 / フォルダ移動 {result.Moved} / リネーム {result.Renamed} / スキップ {result.Skipped}");
        }

        public static Result Reorganize(string gameDataRoot)
        {
            var result = new Result();

            if (!AssetDatabase.IsValidFolder(gameDataRoot))
            {
                return result;
            }

            var definitionByDataType = Inspectors.AssetIdLookup.GetAllDefinitions()
                .Where(d => d.dataType.Namespace?.Contains("Tests") != true)
                .ToDictionary(d => d.dataType, d => d.assetType);

            // [09] §9: FindAssets は AssetSearch 経由(ネイティブメモリを抱え込まないようキャッシュする)。
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(AssetDataBase), new[] { gameDataRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(path);
                if (asset == null || !definitionByDataType.TryGetValue(asset.GetType(), out var assetType))
                {
                    continue;
                }

                result.Scanned++;

                var currentName = System.IO.Path.GetFileNameWithoutExtension(path);
                var currentFolder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
                var expectedFolder = $"{gameDataRoot}/{AssetNamingService.GetTargetFolder(assetType, asset.Category)}";
                var expectedName = ExpectedFileName(assetType, asset.Category, currentName);
                var expectedPath = $"{expectedFolder}/{expectedName}.asset";

                if (expectedPath == path)
                {
                    continue;
                }

                if (AssetDatabase.LoadAssetAtPath<Object>(expectedPath) != null)
                {
                    result.Skipped++;
                    result.Warnings.Add($"{path}: 移動先 '{expectedPath}' に別のファイルが存在するためスキップしました。");
                    continue;
                }

                AssetCreationService.EnsureFolder(expectedFolder);

                var error = AssetDatabase.MoveAsset(path, expectedPath);
                if (!string.IsNullOrEmpty(error))
                {
                    result.Skipped++;
                    result.Warnings.Add($"{path}: 移動に失敗しました({error})。");
                    continue;
                }

                if (currentFolder != expectedFolder)
                {
                    result.Moved++;
                }

                if (currentName != expectedName)
                {
                    result.Renamed++;
                    UpdateCatalogAddress(gameDataRoot, assetType, asset, expectedName);
                }

                MoveCompanionWavs(currentFolder, currentName, expectedFolder, expectedName, result);
            }

            // [44_review_2026-09-19.md] P1-1: 規約違反の移動/リネームを一括で直す機械的な整理処理なので、
            // 版数を進めない(何件動くか呼び出し元も把握しきれない)。
            DDriveAssetSave.SaveAllSuppressed();
            RefreshOpenBrowsers();
            return result;
        }

        // 規約ファイル名 = <種別プレフィックス>_<カテゴリ末尾セグメント>_<識別子>。
        // 識別子はアセットに保存されないため、現ファイル名の末尾トークンから復元する。
        // 復元できない(手付け名や重複サフィックス " 1" 等)場合は名前を維持し、フォルダ移動のみ行う
        // (無理にリネームすると GenerateUniqueAssetPath 由来の名前が毎回変わる「churn」を起こすため)。
        private static string ExpectedFileName(AssetType assetType, string category, string currentName)
        {
            var tokens = currentName.Split('_');
            var identifier = tokens[tokens.Length - 1];

            if (tokens.Length < 2 ||
                tokens[0] != AssetNamingService.GetTypePrefix(assetType) ||
                !AssetNamingService.IsValidIdentifier(identifier))
            {
                return currentName;
            }

            return AssetNamingService.BuildFileName(assetType, category, identifier);
        }

        // リネームで Address(=ファイル名)が変わった場合、カタログの該当エントリを ID キーで上書きする。
        private static void UpdateCatalogAddress(string gameDataRoot, AssetType assetType, AssetDataBase asset, string newAddress)
        {
            var catalogPath = $"{gameDataRoot}/Catalogs/{AssetCreationService.GetCatalogName(assetType)}.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>(catalogPath);
            if (catalog == null)
            {
                return;
            }

            catalog.AddOrUpdate(new CatalogEntry
            {
                Id = asset.Id,
                Type = assetType,
                Address = newAddress,
                Flags = asset.Flags,
            });

            EditorUtility.SetDirty(catalog);
        }

        // SeTrimApplier がベイクした <base>_Trimmed_<i>.wav は SeData の隣に置く規約のため、一緒に移動する。
        // 参照は GUID なので移動しても Clips は切れないが、置き去りにすると次回のトリム適用で孤児化する。
        private static void MoveCompanionWavs(string oldFolder, string oldName, string newFolder, string newName, Result result)
        {
            for (var i = 0; i < CompanionProbeLimit; i++)
            {
                var oldWav = $"{oldFolder}/{oldName}_Trimmed_{i}.wav";
                if (AssetDatabase.LoadAssetAtPath<AudioClip>(oldWav) == null)
                {
                    continue;
                }

                var newWav = $"{newFolder}/{newName}_Trimmed_{i}.wav";
                var error = AssetDatabase.MoveAsset(oldWav, newWav);
                if (!string.IsNullOrEmpty(error))
                {
                    result.Warnings.Add($"{oldWav}: トリム済み wav の移動に失敗しました({error})。");
                }
            }
        }

        private static void RefreshOpenBrowsers()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<AssetBrowserWindow>())
            {
                window.Refresh();
            }
        }
    }
}
