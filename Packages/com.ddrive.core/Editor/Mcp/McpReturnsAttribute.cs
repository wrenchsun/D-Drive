using System;

namespace DDrive.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §7 MCP-9(2026-10-07) — ツールが返す JSON の「上位キー」を契約として明示する。
    // 状況によって出ないキー(preview 時だけ・エラー時だけ等)も含めた和集合を書く。
    // McpToolsSnapshotBuilder が mcp-tools.txt に出力し、キーの削除・改名は MAJOR([42] §5.14 E-21)。追加は MINOR。
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class McpReturnsAttribute : Attribute
    {
        public string[] Keys { get; }

        public McpReturnsAttribute(params string[] keys)
        {
            Keys = keys ?? Array.Empty<string>();
        }
    }
}
