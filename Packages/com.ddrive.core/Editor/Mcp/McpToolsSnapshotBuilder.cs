using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §7 MCP-9(2026-10-07) / [42_distribution.md] §5.14 E-21 — ddrive_* ツールの契約スナップショット。
    // 1 ツール 1 行(名前順):
    //   ddrive_status|group=diagnostics|destructive=false|args=sections:string?|returns=version,compile,...
    // args = 引数名:型(任意なら末尾 ?。名前順)、returns = [McpReturns] の上位キー(名前順)。
    // 契約 = ツール名・引数名と型・必須か・Destructive か・返り値の上位キー。削除・改名・型変更は MAJOR、追加のみ MINOR。
    // CompatSnapshotMenu(DDrive.Editor)は DDrive.Editor.Mcp を参照できないため、型名でリフレクション呼び出しする。
    public static class McpToolsSnapshotBuilder
    {
        public static string Build()
        {
            var lines = new List<string>();
            foreach (var type in typeof(McpToolsSnapshotBuilder).Assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var tool = (McpToolAttribute)Attribute.GetCustomAttribute(method, typeof(McpToolAttribute));
                    if (tool != null && tool.Name.StartsWith("ddrive_", StringComparison.Ordinal))
                    {
                        lines.Add(BuildLine(tool, method));
                    }
                }
            }

            lines.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                sb.Append(line).Append('\n');
            }

            return sb.ToString();
        }

        public static string BuildLine(McpToolAttribute tool, MethodInfo method)
        {
            var args = new List<string>();
            foreach (var p in method.GetParameters())
            {
                var arg = (McpArgAttribute)Attribute.GetCustomAttribute(p, typeof(McpArgAttribute));
                var name = !string.IsNullOrEmpty(arg?.Name) ? arg.Name : p.Name;
                var optional = p.HasDefaultValue && !(arg != null && arg.Required);
                args.Add(name + ":" + TypeLabel(p.ParameterType) + (optional ? "?" : string.Empty));
            }

            args.Sort(StringComparer.Ordinal);

            var returns = new List<string>();
            var attr = (McpReturnsAttribute)Attribute.GetCustomAttribute(method, typeof(McpReturnsAttribute));
            if (attr != null)
            {
                returns.AddRange(attr.Keys);
            }

            returns.Sort(StringComparer.Ordinal);

            return tool.Name
                + "|group=" + (tool.Group ?? string.Empty)
                + "|destructive=" + (tool.Destructive ? "true" : "false")
                + "|args=" + string.Join(",", args)
                + "|returns=" + string.Join(",", returns);
        }

        private static string TypeLabel(Type type)
        {
            var inner = Nullable.GetUnderlyingType(type) ?? type;
            if (inner == typeof(string)) return "string";
            if (inner == typeof(bool)) return "bool";
            if (inner == typeof(int)) return "int";
            if (inner == typeof(long)) return "long";
            if (inner == typeof(double)) return "double";
            if (inner == typeof(float)) return "float";
            return inner.Name;
        }
    }
}
