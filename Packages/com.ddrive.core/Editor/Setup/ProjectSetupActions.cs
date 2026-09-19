using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Codegen;
using DDrive.Editor.Import;
using DDrive.Editor.Settings;
using DDrive.Editor.Spec;
using DDrive.Editor.Versioning;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace DDrive.Editor.Setup
{
    // [42_distribution.md] §3.6/§6 P-6(2026-09-20) — ウィザードの「適用」段の実処理。
    // ProjectSetupInspector(検査、副作用なし)と対になる「適用」側。実 manifest.json /
    // Assets への書き込みを行うメソッドは EditMode テストから直接は呼ばない
    // (このチケットの指示「開発リポジトリの状態を壊さない: ウィザードの適用は EditMode テストからは
    // 呼ばない。検査と純関数だけ」)。テスト可能な計算(ComputeFolderLayout 等)は
    // ProjectSetupInspector 側の純関数を使う。
    public static class ProjectSetupActions
    {
        // ── 1. 依存の追加 ──

        // git 配布パッケージ(UniTask/R3)を Client.Add で追加開始する。scoped registry 経由の
        // パッケージ(org.nuget.r3)も scoped registry 追加後であれば同じ経路で追加できる。
        // scoped registry 自体の追加は AddScopedRegistryToProjectManifest を使う
        // (UnityEditor.PackageManager.Client に AddScopedRegistry が無いことを 2026-09-20 に確認済み。
        // [42_distribution.md] §7 C-1)。
        public static AddRequest AddDependency(MissingDependency dependency)
        {
            if (dependency.Kind == MissingDependencyKind.ScopedRegistry)
            {
                return null;
            }

            var identifier = dependency.Kind == MissingDependencyKind.GitPackage
                ? dependency.Value
                : $"{dependency.PackageId}@{dependency.Value}";
            return Client.Add(identifier);
        }

        public static void AddScopedRegistryToProjectManifest(string name, string url, string scope)
        {
            var manifest = ManifestJson.LoadProjectManifest();
            if (manifest == null)
            {
                Debug.LogError("[DDrive] Packages/manifest.json が読めませんでした。");
                return;
            }

            ManifestJson.AddScopedRegistry(manifest, name, url, scope);
            ManifestJson.SaveProjectManifest(manifest);
            AssetDatabase.Refresh();
        }

        // ── 5. Addressables 初期化 ──

        public static void EnsureAddressablesInitialized()
        {
            if (!AddressableAssetSettingsDefaultObject.SettingsExists)
            {
                AddressableAssetSettingsDefaultObject.GetSettings(true);
            }
        }

        // ── 4. 既定フォルダ・設定の生成 ──

        public sealed class DefaultFoldersResult
        {
            public readonly List<string> Lines = new();
            public void Log(string line) => Lines.Add(line);
            public override string ToString() => string.Join("\n", Lines);
        }

        // GameData/SourceAssets の既定フォルダ、UiLayerSettings、DDriveSpecSettings(+TuningTable)、
        // 全カタログ(空でも可)を生成する(冪等。既にあるものには触らない)。
        public static DefaultFoldersResult EnsureDefaultFoldersAndSettings(DDriveProjectSettings settings)
        {
            var report = new DefaultFoldersResult();
            var gameDataRoot = settings.GameDataRoot;

            if (!AssetDatabase.IsValidFolder(gameDataRoot))
            {
                AssetCreationService.EnsureFolder(gameDataRoot);
                report.Log($"新規フォルダ: {gameDataRoot}");
            }

            var sourceAssetsReport = ImportRuleDefaultFolders.EnsureDefaultFolders(settings.SourceAssetsRoot);
            report.Log(sourceAssetsReport.ToString());

            var uiLayerSettingsPath = $"{gameDataRoot}/Ui/UI_LayerSettings.asset";
            if (AssetDatabase.LoadAssetAtPath<UiLayerSettings>(uiLayerSettingsPath) == null)
            {
                AssetCreationService.EnsureFolder($"{gameDataRoot}/Ui");
                var uiLayerSettings = ScriptableObject.CreateInstance<UiLayerSettings>();
                AssetDatabase.CreateAsset(uiLayerSettings, uiLayerSettingsPath);
                report.Log($"新規: {uiLayerSettingsPath}");
            }

            var specSettings = DDriveSpecSettings.GetOrCreate();
            specSettings.GetOrCreateTuningTable();
            report.Log($"確認済み: {DDriveSpecSettings.DefaultPath}");

            foreach (var catalogName in AssetCreationService.AllCatalogNames())
            {
                AssetCreationService.EnsureCatalogFile(catalogName, gameDataRoot);
            }
            report.Log($"カタログ確認済み: {AssetCreationService.AllCatalogNames().Count} 件");

            // [44_review_2026-09-19.md] P1-1: 既定フォルダ・設定の生成は「一括処理」なので版数を進めない。
            DDriveAssetSave.SaveAllSuppressed();
            return report;
        }

        // AssetIdGenerator/TuningCodegen の初回(以降)生成。emitGeneratedAsmdef はウィザードの
        // チェックボックスの値をそのまま DDriveProjectSettings へ反映してから生成する
        // (A-8: 既定 ON。§2.3-7)。
        public static void RegenerateGeneratedCode(bool emitGeneratedAsmdef)
        {
            DDriveProjectSettings.instance.EmitGeneratedAsmdef = emitGeneratedAsmdef;
            AssetIdGenerator.Regenerate();
            TuningCodegen.Regenerate();
        }

        // ── 7. テストを有効化する(既定 OFF) ──

        public const string DDriveCorePackageId = "com.ddrive.core";

        public static void SetTestablesEnabled(bool enabled)
        {
            var manifest = ManifestJson.LoadProjectManifest();
            if (manifest == null)
            {
                Debug.LogError("[DDrive] Packages/manifest.json が読めませんでした。");
                return;
            }

            ManifestJson.SetTestable(manifest, DDriveCorePackageId, enabled);
            ManifestJson.SaveProjectManifest(manifest);
            AssetDatabase.Refresh();
        }

        public static bool IsTestablesEnabled()
        {
            var manifest = ManifestJson.LoadProjectManifest();
            return manifest != null && ManifestJson.HasTestable(manifest, DDriveCorePackageId);
        }

        // ── §7 B-6: フォルダ配置の適用 ──

        // 既に Data がある状態で変えると新規作成分だけが新しい場所に作られ、既存データは古い場所に
        // 残る(移動はしない。AssetReorganizer の案内に従うこと)。呼び出し側(ウィザード)が
        // ProjectSetupInspector.InspectFolderLayout 等で既存データの有無を見て警告を出す。
        public static void ApplyFolderLayout(FolderLayoutPaths paths)
        {
            var settings = DDriveProjectSettings.instance;
            settings.GameDataRoot = paths.GameDataRoot;
            settings.GeneratedRoot = paths.GeneratedRoot;
            settings.SourceAssetsRoot = paths.SourceAssetsRoot;
            settings.SpecsRoot = paths.SpecsRoot;
        }

        // ── 8. 消費側スキルのコピー(B-4) ──

        public const string ConsumerSkillRelativePath = "Documentation~/skills/ddrive-consumer";
        public const string ConsumerSkillDestinationRelativePath = ".claude/skills/ddrive-consumer";
        public const string ConsumerSkillVersionStampFileName = ".ddrive-version";

        public enum ConsumerSkillCopyResult
        {
            NotBundled,   // パッケージに Documentation~/skills/ddrive-consumer が無い(P-10 で追加予定)。
            Copied,
        }

        // パッケージに同梱されていれば .claude/skills/ddrive-consumer へコピーし、版のスタンプを残す。
        // 同梱が無ければ no-op(NotBundled を返すだけ)。
        public static ConsumerSkillCopyResult CopyConsumerSkillIfBundled(string projectRoot, string packageVersion)
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DDriveProjectSettings).Assembly);
            if (packageInfo == null)
            {
                return ConsumerSkillCopyResult.NotBundled;
            }

            var sourceDir = System.IO.Path.Combine(packageInfo.resolvedPath, "Documentation~", "skills", "ddrive-consumer");
            if (!System.IO.Directory.Exists(sourceDir))
            {
                return ConsumerSkillCopyResult.NotBundled;
            }

            var destDir = System.IO.Path.Combine(projectRoot, ".claude", "skills", "ddrive-consumer");
            CopyDirectoryRecursive(sourceDir, destDir);

            var stampPath = System.IO.Path.Combine(destDir, ConsumerSkillVersionStampFileName);
            System.IO.File.WriteAllText(stampPath, packageVersion + "\n");

            return ConsumerSkillCopyResult.Copied;
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            System.IO.Directory.CreateDirectory(destDir);
            foreach (var filePath in System.IO.Directory.GetFiles(sourceDir))
            {
                var fileName = System.IO.Path.GetFileName(filePath);
                System.IO.File.Copy(filePath, System.IO.Path.Combine(destDir, fileName), overwrite: true);
            }

            foreach (var subDir in System.IO.Directory.GetDirectories(sourceDir))
            {
                var dirName = System.IO.Path.GetFileName(subDir);
                CopyDirectoryRecursive(subDir, System.IO.Path.Combine(destDir, dirName));
            }
        }
    }
}
