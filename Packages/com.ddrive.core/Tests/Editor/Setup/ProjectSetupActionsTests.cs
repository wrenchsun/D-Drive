using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Setup;
using DDrive.Foundation.Identity;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

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
        private const string TestRoot = TestTempFolder.Root + "/TempGameDataSetup";

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
            // [47_review_p_tickets_2026-09-20.md] テストの穴 8 — 旧アサート
            // `names.Contains("MiscCatalog") || names.All(n => n != "MiscCatalog")` は
            // 「含む、または含まない」という恒真式で何も検査していなかった。意図(各名前が空白でない)を
            // 検査する形に直す。
            Assert.IsTrue(names.All(n => !string.IsNullOrWhiteSpace(n)), "カタログ名はすべて非空であること");
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

        // [42_distribution.md] §2.3 #12(b)(P-12 で発見、docs/49) — Addressables 自身が既定で作る
        // "Default Local Group"・"Packed Assets" はアセット名にスペースを含む。実プロジェクトの
        // AddressableAssetSettingsDefaultObject には一切触れず、独立した一時 AddressableAssetSettings
        // (settings を引数で受け取るオーバーロードを使う)で検証する。
        //
        // 重要: グループは `settings.CreateGroup(...)` を使わず、`ScriptableObject.CreateInstance` +
        // `settings.groups.Add(...)` だけで組み立てる(永続化しない)。`CreateGroup` は実際に
        // `AddressableAssetGroup` 型の .asset をディスクに作成するが、Addressables 自身の
        // `AddressableAssetSettings.OnPostprocessAllAssets` は「新規 AddressableAssetGroup アセットを
        // 見つけたら(この一時 settings ではなく)実プロジェクトの既定 AddressableAssetSettings.groups に
        // 登録してしまう」という副作用を持つため、一時オブジェクトのつもりが実プロジェクトの
        // Assets/AddressableAssetsData を汚してしまう事故が実際に起きた(本チケットの作業中に発見・復旧済み)。
        // `AddressableAssetGroup.Name` セッターは「永続化されていないオブジェクトならメモリ上だけで
        // リネームする」分岐を持つため、永続化しなくてもリネームロジックの検証はできる。
        // テンプレート側は Addressables 側に同種の自動収集ロジックが無いため実アセットとして作ってよい
        // (`AssetDatabase.RenameAsset` によるパスのリネームまで検証するために必要)。
        [Test]
        public void RenameDefaultAddressablesAssetsToAvoidSpaces_RenamesGroupAndTemplate_InIsolatedSettings()
        {
            var settings = ScriptableObject.CreateInstance<AddressableAssetSettings>();
            var group = ScriptableObject.CreateInstance<AddressableAssetGroup>();
            group.Name = "Default Local Group";
            settings.groups.Add(group);

            TestTempFolder.CreateFolder("TempGameDataSetup");
            var template = ScriptableObject.CreateInstance<AddressableAssetGroupTemplate>();
            AssetDatabase.CreateAsset(template, $"{TestRoot}/Packed Assets.asset");
            settings.GroupTemplateObjects.Add(template);

            var renamed = ProjectSetupActions.RenameDefaultAddressablesAssetsToAvoidSpaces(settings);

            Assert.IsTrue(renamed);
            Assert.AreEqual("DDriveDefaultLocalGroup", group.Name, "グループ名がスペース無しにリネームされること");
            Assert.AreEqual("PackedAssets", template.name, "テンプレート名(アセット名)がスペース無しにリネームされること");
        }

        // 対象名(Default Local Group)でなければ触らないことの確認。グループを永続化しない理由は
        // 上のテストのコメントを参照(実プロジェクトの Addressables 設定を汚さないため)。
        [Test]
        public void RenameDefaultAddressablesAssetsToAvoidSpaces_GroupWithDifferentName_IsNotRenamed()
        {
            var settings = ScriptableObject.CreateInstance<AddressableAssetSettings>();
            var group = ScriptableObject.CreateInstance<AddressableAssetGroup>();
            group.Name = "MyCustomGroup";
            settings.groups.Add(group);

            ProjectSetupActions.RenameDefaultAddressablesAssetsToAvoidSpaces(settings);

            Assert.AreEqual("MyCustomGroup", group.Name, "対象名(Default Local Group)でなければ触らない");
        }

        [Test]
        public void RenameDefaultAddressablesAssetsToAvoidSpaces_NullSettings_ReturnsFalse()
        {
            Assert.IsFalse(ProjectSetupActions.RenameDefaultAddressablesAssetsToAvoidSpaces((AddressableAssetSettings)null));
        }

        // 実プロジェクト(AddressableAssetSettingsDefaultObject)に対しては読み取りのみ(副作用が
        // あるのは HasAddressablesDefaultNameWithSpace() ではなく RenameDefaultAddressablesAssetsToAvoidSpaces()
        // 側なので、こちらは実プロジェクトに対して呼んでも安全)。
        [Test]
        public void HasAddressablesDefaultNameWithSpace_RealProject_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ProjectSetupInspector.HasAddressablesDefaultNameWithSpace());
        }
    }
}
