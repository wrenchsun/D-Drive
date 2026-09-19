using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Settings;
using DDrive.Editor.Spec;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine.Rendering;

namespace DDrive.Editor.Setup
{
    // [42_distribution.md] §3.5/§3.6/§6 P-6(2026-09-20) — ウィザードの「検査」段(1・2・4・5)の
    // 判定ロジック本体。EditorWindow に依存しない(§6 の指示「ウィンドウ非依存の純関数/サービス」)。
    // 副作用のある「適用」は ProjectSetupActions 側に置く(このクラスは読むだけ)。
    public static class ProjectSetupInspector
    {
        // ── 1. 依存(package.json に書けない git 配布・scoped registry) ──
        // [42_distribution.md] §3.5 の値をそのまま定数化する(persistent snapshot テストが無いため、
        // ここが唯一の情報源。バージョンを上げる場合は本ファイルと docs/42 §3.5 を同じ PR で合わせる)。

        public const string UniTaskPackageId = "com.cysharp.unitask";
        public const string UniTaskGitUrl = "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11";

        public const string R3PackageId = "com.cysharp.r3";
        public const string R3GitUrl = "https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity#1.3.1";

        public const string R3NuGetPackageId = "org.nuget.r3";
        public const string R3NuGetVersion = "1.3.1";

        public const string NuGetScopedRegistryName = "Unity NuGet";
        public const string NuGetScopedRegistryUrl = "https://unitynuget-registry.openupm.com";
        public const string NuGetScopedRegistryScope = "org.nuget";

        // NGO は任意依存(A-7、versionDefines で切り離し済み)。「無い」ことは Warning にしない
        // (ウィザードの表示・案内専用)。
        public const string NgoPackageId = "com.unity.netcode.gameobjects";
        public const string NgoVersion = "2.13.2";

        public static IReadOnlyList<MissingDependency> InspectMissingGitDependencies(JObject manifest)
        {
            var result = new List<MissingDependency>();
            if (manifest == null)
            {
                return result;
            }

            if (!ManifestJson.HasDependency(manifest, UniTaskPackageId))
            {
                result.Add(new MissingDependency(
                    UniTaskPackageId,
                    "DD-SETUP-DEP-UNITASK",
                    $"UniTask({UniTaskPackageId})が manifest.json にありません。",
                    UniTaskGitUrl,
                    MissingDependencyKind.GitPackage));
            }

            if (!ManifestJson.HasDependency(manifest, R3PackageId))
            {
                result.Add(new MissingDependency(
                    R3PackageId,
                    "DD-SETUP-DEP-R3",
                    $"R3({R3PackageId})が manifest.json にありません。",
                    R3GitUrl,
                    MissingDependencyKind.GitPackage));
            }

            // org.nuget.r3 は scoped registry が無いと解決できないため、scoped registry 自体が
            // 無ければそちらを先に報告する(パッケージ追加は scoped registry 追加後の別段にする)。
            if (!ManifestJson.HasScopedRegistry(manifest, NuGetScopedRegistryUrl, NuGetScopedRegistryScope))
            {
                result.Add(new MissingDependency(
                    NuGetScopedRegistryName,
                    "DD-SETUP-DEP-R3-NUGET-REGISTRY",
                    $"{R3NuGetPackageId} の解決に必要な scoped registry(Unity NuGet)が manifest.json にありません。",
                    NuGetScopedRegistryUrl,
                    MissingDependencyKind.ScopedRegistry));
            }
            else if (!ManifestJson.HasDependency(manifest, R3NuGetPackageId))
            {
                result.Add(new MissingDependency(
                    R3NuGetPackageId,
                    "DD-SETUP-DEP-R3-NUGET",
                    $"{R3NuGetPackageId} が manifest.json にありません。",
                    R3NuGetVersion,
                    MissingDependencyKind.RegistryPackage));
            }

            return result;
        }

        public static bool IsNgoPresent(JObject manifest) => ManifestJson.HasDependency(manifest, NgoPackageId);

        // ── 2. ProjectSettings(URP / Input System / API Compatibility Level) ──

        public static ProjectSettingsStatus InspectProjectSettings()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            var urpActive = pipeline != null && pipeline.GetType().Name.Contains("Universal");

            // activeInputHandler は PlayerSettings の公開プロパティ/メソッドが無く(2026-09-20 に
            // unity_reflect で確認済み)、ProjectSettings.asset 自身を SerializedObject で読むのが
            // 唯一の確実な手段(GetPropertyInt("activeInputHandler") は正しい値を返さなかった)。
            // 0=Input Manager(旧) / 1=Input System(新) / 2=Both。
            var activeInputHandler = ReadActiveInputHandler();
            var inputSystemActive = activeInputHandler == 1 || activeInputHandler == 2;

            var apiLevel = PlayerSettings.GetApiCompatibilityLevel(UnityEditor.Build.NamedBuildTarget.Standalone);
            var apiOk = apiLevel == ApiCompatibilityLevel.NET_Standard_2_0;

            return new ProjectSettingsStatus(urpActive, inputSystemActive, apiOk);
        }

        // -1 は「読めなかった」(通常は起きない。防御的に Input System 有効扱いにしない)。
        public static int ReadActiveInputHandler()
        {
            var objects = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (objects == null || objects.Length == 0)
            {
                return -1;
            }

            var so = new SerializedObject(objects[0]);
            var prop = so.FindProperty("activeInputHandler");
            return prop != null ? prop.intValue : -1;
        }

        // ── 5. Addressables 初期化 ──

        public static bool IsAddressablesInitialized() => AddressableAssetSettingsDefaultObject.SettingsExists;

        // ── 4. 既定フォルダ・設定 ──

        public static FolderLayoutStatus InspectFolderLayout(DDriveProjectSettings settings)
        {
            var gameDataRoot = settings != null ? settings.GameDataRoot : AssetCreationService.DefaultGameDataRoot;
            var gameDataExists = AssetDatabase.IsValidFolder(gameDataRoot);

            var uiLayerSettingsExists = AssetDatabase.LoadAssetAtPath<DDrive.Runtime.Ui.UiLayerSettings>(
                $"{gameDataRoot}/Ui/UI_LayerSettings.asset") != null;

            var specSettingsExists = DDriveSpecSettings.Load() != null;

            return new FolderLayoutStatus(gameDataExists, uiLayerSettingsExists, specSettingsExists);
        }

        // ── A-9: 持ち込み先での改造の可能性 ──

        // Embedded(パッケージソースが「埋め込み」)かつ開発リポジトリでない = 改造している可能性。
        // 開発リポジトリ自身も Embedded だが、DDriveProjectSettings.IsDevelopmentRepo が
        // DevRepoSettingsSync により true に保たれるため誤検出しない。
        public static bool IsPossiblyModifiedEmbeddedPackage()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DDriveProjectSettings).Assembly);
            if (packageInfo == null || packageInfo.source != UnityEditor.PackageManager.PackageSource.Embedded)
            {
                return false;
            }

            return !DDriveProjectSettings.instance.IsDevelopmentRepo;
        }

        // ── §7 B-6: フォルダ配置プリセットの計算(純粋関数) ──

        public const string DefaultGameDataFolderName = "GameData";
        public const string DefaultGeneratedFolderName = "Generated";
        public const string DefaultSourceAssetsFolderName = "SourceAssets";
        public const string DefaultSpecsFolderName = "Specs";

        // parentFolder の例: "Assets/_Project/DDrive"。末尾のスラッシュは無視する。
        public static FolderLayoutPaths ComputeFolderLayout(FolderLayoutPreset preset, string parentFolder, FolderLayoutPaths custom)
        {
            switch (preset)
            {
                case FolderLayoutPreset.UnderParentFolder:
                    var parent = string.IsNullOrEmpty(parentFolder) ? "Assets/_Project/DDrive" : parentFolder.TrimEnd('/');
                    return new FolderLayoutPaths(
                        $"{parent}/{DefaultGameDataFolderName}",
                        $"{parent}/{DefaultGeneratedFolderName}",
                        $"{parent}/{DefaultSourceAssetsFolderName}",
                        $"{parent}/{DefaultSpecsFolderName}");
                case FolderLayoutPreset.Custom:
                    return custom;
                default:
                    return new FolderLayoutPaths(
                        AssetCreationService.DefaultGameDataRoot,
                        "Assets/Generated",
                        DDrive.Editor.Import.ImportRuleService.DefaultSourceRoot,
                        "Specs");
            }
        }
    }
}
