using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor.Validation
{
    // [42_distribution.md] §4.3/§4.6/§6 P-7(2026-09-20) — 「SchemaVersion が古い」Warning を検証する。
    // メモリ上だけの(保存しない)ダミー Data を使う(実 GameData には触れない)。
    public class SchemaVersionValidatorTests
    {
        private TestAssetData _dummy;

        [SetUp]
        public void SetUp()
        {
            _dummy = ScriptableObject.CreateInstance<TestAssetData>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_dummy != null)
            {
                Object.DestroyImmediate(_dummy);
            }
        }

        [Test]
        public void Validate_SchemaVersionBelowCurrent_ReturnsWarningWithCode()
        {
            _dummy.SchemaVersion = 0;
            Assert.Less(_dummy.SchemaVersion, DDriveSchema.Current, "このテストは Current > 0 である前提");

            var ctx = new ValidationContext(new List<AssetDataBase> { _dummy });
            var results = new SchemaVersionValidator().Validate(_dummy, ctx).ToList();

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationSeverity.Warning, results[0].Severity, "更新直後に Error を増やさない(§5.8)");
            Assert.AreEqual("DD-SCHEMA-OUTDATED", results[0].Code);
        }

        [Test]
        public void Validate_SchemaVersionAtCurrent_ReturnsNoResults()
        {
            _dummy.SchemaVersion = DDriveSchema.Current;

            var ctx = new ValidationContext(new List<AssetDataBase> { _dummy });
            var results = new SchemaVersionValidator().Validate(_dummy, ctx).ToList();

            Assert.IsEmpty(results);
        }

        [Test]
        public void Target_IsNone_UniversalValidator()
        {
            var validator = new SchemaVersionValidator();
            Assert.IsInstanceOf<IUniversalValidator>(validator);
        }
    }
}
