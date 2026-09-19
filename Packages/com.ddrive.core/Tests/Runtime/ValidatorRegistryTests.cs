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
    }
}
