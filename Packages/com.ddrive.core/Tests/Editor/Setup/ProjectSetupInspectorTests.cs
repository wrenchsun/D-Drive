using DDrive.Editor.Settings;
using DDrive.Editor.Setup;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Setup
{
    // [42_distribution.md] §3.6/§6 P-6(2026-09-20) — ウィザードの検査ロジック(ProjectSetupInspector、
    // ウィンドウ非依存の純関数)を固定する。ProjectSettings/Addressables/フォルダ検査は実プロジェクト
    // (このリポジトリ、開発リポジトリ)の状態をそのまま読むだけ(副作用なし)。このリポジトリは
    // 既にセットアップ済み(URP/Input System/API Level/Addressables/GameData/UiLayerSettings/
    // DDriveSpecSettings すべて揃っている)なので、AllOk 系のアサーションはそのまま「現状が壊れて
    // いないこと」の回帰テストにもなる。
    public class ProjectSetupInspectorTests
    {
        private static JObject EmptyManifest() => JObject.Parse(@"{ ""dependencies"": {} }");

        private static JObject FullyConfiguredManifest() => JObject.Parse(@"{
            ""dependencies"": {
                ""com.cysharp.unitask"": """ + ProjectSetupInspector.UniTaskGitUrl + @""",
                ""com.cysharp.r3"": """ + ProjectSetupInspector.R3GitUrl + @""",
                """ + ProjectSetupInspector.R3NuGetPackageId + @""": """ + ProjectSetupInspector.R3NuGetVersion + @"""
            },
            ""scopedRegistries"": [
                { ""name"": ""Unity NuGet"", ""url"": """ + ProjectSetupInspector.NuGetScopedRegistryUrl + @""", ""scopes"": [""" + ProjectSetupInspector.NuGetScopedRegistryScope + @"""] }
            ]
        }");

        [Test]
        public void InspectMissingGitDependencies_EmptyManifest_ReturnsAllThree()
        {
            var missing = ProjectSetupInspector.InspectMissingGitDependencies(EmptyManifest());

            Assert.AreEqual(3, missing.Count); // UniTask / R3 / scoped registry(org.nuget.r3 はさらにその後ろ)
        }

        [Test]
        public void InspectMissingGitDependencies_NullManifest_ReturnsEmpty()
        {
            var missing = ProjectSetupInspector.InspectMissingGitDependencies(null);
            Assert.AreEqual(0, missing.Count);
        }

        [Test]
        public void InspectMissingGitDependencies_FullyConfigured_ReturnsEmpty()
        {
            var missing = ProjectSetupInspector.InspectMissingGitDependencies(FullyConfiguredManifest());
            Assert.AreEqual(0, missing.Count);
        }

        [Test]
        public void InspectMissingGitDependencies_ScopedRegistryMissing_ReportsRegistryNotPackage()
        {
            var manifest = JObject.Parse(@"{ ""dependencies"": {
                ""com.cysharp.unitask"": ""x"", ""com.cysharp.r3"": ""x""
            } }");

            var missing = ProjectSetupInspector.InspectMissingGitDependencies(manifest);

            Assert.AreEqual(1, missing.Count);
            Assert.AreEqual(DDrive.Editor.Setup.MissingDependencyKind.ScopedRegistry, missing[0].Kind);
        }

        [Test]
        public void InspectMissingGitDependencies_RegistryPresentButPackageMissing_ReportsRegistryPackage()
        {
            var manifest = JObject.Parse(@"{
                ""dependencies"": { ""com.cysharp.unitask"": ""x"", ""com.cysharp.r3"": ""x"" },
                ""scopedRegistries"": [ { ""name"": ""Unity NuGet"", ""url"": """ + ProjectSetupInspector.NuGetScopedRegistryUrl + @""", ""scopes"": [""" + ProjectSetupInspector.NuGetScopedRegistryScope + @"""] } ]
            }");

            var missing = ProjectSetupInspector.InspectMissingGitDependencies(manifest);

            Assert.AreEqual(1, missing.Count);
            Assert.AreEqual(ProjectSetupInspector.R3NuGetPackageId, missing[0].PackageId);
            Assert.AreEqual(MissingDependencyKind.RegistryPackage, missing[0].Kind);
        }

        [Test]
        public void IsNgoPresent_WhenListed_ReturnsTrue()
        {
            var manifest = JObject.Parse(@"{ ""dependencies"": { """ + ProjectSetupInspector.NgoPackageId + @""": ""2.13.2"" } }");
            Assert.IsTrue(ProjectSetupInspector.IsNgoPresent(manifest));
        }

        [Test]
        public void IsNgoPresent_WhenAbsent_ReturnsFalse()
        {
            Assert.IsFalse(ProjectSetupInspector.IsNgoPresent(EmptyManifest()));
        }

        [Test]
        public void InspectProjectSettings_DevRepo_AllOk()
        {
            // このリポジトリ(開発リポジトリ)は URP / Input System / .NET Standard 2.1 が
            // 既に設定済み([42_distribution.md] §1.1)。
            var status = ProjectSetupInspector.InspectProjectSettings();

            Assert.IsTrue(status.UrpActive, "URP がアクティブなレンダーパイプラインであること");
            Assert.IsTrue(status.InputSystemActive, "Active Input Handling が Input System(または Both)であること");
            Assert.IsTrue(status.ApiCompatibilityOk, "API Compatibility Level が .NET Standard 2.1 相当であること");
        }

        [Test]
        public void IsAddressablesInitialized_DevRepo_ReturnsTrue()
        {
            Assert.IsTrue(ProjectSetupInspector.IsAddressablesInitialized());
        }

        [Test]
        public void InspectFolderLayout_DevRepo_AllOk()
        {
            var status = ProjectSetupInspector.InspectFolderLayout(DDriveProjectSettings.instance);

            Assert.IsTrue(status.GameDataRootExists);
            Assert.IsTrue(status.UiLayerSettingsExists);
            Assert.IsTrue(status.SpecSettingsExists);
        }

        [Test]
        public void IsPossiblyModifiedEmbeddedPackage_DevRepo_ReturnsFalse()
        {
            // DevRepoSettingsSync が DDRIVE_DEV_REPO 定義時に IsDevelopmentRepo=true を自動で立てるため、
            // Embedded であっても「改造している可能性」の誤検出はしない。
            Assert.IsFalse(ProjectSetupInspector.IsPossiblyModifiedEmbeddedPackage());
        }

        [Test]
        public void ComputeFolderLayout_Default_ReturnsCurrentDefaults()
        {
            var paths = ProjectSetupInspector.ComputeFolderLayout(FolderLayoutPreset.Default, null, default);

            Assert.AreEqual("Assets/GameData", paths.GameDataRoot);
            Assert.AreEqual("Assets/Generated", paths.GeneratedRoot);
            Assert.AreEqual("Assets/SourceAssets", paths.SourceAssetsRoot);
            Assert.AreEqual("Specs", paths.SpecsRoot);
        }

        [Test]
        public void ComputeFolderLayout_UnderParentFolder_PrefixesAllFour()
        {
            var paths = ProjectSetupInspector.ComputeFolderLayout(FolderLayoutPreset.UnderParentFolder, "Assets/_Project/DDrive", default);

            Assert.AreEqual("Assets/_Project/DDrive/GameData", paths.GameDataRoot);
            Assert.AreEqual("Assets/_Project/DDrive/Generated", paths.GeneratedRoot);
            Assert.AreEqual("Assets/_Project/DDrive/SourceAssets", paths.SourceAssetsRoot);
            Assert.AreEqual("Assets/_Project/DDrive/Specs", paths.SpecsRoot);
        }

        [Test]
        public void ComputeFolderLayout_UnderParentFolder_TrimsTrailingSlash()
        {
            var paths = ProjectSetupInspector.ComputeFolderLayout(FolderLayoutPreset.UnderParentFolder, "Assets/_Project/DDrive/", default);
            Assert.AreEqual("Assets/_Project/DDrive/GameData", paths.GameDataRoot);
        }

        [Test]
        public void ComputeFolderLayout_Custom_ReturnsGivenPaths()
        {
            var custom = new FolderLayoutPaths("A", "B", "C", "D");
            var paths = ProjectSetupInspector.ComputeFolderLayout(FolderLayoutPreset.Custom, null, custom);

            Assert.AreEqual("A", paths.GameDataRoot);
            Assert.AreEqual("B", paths.GeneratedRoot);
            Assert.AreEqual("C", paths.SourceAssetsRoot);
            Assert.AreEqual("D", paths.SpecsRoot);
        }
    }
}
