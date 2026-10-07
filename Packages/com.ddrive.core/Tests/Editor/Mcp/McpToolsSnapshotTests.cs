using DDrive.Editor.Compat;
using DDrive.Editor.Mcp;
using DDrive.Tests.Editor.Compat;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §7 MCP-9(2026-10-07) / [42_distribution.md] §5.14 E-21 — ddrive_* ツールの契約スナップショット。
    // ツール名・引数名と型・必須か・Destructive か・返り値の上位キーを固定する。
    // 他の Compat テスト(EditorContractSnapshotTests 等)と同じ規則 = 完全一致(行の削除・改名・型変更だけでなく追加も、
    // スナップショットの意図的な更新を促すため赤にする)。削除・改名・型変更は MAJOR(§5.12)、追加は MINOR。
    public class McpToolsSnapshotTests
    {
        private const string Hint =
            "ddrive_* ツールの名前・引数名と型・必須・Destructive・返り値の上位キー・ddrive_help の topic 名は契約([42] §5.14 E-21)。" +
            "削除・改名・型変更は MAJOR(ユーザー承認が必須)、追加のみ MINOR。返り値のキーは [McpReturns] で明示する。";

        [Test]
        public void MatchesGolden()
        {
            var actual = McpToolsSnapshotBuilder.Build();
            CompatGoldenAssert.AssertMatches(CompatSnapshotPaths.McpTools, actual, Hint);
        }

        [Test]
        public void EveryTool_DeclaresReturnKeys()
        {
            foreach (var line in McpToolsSnapshotBuilder.Build().Split('\n'))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                StringAssert.DoesNotEndWith("|returns=", line, "[McpReturns] が無い/空: " + line);
            }
        }

        [Test]
        public void Build_IsSortedAndOneLinePerTool()
        {
            var lines = McpToolsSnapshotBuilder.Build().TrimEnd('\n').Split('\n');
            Assert.AreEqual(McpToolBudgetTests.MaxTools, lines.Length);
            var sorted = (string[])lines.Clone();
            System.Array.Sort(sorted, System.StringComparer.Ordinal);
            CollectionAssert.AreEqual(sorted, lines);
        }
    }
}
