using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    public class ValidatorRegistryTests
    {
        private struct DummyMarker
        {
        }

        [AssetIdDefinition(AssetType.Vfx, typeof(DummyMarker), "DUMMYID")]
        private sealed class DummyVfxData : AssetDataBase
        {
        }

        private sealed class OtherData : AssetDataBase
        {
        }

        private sealed class AlwaysErrorValidator : IValidator
        {
            public AssetType Target => AssetType.Vfx;

            public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
            {
                yield return ValidationResult.Error("always fails");
            }
        }

        [Test]
        public void RunAll_OnlyAppliesValidatorToMatchingAssetType()
        {
            var registry = new ValidatorRegistry();
            registry.Register(new AlwaysErrorValidator());

            var vfx = ScriptableObject.CreateInstance<DummyVfxData>();
            var other = ScriptableObject.CreateInstance<OtherData>();

            var reports = registry.RunAll(new AssetDataBase[] { vfx, other });

            Assert.AreEqual(1, reports.Count);
            Assert.AreSame(vfx, reports[0].Asset);
            Assert.AreEqual(ValidationSeverity.Error, reports[0].Result.Severity);
        }

        [Test]
        public void RunAll_NoValidators_ReturnsEmpty()
        {
            var registry = new ValidatorRegistry();
            var vfx = ScriptableObject.CreateInstance<DummyVfxData>();

            var reports = registry.RunAll(new AssetDataBase[] { vfx });

            Assert.AreEqual(0, reports.Count);
        }

        private sealed class AlwaysWarnsUniversalValidator : IUniversalValidator
        {
            public AssetType Target => AssetType.None;

            public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
            {
                yield return ValidationResult.Warning("universal check ran, data=" + (data == null ? "null" : "non-null"));
            }
        }

        // [47_review_p_tickets_2026-09-20.md] P2-5 — Data が 0 件でも IUniversalValidator は 1 回走る
        // (空プロジェクトの「Run All で Error 0」が検査の空振りにならないようにするため)。
        [Test]
        public void RunAll_NoAssets_StillRunsUniversalValidatorOnce()
        {
            var registry = new ValidatorRegistry();
            registry.Register(new AlwaysWarnsUniversalValidator());

            var reports = registry.RunAll(System.Array.Empty<AssetDataBase>());

            Assert.AreEqual(1, reports.Count);
            Assert.IsNull(reports[0].Asset);
            Assert.AreEqual(ValidationSeverity.Warning, reports[0].Result.Severity);
            StringAssert.Contains("data=null", reports[0].Result.Message);
        }

        // Data が 1 件以上あるときは、Data ごとに(通常どおり)呼ばれるだけで、
        // 「0 件のときの特別扱い」による二重呼び出しは起きない。
        [Test]
        public void RunAll_WithAssets_UniversalValidatorRunsOncePerAsset_NotDouble()
        {
            var registry = new ValidatorRegistry();
            registry.Register(new AlwaysWarnsUniversalValidator());
            var vfx = ScriptableObject.CreateInstance<DummyVfxData>();

            var reports = registry.RunAll(new AssetDataBase[] { vfx });

            Assert.AreEqual(1, reports.Count);
            Assert.AreSame(vfx, reports[0].Asset);
        }
    }
}
