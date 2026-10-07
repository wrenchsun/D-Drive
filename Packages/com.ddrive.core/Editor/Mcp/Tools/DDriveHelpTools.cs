using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Menu;
using DDrive.Foundation.Identity;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Editor.Mcp.Tools
{
    // [1002_ddrive_mcp.md] §4.1 / §5.4 MCP-2(2026-10-07) — ddrive_help(AI 向けの短いカード)。
    // CLAUDE.md §0 や各設計 doc を AI が読む代わりに、必要な 1 枚だけを返してトークンを減らす。
    //  - rules        : Editor/Mcp/Cards/rules.md(静的カード)
    //  - types        : AssetType × Data クラス × ID 定数クラス × ファイル接頭辞(反射で生成)+ 読み取り専用欄
    //  - menu         : Tools/D-Drive 配下の [MenuItem] 一覧(反射で生成。DDriveMenu.Root 以降のパスのみ)
    //  - tool:<name>  : [McpTool]/[McpArg] から生成(引数・必須・例)
    //  - validation:<code> : Editor/Mcp/Cards/validation.md の `## <CODE>` 節
    // カードの正本は Editor/Mcp/Cards/(Q-10 の変更。docs/1002 §5.4)。パッケージの場所は PackageInfo から引くので
    // 埋め込み・git URL(Library/PackageCache)のどちらでも動く。
    public static class DDriveHelpTools
    {
        public const string CardRules = "rules.md";
        public const string CardValidation = "validation.md";

        private const string TopicList = "rules, types, menu, tool:<name>, validation:<code>";

        [McpTool(
            "ddrive_help",
            "短い案内カード。topic=rules|types|menu|tool:<name>|validation:<code>",
            Idempotency = McpIdempotency.Safe,
            Group = "diagnostics",
            Examples = new[] { "{\"topic\":\"validation:DD-ADDR-MISSING\"}" })]
        public static JObject Help(
            [McpArg("topic", "rules|types|menu|tool:名|validation:コード", Required = true)]
            string topic,
            [McpArg("max_chars", "返す最大文字数(既定 4000)")]
            int max_chars = McpGuard.DefaultMaxChars)
        {
            return McpGuard.Run(() =>
            {
                var key = (topic ?? string.Empty).Trim();
                var text = Resolve(key);
                var (cut, truncated) = McpGuard.Truncate(text, max_chars);
                return McpJson.Obj(("topic", key), ("text", cut), ("truncated", truncated));
            });
        }

        private static string Resolve(string topic)
        {
            if (topic.Length == 0)
            {
                throw UnknownTopic(topic);
            }

            switch (topic.ToLowerInvariant())
            {
                case "rules":
                    return ReadCard(CardRules);
                case "types":
                    return BuildTypes();
                case "menu":
                    return BuildMenu();
                case "validation":
                    return ListValidationCodes();
            }

            var colon = topic.IndexOf(':');
            if (colon > 0)
            {
                var head = topic.Substring(0, colon).ToLowerInvariant();
                var arg = topic.Substring(colon + 1).Trim();
                if (head == "tool")
                {
                    return BuildToolCard(arg);
                }

                if (head == "validation")
                {
                    return BuildValidationCard(arg);
                }
            }

            throw UnknownTopic(topic);
        }

        private static McpToolError UnknownTopic(string topic) =>
            new McpToolError(McpGuard.CodeInvalidParams, $"未知の topic '{topic}'。使えるもの: {TopicList}");

        // ── カード読み込み ──

        // カードのフォルダ。PackageInfo の resolvedPath(埋め込み / git URL どちらも)を優先し、
        // 取れなければプロジェクト直下の Packages/com.ddrive.core を試す。
        public static string CardsDirectory()
        {
            string root = null;
            try
            {
                root = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DDriveHelpTools).Assembly)?.resolvedPath;
            }
            catch (Exception)
            {
                // 下のフォールバックへ。
            }

            if (string.IsNullOrEmpty(root))
            {
                root = Path.GetFullPath("Packages/com.ddrive.core");
            }

            return Path.Combine(root, "Editor", "Mcp", "Cards");
        }

        public static string ReadCard(string fileName)
        {
            var path = Path.Combine(CardsDirectory(), fileName);
            if (!File.Exists(path))
            {
                throw new McpToolError(McpGuard.CodeException, $"カード '{fileName}' が見つかりません");
            }

            return File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd();
        }

        // ── validation ──

        private static List<(string code, string body)> ParseValidationCard()
        {
            var result = new List<(string, string)>();
            string code = null;
            var body = new StringBuilder();
            foreach (var line in ReadCard(CardValidation).Split('\n'))
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (code != null)
                    {
                        result.Add((code, body.ToString().Trim()));
                    }

                    code = line.Substring(3).Trim();
                    body.Clear();
                    continue;
                }

                if (code != null)
                {
                    body.Append(line).Append('\n');
                }
            }

            if (code != null)
            {
                result.Add((code, body.ToString().Trim()));
            }

            return result;
        }

        private static string BuildValidationCard(string code)
        {
            foreach (var (c, body) in ParseValidationCard())
            {
                if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase))
                {
                    return c + "\n" + body;
                }
            }

            throw new McpToolError(
                McpGuard.CodeInvalidParams,
                $"カードに無い code '{code}'。topic=validation で一覧、詳しくは ddrive_validate の msg を参照");
        }

        private static string ListValidationCodes()
        {
            var sb = new StringBuilder("validation:<code> の code 一覧\n");
            var first = true;
            foreach (var (c, _) in ParseValidationCard())
            {
                sb.Append(first ? string.Empty : ", ").Append(c);
                first = false;
            }

            return sb.ToString();
        }

        // ── types ──

        private static string BuildTypes()
        {
            var dataClasses = new Dictionary<AssetType, List<(string data, string idClass)>>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name;
                if (name.StartsWith("DDrive.Tests", StringComparison.Ordinal))
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }

                if (types == null)
                {
                    continue;
                }

                foreach (var t in types)
                {
                    var attr = t?.GetCustomAttribute<AssetIdDefinitionAttribute>(false);
                    if (attr == null)
                    {
                        continue;
                    }

                    if (!dataClasses.TryGetValue(attr.Type, out var list))
                    {
                        list = new List<(string, string)>();
                        dataClasses[attr.Type] = list;
                    }

                    list.Add((t.Name, attr.ConstantsClassName));
                }
            }

            var sb = new StringBuilder("種別 / Data クラス / ID 定数 / ファイル接頭辞\n");
            foreach (AssetType type in Enum.GetValues(typeof(AssetType)))
            {
                if (type == AssetType.None)
                {
                    continue;
                }

                sb.Append(type).Append(' ');
                if (dataClasses.TryGetValue(type, out var list))
                {
                    list.Sort((a, b) => string.CompareOrdinal(a.data, b.data));
                    sb.Append(string.Join("|", list.ConvertAll(x => x.data)))
                        .Append(' ')
                        .Append(string.Join("|", list.ConvertAll(x => x.idClass)));
                }
                else
                {
                    sb.Append("- -");
                }

                sb.Append(' ').Append(AssetNamingService.GetTypePrefix(type)).Append('\n');
            }

            sb.Append("読み取り専用欄: ").Append(string.Join(", ", McpGuard.ReadOnlyFields));
            return sb.ToString();
        }

        // ── menu ──

        private static string BuildMenu()
        {
            var root = DDriveMenu.Root;
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var method in TypeCache.GetMethodsWithAttribute<MenuItem>())
            {
                foreach (var attr in method.GetCustomAttributes(typeof(MenuItem), false))
                {
                    var item = (MenuItem)attr;
                    if (item.validate || item.menuItem == null || !item.menuItem.StartsWith(root, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // 末尾のショートカット指定(" %#g" 等)は表示パスではないので落とす。
                    var path = Regex.Replace(item.menuItem.Substring(root.Length), @"\s[%#&_]\S*$", string.Empty);
                    paths.Add(path);
                }
            }

            var sb = new StringBuilder("Tools/D-Drive/ 以下のメニュー\n");
            foreach (var p in paths)
            {
                sb.Append(p).Append('\n');
            }

            return sb.ToString().TrimEnd();
        }

        // ── tool ──

        private static string BuildToolCard(string name)
        {
            var known = new List<string>();
            foreach (var type in typeof(McpGuard).Assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    var tool = method.GetCustomAttribute<McpToolAttribute>();
                    if (tool == null)
                    {
                        continue;
                    }

                    known.Add(tool.Name);
                    if (string.Equals(tool.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return DescribeTool(tool, method);
                    }
                }
            }

            known.Sort(StringComparer.Ordinal);
            throw new McpToolError(
                McpGuard.CodeInvalidParams,
                $"未知のツール '{name}'。ある: {string.Join(", ", known)}");
        }

        private static string DescribeTool(McpToolAttribute tool, MethodInfo method)
        {
            var sb = new StringBuilder();
            sb.Append(tool.Name).Append(" [").Append(tool.Group)
                .Append(tool.Idempotency == McpIdempotency.Safe ? ", 読み取り" : ", 書き込み/副作用あり").Append("]\n");
            sb.Append(tool.Description).Append('\n');
            var parameters = method.GetParameters();
            if (parameters.Length > 0)
            {
                sb.Append("引数:\n");
                foreach (var p in parameters)
                {
                    var arg = p.GetCustomAttribute<McpArgAttribute>();
                    var argName = !string.IsNullOrEmpty(arg?.Name) ? arg.Name : p.Name;
                    var required = (arg != null && arg.Required) || !p.HasDefaultValue;
                    sb.Append("  ").Append(argName).Append(" (").Append(TypeLabel(p.ParameterType))
                        .Append(required ? ", 必須" : ", 任意").Append("): ")
                        .Append(arg?.Description ?? string.Empty).Append('\n');
                }
            }

            if (tool.Examples != null && tool.Examples.Length > 0)
            {
                sb.Append("例: ").Append(tool.Examples[0]).Append('\n');
            }

            return sb.ToString().TrimEnd();
        }

        private static string TypeLabel(Type type)
        {
            if (type == typeof(string))
            {
                return "string";
            }

            if (type == typeof(int))
            {
                return "int";
            }

            if (type == typeof(bool))
            {
                return "bool";
            }

            if (type == typeof(float) || type == typeof(double))
            {
                return "number";
            }

            return type.Name;
        }
    }
}
