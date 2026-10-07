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
    // [1002_ddrive_mcp.md] §4.3 MCP-6(2026-10-07) — ddrive_compat / ddrive_compat_update。
    // 実スナップショットとの比較は読み取りだけ。compat_update は write_disabled の拒否だけ検証し、書き込みは走らせない。
    public class DDriveCompatToolTests
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

        [Test]
        public void Diff_AddedAndRemovedLines_AreCountedWithSample()
        {
            var row = DDriveCompatTools.Diff("x", "a\nb\nc\n", "a\nc\nd\ne\n");
            Assert.AreEqual(2, row.Added);
            Assert.AreEqual(1, row.Removed);
            // removed を先に出す。
            Assert.AreEqual("-b", row.Sample[0]);
            Assert.AreEqual("+d", row.Sample[1]);
            Assert.IsFalse(row.MissingFile);
        }

        [Test]
        public void Diff_IgnoresBlankLinesAndLineEndings()
        {
            var row = DDriveCompatTools.Diff("x", "a\r\nb\r\n\r\n", "a\nb\n");
            Assert.AreEqual(0, row.Added);
            Assert.AreEqual(0, row.Removed);
        }

        [Test]
        public void Diff_SampleIsCappedAtFive()
        {
            var row = DDriveCompatTools.Diff("x", "", "1\n2\n3\n4\n5\n6\n7\n");
            Assert.AreEqual(7, row.Added);
            Assert.AreEqual(5, row.Sample.Count);
        }

        [Test]
        public void Diff_MissingSavedFile_IsAllAdded()
        {
            var row = DDriveCompatTools.Diff("x", null, "a\nb\n");
            Assert.IsTrue(row.MissingFile);
            Assert.AreEqual(2, row.Added);
        }

        [Test]
        public void DiffJson_RemovedMakesNotOkWithWarning()
        {
            var rows = new List<DDriveCompatTools.DiffRow>
            {
                new DDriveCompatTools.DiffRow { Snapshot = "enums", Added = 1, Removed = 2, Sample = { "-a", "+b" } },
            };
            var json = DDriveCompatTools.DiffJson(rows);
            Assert.AreEqual(false, (bool)json["ok"]);
            StringAssert.Contains("MAJOR", (string)json["warning"]);
            Assert.AreEqual("enums", (string)json["changed"][0]["snapshot"]);
            Assert.AreEqual(1, (int)json["changed"][0]["added"]);
            Assert.AreEqual(2, (int)json["changed"][0]["removed"]);
            Assert.AreEqual("-a", (string)json["changed"][0]["sample"][0]);
        }

        [Test]
        public void DiffJson_AddedOnlyIsStillOk()
        {
            var rows = new List<DDriveCompatTools.DiffRow>
            {
                new DDriveCompatTools.DiffRow { Snapshot = "enums", Added = 1 },
            };
            var json = DDriveCompatTools.DiffJson(rows);
            Assert.AreEqual(true, (bool)json["ok"]);
            Assert.IsNull(json["warning"]);
        }

        [Test]
        public void UpdateJson_ListsSnapshotsThatChange()
        {
            var rows = new List<DDriveCompatTools.DiffRow>
            {
                new DDriveCompatTools.DiffRow { Snapshot = "enums", Added = 1 },
            };
            var json = DDriveCompatTools.UpdateJson(rows);
            Assert.AreEqual("enums", (string)json["updated"][0]);
            StringAssert.Contains("CHANGELOG", (string)json["hint"]);
            Assert.IsNull(json["warning"]);
        }

        [Test]
        public void Snapshots_AreTheSevenMenuUpdatedKinds()
        {
            Assert.AreEqual(7, DDriveCompatTools.Snapshots.Count);
        }

        // この assert はブランチが互換性を保っていることの実検査(removed 行が出たら MAJOR の疑い)。
        [Test]
        public void Compat_RealSnapshots_HaveNoDifference()
        {
            var json = DDriveCompatTools.Compat();
            Assert.IsNull(json["error"], json.ToString());
            Assert.AreEqual(true, (bool)json["ok"], json.ToString());
            Assert.AreEqual(0, ((JArray)json["changed"]).Count, json.ToString());
        }

        [Test]
        public void CompatUpdate_RefusedWhenWriteDisabled()
        {
            DDriveProjectSettings.instance.SetMcpAllowWrite(false, save: false);
            Assert.AreEqual(McpGuard.CodeWriteDisabled, (string)DDriveCompatTools.CompatUpdate()["error"]["code"]);
        }

        [Test]
        public void ToolAttributes_DiffIsSafeUpdateIsDestructive()
        {
            var diff = (McpToolAttribute)Attribute.GetCustomAttribute(
                typeof(DDriveCompatTools).GetMethod("Compat"), typeof(McpToolAttribute));
            var update = (McpToolAttribute)Attribute.GetCustomAttribute(
                typeof(DDriveCompatTools).GetMethod("CompatUpdate"), typeof(McpToolAttribute));
            Assert.AreEqual("ddrive_compat", diff.Name);
            Assert.IsFalse(diff.Destructive);
            Assert.AreEqual("ddrive_compat_update", update.Name);
            Assert.IsTrue(update.Destructive);
            Assert.LessOrEqual(diff.Description.Length, 80);
            Assert.LessOrEqual(update.Description.Length, 80);
        }
    }
}
