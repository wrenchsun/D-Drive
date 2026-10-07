using System;
using DDrive.Editor.Build;
using DDrive.Editor.Mcp;
using DDrive.Editor.Mcp.Tools;
using DDrive.Editor.Settings;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.4 MCP-7(2026-10-07) — ddrive_build_netcheck。実ビルド(1〜3 分)は走らせず、
    // BuildOverride で差し替えて形・ガード・引数の受け渡しを確認する。実ビルドは HTTP で 1 回確認した(実装メモ)。
    public class DDriveBuildToolTests
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
            DDriveBuildTools.BuildOverride = null;
            DDriveProjectSettings.instance.SetMcpAllowWrite(_originalAllowWrite, save: false);
        }

        private static string Code(JObject r) => (string)r["error"]?["code"];

        [Test]
        public void Build_RejectedWhenWriteDisabled()
        {
            DDriveProjectSettings.instance.SetMcpAllowWrite(false, save: false);
            var called = false;
            DDriveBuildTools.BuildOverride = _ =>
            {
                called = true;
                return default;
            };
            Assert.AreEqual(McpGuard.CodeWriteDisabled, Code(DDriveBuildTools.BuildNetCheck()));
            Assert.IsFalse(called, "ビルドは走らない");
        }

        [Test]
        public void Build_Success_ReturnsPathsAndSeconds_AndPassesDevelopment()
        {
            bool? seen = null;
            DDriveBuildTools.BuildOverride = dev =>
            {
                seen = dev;
                return new NetCheckBuilder.BuildResult { Success = true, ExecutablePath = "x/DDriveNetCheck.exe", ZipPath = "x.zip" };
            };

            var r = DDriveBuildTools.BuildNetCheck(development: false);
            Assert.IsNull(r["error"], r.ToString());
            Assert.IsTrue((bool)r["success"]);
            Assert.IsNotNull(r["exe"]);
            Assert.IsNotNull(r["zip"]);
            Assert.IsNotNull(r["seconds"]);
            Assert.AreEqual(false, seen);
        }

        [Test]
        public void Build_Failure_ReturnsSuccessFalseAndError()
        {
            DDriveBuildTools.BuildOverride = _ => new NetCheckBuilder.BuildResult { Success = false, Error = "シーンが無い" };
            var r = DDriveBuildTools.BuildNetCheck();
            Assert.IsFalse((bool)r["success"]);
            Assert.AreEqual("シーンが無い", (string)r["error"]);
            Assert.IsNull(r["exe"]);
        }

        [Test]
        public void Build_ExceptionIsFoldedIntoError()
        {
            DDriveBuildTools.BuildOverride = _ => throw new InvalidOperationException("boom");
            Assert.AreEqual(McpGuard.CodeException, Code(DDriveBuildTools.BuildNetCheck()));
        }

        [Test]
        public void ToolDefinition_IsWithinBudget()
        {
            var attr = (McpToolAttribute)Attribute.GetCustomAttribute(
                typeof(DDriveBuildTools).GetMethod("BuildNetCheck"), typeof(McpToolAttribute));
            Assert.AreEqual("ddrive_build_netcheck", attr.Name);
            Assert.AreEqual("build", attr.Group);
            Assert.LessOrEqual(attr.Description.Length, 80);
        }
    }
}
