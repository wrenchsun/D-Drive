using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;

namespace DDrive.Tests.Editor
{
    // [29_network_device_test.md] §18(2026-09-18) — AddressablesRegistrationValidator の検出漏れ修正
    // (CatalogAddressCoverageValidator)の検証。ContentHashCatalogCoverageValidatorTests と同じ手法
    // (一時 GameData フォルダ、実カタログの状態には baseline との差分だけで依存する)を使う。
    //
    // 再現したい事故: 「カタログにはエントリがある(Address 文字列を持つ)のに、Addressables 側にその
    // Address を持つエントリが 1 つも無い」状態(§18 では Data(.asset)を git 復元しても Addressables
    // 側のグループ登録は追従しなかった)。
    public class CatalogAddressCoverageValidatorTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempGameDataAddrCoverage";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                AssetDatabase.SaveAssets();
            }
        }

        private static List<ValidationResult> Run(AssetDataBase triggerAsset, bool includeTestFolders = true)
        {
            var validator = new CatalogAddressCoverageValidator { IncludeTestFolders = includeTestFolders };
            return new List<ValidationResult>(validator.Validate(triggerAsset, new ValidationContext(new List<AssetDataBase> { triggerAsset })));
        }

        // baseline に無い Error だけを返す(実カタログ側の既存エラーは無視し、このテストが作った差分だけを見る)。
        private static List<ValidationResult> NewErrors(List<ValidationResult> baseline, List<ValidationResult> current)
        {
            var remainingBaselineCount = new Dictionary<string, int>();
            foreach (var r in baseline)
            {
                remainingBaselineCount.TryGetValue(r.Message, out var c);
                remainingBaselineCount[r.Message] = c + 1;
            }

            var result = new List<ValidationResult>();
            foreach (var r in current)
            {
                if (r.Severity != ValidationSeverity.Error)
                {
                    continue;
                }

                if (remainingBaselineCount.TryGetValue(r.Message, out var remaining) && remaining > 0)
                {
                    remainingBaselineCount[r.Message] = remaining - 1;
                    continue;
                }

                result.Add(r);
            }

            return result;
        }

        [Test]
        public void CatalogCreatedByStandardWorkflow_HasNoCoverageError()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "AddrCovOk", gameDataRoot: TestRoot);
            var baseline = Run(asset);

            Assert.AreEqual(0, NewErrors(baseline, Run(asset)).Count, "標準の作成フローで作った登録は新たなエラーを増やさない");
        }

        // §18 の再現: Data(.asset)自体は存在するが、対象を「見つからない」ものとして扱っても
        // (=ValidationContext.AllAssets に含めなくても)、カタログ起点のこの Validator は検出できることを確認する。
        [Test]
        public void CatalogEntryWithMissingAddressablesEntry_IsDetected_EvenWhenTargetAssetIsNotInContext()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var missingAsset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "AddrCovMissing", gameDataRoot: TestRoot);
            var otherAsset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "AddrCovOther", gameDataRoot: TestRoot);
            var expectedAddress = AddressablesSync.FindCatalogAddress(missingAsset.Id, out _, includeTestFolders: true);
            Assert.IsNotNull(expectedAddress, "前提: カタログには登録されている");

            var baseline = Run(otherAsset);

            // Addressables 側の登録だけを外す(§18 で起きた「Data ファイルは戻ったが Addressables 登録が
            // 追従しなかった」状態を模す)。
            Assert.IsTrue(AddressablesSync.RemoveEntry(missingAsset));

            // ValidationContext には missingAsset を含めず otherAsset だけで検証をトリガーする
            // (「missingAsset というファイル自体がプロジェクトに見つからない」状況の近似)。
            var validator = new CatalogAddressCoverageValidator { IncludeTestFolders = true };
            var ctx = new ValidationContext(new List<AssetDataBase> { otherAsset });
            var current = new List<ValidationResult>(validator.Validate(otherAsset, ctx));

            var newErrors = NewErrors(baseline, current);
            Assert.AreEqual(1, newErrors.Count, "カタログの Address が Addressables に無いことを、対象アセットを直接検証しなくても検出できる");
            StringAssert.Contains("カタログ Address 未実在", newErrors[0].Message);
            StringAssert.Contains(expectedAddress, newErrors[0].Message);
            Assert.IsNull(newErrors[0].FixAction, "対象アセットが ValidationContext に無いときは自動修正しない");
        }

        [Test]
        public void CatalogEntryWithMissingAddressablesEntry_IsDetected_AndFixActionRepairsWhenAssetIsInContext()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var missingAsset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "AddrCovFix", gameDataRoot: TestRoot);
            var baseline = Run(missingAsset);

            Assert.IsTrue(AddressablesSync.RemoveEntry(missingAsset));

            var newErrors = NewErrors(baseline, Run(missingAsset));
            Assert.AreEqual(1, newErrors.Count);
            Assert.IsNotNull(newErrors[0].FixAction, "対象アセットが ValidationContext にあれば FixAction で登録し直せる");

            newErrors[0].FixAction();
            Assert.IsNotNull(AddressablesSync.FindEntry(missingAsset));
            Assert.AreEqual(0, NewErrors(baseline, Run(missingAsset)).Count);
        }

        [Test]
        public void TestFoldersExcluded_ByDefault()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "AddrCovExcluded", gameDataRoot: TestRoot);
            var baseline = Run(asset, includeTestFolders: false);

            Assert.IsTrue(AddressablesSync.RemoveEntry(asset));

            // IncludeTestFolders=false(既定の Run All と同じ)だと /Tests/ 配下は対象外なので新たなエラーは出ない。
            Assert.AreEqual(0, NewErrors(baseline, Run(asset, includeTestFolders: false)).Count);
        }
    }
}
