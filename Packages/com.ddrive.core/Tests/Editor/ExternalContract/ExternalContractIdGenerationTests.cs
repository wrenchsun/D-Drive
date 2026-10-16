using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Codegen;
using DDrive.Foundation.Extension;
using DDrive.Foundation.Identity;
using DDrive.Tests.Editor;
using NUnit.Framework;
using UnityEditor;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] E-29 — Extension 種別の ID 生成(X-5、[1014] §2.5)。
    //  (a) KindKey ごとの定数クラス(SMPID.Xxx)に出る  (b) KnownPrefixes に provider の Prefix が効く(定数名に SMP_ が残らない)
    //  (c) 既存種別の生成は不変(テスト用 Data 型を含めても、組み込み種別のクラスの集合は変わらない)。
    // E-26 の E26_Generator_* が 1 つの種別の出力を固定するのに対し、こちらは「生成物の形」(クラス名・定数名・集合)を固定する。
    public class ExternalContractIdGenerationTests
    {
        private const string TestRoot = TestTempFolder.Root + "/TempGameDataIdGen";

        private bool _savedInclude;
        private string _tempOutputPath;

        [SetUp]
        public void SetUp()
        {
            _savedInclude = AssetKindRegistry.IncludeTestAssemblies;
            AssetKindRegistry.IncludeTestAssemblies = true;
            AssetKindRegistry.Refresh();
            _tempOutputPath = Path.Combine(Path.GetTempPath(), "ddrive_test_e29_ids_" + System.Guid.NewGuid().ToString("N") + ".cs");
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            }

            if (File.Exists(_tempOutputPath))
            {
                File.Delete(_tempOutputPath);
            }

            AssetKindRegistry.IncludeTestAssemblies = _savedInclude;
            AssetKindRegistry.Refresh();
        }

        private string Generate(bool includeTestAssemblies)
        {
            var result = AssetIdGenerator.Regenerate(_tempOutputPath, includeTestAssemblies);
            Assert.IsTrue(result.Success);
            return File.ReadAllText(_tempOutputPath);
        }

        private static HashSet<string> ClassNames(string text)
        {
            var set = new HashSet<string>();
            foreach (Match m in Regex.Matches(text, @"public static class (\w+)"))
            {
                set.Add(m.Groups[1].Value);
            }

            return set;
        }

        // (a) KindKey ごとの定数クラス。同じ種別の Data が複数あれば同じクラスに並び、別のクラスには出ない。
        [Test]
        public void E29_Constants_AreEmittedIntoTheClassOfTheirKind()
        {
            var a = AssetCreationService.Create(typeof(ExternalSampleData), AssetType.Extension, "A", "", "Alpha", gameDataRoot: TestRoot);
            var b = AssetCreationService.Create(typeof(ExternalSampleData), AssetType.Extension, "B", "", "Beta", gameDataRoot: TestRoot);
            Assert.IsNotNull(a);
            Assert.IsNotNull(b);

            var entries = AssetIdGenerator.CollectConstantEntries(includeTestAssemblies: true);
            var sample = entries.FindAll(e => e.ConstantReference.StartsWith("SMPID."));
            Assert.AreEqual(2, sample.Count, "SMPID クラスに 2 件");
            foreach (var e in sample)
            {
                Assert.AreEqual(AssetType.Extension, e.Type);
            }

            var text = Generate(true);
            StringAssert.Contains("public static class SMPID", text);
            StringAssert.Contains($"AssetId<{typeof(ExternalSampleMarker).FullName}> Alpha = new(0x{a.Id:X}UL, AssetType.Extension);", text);
            StringAssert.Contains($"AssetId<{typeof(ExternalSampleMarker).FullName}> Beta = new(0x{b.Id:X}UL, AssetType.Extension);", text);
        }

        // (b) provider の Prefix(SMP)が既知の接頭辞として効き、ファイル名 SMP_… の先頭が定数名に残らない。
        [Test]
        public void E29_ProviderPrefix_IsKnownAndDroppedFromConstantNames()
        {
            Assert.IsTrue(AssetKindRegistry.IsKnownPrefix("SMP"));
            Assert.IsTrue(AssetKindRegistry.IsKnownPrefix("smp"), "大小文字無視");
            Assert.IsFalse(AssetKindRegistry.IsKnownPrefix("NOPE"));

            var asset = AssetCreationService.Create(typeof(ExternalSampleData), AssetType.Extension, "P", "", "Prefixed", gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);
            StringAssert.StartsWith("SMP_", Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(asset)));

            var all = AssetIdGenerator.CollectConstantEntries(includeTestAssemblies: true);
            var index = all.FindIndex(e => e.Id == asset.Id);
            Assert.GreaterOrEqual(index, 0, "生成対象に入る");
            var entry = all[index];
            StringAssert.StartsWith("SMPID.", entry.ConstantReference);
            StringAssert.DoesNotStartWith("SMPID.SMP", entry.ConstantReference, "接頭辞 SMP は定数名から落ちる");

            // provider が見えない(テスト用アセンブリを除外した)ときは、接頭辞は既知ではない。
            AssetKindRegistry.IncludeTestAssemblies = false;
            AssetKindRegistry.Refresh();
            Assert.IsFalse(AssetKindRegistry.IsKnownPrefix("SMP"));
        }

        // (c) 既存種別の生成は不変: テスト用 Data 型(Extension)を含めても含めなくても、組み込み種別の定数クラスの集合は同じで、
        // 増えるのはテスト用アセンブリの種別(SMPID ほか)だけ。既定(テスト用アセンブリを含めない)では SMPID は出ない。
        [Test]
        public void E29_BuiltInGeneration_IsUnchanged_BySampleKind()
        {
            var asset = AssetCreationService.Create(typeof(ExternalSampleData), AssetType.Extension, "U", "", "Unchanged", gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);

            var without = ClassNames(Generate(false));
            var with = ClassNames(Generate(true));

            Assert.IsFalse(without.Contains("SMPID"), "既定ではテスト用の種別は生成物に載らない");
            Assert.IsTrue(with.Contains("SMPID"));
            var onlyWith = new HashSet<string>(with);
            onlyWith.ExceptWith(without);
            Assert.IsTrue(onlyWith.Contains("SMPID"), "増えるのはテスト用アセンブリの種別のクラス(他のテストが定義した種別を含む)");
            foreach (var builtIn in new[] { "SEID", "BGMID", "VFXID", "ANIMID", "MATID", "CANVASID" })
            {
                Assert.IsFalse(onlyWith.Contains(builtIn), "組み込み種別のクラスは増減しない: " + builtIn);
            }

            var onlyWithout = new HashSet<string>(without);
            onlyWithout.ExceptWith(with);
            Assert.IsEmpty(onlyWithout, "組み込み種別のクラスは減らない");
        }
    }
}
