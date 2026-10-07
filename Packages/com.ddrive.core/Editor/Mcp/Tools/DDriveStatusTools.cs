using System;
using System.Collections.Generic;
using DDrive.Editor.Settings;
using DDrive.Runtime;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Editor.Mcp.Tools
{
    // [1002_ddrive_mcp.md] §4.1 MCP-1(2026-10-07) — ddrive_status(最小版)。
    // MCP-1 の範囲は version / compile / mcp の 3 セクション。
    // TODO(MCP-2): tests / validation / migration / addressables セクションと schema を足す。
    public static class DDriveStatusTools
    {
        private static readonly string[] AllSections = { "version", "compile", "mcp" };

        [McpTool(
            "ddrive_status",
            "D-Drive の状態を 1 回で返す(version/compile/mcp)。sections で絞る",
            Idempotency = McpIdempotency.Safe,
            Group = "diagnostics")]
        public static JObject Status(
            [McpArg("sections", "version,compile,mcp のカンマ区切り。省略で全部")]
            string sections = null)
        {
            return McpGuard.Run(() =>
            {
                var wanted = ParseSections(sections);
                var result = new JObject();

                if (wanted.Contains("version"))
                {
                    result["version"] = DDriveVersion.Value;
                }

                if (wanted.Contains("compile"))
                {
                    result["compile"] = McpJson.Obj(("ok", McpJson.Keep(!EditorUtility.scriptCompilationFailed)));
                }

                if (wanted.Contains("mcp"))
                {
                    result["mcp"] = McpJson.Obj(
                        ("writeEnabled", McpJson.Keep(DDriveProjectSettings.instance.McpAllowWrite)),
                        ("playing", McpGuard.IsPlaying),
                        ("project", Application.productName));
                }

                return result;
            });
        }

        private static HashSet<string> ParseSections(string sections)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(sections))
            {
                set.UnionWith(AllSections);
                return set;
            }

            foreach (var part in sections.Split(','))
            {
                var name = part.Trim().ToLowerInvariant();
                if (name.Length == 0)
                {
                    continue;
                }

                if (Array.IndexOf(AllSections, name) < 0)
                {
                    throw new McpToolError(McpGuard.CodeInvalidParams, $"未知の section '{name}'(version,compile,mcp)");
                }

                set.Add(name);
            }

            return set;
        }
    }
}
