using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;

namespace DDrive.Tests.Editor
{
    // [02] §11 / [09] §1 — 作成 → カタログ → Addressables が一つの導線になっていることの検証。
    public class AddressablesRegistrationValidatorTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempGameDataAddr";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                AssetDatabase.SaveAssets(); // Addressables 設定の dirty を後続テストに持ち越さない
            }
        }

        private static List<ValidationResult> Run(AssetDataBase asset)
        {
            var validator = new AddressablesRegistrationValidator { IncludeTestFolders = true };
            return new List<ValidationResult>(validator.Validate(asset, new ValidationContext(new List<AssetDataBase> { asset })));
        }

        private static int Errors(List<ValidationResult> results)
        {
            var n = 0;
            foreach (var r in results)
            {
                if (r.Severity == ValidationSeverity.Error)
                {
                    n++;
                }
            }

            return n;
        }

        [Test]
        public void Create_RegistersAddressablesEntry_AndValidatorPasses()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "AddrCheck", gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);

            var entry = AddressablesSync.FindEntry(asset);
            Assert.IsNotNull(entry, "作成時に Addressables へ登録される");
            Assert.AreEqual("SE_Test_AddrCheck", entry.address, "address はカタログの Address(ファイル名)と一致する");

            Assert.AreEqual(0, Errors(Run(asset)));
        }

        [Test]
        public void MissingEntry_IsError_AndFixActionRegisters()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "AddrMissing", gameDataRoot: TestRoot);
            Assert.IsTrue(AddressablesSync.RemoveEntry(asset));

            var results = Run(asset);
            Assert.AreEqual(1, Errors(results), "Addressables 未登録は Error");
            Assert.IsNotNull(results[0].FixAction);

            results[0].FixAction();
            Assert.IsNotNull(AddressablesSync.FindEntry(asset));
            Assert.AreEqual(0, Errors(Run(asset)));
        }

        [Test]
        public void AddressMismatch_IsError_AndFixActionRepairs()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "AddrWrong", gameDataRoot: TestRoot);
            var entry = AddressablesSync.FindEntry(asset);
            entry.SetAddress("wrong_address", false);

            var results = Run(asset);
            Assert.AreEqual(1, Errors(results));
            StringAssert.Contains("不一致", results[0].Message);

            results[0].FixAction();
            Assert.AreEqual("SE_Test_AddrWrong", AddressablesSync.FindEntry(asset).address);
        }

        [Test]
        public void CatalogRegistered_WithLabel()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "AddrCatalog", gameDataRoot: TestRoot);
            var catalog = AssetDatabase.LoadAssetAtPath<DDrive.Foundation.Registry.AssetCatalog>($"{TestRoot}/Catalogs/AudioCatalog.asset");
            Assert.IsNotNull(catalog);

            var entry = AddressablesSync.FindEntry(catalog);
            Assert.IsNotNull(entry, "カタログも Addressables に登録される");
            Assert.IsTrue(entry.labels.Contains(AddressablesSync.CatalogLabel), "起動時にラベルで集められる");
        }

        // U-20([39_usability_fixes_2026-09-17.md]) — Anim2D.Play(ID 版)は AnimManager.Play → ResolveOrPlaceholder
        // でしか解決しないため、Flags.Load が Preload でない既存 Anim2DData(このバグ修正より前に作られた物)は
        // Placeholder(Events 空)になり SE/VFX が鳴らない/出ない。Validator がそれを検出・修正できることを確認する。
        [Test]
        public void Anim2DAsset_CreatedWithPreload_AndFlagsLoadRegression_IsDetectedAndFixed()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var asset = (Anim2DData)AssetCreationService.Create(typeof(Anim2DData), AssetType.Anim2D, "検証用", "Test", "AddrAnim2D", gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);
            Assert.AreEqual(LoadMode.Preload, asset.Flags.Load, "U-20 修正後は作成時点で既定 Preload になっているはず");
            Assert.AreEqual(0, Errors(Run(asset)));

            // このバグ修正より前に作られた既存アセット(Flags.Load=LazyLoad のまま)を模す。
            var flags = asset.Flags;
            flags.Load = LoadMode.LazyLoad;
            asset.Flags = flags;
            EditorUtility.SetDirty(asset);

            var results = Run(asset);
            Assert.AreEqual(1, Errors(results), "Flags.Load が Preload でない Anim2DData は Error");
            StringAssert.Contains("Preload", results[0].Message);

            results[0].FixAction();
            Assert.AreEqual(LoadMode.Preload, asset.Flags.Load, "FixAction で Preload に書き戻る");
            Assert.AreEqual(0, Errors(Run(asset)));
        }
    }
}
