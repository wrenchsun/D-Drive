using System;
using DDrive.Editor.Mcp;
using DDrive.Editor.Settings;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §3 / §5.2 / §7 MCP-1(2026-10-07) — 共通ガードの契約。
    public class McpGuardTests
    {
        [Test]
        public void ReadOnlyFields_ListIsFixed()
        {
            CollectionAssert.AreEquivalent(
                new[] { "Id", "SchemaVersion", "ImportSourceGuid", "Version", "UpdatedAt", "Icon" },
                McpGuard.ReadOnlyFields);
        }

        [Test]
        public void ReadOnlyFields_MatchRealSerializedFieldNames()
        {
            // AssetDataBase の実フィールド名と食い違っていたら(改名等)ここで気づく。
            foreach (var name in McpGuard.ReadOnlyFields)
            {
                Assert.IsNotNull(
                    typeof(DDrive.Foundation.Data.AssetDataBase).GetField(name),
                    $"AssetDataBase に '{name}' が無い");
            }
        }

        [TestCase("Id", true)]
        [TestCase("SchemaVersion", true)]
        [TestCase("ImportSourceGuid", true)]
        [TestCase("Version", true)]
        [TestCase("UpdatedAt", true)]
        [TestCase("Icon", true)]
        [TestCase("Icon.Array.data[0]", true)]
        [TestCase("Version[0]", true)]
        [TestCase("DisplayName", false)]
        [TestCase("IdExtra", false)]
        [TestCase("id", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsReadOnlyField_Matches(string path, bool expected)
        {
            Assert.AreEqual(expected, McpGuard.IsReadOnlyField(path));
        }

        [Test]
        public void EnsureFieldWritable_ReadOnly_ThrowsReadOnlyField()
        {
            var ex = Assert.Throws<McpToolError>(() => McpGuard.EnsureFieldWritable("Id"));
            Assert.AreEqual(McpGuard.CodeReadOnlyField, ex.Code);
            Assert.DoesNotThrow(() => McpGuard.EnsureFieldWritable("DisplayName"));
        }

        [Test]
        public void Truncate_AtBoundary()
        {
            var s = new string('a', 10);

            var exact = McpGuard.Truncate(s, 10);
            Assert.AreEqual(s, exact.text);
            Assert.IsFalse(exact.truncated);

            var over = McpGuard.Truncate(s, 9);
            Assert.AreEqual(9, over.text.Length);
            Assert.IsTrue(over.truncated);
        }

        [Test]
        public void Truncate_DefaultMaxChars_Is4000()
        {
            Assert.AreEqual(4000, McpGuard.DefaultMaxChars);
            var r = McpGuard.Truncate(new string('x', 4001));
            Assert.AreEqual(4000, r.text.Length);
            Assert.IsTrue(r.truncated);
            Assert.IsFalse(McpGuard.Truncate(new string('x', 4000)).truncated);
        }

        [Test]
        public void Run_Success_ReturnsBodyResult()
        {
            var r = McpGuard.Run(() => new JObject { ["a"] = 1 });
            Assert.AreEqual(1, (int)r["a"]);
        }

        [Test]
        public void Run_FoldsMcpToolError()
        {
            var r = McpGuard.Run(() => throw new McpToolError("play_mode", "stop first"));
            Assert.AreEqual("play_mode", (string)r["error"]["code"]);
            Assert.AreEqual("stop first", (string)r["error"]["msg"]);
        }

        [Test]
        public void Run_FoldsGenericException_WithoutStackTrace()
        {
            var r = McpGuard.Run(() => throw new InvalidOperationException(new string('z', 500)));
            Assert.AreEqual("exception", (string)r["error"]["code"]);
            var msg = (string)r["error"]["msg"];
            StringAssert.StartsWith("InvalidOperationException: ", msg);
            Assert.LessOrEqual(msg.Length, 200);
            StringAssert.DoesNotContain(" at ", msg);
        }

        [Test]
        public void EnsureWriteAllowed_FollowsSetting()
        {
            var settings = DDriveProjectSettings.instance;
            var original = settings.McpAllowWrite;
            try
            {
                settings.SetMcpAllowWrite(false, save: false);
                var ex = Assert.Throws<McpToolError>(McpGuard.EnsureWriteAllowed);
                Assert.AreEqual(McpGuard.CodeWriteDisabled, ex.Code);

                settings.SetMcpAllowWrite(true, save: false);
                Assert.DoesNotThrow(McpGuard.EnsureWriteAllowed);
            }
            finally
            {
                settings.SetMcpAllowWrite(original, save: false);
            }
        }

        [Test]
        public void EnsureNotPlaying_NotPlayingInEditMode_DoesNotThrow()
        {
            // EditMode テスト実行中は Play Mode ではない。
            Assert.IsFalse(McpGuard.IsPlaying);
            Assert.DoesNotThrow(McpGuard.EnsureNotPlaying);
        }
    }
}
