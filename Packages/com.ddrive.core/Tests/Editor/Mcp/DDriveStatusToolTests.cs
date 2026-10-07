using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using DDrive.Editor.Mcp;
using DDrive.Editor.Mcp.Tools;
using DDrive.Editor.Settings;
using DDrive.Foundation.Validation;
using DDrive.Runtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.1 / §7 MCP-1(2026-10-07) — ddrive_status とツール定義の規約。
    // ツールは MCP を通さず static メソッドを直接呼ぶ(isuzu のトランスポートはテストしない)。
    public class DDriveStatusToolTests
    {
        private static readonly Regex NamePattern = new(@"^[a-z][a-z0-9_]{0,63}$");

        [Test]
        public void Status_ReturnsVersionAndMcpSection()
        {
            var r = DDriveStatusTools.Status();
            Assert.AreEqual(DDriveVersion.Value, (string)r["version"]);
            Assert.IsNotNull(r["compile"]);
            Assert.IsNotNull(r["mcp"]);
            Assert.IsNull(r["error"]);
        }

        [Test]
        public void Status_WriteEnabled_MatchesSetting()
        {
            var settings = DDriveProjectSettings.instance;
            var original = settings.McpAllowWrite;
            try
            {
                settings.SetMcpAllowWrite(true, save: false);
                Assert.AreEqual(true, (bool)DDriveStatusTools.Status("mcp")["mcp"]["writeEnabled"]);

                settings.SetMcpAllowWrite(false, save: false);
                Assert.AreEqual(false, (bool)DDriveStatusTools.Status("mcp")["mcp"]["writeEnabled"]);
            }
            finally
            {
                settings.SetMcpAllowWrite(original, save: false);
            }
        }

        [Test]
        public void Status_Sections_FilterResult()
        {
            var r = DDriveStatusTools.Status("version");
            Assert.IsNotNull(r["version"]);
            Assert.IsNull(r["compile"]);
            Assert.IsNull(r["mcp"]);
        }

        [Test]
        public void Status_UnknownSection_IsIgnored()
        {
            var r = DDriveStatusTools.Status("bogus,version");
            Assert.IsNull(r["error"]);
            Assert.IsNotNull(r["version"]);
            Assert.IsNull(r["mcp"]);

            var onlyUnknown = DDriveStatusTools.Status("bogus");
            Assert.IsNull(onlyUnknown["error"]);
            Assert.AreEqual(0, onlyUnknown.Count);
        }

        [Test]
        public void Status_AllSections_DefaultContainsEveryKey()
        {
            var r = DDriveStatusTools.Status();
            Assert.IsNull(r["error"]);
            foreach (var key in new[] { "version", "schema", "compile", "validation", "migration", "mcp" })
            {
                Assert.IsNotNull(r[key], key);
            }
        }

        [Test]
        public void Status_Mcp_HasProjectAndNeverLeaksToken()
        {
            var mcp = DDriveStatusTools.Status("mcp")["mcp"];
            Assert.IsFalse(string.IsNullOrEmpty((string)mcp["project"]));
            Assert.IsNull(mcp["token"]);
            StringAssert.DoesNotContain("token", mcp.ToString().ToLowerInvariant());
        }

        [Test]
        public void Status_Mcp_HasIsuzuVersion_AndOtherMcpIsArrayWhenPresent()
        {
            // [1002] §11.2 D MCP-14: isuzuVersion は isuzu が manifest にあるとき必ず出る(この Editor は isuzu で動いている)。
            // otherMcp は他の MCP があるときだけ出る配列(無ければ省略)。
            var mcp = DDriveStatusTools.Status("mcp")["mcp"];
            Assert.IsFalse(string.IsNullOrEmpty((string)mcp["isuzuVersion"]));
            if (mcp["otherMcp"] != null)
            {
                Assert.AreEqual(JTokenType.Array, mcp["otherMcp"].Type);
                Assert.Greater(((JArray)mcp["otherMcp"]).Count, 0);
            }
        }

        [Test]
        public void Status_Validation_NotCachedWhenEmpty()
        {
            McpValidationCache.Clear();
            var v = DDriveStatusTools.Status("validation")["validation"];
            Assert.AreEqual(false, (bool)v["cached"]);
            Assert.IsNull(v["errors"]);
        }

        [Test]
        public void Status_Validation_ReflectsRecordedSummary()
        {
            McpValidationCache.Clear();
            try
            {
                McpValidationCache.Record(new List<ValidationReport>
                {
                    new ValidationReport(null, ValidationResult.Error("e1")),
                    new ValidationReport(null, ValidationResult.Error("e2")),
                    new ValidationReport(null, ValidationResult.Warning("w")),
                    new ValidationReport(null, ValidationResult.Info("i")),
                });
                var v = DDriveStatusTools.Status("validation")["validation"];
                Assert.AreEqual(2, (int)v["errors"]);
                Assert.AreEqual(1, (int)v["warnings"]);
                Assert.AreEqual(1, (int)v["infos"]);
                Assert.IsFalse(string.IsNullOrEmpty((string)v["at"]));
                Assert.IsNull(v["cached"]);
            }
            finally
            {
                McpValidationCache.Clear();
            }
        }

        [Test]
        public void Status_Migration_PendingIsNumber()
        {
            var m = DDriveStatusTools.Status("migration")["migration"];
            Assert.AreEqual(JTokenType.Integer, m["pending"].Type);
        }

        [Test]
        public void AllMcpTools_FollowNamingAndDescriptionRules()
        {
            var count = 0;
            foreach (var type in typeof(McpGuard).Assembly.GetTypes())
            {
                var methods = type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var method in methods)
                {
                    var attr = method.GetCustomAttribute<McpToolAttribute>();
                    if (attr == null)
                    {
                        continue;
                    }

                    count++;
                    Assert.IsTrue(method.IsStatic, $"{type.Name}.{method.Name} は static であること");
                    StringAssert.StartsWith("ddrive_", attr.Name, $"{type.Name}.{method.Name}: ツール名は ddrive_ で始める");
                    Assert.IsTrue(NamePattern.IsMatch(attr.Name), $"{attr.Name} が isuzu の名前規則に合わない");
                    Assert.LessOrEqual(attr.Description.Length, 80, $"{attr.Name} の説明は 80 文字以内(§5.1)");
                    Assert.IsTrue(
                        attr.Group == "authoring" || attr.Group == "diagnostics" || attr.Group == "build",
                        $"{attr.Name} の Group は authoring / diagnostics / build のいずれか明示");
                }
            }

            Assert.GreaterOrEqual(count, 1, "ddrive_status が見つからない");
        }
    }
}
