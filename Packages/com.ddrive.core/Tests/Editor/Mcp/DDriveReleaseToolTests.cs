using System;
using DDrive.Editor.Mcp;
using DDrive.Editor.Mcp.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.3 / §9 Q-9 MCP-6(2026-10-07) — ddrive_release_check。
    // 実行テストは pwsh が PATH にあるときだけ。テスト中は作業ツリーが汚れているので ok の値ではなく形だけ検証する。
    public class DDriveReleaseToolTests
    {
        [Test]
        public void BuildArguments_AddsJsonAndOptionalFlags()
        {
            var plain = DDriveReleaseTools.BuildArguments("s.ps1", null, false);
            CollectionAssert.AreEqual(new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "s.ps1", "-Json" }, plain);

            var full = DDriveReleaseTools.BuildArguments("s.ps1", " v1.4.1 ", true);
            CollectionAssert.AreEqual(
                new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "s.ps1", "-Json", "-Base", "v1.4.1", "-GuardOnly" }, full);
        }

        [Test]
        public void BuildArguments_RejectsUnsafeBase()
        {
            Assert.Throws<McpToolError>(() => DDriveReleaseTools.BuildArguments("s.ps1", "-Foo", false));
            Assert.Throws<McpToolError>(() => DDriveReleaseTools.BuildArguments("s.ps1", "v1; rm -rf", false));
            Assert.Throws<McpToolError>(() => DDriveReleaseTools.BuildArguments("s.ps1", "a b", false));
            Assert.DoesNotThrow(() => DDriveReleaseTools.BuildArguments("s.ps1", "origin/main", false));
        }

        [Test]
        public void ParseOutput_ReadsJsonLine()
        {
            var json = DDriveReleaseTools.ParseOutput("noise\n{\"ok\":false,\"checks\":[{\"name\":\"a\",\"ok\":false,\"msg\":\"m\"}]}\n", "", 1);
            Assert.AreEqual(false, (bool)json["ok"]);
            Assert.AreEqual("a", (string)json["checks"][0]["name"]);
            Assert.IsNull(json["raw"]);
        }

        [Test]
        public void ParseOutput_NonJson_ReturnsRawCappedAt500()
        {
            var json = DDriveReleaseTools.ParseOutput(new string('x', 800), "", 2);
            Assert.AreEqual(false, (bool)json["ok"]);
            Assert.AreEqual(500, ((string)json["raw"]).Length);
            Assert.AreEqual(2, (int)json["exitCode"]);

            var errOnly = DDriveReleaseTools.ParseOutput("", "boom", 1);
            Assert.AreEqual("boom", (string)errOnly["raw"]);
        }

        [Test]
        public void ParseOutput_JsonWithoutChecks_IsRaw()
        {
            var json = DDriveReleaseTools.ParseOutput("{\"foo\":1}", "", 0);
            Assert.AreEqual(false, (bool)json["ok"]);
            Assert.IsNotNull(json["raw"]);
        }

        [Test]
        public void ReleaseCheck_Real_ReturnsShape()
        {
            var r = DDriveReleaseTools.ReleaseCheck("HEAD", false);
            if (r["error"] != null && ((string)r["error"]["msg"]).Contains("pwsh"))
            {
                Assert.Ignore("pwsh が無い環境");
            }

            if (r["error"] != null && ((string)r["error"]["code"]) == McpGuard.CodeInvalidParams)
            {
                Assert.Ignore("開発リポジトリではない: " + r["error"]["msg"]);
            }

            Assert.IsNull(r["error"], r.ToString());
            Assert.AreEqual(JTokenType.Boolean, r["ok"].Type, r.ToString());
            var checks = (JArray)r["checks"];
            Assert.GreaterOrEqual(checks.Count, 3, r.ToString());
            Assert.IsNotNull(checks[0]["name"]);
            Assert.AreEqual(JTokenType.Boolean, checks[0]["ok"].Type);
        }

        [Test]
        public void ReleaseCheck_UnsafeBase_IsInvalidParams()
        {
            var r = DDriveReleaseTools.ReleaseCheck("-Foo", true);
            Assert.AreEqual(McpGuard.CodeInvalidParams, (string)r["error"]["code"]);
        }

        [Test]
        public void ToolAttribute_IsSafe()
        {
            var a = (McpToolAttribute)Attribute.GetCustomAttribute(
                typeof(DDriveReleaseTools).GetMethod("ReleaseCheck"), typeof(McpToolAttribute));
            Assert.AreEqual("ddrive_release_check", a.Name);
            Assert.IsFalse(a.Destructive);
            Assert.LessOrEqual(a.Description.Length, 80);
        }
    }
}
