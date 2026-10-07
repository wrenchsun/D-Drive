using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using DDrive.Editor.Settings;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Editor.Mcp.Tools
{
    // [1002_ddrive_mcp.md] §4.3 / §5.2 MCP-5(2026-10-07) — ddrive_validate / ddrive_validate_fix / ddrive_forbidden_api。
    // ツールはアダプタに徹する。検査そのものは CI.RunValidation / DataValidationRunner / ProjectWideValidationFixes /
    // ForbiddenApiScanner を呼ぶだけで、ここは「要約する・ページに切る・文字数に収める」だけを持つ。
    //  - 件数・byCode・items の組み立ては ValidationSummary(純粋関数。テストから合成した ValidationReport で検証する)
    //  - items が max_chars に収まらないときは、JSON を途中で切らず末尾の項目を丸ごと落とし、next を落とした先頭にする
    public static class DDriveValidationTools
    {
        // ── ddrive_validate ──

        [McpTool(
            "ddrive_validate",
            "D-Drive の検査(Validation)を実行して要約を返す。既定は件数と Code 別の表だけ。指摘の本文は detail=errors/all で",
            Idempotency = McpIdempotency.Safe,
            Group = "diagnostics")]
        public static JObject Validate(
            [McpArg("scope", "all(既定) / project(全体の指摘のみ) / type:<種別名> / asset:<種別名>:<id>")]
            string scope = null,
            [McpArg("detail", "summary(既定) / errors(Error の本文も) / all(全部の本文も)")]
            string detail = null,
            [McpArg("codes", "カンマ区切りの Code で絞る(Code 無しの指摘は (none))")]
            string codes = null,
            [McpArg("limit", "items の最大件数(既定 50、最大 200)")]
            int limit = 0,
            [McpArg("cursor", "続き(返り値の next をそのまま渡す)")]
            string cursor = null,
            [McpArg("max_chars", "返り値の最大文字数(既定 4000)")]
            int max_chars = 0)
        {
            return McpGuard.Run(() =>
            {
                var parsed = ValidationScope.Parse(scope);
                var mode = ValidationSummary.ParseDetail(detail);
                var codeSet = ValidationSummary.ParseCodes(codes);

                var reports = RunScope(parsed, out var allReports);
                if (allReports != null)
                {
                    // 全体を走らせたときだけ ddrive_status 用の要約を更新する(絞った結果で上書きしない)。
                    McpValidationCache.Record(allReports);
                }

                var summary = ValidationSummary.Build(reports, codeSet);
                return summary.ToJson(parsed.Text, mode, cursor, limit, max_chars);
            });
        }

        // scope ごとの実行。全体(all)を走らせた場合は allReports に未加工の全結果を返す(キャッシュ用)。
        //  - all / project / type : CI.RunValidation(true) を 1 回走らせて絞る。type は Asset の種別で絞り込み。
        //    DataValidationRunner.Run を種別内の全アセットに回す方式は、重複 ID・参照先の検査に要る「全アセットの文脈」が
        //    欠けて Run All と結果がずれるため採らなかった。
        //  - asset : DataValidationRunner.Run(asset, false)(その 1 アセットだけ。安い)
        private static List<ValidationReport> RunScope(ValidationScope scope, out IReadOnlyList<ValidationReport> allReports)
        {
            allReports = null;
            var result = new List<ValidationReport>();
            switch (scope.Kind)
            {
                case ValidationScope.ScopeKind.Asset:
                {
                    var asset = FindAsset(scope.Type, scope.Id);
                    foreach (var r in DataValidationRunner.Run(asset, false))
                    {
                        result.Add(new ValidationReport(asset, r));
                    }

                    return result;
                }

                case ValidationScope.ScopeKind.Type:
                {
                    foreach (var report in CI.RunValidation(true))
                    {
                        if (report.Asset != null && ValidationSummary.ResolveType(report.Asset) == scope.Type)
                        {
                            result.Add(report);
                        }
                    }

                    return result;
                }

                case ValidationScope.ScopeKind.Project:
                {
                    foreach (var report in CI.RunValidation(true))
                    {
                        if (report.Asset == null)
                        {
                            result.Add(report);
                        }
                    }

                    return result;
                }

                default:
                {
                    var all = CI.RunValidation(true);
                    allReports = all;
                    result.AddRange(all);
                    return result;
                }
            }
        }

        private static AssetDataBase FindAsset(AssetType type, ulong id)
        {
            foreach (var guid in DDrive.Editor.AssetSearch.FindAssets("t:" + nameof(AssetDataBase)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.Id == id && ValidationSummary.ResolveType(asset) == type)
                {
                    return asset;
                }
            }

            throw new McpToolError(McpGuard.CodeInvalidParams, $"asset:{type}:{id} のアセットが見つかりません");
        }

        // ── ddrive_validate_fix ──

        // confirm / dry_run は isuzu の ToolInvoker が Destructive の呼び出しにだけ注入し、メソッドには渡さない
        // (confirm 無し → confirmation_required、dry_run=true → メソッドを呼ばず「実行予定」の定型だけ返す)。
        // そのため「どの検査をいくつ直すか」の読み取り専用の確認は、メソッドの引数 preview=true(それでも confirm は要る)と、
        // ddrive_validate の fixable で行う。
        [McpTool(
            "ddrive_validate_fix",
            "全体の指摘のうち FixAction 付きだけ直す(Undo 不可)。先に preview=true で確認",
            Destructive = true,
            UndoGroup = "D-Drive MCP: 検査の修正",
            Group = "authoring")]
        public static JObject ValidateFix(
            [McpArg("codes", "直す Code のカンマ区切り。省略で FixAction 付きを全部")]
            string codes = null,
            [McpArg("preview", "true なら何も書かず wouldApply(直す予定の Code と件数)だけ返す")]
            bool preview = false)
        {
            return McpGuard.Run(() =>
            {
                McpGuard.EnsureCanWrite();
                var codeSet = ValidationSummary.ParseCodes(codes);

                var reports = CI.RunValidation(true);
                var fixable = ProjectWideValidationFixes.FindFixable(reports);
                var selected = new List<ValidationReport>();
                foreach (var report in fixable)
                {
                    if (ValidationSummary.CodeMatches(codeSet, report.Result.Code))
                    {
                        selected.Add(report);
                    }
                }

                if (preview)
                {
                    return McpJson.Obj(
                        ("wouldApply", McpJson.Keep(CountByCode(selected))),
                        ("skipped", BuildNoFixSkipped(codeSet, selected)));
                }

                var applied = new List<ValidationReport>();
                var failed = new List<ValidationReport>();
                foreach (var report in ProjectWideValidationFixes.OrderForApply(selected))
                {
                    // 1 件ずつ回して、どの Code の修正が失敗したか(Apply は例外を警告に畳む)を数える。
                    if (ProjectWideValidationFixes.Apply(new[] { report }) > 0)
                    {
                        applied.Add(report);
                    }
                    else
                    {
                        failed.Add(report);
                    }
                }

                var skipped = BuildNoFixSkipped(codeSet, selected);
                foreach (var row in CountByCode(failed))
                {
                    ((JObject)row)["reason"] = "fix_failed";
                    skipped.Add(row);
                }

                var after = CI.RunValidation(true);
                var counts = McpValidationCache.Record(after);
                return McpJson.Obj(
                    ("applied", McpJson.Keep(CountByCode(applied))),
                    ("skipped", skipped),
                    ("after", new JObject { ["errors"] = counts["errors"], ["warnings"] = counts["warnings"] }));
            });
        }

        // 指定された Code のうち、直せる指摘が 1 つも無かったもの(typo や Asset 単位の指摘)を理由付きで返す。
        private static JArray BuildNoFixSkipped(HashSet<string> codeSet, List<ValidationReport> selected)
        {
            var skipped = new JArray();
            if (codeSet == null)
            {
                return skipped;
            }

            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var report in selected)
            {
                found.Add(ValidationSummary.CodeKey(report.Result.Code));
            }

            var missing = new List<string>(codeSet);
            missing.Sort(StringComparer.Ordinal);
            foreach (var code in missing)
            {
                if (!found.Contains(code))
                {
                    skipped.Add(new JObject { ["code"] = code, ["count"] = 0, ["reason"] = "no_fixable" });
                }
            }

            return skipped;
        }

        public static JArray CountByCode(List<ValidationReport> reports)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var report in reports)
            {
                var code = ValidationSummary.CodeKey(report.Result.Code);
                if (!counts.ContainsKey(code))
                {
                    order.Add(code);
                    counts[code] = 0;
                }

                counts[code]++;
            }

            var array = new JArray();
            foreach (var code in order)
            {
                array.Add(new JObject { ["code"] = code, ["count"] = counts[code] });
            }

            return array;
        }

        // ── ddrive_forbidden_api ──

        [McpTool(
            "ddrive_forbidden_api",
            "禁止 API の静的検査。規則別の件数。detail=all で file:line も",
            Idempotency = McpIdempotency.Safe,
            Group = "diagnostics")]
        public static JObject ForbiddenApi(
            [McpArg("root", "走査するフォルダ。省略で CI.ResolveForbiddenApiScanRoot()")]
            string root = null,
            [McpArg("detail", "summary(既定) / all(当たりの file:line も)")]
            string detail = null,
            [McpArg("limit", "items の最大件数(既定 50、最大 200)")]
            int limit = 0,
            [McpArg("cursor", "続き(返り値の next をそのまま渡す)")]
            string cursor = null,
            [McpArg("max_chars", "返り値の最大文字数(既定 4000)")]
            int max_chars = 0)
        {
            return McpGuard.Run(() =>
            {
                var withItems = ValidationSummary.ParseDetail(detail, allowErrors: false) != ValidationSummary.DetailMode.Summary;
                var scanRoot = string.IsNullOrWhiteSpace(root) ? CI.ResolveForbiddenApiScanRoot() : root.Trim();
                var scan = ForbiddenApiScanner.ScanDetailed(scanRoot, DDriveProjectSettings.instance.ForbiddenApiAllowEntries);

                var rows = new List<ForbiddenApiSummary.Row>();
                foreach (var v in scan.Violations)
                {
                    rows.Add(new ForbiddenApiSummary.Row(v.RuleName, v.FilePath, v.Line, v.Message, isNotice: false));
                }

                foreach (var n in scan.Notices)
                {
                    rows.Add(new ForbiddenApiSummary.Row(n.Code, n.FilePath, n.Line, n.Message, isNotice: true));
                }

                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                return ForbiddenApiSummary.ToJson(
                    ForbiddenApiSummary.RelativizePath(scanRoot, projectRoot), rows, scan.Violations.Count, scan.Notices.Count,
                    withItems, cursor, limit, max_chars, projectRoot);
            });
        }
    }

    // scope 文字列の解釈(all / project / type:<T> / asset:<T>:<id>)。不正は invalid_params。
    public readonly struct ValidationScope
    {
        public enum ScopeKind
        {
            All,
            Project,
            Type,
            Asset,
        }

        public readonly ScopeKind Kind;
        public readonly AssetType Type;
        public readonly ulong Id;
        public readonly string Text;

        private ValidationScope(ScopeKind kind, AssetType type, ulong id, string text)
        {
            Kind = kind;
            Type = type;
            Id = id;
            Text = text;
        }

        public static ValidationScope Parse(string scope)
        {
            var text = string.IsNullOrWhiteSpace(scope) ? "all" : scope.Trim();
            var lower = text.ToLowerInvariant();
            if (lower == "all")
            {
                return new ValidationScope(ScopeKind.All, AssetType.None, 0, "all");
            }

            if (lower == "project")
            {
                return new ValidationScope(ScopeKind.Project, AssetType.None, 0, "project");
            }

            var parts = text.Split(':');
            if (parts.Length == 2 && string.Equals(parts[0], "type", StringComparison.OrdinalIgnoreCase))
            {
                var type = ParseType(parts[1]);
                return new ValidationScope(ScopeKind.Type, type, 0, "type:" + type);
            }

            if (parts.Length == 3 && string.Equals(parts[0], "asset", StringComparison.OrdinalIgnoreCase))
            {
                var type = ParseType(parts[1]);
                var idText = parts[2].Trim();
                ulong id;
                var ok = idText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? ulong.TryParse(idText.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id)
                    : ulong.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out id);
                if (!ok)
                {
                    throw new McpToolError(McpGuard.CodeInvalidParams, $"scope の id '{idText}' が数値ではありません");
                }

                return new ValidationScope(ScopeKind.Asset, type, id, $"asset:{type}:{id}");
            }

            throw new McpToolError(
                McpGuard.CodeInvalidParams,
                $"scope '{text}' が不正です(all / project / type:<種別> / asset:<種別>:<id>)");
        }

        // 数値文字列("3")は Enum.TryParse が通してしまうので、定義済みの名前との一致だけを許す。
        private static AssetType ParseType(string name)
        {
            var trimmed = (name ?? string.Empty).Trim();
            foreach (var candidate in Enum.GetNames(typeof(AssetType)))
            {
                if (string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(candidate, nameof(AssetType.None), StringComparison.Ordinal))
                {
                    return (AssetType)Enum.Parse(typeof(AssetType), candidate);
                }
            }

            throw new McpToolError(McpGuard.CodeInvalidParams, $"種別 '{trimmed}' が不明です(ddrive_help topic=types で一覧)");
        }
    }

    // ValidationReport の集計と JSON 化。ここは純粋関数(Unity のアセットを読まない)なのでテストから合成データで検証できる。
    public sealed class ValidationSummary
    {
        public const string NoneCode = "(none)";
        public const int MsgMaxChars = 200;

        public enum DetailMode
        {
            Summary,
            Errors,
            All,
        }

        public readonly struct Row
        {
            public readonly string Code;
            public readonly ValidationSeverity Severity;
            public readonly AssetType Type;
            public readonly ulong Id;
            public readonly string Name;
            public readonly string Message;
            public readonly bool IsProject;

            public Row(string code, ValidationSeverity severity, AssetType type, ulong id, string name, string message, bool isProject)
            {
                Code = code;
                Severity = severity;
                Type = type;
                Id = id;
                Name = name;
                Message = message;
                IsProject = isProject;
            }
        }

        public readonly struct CodeCount
        {
            public readonly string Code;
            public readonly ValidationSeverity Severity;
            public readonly int Count;

            public CodeCount(string code, ValidationSeverity severity, int count)
            {
                Code = code;
                Severity = severity;
                Count = count;
            }
        }

        public int Errors;
        public int Warnings;
        public int Infos;
        public readonly List<CodeCount> ByCode = new();
        public readonly List<Row> Rows = new();
        public readonly List<CodeCount> Fixable = new();

        public static ValidationSummary Build(IReadOnlyList<ValidationReport> reports, HashSet<string> codes)
        {
            var summary = new ValidationSummary();
            var byCode = new Dictionary<(string, ValidationSeverity), int>();
            var fixable = new Dictionary<string, int>(StringComparer.Ordinal);
            if (reports == null)
            {
                return summary;
            }

            for (var i = 0; i < reports.Count; i++)
            {
                var report = reports[i];
                var result = report.Result;
                if (!CodeMatches(codes, result.Code))
                {
                    continue;
                }

                var code = CodeKey(result.Code);
                switch (result.Severity)
                {
                    case ValidationSeverity.Error:
                        summary.Errors++;
                        break;
                    case ValidationSeverity.Warning:
                        summary.Warnings++;
                        break;
                    default:
                        summary.Infos++;
                        break;
                }

                var key = (code, result.Severity);
                byCode[key] = byCode.TryGetValue(key, out var n) ? n + 1 : 1;

                var isProject = report.Asset == null;
                if (isProject && result.FixAction != null && result.Severity != ValidationSeverity.Info)
                {
                    fixable[code] = fixable.TryGetValue(code, out var f) ? f + 1 : 1;
                }

                summary.Rows.Add(new Row(
                    code,
                    result.Severity,
                    isProject ? AssetType.None : ResolveType(report.Asset),
                    isProject ? 0UL : report.Asset.Id,
                    isProject ? null : (string.IsNullOrEmpty(report.Asset.DisplayName) ? report.Asset.name : report.Asset.DisplayName),
                    result.Message,
                    isProject));
            }

            foreach (var kv in byCode)
            {
                summary.ByCode.Add(new CodeCount(kv.Key.Item1, kv.Key.Item2, kv.Value));
            }

            // Error → Warning → Info の順、同じ重大度では件数の多い順、最後は Code の辞書順(順序を安定させる)。
            summary.ByCode.Sort((a, b) =>
            {
                var s = SeverityRank(a.Severity).CompareTo(SeverityRank(b.Severity));
                if (s != 0)
                {
                    return s;
                }

                var c = b.Count.CompareTo(a.Count);
                return c != 0 ? c : string.CompareOrdinal(a.Code, b.Code);
            });

            foreach (var kv in fixable)
            {
                summary.Fixable.Add(new CodeCount(kv.Key, ValidationSeverity.Warning, kv.Value));
            }

            summary.Fixable.Sort((a, b) =>
            {
                var c = b.Count.CompareTo(a.Count);
                return c != 0 ? c : string.CompareOrdinal(a.Code, b.Code);
            });

            return summary;
        }

        private static int SeverityRank(ValidationSeverity s) => s == ValidationSeverity.Error ? 0 : s == ValidationSeverity.Warning ? 1 : 2;

        public static string SeverityName(ValidationSeverity s) => s == ValidationSeverity.Error ? "error" : s == ValidationSeverity.Warning ? "warning" : "info";

        public static string CodeKey(string code) => string.IsNullOrEmpty(code) ? NoneCode : code;

        // codes が null(未指定)なら全部通す。
        public static bool CodeMatches(HashSet<string> codes, string code) => codes == null || codes.Contains(CodeKey(code));

        public static HashSet<string> ParseCodes(string codes)
        {
            if (string.IsNullOrWhiteSpace(codes))
            {
                return null;
            }

            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in codes.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    set.Add(trimmed);
                }
            }

            return set.Count == 0 ? null : set;
        }

        public static DetailMode ParseDetail(string detail, bool allowErrors = true)
        {
            var d = string.IsNullOrWhiteSpace(detail) ? "summary" : detail.Trim().ToLowerInvariant();
            switch (d)
            {
                case "summary":
                    return DetailMode.Summary;
                case "errors" when allowErrors:
                    return DetailMode.Errors;
                case "all":
                    return DetailMode.All;
                default:
                    throw new McpToolError(
                        McpGuard.CodeInvalidParams,
                        allowErrors ? $"detail '{detail}' が不正です(summary / errors / all)" : $"detail '{detail}' が不正です(summary / all)");
            }
        }

        public static AssetType ResolveType(AssetDataBase asset)
            => asset == null ? AssetType.None : asset.GetType().GetCustomAttribute<AssetIdDefinitionAttribute>()?.Type ?? AssetType.None;

        public static string CutMessage(string message)
        {
            message ??= string.Empty;
            return message.Length <= MsgMaxChars ? message : message.Substring(0, MsgMaxChars);
        }

        public JObject ToJson(string scope, DetailMode mode, string cursor, int limit, int maxChars)
        {
            var byCode = new JArray();
            foreach (var c in ByCode)
            {
                byCode.Add(new JObject { ["code"] = c.Code, ["sev"] = SeverityName(c.Severity), ["count"] = c.Count });
            }

            var fixable = new JArray();
            foreach (var c in Fixable)
            {
                fixable.Add(new JObject { ["code"] = c.Code, ["count"] = c.Count });
            }

            var head = McpJson.Obj(
                ("scope", scope),
                ("errors", McpJson.Keep(Errors)),
                ("warnings", McpJson.Keep(Warnings)),
                ("infos", McpJson.Keep(Infos)),
                ("byCode", McpJson.Keep(byCode)),
                ("fixable", fixable));

            if (mode == DetailMode.Summary)
            {
                // byCode が極端に多く max_chars を超えるときは、件数だけに縮めて印を付ける(黙って切らない)。
                var limitChars = maxChars <= 0 ? McpGuard.DefaultMaxChars : maxChars;
                if (McpJson.Compact(head).Length <= limitChars)
                {
                    return head;
                }

                return new JObject { ["scope"] = scope, ["errors"] = Errors, ["warnings"] = Warnings, ["infos"] = Infos, ["truncated"] = true };
            }

            var selected = new List<Row>();
            foreach (var row in Rows)
            {
                if (mode == DetailMode.All || row.Severity == ValidationSeverity.Error)
                {
                    selected.Add(row);
                }
            }

            return ItemPaging.Fit(head, selected, cursor, limit, maxChars, MapRow);
        }

        public static JObject MapRow(Row row)
        {
            var obj = new JObject
            {
                ["code"] = row.Code,
                ["sev"] = SeverityName(row.Severity),
            };
            if (!row.IsProject)
            {
                obj["type"] = row.Type.ToString();
                obj["id"] = McpJson.FormatId(row.Id);
                obj["name"] = row.Name ?? string.Empty;
            }

            obj["msg"] = CutMessage(row.Message);
            return obj;
        }
    }

    // items のページ切り + 文字数への収め込み。JSON を途中で切らず、末尾の項目を丸ごと落とす。
    public static class ItemPaging
    {
        // head に items / next / truncated を足して返す。
        // 1) limit / cursor で McpJson.Page と同じ規則でページを切る  2) Compact が maxChars を超えるなら末尾の項目を落とし、
        // next を「落とした先頭の位置」に置いて truncated=true を付ける。
        public static JObject Fit<T>(
            JObject head, IReadOnlyList<T> rows, string cursor, int limit, int maxChars, Func<T, JObject> map)
        {
            if (maxChars <= 0)
            {
                maxChars = McpGuard.DefaultMaxChars;
            }

            var offset = 0;
            if (!string.IsNullOrEmpty(cursor) && (!int.TryParse(cursor, out offset) || offset < 0))
            {
                throw new McpToolError(McpGuard.CodeInvalidParams, $"cursor '{cursor}' が不正です");
            }

            var page = McpJson.Page(rows, cursor, limit, map);
            var items = (JArray)page["items"];
            var nextIndex = page["next"] != null ? int.Parse((string)page["next"]) : -1;

            var result = (JObject)head.DeepClone();
            // items / next / truncated 分の余白(キー名・括弧・カンマ・next の桁)。
            var budget = maxChars - McpJson.Compact(result).Length - 64;
            var kept = 0;
            var used = 0;
            for (var i = 0; i < items.Count; i++)
            {
                var len = McpJson.Compact((JObject)items[i]).Length + 1;
                if (used + len > budget)
                {
                    break;
                }

                used += len;
                kept++;
            }

            var truncated = kept < items.Count;
            if (truncated)
            {
                while (items.Count > kept)
                {
                    items.RemoveAt(items.Count - 1);
                }

                nextIndex = offset + kept;
            }

            result["items"] = items;
            if (nextIndex >= 0)
            {
                result["next"] = nextIndex.ToString();
            }

            if (truncated)
            {
                result["truncated"] = true;
            }

            return result;
        }
    }

    // ddrive_forbidden_api の集計。ForbiddenApiScanner の結果を Row に写してから渡す(純粋関数でテストできるように)。
    public static class ForbiddenApiSummary
    {
        public const string GlobalRule = "(scan)";

        public readonly struct Row
        {
            public readonly string Rule;
            public readonly string File;
            public readonly int Line;
            public readonly string Message;
            public readonly bool IsNotice;

            public Row(string rule, string file, int line, string message, bool isNotice)
            {
                Rule = rule;
                File = file;
                Line = line;
                Message = message;
                IsNotice = isNotice;
            }
        }

        public static JObject ToJson(
            string root, IReadOnlyList<Row> rows, int violations, int notices,
            bool withItems, string cursor, int limit, int maxChars, string projectRoot)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var rule = string.IsNullOrEmpty(row.Rule) ? GlobalRule : row.Rule;
                counts[rule] = counts.TryGetValue(rule, out var n) ? n + 1 : 1;
            }

            var ordered = new List<KeyValuePair<string, int>>(counts);
            ordered.Sort((a, b) =>
            {
                var c = b.Value.CompareTo(a.Value);
                return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
            });

            var byRule = new JArray();
            foreach (var kv in ordered)
            {
                byRule.Add(new JObject { ["rule"] = kv.Key, ["count"] = kv.Value });
            }

            var head = McpJson.Obj(
                ("root", root),
                ("violations", McpJson.Keep(violations)),
                ("notices", McpJson.Keep(notices)),
                ("byRule", byRule));

            if (!withItems)
            {
                return head;
            }

            return ItemPaging.Fit(head, rows, cursor, limit, maxChars, row => MapRow(row, projectRoot));
        }

        private static JObject MapRow(Row row, string projectRoot)
        {
            var obj = new JObject
            {
                ["rule"] = string.IsNullOrEmpty(row.Rule) ? GlobalRule : row.Rule,
                ["file"] = RelativizePath(row.File, projectRoot),
                ["line"] = row.Line,
            };
            if (row.IsNotice)
            {
                obj["notice"] = true;
            }

            // 規則名の付く当たりのメッセージは規則ごとの定型なので省く(件数が多いと重い)。規則名の無い全体エラーと notice は本文を付ける。
            if (row.IsNotice || string.IsNullOrEmpty(row.Rule))
            {
                obj["msg"] = ValidationSummary.CutMessage(row.Message);
            }

            return obj;
        }

        // プロジェクト直下からの相対パス(/ 区切り)にする。外のパスはそのまま。
        public static string RelativizePath(string path, string projectRoot)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            var normalized = path.Replace('\\', '/');
            if (string.IsNullOrEmpty(projectRoot))
            {
                return normalized;
            }

            var root = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            return normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? normalized.Substring(root.Length) : normalized;
        }
    }
}
