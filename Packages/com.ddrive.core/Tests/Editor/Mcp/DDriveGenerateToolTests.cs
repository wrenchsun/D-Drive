using System;
using System.IO;
using DDrive.Editor.Codegen;
using DDrive.Editor.Mcp;
using DDrive.Editor.Mcp.Tools;
using DDrive.Editor.Settings;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.3 MCP-6(2026-10-07) — ddrive_generate。
    // 実 Assets/Generated・実 Addressables・実依存グラフは書き換えない(preview と純粋関数の整形だけ検証する)。
    // deps の実再構築は重いので DDriveGenerateTools.RebuildOverride で差し替える。
    public class DDriveGenerateToolTests
    {
        private bool _originalAllowWrite;

        [SetUp]
        public void SetUp()
        {
            _originalAllowWrite = DDriveProjectSettings.instance.McpAllowWrite;
            DDriveProjectSettings.instance.SetMcpAllowWrite(true, save: false);
        }

        [TearDown]
        public void TearDown()
        {
            DDriveGenerateTools.RebuildOverride = null;
            DDriveProjectSettings.instance.SetMcpAllowWrite(_originalAllowWrite, save: false);
        }

        private static string Code(JObject r) => (string)r["error"]?["code"];

        [Test]
        public void ParseTarget_AcceptsAllSevenAndIsCaseInsensitive()
        {
            foreach (var t in DDriveGenerateTools.Targets)
            {
                Assert.AreEqual(t, DDriveGenerateTools.ParseTarget(t.ToUpperInvariant()));
            }

            Assert.AreEqual(7, DDriveGenerateTools.Targets.Length);
        }

        [Test]
        public void Generate_UnknownTarget_IsInvalidParamsListingTargets()
        {
            var r = DDriveGenerateTools.Generate("nope");
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(r));
            var msg = (string)r["error"]["msg"];
            StringAssert.Contains("ids", msg);
            StringAssert.Contains("icons", msg);
            Assert.AreEqual(McpGuard.CodeInvalidParams, Code(DDriveGenerateTools.Generate(null)));
        }

        [Test]
        public void ParseScene_DefaultsToCurrentAndRejectsUnknown()
        {
            Assert.AreEqual("current", DDriveGenerateTools.ParseScene(null));
            Assert.AreEqual("all", DDriveGenerateTools.ParseScene(" ALL "));
            Assert.Throws<McpToolError>(() => DDriveGenerateTools.ParseScene("some"));
        }

        [Test]
        public void Generate_AllTargets_RefusedWhenWriteDisabled()
        {
            DDriveProjectSettings.instance.SetMcpAllowWrite(false, save: false);
            foreach (var t in DDriveGenerateTools.Targets)
            {
                Assert.AreEqual(McpGuard.CodeWriteDisabled, Code(DDriveGenerateTools.Generate(t, preview: true)), t);
            }
        }

        [Test]
        public void Ids_Preview_WritesNothing()
        {
            var path = DDriveGenerateTools.IdsPath();
            var exists = File.Exists(path);
            var beforeText = exists ? File.ReadAllText(path) : null;
            var beforeTime = exists ? File.GetLastWriteTimeUtc(path) : default;

            var r = DDriveGenerateTools.Generate("ids", preview: true);

            Assert.AreEqual("ids", (string)r["target"]);
            Assert.IsTrue((bool)r["preview"]);
            Assert.AreEqual(path, (string)r["summary"]["wouldWrite"]);
            Assert.AreEqual(exists, File.Exists(path));
            if (exists)
            {
                Assert.AreEqual(beforeText, File.ReadAllText(path));
                Assert.AreEqual(beforeTime, File.GetLastWriteTimeUtc(path));
            }
        }

        [Test]
        public void IdsResult_Duplicates_MeansNotOk()
        {
            var result = new AssetIdGenerator.Result { TotalCount = 3, AssignedCount = 1 };
            result.Duplicates.Add(new AssetIdGenerator.Duplicate("A/a.asset", "B/b.asset", 42UL));

            var json = DDriveGenerateTools.IdsResult(result, "x", "x", "Assets/Generated/AssetIds.g.cs");

            Assert.AreEqual(false, (bool)json["ok"]);
            Assert.AreEqual(3, (int)json["summary"]["total"]);
            Assert.AreEqual(1, (int)json["summary"]["assigned"]);
            Assert.AreEqual("42", (string)json["summary"]["duplicates"][0]["id"]);
            Assert.AreEqual("B/b.asset", (string)json["summary"]["duplicates"][0]["paths"][1]);
            Assert.AreEqual(false, (bool)json["changed"]);
        }

        [Test]
        public void IdsResult_ChangedByTextOrNewlyAssignedIds()
        {
            var plain = new AssetIdGenerator.Result { TotalCount = 2 };
            Assert.IsNull(DDriveGenerateTools.IdsResult(plain, "a", "a", "p")["ok"]);
            Assert.AreEqual(false, (bool)DDriveGenerateTools.IdsResult(plain, "a", "a", "p")["changed"]);
            Assert.AreEqual(true, (bool)DDriveGenerateTools.IdsResult(plain, "a", "b", "p")["changed"]);
            Assert.AreEqual(true, (bool)DDriveGenerateTools.IdsResult(plain, null, "b", "p")["changed"]);

            var assigned = new AssetIdGenerator.Result { TotalCount = 2, AssignedCount = 1 };
            Assert.AreEqual(true, (bool)DDriveGenerateTools.IdsResult(assigned, "a", "a", "p")["changed"]);
        }

        [Test]
        public void Tuning_Preview_DoesNotTouchRealFile()
        {
            var path = DDriveProjectSettings.instance.GeneratedRoot + "/Tuning.g.cs";
            var exists = File.Exists(path);
            var beforeTime = exists ? File.GetLastWriteTimeUtc(path) : default;
            var beforeText = exists ? File.ReadAllText(path) : null;

            var r = DDriveGenerateTools.Generate("tuning", preview: true);

            Assert.IsNull(r["error"], r.ToString());
            Assert.IsTrue((bool)r["preview"]);
            Assert.GreaterOrEqual((int)r["summary"]["keys"], 0);
            if (exists)
            {
                Assert.AreEqual(beforeTime, File.GetLastWriteTimeUtc(path));
                Assert.AreEqual(beforeText, File.ReadAllText(path));
            }
            else
            {
                Assert.IsFalse(File.Exists(path));
            }
        }

        [Test]
        public void TuningResult_MapsCountsAndChange()
        {
            var r = new TuningCodegen.Result { TotalCount = 4, TableCount = 2, ColumnCount = 5, Success = true };
            var json = DDriveGenerateTools.TuningResult(r, "old", "new", "Assets/Generated/Tuning.g.cs", preview: false);
            Assert.AreEqual(4, (int)json["summary"]["keys"]);
            Assert.AreEqual(2, (int)json["summary"]["tables"]);
            Assert.AreEqual(5, (int)json["summary"]["columns"]);
            Assert.AreEqual(true, (bool)json["changed"]);
            Assert.IsNull(json["preview"]);

            var failed = new TuningCodegen.Result { Success = false };
            var bad = DDriveGenerateTools.TuningResult(failed, "a", "a", "p", preview: true);
            Assert.AreEqual(false, (bool)bad["ok"]);
            Assert.IsTrue((bool)bad["preview"]);
        }

        [Test]
        public void Addressables_Preview_ReturnsMissingCount()
        {
            var r = DDriveGenerateTools.Generate("addressables", preview: true);
            Assert.IsNull(r["error"], r.ToString());
            if (r["ok"] != null)
            {
                // Addressables 設定が無い環境。
                Assert.AreEqual(false, (bool)r["ok"]);
                return;
            }

            Assert.GreaterOrEqual((int)r["summary"]["missing"], 0);
            Assert.IsTrue((bool)r["preview"]);
        }

        [Test]
        public void AddressablesResult_ChangedWhenSomethingFixed()
        {
            var json = DDriveGenerateTools.AddressablesResult(3, 5, 1);
            Assert.AreEqual(true, (bool)json["changed"]);
            Assert.AreEqual(3, (int)json["summary"]["fixedAssets"]);
            Assert.AreEqual(5, (int)json["summary"]["catalogs"]);
            Assert.AreEqual(1, (int)json["summary"]["missingCatalog"]);
            Assert.AreEqual(false, (bool)DDriveGenerateTools.AddressablesResult(0, 5, 0)["changed"]);
        }

        [Test]
        public void Prefabs_Preview_ListsOnlyMissing()
        {
            var r = DDriveGenerateTools.Generate("prefabs", preview: true);
            Assert.IsNull(r["error"], r.ToString());
            Assert.IsTrue((bool)r["preview"]);
            Assert.IsNotNull(r["summary"]["wouldCreate"]);
        }

        [Test]
        public void Deps_Preview_DoesNotRebuild()
        {
            var called = false;
            DDriveGenerateTools.RebuildOverride = () => called = true;
            var r = DDriveGenerateTools.Generate("deps", preview: true);
            Assert.IsFalse(called);
            Assert.IsTrue((bool)r["summary"]["wouldRebuild"]);
        }

        [Test]
        public void Deps_Run_UsesRebuildAndReportsSummary()
        {
            var called = false;
            DDriveGenerateTools.RebuildOverride = () => called = true;
            var r = DDriveGenerateTools.Generate("deps");
            Assert.IsTrue(called);
            Assert.AreEqual(true, (bool)r["changed"]);
            Assert.IsNotNull(r["summary"]["files"]);
            Assert.IsNotNull(r["summary"]["edges"]);
            Assert.IsNotNull(r["summary"]["seconds"]);
        }

        [Test]
        public void DepsResult_MapsNumbers()
        {
            var json = DDriveGenerateTools.DepsResult(120, 450, 12.34);
            Assert.AreEqual(120, (int)json["summary"]["files"]);
            Assert.AreEqual(450, (int)json["summary"]["edges"]);
            Assert.AreEqual(12.3, (double)json["summary"]["seconds"], 0.001);
        }

        [Test]
        public void Icons_Preview_DoesNotRun()
        {
            var r = DDriveGenerateTools.Generate("icons", preview: true);
            Assert.IsTrue((bool)r["preview"]);
            Assert.IsTrue((bool)r["summary"]["wouldRun"]);
        }

        [Test]
        public void ToolAttributes_AreWriteTools()
        {
            var a = (McpToolAttribute)Attribute.GetCustomAttribute(
                typeof(DDriveGenerateTools).GetMethod("Generate"), typeof(McpToolAttribute));
            Assert.AreEqual("ddrive_generate", a.Name);
            Assert.IsFalse(a.Destructive);
            Assert.IsNotNull(a.UndoGroup);
            Assert.LessOrEqual(a.Description.Length, 80);
        }
    }
}
