using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anchoring;
using NUnit.Framework;

namespace DDrive.Tests.Runtime
{
    // [21_anchor_spec.md] §3.7
    public class AnchorDataValidatorTests
    {
        private static List<ValidationResult> Run(AnchorData target, params AnchorData[] others)
        {
            var all = new List<AssetDataBase> { target };
            all.AddRange(others);
            return new AnchorDataValidator().Validate(target, new ValidationContext(all)).ToList();
        }

        private static bool Has(List<ValidationResult> results, ValidationSeverity severity, string fragment)
            => results.Any(r => r.Severity == severity && r.Message.Contains(fragment));

        [Test]
        public void MissingParent_IsError()
        {
            var child = AnchorChainTestRegistry.Anchor(2, parent: 99);
            Assert.IsTrue(Has(Run(child), ValidationSeverity.Error, "登録されていません"));
        }

        [Test]
        public void Cycle_IsError()
        {
            var a = AnchorChainTestRegistry.Anchor(1, parent: 2);
            var b = AnchorChainTestRegistry.Anchor(2, parent: 1);
            Assert.IsTrue(Has(Run(a, b), ValidationSeverity.Error, "循環"));
        }

        [Test]
        public void RootWithoutPath_IsError()
        {
            var root = AnchorChainTestRegistry.Anchor(1, space: AnchorSpace.BoneName, path: "");
            Assert.IsTrue(Has(Run(root), ValidationSeverity.Error, "Path が未設定"));
        }

        [Test]
        public void ChildWithRootOnlyFields_IsWarning()
        {
            var root = AnchorChainTestRegistry.Anchor(1);
            var child = AnchorChainTestRegistry.Anchor(2, parent: 1, space: AnchorSpace.NamedObject, path: "X");
            child.FollowRotation = true;
            var results = Run(child, root);
            Assert.IsTrue(Has(results, ValidationSeverity.Warning, "Space/Path"));
            Assert.IsTrue(Has(results, ValidationSeverity.Warning, "FollowRotation"));
        }

        [Test]
        public void ChanceZero_IsWarning_AndValidChainHasNoError()
        {
            var root = AnchorChainTestRegistry.Anchor(1);
            var child = AnchorChainTestRegistry.Anchor(2, parent: 1);
            child.SpawnChance = 0f;
            var results = Run(child, root);
            Assert.IsTrue(Has(results, ValidationSeverity.Warning, "SpawnChance"));
            Assert.IsFalse(results.Any(r => r.Severity == ValidationSeverity.Error));
        }
    }
}
