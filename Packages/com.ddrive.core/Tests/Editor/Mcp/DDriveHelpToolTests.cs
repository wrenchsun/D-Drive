using System;
using DDrive.Editor.Mcp;
using DDrive.Editor.Mcp.Tools;
using DDrive.Foundation.Identity;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.1 / §5.4 MCP-2(2026-10-07) — ddrive_help のカード。
    public class DDriveHelpToolTests
    {
        [Test]
        public void Rules_IsNonEmptyAndShort()
        {
            var r = DDriveHelpTools.Help("rules");
            Assert.IsNull(r["error"]);
            var text = (string)r["text"];
            Assert.IsFalse(string.IsNullOrWhiteSpace(text));
            Assert.LessOrEqual(text.Length, 4000);
            Assert.LessOrEqual(text.Length, 600, "rules カードは 600 文字以内(§5.4)");
            StringAssert.Contains("ddrive_status", text);
            Assert.IsNull(r["truncated"]);
        }

        [Test]
        public void Types_ContainsEveryAssetTypeExceptNone()
        {
            var text = (string)DDriveHelpTools.Help("types", 100000)["text"];
            foreach (AssetType type in Enum.GetValues(typeof(AssetType)))
            {
                if (type == AssetType.None)
                {
                    continue;
                }

                StringAssert.Contains(type.ToString(), text);
            }

            StringAssert.Contains("SeData", text);
            StringAssert.Contains("SEID", text);
            foreach (var field in McpGuard.ReadOnlyFields)
            {
                StringAssert.Contains(field, text);
            }
        }

        [Test]
        public void Menu_ListsValidationRunAll()
        {
            var text = (string)DDriveHelpTools.Help("menu", 100000)["text"];
            StringAssert.Contains("Validation/Run All", text);
        }

        [Test]
        public void Tool_Status_ListsSectionsArgument()
        {
            var r = DDriveHelpTools.Help("tool:ddrive_status");
            Assert.IsNull(r["error"]);
            var text = (string)r["text"];
            StringAssert.Contains("ddrive_status", text);
            StringAssert.Contains("sections", text);
        }

        [Test]
        public void Tool_Help_ShowsRequiredTopicAndExample()
        {
            var text = (string)DDriveHelpTools.Help("tool:ddrive_help")["text"];
            StringAssert.Contains("topic", text);
            StringAssert.Contains("必須", text);
            StringAssert.Contains("例:", text);
        }

        [Test]
        public void Tool_Unknown_IsInvalidParams()
        {
            var r = DDriveHelpTools.Help("tool:nope");
            Assert.AreEqual(McpGuard.CodeInvalidParams, (string)r["error"]["code"]);
        }

        [Test]
        public void Validation_KnownCode_ReturnsCardText()
        {
            var r = DDriveHelpTools.Help("validation:DD-ADDR-MISSING");
            Assert.IsNull(r["error"]);
            var text = (string)r["text"];
            StringAssert.StartsWith("DD-ADDR-MISSING", text);
            StringAssert.Contains("Addressables", text);
        }

        [Test]
        public void Validation_CodeIsCaseInsensitive()
        {
            Assert.IsNull(DDriveHelpTools.Help("validation:dd-addr-missing")["error"]);
        }

        [Test]
        public void Validation_UnknownCode_IsInvalidParams()
        {
            var r = DDriveHelpTools.Help("validation:DD-NOPE");
            Assert.AreEqual(McpGuard.CodeInvalidParams, (string)r["error"]["code"]);
        }

        [Test]
        public void Validation_Bare_ListsCodes()
        {
            var text = (string)DDriveHelpTools.Help("validation", 100000)["text"];
            StringAssert.Contains("DD-ADDR-MISSING", text);
            StringAssert.Contains("DD-CANVAS-EMBED-ROOT", text);
        }

        [Test]
        public void UnknownTopic_IsInvalidParams_WithTopicList()
        {
            var r = DDriveHelpTools.Help("bogus");
            Assert.AreEqual(McpGuard.CodeInvalidParams, (string)r["error"]["code"]);
            StringAssert.Contains("rules", (string)r["error"]["msg"]);
        }

        [Test]
        public void EmptyTopic_IsInvalidParams()
        {
            Assert.AreEqual(McpGuard.CodeInvalidParams, (string)DDriveHelpTools.Help("")["error"]["code"]);
            Assert.AreEqual(McpGuard.CodeInvalidParams, (string)DDriveHelpTools.Help(null)["error"]["code"]);
        }

        [Test]
        public void MaxChars_TruncatesAndFlags()
        {
            var r = DDriveHelpTools.Help("types", 50);
            Assert.AreEqual(true, (bool)r["truncated"]);
            Assert.AreEqual(50, ((string)r["text"]).Length);
        }

        [Test]
        public void ValidationCard_HasAtLeast25Codes()
        {
            var text = DDriveHelpTools.ReadCard(DDriveHelpTools.CardValidation);
            var count = 0;
            foreach (var line in text.Split('\n'))
            {
                if (line.StartsWith("## DD-", StringComparison.Ordinal))
                {
                    count++;
                }
            }

            Assert.GreaterOrEqual(count, 25);
        }
    }
}
