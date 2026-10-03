using System.Collections.Generic;
using System.Linq;
using DDrive.Editor;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using ExternalPackage.Fake;
using NUnit.Framework;
using UnityEngine;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] 外部拡張の契約(Validation)。E-6(A-6)。
    // 検査対象が AssetDataBase だけである点(R-4)は契約にしない(docs/51 §3.2 R-4。T-Drive 側が入口を CutsceneData / ModelData にする)。
    public class ExternalContractValidatorTests
    {
        [Test]
        public void E6_ValidatorInExternalAssembly_IsDiscoveredByCI()
        {
            var found = CI.DiscoverValidators().OfType<ExternalDummyValidator>().ToList();
            Assert.AreEqual(1, found.Count, "名前が DDrive.Tests で始まらないアセンブリの IValidator(public な引数なしコンストラクタ)は発見される");
            Assert.AreEqual("ExternalContract.Tests.Editor", found[0].GetType().Assembly.GetName().Name);
        }

        [Test]
        public void E6_DiscoveredValidator_RunsOnItsData_AndReportsNothingForRealDataTypes()
        {
            var registry = new ValidatorRegistry();
            foreach (var v in CI.DiscoverValidators().OfType<ExternalDummyValidator>())
            {
                registry.Register(v);
            }

            var dummy = ScriptableObject.CreateInstance<ExternalDummyData>();
            var material = ScriptableObject.CreateInstance<DDrive.Runtime.Material.MaterialData>();
            var cutscene = ScriptableObject.CreateInstance<DDrive.Runtime.Cutscene.CutsceneData>();
            try
            {
                var reports = registry.RunAll(new List<AssetDataBase> { dummy, material, cutscene });
                Assert.AreEqual(1, reports.Count, "ダミー Data にだけ結果が出る。実データの種別には何も報告しない(Run All を汚さない)");
                Assert.AreSame(dummy, reports[0].Asset);
                Assert.AreEqual(ExternalDummyValidator.Message, reports[0].Result.Message);
            }
            finally
            {
                Object.DestroyImmediate(dummy);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(cutscene);
            }
        }

        // 現仕様(CI.cs): DDrive.Tests で始まる名前のアセンブリの IValidator は発見されない(テストが残すダミーを Run All に載せないため)。
        [Test]
        public void E6_ValidatorsInDDriveTestsAssemblies_AreNotDiscovered()
        {
            var discovered = CI.DiscoverValidators().ToList();
            Assert.IsFalse(
                discovered.Any(v => v.GetType().Assembly.GetName().Name.StartsWith("DDrive.Tests", System.StringComparison.Ordinal)),
                "DDrive.Tests* アセンブリの IValidator は発見されない");
        }
    }
}
