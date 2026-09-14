using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Data;
using UnityEditor;
using UnityEngine;

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

        // 既定フォルダ作成(ImportRuleDefaultFolders)・デザイナーマニュアル生成等、種別一覧を横断して
        // 使いたい側への公開窓口。ハンドラの追加(Cutscene 等)がここにも自動で反映される。
        public static IReadOnlyList<IImportRuleHandler> Handlers => AllHandlers;

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

        // SourceAssets 直下にあるが ImportRule の対象ではないと分かっている(=別の経路が既に使っている)フォルダ。
        // 「案内ログ」(下記)の対象からも除外する。新しく増やす場合はここに 1 行足す。
        // Shaders: [06_material_texture.md] Maya→Material の AiStandardSurface シェーダー置き場(AiStandardSurfacePreprocessor.ShaderPath)。
        // Data: サンプル/テスト用の元アセット置き場(UnityChan 一式・ImportRuleServiceTests が参照する fbx 等)。
        // Samples: サンプル素材の退避先([10_workflow.md] §3.3、2026-09-14。SourceAssets/model(shizuku)が
        // 種別フォルダ "Model" と大文字小文字違いで衝突したため Samples/Shizuku へ退避した。ImportRule の対象外)。
        private static readonly HashSet<string> KnownNonTargetTypeFolders = new(StringComparer.Ordinal)
        {
            "Shaders",
            "Data",
            "Samples",
        };

        private static string _allowedTypeFolderList;

        private static string AllowedTypeFolderList
            => _allowedTypeFolderList ??= string.Join(" / ", AllHandlers.Select(h => h.TypeFolder));

        // 案内ログ(下記)をセッション内でパスごとに 1 回だけ出すための既知集合。
        // Editor スクリプトリロード(コンパイル)で静的フィールドは失われるため「セッション内」= このドメインリロードの間、の意味になる。
        private static readonly HashSet<string> WarnedPaths = new(StringComparer.Ordinal);

        // テストからのみ呼ぶ想定(ImportRulePostprocessor.Suppress 等と同じく公開フィールド/メソッドで抑止する既存方針に合わせる)。
        // static な WarnedPaths が NUnit のテスト間で残ってしまうと「1 回だけ警告する」の確認ができなくなるため、[SetUp] でリセットする。
        public static void ResetImportHintStateForTests() => WarnedPaths.Clear();

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
            var hints = new ImportHints();

            foreach (var rawPath in paths)
            {
                var path = rawPath?.Replace('\\', '/');
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                // フォルダ自体・隠しファイル(.始まり/~始まり)・.meta は元ファイルとして扱わない
                // (案内ログの対象にもしない。フォルダの「作成/移動」も OnPostprocessAllAssets に来るため無視する)。
                var fileNameOnly = Path.GetFileName(path);
                if (string.IsNullOrEmpty(fileNameOnly)
                    || fileNameOnly.StartsWith(".", StringComparison.Ordinal)
                    || fileNameOnly.StartsWith("~", StringComparison.Ordinal)
                    || fileNameOnly.Equals("README.md", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                    || AssetDatabase.IsValidFolder(path))
                {
                    continue;
                }

                if (!TryMatchRule(path, sourceRoot, out var handler, out var category))
                {
                    RecordHint(path, sourceRoot, hints);
                    continue;
                }

                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (Array.IndexOf(handler.Extensions, ext) < 0)
                {
                    RecordHint(path, sourceRoot, hints);
                    continue;
                }

                ImportOne(handler, path, category, gameDataRoot, indexCache, report);
            }

            FlushHints(hints, report);
            return report;
        }

        // ルールに合わない置き方(種別フォルダの直下/不明な種別フォルダ/対応外拡張子)を、正しい置き場所の案内として
        // Console に警告する([11_tasks.md] 5-11 の追加分、2026-09-14)。デザイナーが「置いたのに何も起きない」で
        // 詰まらないための案内であり、例外にはしない([00_requirements.md] §0 TL;DR 4)。
        private static void RecordHint(string assetPath, string sourceRoot, ImportHints hints)
        {
            var root = (string.IsNullOrEmpty(sourceRoot) ? DefaultSourceRoot : sourceRoot).TrimEnd('/') + "/";
            if (!assetPath.StartsWith(root, StringComparison.Ordinal))
            {
                return; // SourceAssets 配下ではない(プロジェクト内の無関係なインポート) → 案内の対象外
            }

            if (!WarnedPaths.Add(assetPath))
            {
                return; // このセッションで既に案内済み
            }

            var rest = assetPath.Substring(root.Length);
            var firstSlash = rest.IndexOf('/');
            if (firstSlash < 0)
            {
                hints.DirectlyUnderRoot.Add(assetPath);
                return;
            }

            var typeFolder = rest.Substring(0, firstSlash);
            if (KnownNonTargetTypeFolders.Contains(typeFolder))
            {
                return; // Maya→Material 経路・サンプル資産等、既に用途が決まっているフォルダ → 案内しない
            }

            if (!HandlersByFolder.ContainsKey(typeFolder))
            {
                GetOrAddList(hints.UnknownTypeFolder, typeFolder).Add(assetPath);
                return;
            }

            // 種別フォルダ自体は正しいので、ここに来るのは拡張子が対象外だったケース。
            GetOrAddList(hints.UnsupportedExtension, typeFolder).Add(assetPath);
        }

        private static List<string> GetOrAddList(Dictionary<string, List<string>> dict, string key)
        {
            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<string>();
                dict[key] = list;
            }

            return list;
        }

        private static void FlushHints(ImportHints hints, Report report)
        {
            if (hints.DirectlyUnderRoot.Count > 0)
            {
                Emit(
                    $"種別フォルダの下に置いてください(例: {DefaultSourceRoot}/Texture/<カテゴリ>/)。対応フォルダ: {AllowedTypeFolderList}",
                    hints.DirectlyUnderRoot, report);
            }

            foreach (var entry in hints.UnknownTypeFolder)
            {
                Emit(
                    $"'{entry.Key}' は種別フォルダではありません(対応フォルダ: {AllowedTypeFolderList}。大文字・小文字も一致させてください)",
                    entry.Value, report);
            }

            foreach (var entry in hints.UnsupportedExtension)
            {
                var exts = string.Join(" / ", HandlersByFolder[entry.Key].Extensions);
                Emit($"'{entry.Key}' の対象拡張子は {exts} です", entry.Value, report);
            }
        }

        private const int HintPreviewCount = 5;

        private static void Emit(string message, List<string> paths, Report report)
        {
            var preview = string.Join(", ", paths.Take(HintPreviewCount));
            var overflow = paths.Count > HintPreviewCount ? $" 他{paths.Count - HintPreviewCount}件" : string.Empty;
            var line = $"[DDrive] ImportRule 案内: {message}({paths.Count}件: {preview}{overflow})";
            Debug.LogWarning(line);
            report.Log(line);
        }

        // 案内ログの集計バッファ(ProcessPaths の 1 回の呼び出し分。ScanAll のような一括実行でも
        // カテゴリごとに 1 行にまとめて Console が荒れないようにする)。
        private sealed class ImportHints
        {
            public readonly List<string> DirectlyUnderRoot = new();
            public readonly Dictionary<string, List<string>> UnknownTypeFolder = new(StringComparer.Ordinal);
            public readonly Dictionary<string, List<string>> UnsupportedExtension = new(StringComparer.Ordinal);
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
