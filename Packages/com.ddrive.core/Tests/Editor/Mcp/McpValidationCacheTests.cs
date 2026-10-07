using System.Collections.Generic;
using DDrive.Editor.Mcp;
using DDrive.Foundation.Validation;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.1 MCP-2(2026-10-07) — ddrive_status 用の Validation 要約キャッシュ。
    public class McpValidationCacheTests
    {
        [SetUp]
        public void SetUp() => McpValidationCache.Clear();

        [TearDown]
        public void TearDown() => McpValidationCache.Clear();

        [Test]
        public void TryGet_WhenEmpty_ReturnsFalse()
        {
            Assert.IsFalse(McpValidationCache.TryGet(out var summary));
            Assert.IsNull(summary);
        }

        [Test]
        public void Record_CountsBySeverity_AndRoundTrips()
        {
            var recorded = McpValidationCache.Record(new List<ValidationReport>
            {
                new ValidationReport(null, ValidationResult.Error("e")),
                new ValidationReport(null, ValidationResult.Warning("w1")),
                new ValidationReport(null, ValidationResult.Warning("w2")),
                new ValidationReport(null, ValidationResult.Info("i")),
                new ValidationReport(null, ValidationResult.Info("i")),
                new ValidationReport(null, ValidationResult.Info("i")),
            });

            Assert.AreEqual(1, (int)recorded["errors"]);
            Assert.IsTrue(McpValidationCache.TryGet(out var read));
            Assert.AreEqual(1, (int)read["errors"]);
            Assert.AreEqual(2, (int)read["warnings"]);
            Assert.AreEqual(3, (int)read["infos"]);
            StringAssert.EndsWith("Z", (string)read["at"]);
        }

        [Test]
        public void Record_Null_StoresZeros()
        {
            McpValidationCache.Record(null);
            Assert.IsTrue(McpValidationCache.TryGet(out var read));
            Assert.AreEqual(0, (int)read["errors"]);
            Assert.AreEqual(0, (int)read["warnings"]);
            Assert.AreEqual(0, (int)read["infos"]);
        }

        [Test]
        public void Clear_RemovesSummary()
        {
            McpValidationCache.Record(new List<ValidationReport>());
            McpValidationCache.Clear();
            Assert.IsFalse(McpValidationCache.TryGet(out _));
        }
    }
}
