using System.Linq;
using System.Text.RegularExpressions;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Editor
{
    public class AssetCreationServiceTests
    {
        // テスト専用の GameData ルート(実データを汚さない)。
        private const string TestRoot = TestTempFolder.Root + "/TempGameData";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); } // Addressables 設定の dirty を後続テストに持ち越さない
            }
        }

        [Test]
        public void Create_GeneratesConventionalFileNameIdAndCatalogEntry()
        {
            var asset = AssetCreationService.Create(
                typeof(SeData), AssetType.Se,
                displayName: "剣の斬撃音", category: "Player", identifier: "Slash",
                gameDataRoot: TestRoot);

            Assert.IsNotNull(asset);

            var path = AssetDatabase.GetAssetPath(asset);
            StringAssert.EndsWith("Audio/SE/Player/SE_Player_Slash.asset", path);
            Assert.AreEqual("剣の斬撃音", asset.DisplayName);
            Assert.AreEqual("Player", asset.Category);
            Assert.AreNotEqual(0UL, asset.Id);
            // [44_review_2026-09-19.md] P1-1(テストの穴 2): 新規作成は StampNew で v1 になり、その後の
            // カタログ/Addressables 登録の SaveAssets(SaveAllSuppressed)で 2 に進んでしまわないことを固定する。
            Assert.AreEqual(1, asset.Version);

            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>($"{TestRoot}/Catalogs/AudioCatalog.asset");
            Assert.IsNotNull(catalog, "AudioCatalog should be auto-created");
            Assert.IsTrue(catalog.Entries.Any(e => e.Id == asset.Id && e.Type == AssetType.Se && e.Address == "SE_Player_Slash"));
        }

        [Test]
        public void Create_SecondAssetSameName_GetsUniquePathAndDistinctId()
        {
            var first = AssetCreationService.Create(typeof(SeData), AssetType.Se, "A", "Player", "Slash", gameDataRoot: TestRoot);
            var second = AssetCreationService.Create(typeof(SeData), AssetType.Se, "B", "Player", "Slash", gameDataRoot: TestRoot);

            Assert.IsNotNull(second);
            Assert.AreNotEqual(AssetDatabase.GetAssetPath(first), AssetDatabase.GetAssetPath(second));
            Assert.AreNotEqual(first.Id, second.Id);
        }

        [Test]
        public void Create_BgmAndSe_ShareTheAudioCatalog()
        {
            var se = AssetCreationService.Create(typeof(SeData), AssetType.Se, "SE", "X", "Alpha", gameDataRoot: TestRoot);
            var bgm = AssetCreationService.Create(typeof(BgmData), AssetType.Bgm, "BGM", "X", "Beta", gameDataRoot: TestRoot);

            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>($"{TestRoot}/Catalogs/AudioCatalog.asset");
            Assert.IsNotNull(catalog);
            Assert.IsTrue(catalog.Entries.Any(e => e.Id == se.Id && e.Type == AssetType.Se));
            Assert.IsTrue(catalog.Entries.Any(e => e.Id == bgm.Id && e.Type == AssetType.Bgm));
        }

        [Test]
        public void Create_CatalogEntries_AreSortedById()
        {
            AssetCreationService.Create(typeof(SeData), AssetType.Se, "A", "", "Alpha", gameDataRoot: TestRoot);
            AssetCreationService.Create(typeof(SeData), AssetType.Se, "B", "", "Beta", gameDataRoot: TestRoot);
            AssetCreationService.Create(typeof(SeData), AssetType.Se, "C", "", "Gamma", gameDataRoot: TestRoot);

            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>($"{TestRoot}/Catalogs/AudioCatalog.asset");
            var ids = catalog.Entries.Select(e => e.Id).ToList();
            CollectionAssert.AreEqual(ids.OrderBy(v => v).ToList(), ids, "catalog entries must stay sorted by Id");
        }

        [Test]
        public void Create_InvalidIdentifier_ReturnsNullWithError()
        {
            LogAssert.Expect(LogType.Error, new Regex(".*Identifier.*"));
            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "X", "", "invalid_name", gameDataRoot: TestRoot);
            Assert.IsNull(asset);
        }
    }
}
