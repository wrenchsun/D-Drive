using System;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.Mcp.Tools;
using NUnit.Framework;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §5.1 MCP-7(2026-10-07) — ツール定義は毎ターン送られるので、数と説明文の長さに上限を置く。
    // ddrive_* は 20 個以内(MCP-7 時点でちょうど 20)・説明文は 80 文字以内・Group を明示する。
    public class McpToolBudgetTests
    {
        public const int MaxTools = 20;

        private static List<McpToolAttribute> DDriveTools()
        {
            var result = new List<McpToolAttribute>();
            foreach (var type in typeof(DDrivePreviewTools).Assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                {
                    var attr = (McpToolAttribute)Attribute.GetCustomAttribute(method, typeof(McpToolAttribute));
                    if (attr != null && attr.Name.StartsWith("ddrive_", StringComparison.Ordinal))
                    {
                        result.Add(attr);
                    }
                }
            }

            return result;
        }

        [Test]
        public void ToolCount_IsWithinBudget()
        {
            var tools = DDriveTools();
            Assert.LessOrEqual(tools.Count, MaxTools, "ddrive_* は 20 個以内(§5.1)。増やすなら既存ツールの引数に足す");
            Assert.AreEqual(MaxTools, tools.Count, "MCP-7 完了時点は 20 個(増減したらこの値と docs/1002 §5.1 を更新)");
        }

        [Test]
        public void EveryTool_HasShortDescriptionAndGroup()
        {
            foreach (var tool in DDriveTools())
            {
                Assert.LessOrEqual(tool.Description.Length, 80, $"{tool.Name} の説明が 80 文字を超えている");
                Assert.IsFalse(string.IsNullOrEmpty(tool.Group), $"{tool.Name} に Group が無い");
            }
        }

        [Test]
        public void ToolNames_AreUnique()
        {
            var seen = new HashSet<string>();
            foreach (var tool in DDriveTools())
            {
                Assert.IsTrue(seen.Add(tool.Name), $"{tool.Name} が重複している");
            }
        }
    }
}
