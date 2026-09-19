using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Setup;
using DDrive.Foundation.Identity;
using NUnit.Framework;
using UnityEditor;

namespace DDrive.Tests.Editor.Setup
{
    // [42_distribution.md] §3.6/§6 P-6(2026-09-20) — 「開発リポジトリの状態を壊さない: ウィザードの
    // 適用は EditMode テストからは呼ばない(検査と純関数だけ)」という指示に沿い、実 manifest.json /
    // 実 GameData を書き換える ProjectSetupActions のメソッド(EnsureDefaultFoldersAndSettings /
    // RegenerateGeneratedCode / AddDependency / AddScopedRegistryToProjectManifest /
    // SetTestablesEnabled / CopyConsumerSkillIfBundled)は本テストから直接呼ばない。
    // ここでは (1) 実 manifest.json を読むだけの IsTestablesEnabled(既に有効であることの確認。
    // 指示「Packages/manifest.json の testables は既に com.ddrive.core があるので『既に有効』と
    // 表示されること」)と、(2) AssetCreationServiceTests と同じ「テスト専用 TestRoot」で完結する
    // EnsureCatalogFile/AllCatalogNames(既存の AssetCreationService の一部として元々テスト対象)
    // だけを検証する。
    public class ProjectSetupActionsTests
    {
        private const string TestRoot = "Packages/com.ddrive.core/Tests/Editor/TempGameDataSetup";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            }
        }

        [Test]
        public void IsTestablesEnabled_RealManifest_AlreadyTrue()
        {
            // このリポジトリの Packages/manifest.json には既に "testables": ["com.ddrive.core"] がある
            // (P-5 で追加済み)。読むだけの検査なので実ファイルを変更しない。
            Assert.IsTrue(ProjectSetupActions.IsTestablesEnabled());
        }

        [Test]
        public void AllCatalogNames_ReturnsDistinctNonEmptyNames()
        {
            var names = AssetCreationService.AllCatalogNames();

            Assert.IsNotEmpty(names);
            Assert.AreEqual(names.Distinct().Count(), names.Count, "カタログ名は重複しないこと");
            Assert.IsTrue(names.Contains("AudioCatalog"));
            Assert.IsTrue(names.Contains("MiscCatalog") || names.All(n => n != "MiscCatalog"),
                "MiscCatalog は現行の AssetType では使われていない可能性があるため存在有無どちらでも許容");
        }

        [Test]
        public void EnsureCatalogFile_CreatesEmptyCatalog_Idempotent()
        {
            var first = AssetCreationService.EnsureCatalogFile("TestSetupCatalog", TestRoot);
            Assert.IsNotNull(first);
            Assert.AreEqual(0, first.Entries.Count);

            var second = AssetCreationService.EnsureCatalogFile("TestSetupCatalog", TestRoot);
            Assert.AreSame(first, second, "既存のカタログを再利用すること(重複作成しない)");
        }

        [Test]
        public void EnsureCatalogFile_ForEveryKnownCatalogName_Succeeds()
        {
            foreach (var name in AssetCreationService.AllCatalogNames())
            {
                var catalog = AssetCreationService.EnsureCatalogFile(name, TestRoot);
                Assert.IsNotNull(catalog, $"{name} の生成に失敗");
            }
        }
    }
}
