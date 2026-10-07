using System;
using System.Collections.Generic;
using DDrive.Editor.Mcp;
using DDrive.Editor.Mcp.Tools;
using DDrive.Editor.Settings;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.3 MCP-6(2026-10-07) — ddrive_migrate。実プロジェクトでは plan(読み取り)だけ走らせ、apply は走らせない。
    public class DDriveMigrateToolTests
    {
        private bool _originalAllowWrite;

        [SetUp]
        public void SetUp()
        {
            _originalAllowWrite = DDriveProjectSettings.instance.McpAllowWrite;
        }

        [TearDown]
        public void TearDown()
        {
            DDriveProjectSettings.instance.SetMcpAllowWrite(_originalAllowWrite, save: false);
        }

        private static string Code(JObject r) => (string)r["error"]?["code"];

        [Test]
        public void ParseMode_DefaultsToPlan()
        {
            Assert.AreEqual("plan", DDriveMigrateTools.ParseMode(null));
            Assert.AreEqual("apply", DDriveMigrateTools.ParseMode("APPLY"));
            Assert.Throws<McpToolError>(() => DDriveMigrateTools.ParseMode("run"));
        }

        [Test]
        public void Migrate_UnknownMode_IsInvalidParams()
        {
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveMigrateTools.Migrate("run")));
        }

        [Test]
        public void Plan_RealProject_HasShapeAndNothingPending()
        {
            DDriveProjectSettings.instance.SetMcpAllowWrite(true, save: false);
            var r = DDriveMigrateTools.Migrate("plan");
            Assert.IsNull(r["error"], r.ToString());
            Assert.IsInstanceOf<JArray>(r["pending"]);
            Assert.AreEqual(0, (int)r["count"], r.ToString());
        }

        [Test]
        public void Plan_AllowedWhenWriteDisabled_ApplyIsNot()
        {
            DDriveProjectSettings.instance.SetMcpAllowWrite(false, save: false);
            Assert.IsNull(DDriveMigrateTools.Migrate("plan")["error"]);
            Assert.AreEqual(McpGuard.CodeWriteDisabled, Code(DDriveMigrateTools.Migrate("apply")));
        }

        [Test]
        public void PlanJson_ShapesRows()
        {
            var rows = new List<DDriveMigrateTools.PendingRow>
            {
                new DDriveMigrateTools.PendingRow("m-1", "data", 12),
                new DDriveMigrateTools.PendingRow("p-1", "project", 1, "説明"),
            };
            var json = DDriveMigrateTools.PlanJson(rows);
            Assert.AreEqual(2, (int)json["count"]);
            Assert.AreEqual("m-1", (string)json["pending"][0]["id"]);
            Assert.AreEqual("data", (string)json["pending"][0]["kind"]);
            Assert.AreEqual(12, (int)json["pending"][0]["targets"]);
            Assert.IsNull(json["pending"][0]["description"]);
            Assert.AreEqual("説明", (string)json["pending"][1]["description"]);
        }

        [Test]
        public void ApplyJson_ShapesAppliedFailedAndAfter()
        {
            var outcome = new DDriveMigrateTools.ApplyOutcome { AfterPending = 1, Warnings = 2 };
            outcome.Applied.Add(new DDriveMigrateTools.PendingRow("m-1", "data", 12));
            outcome.Failed.Add(("p-1", "残った"));
            outcome.Log.Add("警告: x");

            var json = DDriveMigrateTools.ApplyJson(outcome);
            Assert.AreEqual("m-1", (string)json["applied"][0]["id"]);
            Assert.AreEqual(12, (int)json["applied"][0]["targets"]);
            Assert.AreEqual("p-1", (string)json["failed"][0]["id"]);
            Assert.AreEqual("残った", (string)json["failed"][0]["msg"]);
            Assert.AreEqual(1, (int)json["after"]["pending"]);
            Assert.AreEqual(2, (int)json["warnings"]);
            Assert.AreEqual("警告: x", (string)json["log"][0]);

            var clean = DDriveMigrateTools.ApplyJson(new DDriveMigrateTools.ApplyOutcome());
            Assert.AreEqual(0, ((JArray)clean["applied"]).Count);
            Assert.AreEqual(0, (int)clean["after"]["pending"]);
            Assert.IsNull(clean["warnings"]);
        }

        [Test]
        public void ToolAttribute_IsDestructive()
        {
            var a = (McpToolAttribute)Attribute.GetCustomAttribute(
                typeof(DDriveMigrateTools).GetMethod("Migrate"), typeof(McpToolAttribute));
            Assert.AreEqual("ddrive_migrate", a.Name);
            Assert.IsTrue(a.Destructive);
            Assert.LessOrEqual(a.Description.Length, 80);
        }
    }
}
