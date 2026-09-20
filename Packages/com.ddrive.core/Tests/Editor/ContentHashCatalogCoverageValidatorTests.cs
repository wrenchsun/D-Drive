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
    // [11_tasks.md] 6-5 / [14_networking.md] §10 — 「ContentHash 生成対象外のカタログ」→ Error(CI)。
    // AddressablesRegistrationValidatorTests と同じ手法(一時 GameData フォルダ、テスト後に削除)。
    // 実 GameData・カタログ・Addressables には書き込まない。
    //
    // ContentHashCatalogCoverageValidator は(AddressablesRegistrationValidator と違い)渡された単一の
    // AssetDataBase だけを見るのではなく、プロジェクト内の全カタログを走査する設計のため、実 GameData の
    // カタログ(Assets/GameData/Catalogs/*)の状態が結果に混ざり得る。テストは「変更前との差分」だけを見る
    // ことで、実カタログの状態(常に健全であるべきだが保証はしない)に依存しないようにしている。
    public class ContentHashCatalogCoverageValidatorTests
    {
        private const string TestRoot = TestTempFolder.Root + "/TempGameDataContentHash";

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

        private static List<ValidationResult> Run(bool includeTestFolders = true)
        {
            var validator = new ContentHashCatalogCoverageValidator { IncludeTestFolders = includeTestFolders };
            // AssetCatalog は AssetDataBase ではないため、ValidatorRegistry の通常フローには乗らない。
            // ダミーの AssetDataBase(内容は使われない)を渡して Validate を直接呼ぶ。
            var dummy = UnityEngine.ScriptableObject.CreateInstance<SeData>();
            try
            {
                return new List<ValidationResult>(validator.Validate(dummy, new ValidationContext(new List<AssetDataBase> { dummy })));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(dummy);
            }
        }

        private static int ErrorCount(List<ValidationResult> results)
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

        // baseline に無い Error だけを返す(実カタログ側の既存エラーは無視し、このテストが作った差分だけを見る)。
        // メッセージ文字列にはカタログ名しか含まれないため、実カタログと一時カタログの名前が偶然一致し
        // 同一メッセージが複数回出るケースにも対応できるよう、集合ではなく個数(多重集合)で差分を取る。
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

            var baseline = Run();

            AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "CoverageOk", gameDataRoot: TestRoot);
            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>($"{TestRoot}/Catalogs/AudioCatalog.asset");
            Assert.IsNotNull(catalog, "前提: 標準の作成フローでカタログが自動生成される");

            Assert.AreEqual(0, NewErrors(baseline, Run()).Count, "標準の作成フローで作ったカタログは新たなエラーを増やさない");
        }

        [Test]
        public void CatalogWithoutAddressablesEntry_IsError_AndFixActionRegisters()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var baseline = Run();

            AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "CoverageMissingEntry", gameDataRoot: TestRoot);
            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>($"{TestRoot}/Catalogs/AudioCatalog.asset");
            Assert.IsTrue(AddressablesSync.RemoveEntry(catalog));

            var newErrors = NewErrors(baseline, Run());
            Assert.AreEqual(1, newErrors.Count);
            StringAssert.Contains("ContentHash 生成対象外", newErrors[0].Message);
            Assert.IsNotNull(newErrors[0].FixAction);

            newErrors[0].FixAction();
            Assert.AreEqual(0, NewErrors(baseline, Run()).Count);
        }

        [Test]
        public void CatalogWithoutLabel_IsError_AndFixActionRepairsLabel()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var baseline = Run();

            AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "CoverageMissingLabel", gameDataRoot: TestRoot);
            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>($"{TestRoot}/Catalogs/AudioCatalog.asset");
            var entry = AddressablesSync.FindEntry(catalog);
            Assert.IsNotNull(entry);
            entry.SetLabel(AddressablesSync.CatalogLabel, false);

            var newErrors = NewErrors(baseline, Run());
            Assert.AreEqual(1, newErrors.Count);
            StringAssert.Contains("ラベル", newErrors[0].Message);

            newErrors[0].FixAction();
            Assert.IsTrue(AddressablesSync.FindEntry(catalog).labels.Contains(AddressablesSync.CatalogLabel));
            Assert.AreEqual(0, NewErrors(baseline, Run()).Count);
        }

        [Test]
        public void TestFoldersExcluded_ByDefault()
        {
            if (!AddressablesSync.IsAvailable)
            {
                Assert.Ignore("Addressables の設定が無いためスキップ");
            }

            var baseline = Run(includeTestFolders: false);

            AssetCreationService.Create(typeof(SeData), AssetType.Se, "検証用", "Test", "CoverageExcluded", gameDataRoot: TestRoot);
            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>($"{TestRoot}/Catalogs/AudioCatalog.asset");
            Assert.IsTrue(AddressablesSync.RemoveEntry(catalog));

            // IncludeTestFolders=false(既定の Run All と同じ)だと /Tests/ 配下は対象外なので新たなエラーは出ない。
            Assert.AreEqual(0, NewErrors(baseline, Run(includeTestFolders: false)).Count);
        }
    }
}
