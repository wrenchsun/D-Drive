using System;
using System.Collections.Generic;
using System.IO;
using DDrive.Editor.Mcp;
using DDrive.Editor.Mcp.Tools;
using DDrive.Editor.Settings;
using DDrive.Foundation.Validation;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.3 / §5.2 MCP-5(2026-10-07) — ddrive_validate / validate_fix / forbidden_api。
    // 集計・ページ切り・文字数への収め込みは純粋関数(ValidationSummary / ItemPaging / ForbiddenApiSummary)なので、
    // 合成した ValidationReport で検証する。実プロジェクト全体を走らせるテストは書かない(重い・実データに依存する)。
    public class DDriveValidationToolTests
    {
        private static ValidationReport Project(ValidationSeverity sev, string code, string msg = "m", Action fix = null)
            => new ValidationReport(null, new ValidationResult(sev, msg, fix, code));

        private static List<ValidationReport> Sample() => new()
        {
            Project(ValidationSeverity.Warning, "W-A"),
            Project(ValidationSeverity.Warning, "W-A"),
            Project(ValidationSeverity.Warning, "W-A"),
            Project(ValidationSeverity.Error, "E-B"),
            Project(ValidationSeverity.Error, "E-C"),
            Project(ValidationSeverity.Error, "E-C"),
            Project(ValidationSeverity.Info, "I-D"),
            Project(ValidationSeverity.Error, ""),
        };

        // ── 集計 ──

        [Test]
        public void Build_CountsAndByCodeOrdering_ErrorsFirstThenCountDesc()
        {
            var s = ValidationSummary.Build(Sample(), null);
            Assert.AreEqual(4, s.Errors);
            Assert.AreEqual(3, s.Warnings);
            Assert.AreEqual(1, s.Infos);

            var codes = new List<string>();
            foreach (var c in s.ByCode)
            {
                codes.Add(c.Code + ":" + c.Count);
            }

            // Error(件数の多い順 → 同数は Code 順)→ Warning → Info。Code 無しは (none)。
            CollectionAssert.AreEqual(new[] { "E-C:2", "(none):1", "E-B:1", "W-A:3", "I-D:1" }, codes);
        }

        [Test]
        public void Build_CodesFilter_AppliesToCountsAndRows()
        {
            var s = ValidationSummary.Build(Sample(), ValidationSummary.ParseCodes(" E-C , (none) "));
            Assert.AreEqual(3, s.Errors);
            Assert.AreEqual(0, s.Warnings);
            Assert.AreEqual(3, s.Rows.Count);
            Assert.IsNull(ValidationSummary.ParseCodes("  "));
            Assert.IsNull(ValidationSummary.ParseCodes(null));
        }

        [Test]
        public void Build_Fixable_OnlyProjectLevelWithFixAction()
        {
            var reports = new List<ValidationReport>
            {
                Project(ValidationSeverity.Warning, "F-1", fix: () => { }),
                Project(ValidationSeverity.Warning, "F-1", fix: () => { }),
                Project(ValidationSeverity.Info, "F-2", fix: () => { }),
                Project(ValidationSeverity.Error, "F-3"),
            };
            var s = ValidationSummary.Build(reports, null);
            Assert.AreEqual(1, s.Fixable.Count);
            Assert.AreEqual("F-1", s.Fixable[0].Code);
            Assert.AreEqual(2, s.Fixable[0].Count);
        }

        // ── JSON ──

        [Test]
        public void ToJson_Summary_HasNoItems()
        {
            var j = ValidationSummary.Build(Sample(), null).ToJson("all", ValidationSummary.DetailMode.Summary, null, 0, 0);
            Assert.AreEqual("all", (string)j["scope"]);
            Assert.AreEqual(4, (int)j["errors"]);
            Assert.IsNull(j["items"]);
            Assert.AreEqual("E-C", (string)j["byCode"][0]["code"]);
            Assert.AreEqual("error", (string)j["byCode"][0]["sev"]);
        }

        [Test]
        public void ToJson_ErrorsDetail_OnlyErrorItems_ProjectItemOmitsTypeIdName()
        {
            var j = ValidationSummary.Build(Sample(), null).ToJson("all", ValidationSummary.DetailMode.Errors, null, 0, 0);
            var items = (JArray)j["items"];
            Assert.AreEqual(4, items.Count);
            foreach (var item in items)
            {
                Assert.AreEqual("error", (string)item["sev"]);
                Assert.IsNull(item["type"]);
                Assert.IsNull(item["id"]);
                Assert.IsNull(item["name"]);
                Assert.IsNotNull(item["msg"]);
            }

            var all = ValidationSummary.Build(Sample(), null).ToJson("all", ValidationSummary.DetailMode.All, null, 0, 100000);
            Assert.AreEqual(8, ((JArray)all["items"]).Count);
        }

        [Test]
        public void MapRow_CutsMessageTo200Chars()
        {
            var long300 = new string('x', 300);
            var reports = new List<ValidationReport> { Project(ValidationSeverity.Error, "E-L", long300) };
            var j = ValidationSummary.Build(reports, null).ToJson("all", ValidationSummary.DetailMode.Errors, null, 0, 0);
            Assert.AreEqual(200, ((string)j["items"][0]["msg"]).Length);
        }

        [Test]
        public void Paging_LimitProducesNext_AndCursorContinues()
        {
            var reports = new List<ValidationReport>();
            for (var i = 0; i < 7; i++)
            {
                reports.Add(Project(ValidationSeverity.Error, "E-P", "m" + i));
            }

            var s = ValidationSummary.Build(reports, null);
            var p1 = s.ToJson("all", ValidationSummary.DetailMode.Errors, null, 3, 100000);
            Assert.AreEqual(3, ((JArray)p1["items"]).Count);
            Assert.AreEqual("3", (string)p1["next"]);
            Assert.IsNull(p1["truncated"]);

            var p3 = s.ToJson("all", ValidationSummary.DetailMode.Errors, "6", 3, 100000);
            Assert.AreEqual(1, ((JArray)p3["items"]).Count);
            Assert.AreEqual("m6", (string)p3["items"][0]["msg"]);
            Assert.IsNull(p3["next"]);

            var ex = Assert.Throws<McpToolError>(() => s.ToJson("all", ValidationSummary.DetailMode.Errors, "abc", 3, 100000));
            Assert.AreEqual(McpGuard.CodeInvalidParams, ex.Code);
        }

        [Test]
        public void Truncation_DropsWholeItems_AndNextPointsAtFirstDropped()
        {
            var reports = new List<ValidationReport>();
            for (var i = 0; i < 40; i++)
            {
                reports.Add(Project(ValidationSeverity.Error, "E-T", new string('y', 150)));
            }

            var s = ValidationSummary.Build(reports, null);
            var j = s.ToJson("all", ValidationSummary.DetailMode.Errors, null, 40, 1500);
            Assert.AreEqual(true, (bool)j["truncated"]);
            var kept = ((JArray)j["items"]).Count;
            Assert.Greater(kept, 0);
            Assert.Less(kept, 40);
            Assert.AreEqual(kept.ToString(), (string)j["next"]);
            Assert.LessOrEqual(McpJson.Compact(j).Length, 1500);

            // 切り捨て後のカーソルから続きを取ると、落とした項目の先頭から始まる(取りこぼし・重複なし)。
            var rest = s.ToJson("all", ValidationSummary.DetailMode.Errors, (string)j["next"], 40, 100000);
            Assert.AreEqual(40 - kept, ((JArray)rest["items"]).Count);
        }

        // ── scope ──

        [Test]
        public void ScopeParse_ValidForms()
        {
            Assert.AreEqual(ValidationScope.ScopeKind.All, ValidationScope.Parse(null).Kind);
            Assert.AreEqual(ValidationScope.ScopeKind.All, ValidationScope.Parse("ALL").Kind);
            Assert.AreEqual(ValidationScope.ScopeKind.Project, ValidationScope.Parse("project").Kind);

            var t = ValidationScope.Parse("type:se");
            Assert.AreEqual(ValidationScope.ScopeKind.Type, t.Kind);
            Assert.AreEqual(DDrive.Foundation.Identity.AssetType.Se, t.Type);
            Assert.AreEqual("type:Se", t.Text);

            var a = ValidationScope.Parse("asset:Se:123");
            Assert.AreEqual(ValidationScope.ScopeKind.Asset, a.Kind);
            Assert.AreEqual(123UL, a.Id);
            Assert.AreEqual(255UL, ValidationScope.Parse("asset:Bgm:0xFF").Id);
        }

        [TestCase("bogus")]
        [TestCase("type:")]
        [TestCase("type:Nope")]
        [TestCase("type:None")]
        [TestCase("type:3")]
        [TestCase("asset:Se")]
        [TestCase("asset:Se:abc")]
        [TestCase("asset:Se:-1")]
        [TestCase("asset:Nope:1")]
        public void ScopeParse_Invalid_ThrowsInvalidParams(string scope)
        {
            var ex = Assert.Throws<McpToolError>(() => ValidationScope.Parse(scope));
            Assert.AreEqual(McpGuard.CodeInvalidParams, ex.Code);
        }

        [Test]
        public void Validate_BadScopeOrDetail_ReturnsInvalidParamsError()
        {
            Assert.AreEqual(McpGuard.CodeInvalidParams, (string)DDriveValidationTools.Validate("type:Nope")["error"]["code"]);
            Assert.AreEqual(McpGuard.CodeInvalidParams, (string)DDriveValidationTools.Validate("asset:Se:999999999999")["error"]["code"]);
            Assert.AreEqual(McpGuard.CodeInvalidParams, (string)DDriveValidationTools.Validate("project", "bogus")["error"]["code"]);
        }

        [Test]
        public void Validate_NonAllScope_DoesNotOverwriteStatusCache()
        {
            McpValidationCache.Clear();
            try
            {
                DDriveValidationTools.Validate("project");
                Assert.IsFalse(McpValidationCache.TryGet(out _));
            }
            finally
            {
                McpValidationCache.Clear();
            }
        }

        // ── validate_fix ──

        [Test]
        public void ValidateFix_RefusesWhenWriteDisabled()
        {
            var settings = DDriveProjectSettings.instance;
            var original = settings.McpAllowWrite;
            try
            {
                settings.SetMcpAllowWrite(false, save: false);
                var r = DDriveValidationTools.ValidateFix();
                Assert.AreEqual(McpGuard.CodeWriteDisabled, (string)r["error"]["code"]);
            }
            finally
            {
                settings.SetMcpAllowWrite(original, save: false);
            }
        }

        [Test]
        public void ValidateFix_IsDestructiveWithUndoGroup()
        {
            var method = typeof(DDriveValidationTools).GetMethod(nameof(DDriveValidationTools.ValidateFix));
            var attr = (UnityMCP.Editor.Core.Attributes.McpToolAttribute)Attribute.GetCustomAttribute(
                method, typeof(UnityMCP.Editor.Core.Attributes.McpToolAttribute));
            Assert.IsTrue(attr.Destructive);
            Assert.IsFalse(string.IsNullOrEmpty(attr.UndoGroup));
            Assert.AreEqual("authoring", attr.Group);
        }

        // ── forbidden_api ──

        [Test]
        public void ForbiddenApi_TempFolder_ReportsDeliberateHit()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ddrive_mcp_fb_" + Guid.NewGuid().ToString("N"), "Runtime");
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(
                    Path.Combine(dir, "Bad.cs"),
                    "class Bad { void M() {\n  var x = Resources" + ".Load(\"a\");\n} }\n");

                var summary = DDriveValidationTools.ForbiddenApi(dir);
                Assert.IsNull(summary["error"]);
                Assert.AreEqual(1, (int)summary["violations"]);
                Assert.AreEqual("ResourcesLoad", (string)summary["byRule"][0]["rule"]);
                Assert.IsNull(summary["items"]);

                var all = DDriveValidationTools.ForbiddenApi(dir, "all");
                var item = all["items"][0];
                Assert.AreEqual("ResourcesLoad", (string)item["rule"]);
                Assert.AreEqual(2, (int)item["line"]);
                StringAssert.EndsWith("Bad.cs", (string)item["file"]);
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(dir), true);
            }
        }

        [Test]
        public void ForbiddenApi_MissingRoot_ReportsGlobalViolationInsteadOfThrowing()
        {
            var r = DDriveValidationTools.ForbiddenApi(Path.Combine(Path.GetTempPath(), "ddrive_mcp_nope_" + Guid.NewGuid().ToString("N")));
            Assert.IsNull(r["error"]);
            Assert.AreEqual(1, (int)r["violations"]);
            Assert.AreEqual(ForbiddenApiSummary.GlobalRule, (string)r["byRule"][0]["rule"]);
        }

        [Test]
        public void ForbiddenApiSummary_ByRuleSortedAndRelativePaths()
        {
            var rows = new List<ForbiddenApiSummary.Row>
            {
                new("A", "C:/p/Assets/a.cs", 1, "m", false),
                new("B", "C:/p/Assets/b.cs", 2, "m", false),
                new("B", "C:/p/Assets/c.cs", 3, "m", false),
            };
            var j = ForbiddenApiSummary.ToJson("Assets", rows, 3, 0, true, null, 0, 100000, "C:/p");
            Assert.AreEqual("B", (string)j["byRule"][0]["rule"]);
            Assert.AreEqual(2, (int)j["byRule"][0]["count"]);
            Assert.AreEqual("Assets/a.cs", (string)j["items"][0]["file"]);
            Assert.IsNull(j["items"][0]["msg"]);
        }
    }
}
