using System.Linq;
using DDrive.Editor.Validation;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [09_editor_tools.md] §11([39] U-13) — 各専用エディタ共通の「個別検証」の実行部。
    // メモリ上の ScriptableObject だけを使い、実 GameData / カタログ / Addressables には触れない。
    public class DataValidationRunnerTests
    {
        [Test]
        public void Run_Null_ReturnsEmpty_WithoutThrowing()
        {
            Assert.IsEmpty(DataValidationRunner.Run(null));
        }

        [Test]
        public void Run_RunsTypeMatchedValidator()
        {
            var vfx = ScriptableObject.CreateInstance<VfxData>();
            try
            {
                var results = DataValidationRunner.Run(vfx);

                // VfxDataValidator の「Prefab が未設定(または Missing)です」が出ること。
                Assert.IsTrue(
                    results.Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Prefab")),
                    "VfxData の種別 Validator が実行されていない");
            }
            finally
            {
                Object.DestroyImmediate(vfx);
            }
        }

        [Test]
        public void Run_ExcludesProjectWideValidators()
        {
            // 仕様書差分 / カタログ網羅はプロジェクト全体を見る Validator なので個別検証には載せない。
            var names = DataValidationRunner.Validators.Select(v => v.GetType().Name).ToArray();

            CollectionAssert.DoesNotContain(names, "SpecDiffValidator");
            CollectionAssert.DoesNotContain(names, "ContentHashCatalogCoverageValidator");
        }

        [Test]
        public void Run_IncludesPerAssetUniversalValidators()
        {
            var names = DataValidationRunner.Validators.Select(v => v.GetType().Name).ToArray();

            // 1 アセット単位で意味がある IUniversalValidator は残す。
            CollectionAssert.Contains(names, "ValueDefValidator");
            CollectionAssert.Contains(names, "AddressablesRegistrationValidator");
        }
    }
}
