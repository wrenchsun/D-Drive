using System;
using System.Collections.Generic;
using System.IO;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Data;
using UnityEditor;

namespace DDrive.Editor.Import
{
    // [11_tasks.md] 5-11 / [09_editor_tools.md] / [10_workflow.md] §3.3 実装メモ(2026-09-14)。
    // 「SourceAssets/<種別>/<カテゴリ>/ に元ファイルを置くだけで Data・ID・Addressables 登録ができる」を実現する中核。
    // 作成そのものは既存の AssetCreationService.Create(意味情報のみ入力 → ファイル名/ID/カタログ/Addressables を自動生成、
    // [06_material_texture.md] A-2 の Maya→Material と同じ経路)を再利用し、ここでは
    // 「どのフォルダがどの種別か」「元ファイルからどの Object を読むか」「Data のどのフィールドに入れるか」だけを扱う。
    // 再インポート時の二重生成防止は AssetDataBase.ImportSourceGuid(元ファイルの GUID)で同定する。
    // 元ファイルが削除されても Data は消さない(参照フィールドが null になるだけ) — 各種別の既存 Validator
    // (SeDataValidator 等の「未設定(または Missing)です」)がそのまま「欠落」表示を担う。新規 Validator は追加していない。
    public static class ImportRuleService
    {
        public const string DefaultSourceRoot = "Assets/SourceAssets";

        // テスト / 一括インポート中の抑止。既存の MayaModelPostprocessor.Suppress 等と同じ役割。
        public static bool AutoImport = true;

        public sealed class Report
        {
            public int Created;
            public int Skipped;
            public readonly List<string> Lines = new();

            public void Log(string line) => Lines.Add(line);

            public override string ToString()
                => $"Data 新規 {Created}(既存スキップ {Skipped})\n" + string.Join("\n", Lines);
        }

        // フォルダ名(SourceAssets/<ここ>/...) → ハンドラ。Cutscene(6-10c)等の追加はここに 1 行足すだけで済む。
        private static readonly IImportRuleHandler[] AllHandlers =
        {
            new SeImportHandler(),
            new BgmImportHandler(),
            new TextureImportHandler(),
            new ModelImportHandler(),
            new AnimImportHandler(),
            new Anim2DImportHandler(),
            new PrefabImportHandler(),
            new CanvasImportHandler(),
            new VfxImportHandler(),
        };

        private static Dictionary<string, IImportRuleHandler> _handlersByFolder;

        private static Dictionary<string, IImportRuleHandler> HandlersByFolder
        {
            get
            {
                if (_handlersByFolder == null)
                {
                    _handlersByFolder = new Dictionary<string, IImportRuleHandler>(StringComparer.Ordinal);
                    foreach (var handler in AllHandlers)
                    {
                        _handlersByFolder[handler.TypeFolder] = handler;
                    }
                }

                return _handlersByFolder;
            }
        }

        // テストからも直接呼べる中核処理(AssetPostprocessor 経由の実運用では delayCall でまとめて渡される)。
        // sourceRoot/gameDataRoot を引数化しているのは AssetCreationServiceTests 等と同じ「実データを汚さない」ため。
        public static Report ProcessPaths(
            IEnumerable<string> paths,
            string sourceRoot = DefaultSourceRoot,
            string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            var report = new Report();
            if (paths == null)
            {
                return report;
            }

            var indexCache = new Dictionary<Type, Dictionary<string, AssetDataBase>>();

            foreach (var rawPath in paths)
            {
                var path = rawPath?.Replace('\\', '/');
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                if (!TryMatchRule(path, sourceRoot, out var handler, out var category))
                {
                    continue;
                }

                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (Array.IndexOf(handler.Extensions, ext) < 0)
                {
                    continue;
                }

                ImportOne(handler, path, category, gameDataRoot, indexCache, report);
            }

            return report;
        }

        // 既存の SourceAssets/<種別>/ 配下を丸ごと洗い直す(AutoImport=OFF だった期間分の取りこぼしや、
        // この機能を有効化する前から置かれていたファイル用の手動フォールバック。Maya の「選択したモデルから生成」と同じ位置付け)。
        public static Report ScanAll(string sourceRoot = DefaultSourceRoot, string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            if (!AssetDatabase.IsValidFolder(sourceRoot))
            {
                return new Report();
            }

            var absoluteRoot = Path.GetFullPath(sourceRoot);
            if (!Directory.Exists(absoluteRoot))
            {
                return new Report();
            }

            var projectRoot = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";
            var paths = new List<string>();
            foreach (var file in Directory.GetFiles(absoluteRoot, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var normalized = file.Replace('\\', '/');
                if (normalized.StartsWith(projectRoot, StringComparison.Ordinal))
                {
                    paths.Add(normalized.Substring(projectRoot.Length));
                }
            }

            return ProcessPaths(paths, sourceRoot, gameDataRoot);
        }

        private static bool TryMatchRule(string assetPath, string sourceRoot, out IImportRuleHandler handler, out string category)
        {
            handler = null;
            category = null;

            var root = (string.IsNullOrEmpty(sourceRoot) ? DefaultSourceRoot : sourceRoot).TrimEnd('/') + "/";
            if (!assetPath.StartsWith(root, StringComparison.Ordinal))
            {
                return false;
            }

            var rest = assetPath.Substring(root.Length); // "<TypeFolder>/<カテゴリ.../>ファイル名"
            var firstSlash = rest.IndexOf('/');
            if (firstSlash < 0)
            {
                return false; // 種別フォルダの直下(種別が分からない)は対象外
            }

            var typeFolder = rest.Substring(0, firstSlash);
            if (!HandlersByFolder.TryGetValue(typeFolder, out handler))
            {
                return false;
            }

            var remainder = rest.Substring(firstSlash + 1); // "<カテゴリ.../>ファイル名"
            var lastSlash = remainder.LastIndexOf('/');
            var fileName = lastSlash < 0 ? remainder : remainder.Substring(lastSlash + 1);
            category = lastSlash < 0 ? string.Empty : remainder.Substring(0, lastSlash);

            return !string.IsNullOrEmpty(fileName);
        }

        private static void ImportOne(
            IImportRuleHandler handler,
            string assetPath,
            string category,
            string gameDataRoot,
            Dictionary<Type, Dictionary<string, AssetDataBase>> indexCache,
            Report report)
        {
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                return;
            }

            var index = GetIndex(handler.DataType, gameDataRoot, indexCache);
            if (index.TryGetValue(guid, out var existing) && existing != null)
            {
                // 既に取り込み済み(再インポート等)。デザイナーの調整を上書きしないよう、何もしない。
                report.Skipped++;
                return;
            }

            var source = handler.LoadSource(assetPath);
            if (source == null)
            {
                report.Log($"スキップ: {assetPath}(参照できる元データが見つかりません)");
                return;
            }

            // 識別子・表示名は元ファイルの「ファイル名」から作る(FBX ルートやプレハブのルート GameObject の名前は
            // デザイナーが揃えているとは限らないため使わない。「ファイルを置くだけ」の直感に合わせる)。
            var rawName = Path.GetFileNameWithoutExtension(assetPath);
            var identifier = AssetNamingService.ToIdentifier(rawName, handler.IdentifierFallback);

            var capturedSource = source;
            var created = AssetCreationService.Create(handler.DataType, handler.Target, rawName, category, identifier, data =>
            {
                data.ImportSourceGuid = guid;
                handler.Configure(data, capturedSource, assetPath);
            }, gameDataRoot);

            if (created == null)
            {
                report.Log($"失敗: {assetPath}(識別子 '{identifier}' が規約に合わない可能性があります)");
                return;
            }

            index[guid] = created;
            report.Created++;
            report.Log($"新規: {created.name} ← {assetPath}");
        }

        // 種別(DataType)ごとに ImportSourceGuid → Data の索引を 1 度だけ作って使い回す
        // ([06_material_texture.md] A-2 実装メモの MayaMaterialImporter.BeginBatch/EndBatch と同じ狙い)。
        private static Dictionary<string, AssetDataBase> GetIndex(
            Type dataType, string gameDataRoot, Dictionary<Type, Dictionary<string, AssetDataBase>> cache)
        {
            if (cache.TryGetValue(dataType, out var found))
            {
                return found;
            }

            var index = new Dictionary<string, AssetDataBase>(StringComparer.Ordinal);
            if (AssetDatabase.IsValidFolder(gameDataRoot))
            {
                foreach (var guid in AssetSearch.FindAssets("t:" + dataType.Name, new[] { gameDataRoot }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var data = AssetDatabase.LoadAssetAtPath(path, dataType) as AssetDataBase;
                    if (data != null && !string.IsNullOrEmpty(data.ImportSourceGuid))
                    {
                        index.TryAdd(data.ImportSourceGuid, data);
                    }
                }
            }

            cache[dataType] = index;
            return index;
        }
    }
}
