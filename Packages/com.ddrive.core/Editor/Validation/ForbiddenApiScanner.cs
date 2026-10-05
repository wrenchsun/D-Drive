using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DDrive.Editor.Settings;
using DDrive.Foundation.Validation;

namespace DDrive.Editor.Validation
{
    // [00_requirements.md] §5 の禁止事項をソーステキストの静的走査で検出する。
    // 専用 Roslyn Analyzer への置き換えは将来の改善余地(現状はテキストパターン一致の簡易版)。
    public static class ForbiddenApiScanner
    {
        private sealed class Rule
        {
            // [11_tasks.md] M-4 — 許可コメント/設定の許可リストが使う規則名(大文字小文字は区別しない)。
            // 規則名は互換の契約(追加のみ。docs/42 §5.9)。
            public string Name;
            public string Pattern;
            public string Message;
            public string[] AllowedFileSuffixes = Array.Empty<string>();
        }

        // 注意: このファイル自身の Message 文字列が各パターンにマッチしてしまうため、
        // 全ルールの許可リストに "ForbiddenApiScanner.cs" を含める(自己検出の偽陽性防止)。
        private static readonly Rule[] Rules =
        {
            new Rule
            {
                Name = "Time",
                Pattern = @"\bTime\.(time|deltaTime|unscaledDeltaTime|timeAsDouble|unscaledTime)\b",
                Message = "UnityEngine.Time を直接参照しない。ITimeSource を使う([02_core_framework.md] §9.5)",
                AllowedFileSuffixes = new[] { "LocalTimeSource.cs", "NetworkTimeSource.cs", "GameLoopDriver.cs", "ForbiddenApiScanner.cs" },
            },
            new Rule
            {
                Name = "ResourcesLoad",
                Pattern = @"\bResources\.Load\b",
                Message = "Resources.Load は禁止。Addressables 経由(IAssetLoader)を使う([00_requirements.md] §5)",
                AllowedFileSuffixes = new[] { "ForbiddenApiScanner.cs" },
            },
            new Rule
            {
                Name = "AddressablesLoad",
                Pattern = @"\bAddressables\.Load\w*\b",
                Message = "Addressables への直接アクセスは禁止。IAssetLoader 経由にする([00_requirements.md] §5)",
                AllowedFileSuffixes = new[] { "AddressablesAssetLoader.cs", "ForbiddenApiScanner.cs" },
            },
            new Rule
            {
                // PreviewService: 非破壊ループ試聴のための ScriptableObject コピー(GameObject 生成ではない)を許可。
                Name = "Instantiate",
                Pattern = @"\b(?:UnityEngine\.)?Object\.Instantiate\s*\(|(?<![.\w])Instantiate\s*\(",
                Message = "Instantiate の直接呼び出しは禁止。PoolService 経由にする([00_requirements.md] §5)",
                AllowedFileSuffixes = new[] { "PoolService.cs", "PreviewService.cs", "ForbiddenApiScanner.cs" },
            },
            new Rule
            {
                Name = "AudioSourcePlay",
                Pattern = @"\bAudioSource\.Play\b",
                Message = "AudioSource.Play の直接呼び出しは禁止。AudioManager 経由にする([00_requirements.md] §5)",
                AllowedFileSuffixes = new[] { "ForbiddenApiScanner.cs" },
            },
        };

        public readonly struct Violation
        {
            public readonly string FilePath;
            public readonly int Line;
            public readonly string Message;

            public Violation(string filePath, int line, string message)
            {
                FilePath = filePath;
                Line = line;
                Message = message;
            }
        }


        // [11_tasks.md] M-4(2026-10-05) — 許可の仕組み。
        // 行単位の許可コメント: 接頭辞 AllowPrefix の後ろに「規則名(理由)」を書く(書式は docs/42 §5.9 の契約)。
        //   - 同じ行の行末 // コメント、または直前の行(// コメントだけの行)に書く。その 1 行の当たりだけを許可する
        //   - 規則名は RuleNames のいずれか(大文字小文字は区別しない)。理由(括弧内、全角 () も可)は必須
        //   - 1 つの許可コメント = 1 規則。同じ行に 2 規則あるときは接頭辞ごと繰り返す
        //   - 間に空行・別のコメント行を挟むと効かない。/* */ 形式は認識しない
        public const string AllowPrefix = "ddrive-allow:";

        public const string CodeAllowNoReason = "DD-FORBIDDEN-ALLOW-NO-REASON";
        public const string CodeAllowUnknownRule = "DD-FORBIDDEN-ALLOW-UNKNOWN-RULE";
        public const string CodeAllowUnused = "DD-FORBIDDEN-ALLOW-UNUSED";
        public const string CodeAllowSummary = "DD-FORBIDDEN-ALLOW-SUMMARY";
        public const string CodeAllowSettingsInvalid = "DD-FORBIDDEN-ALLOW-SETTINGS-INVALID";

        public enum AllowSource
        {
            Comment,
            Settings,
        }

        public readonly struct AllowedHit
        {
            public readonly string FilePath;
            public readonly int Line;
            public readonly string RuleName;
            public readonly string Reason;
            public readonly AllowSource Source;

            public AllowedHit(string filePath, int line, string ruleName, string reason, AllowSource source)
            {
                FilePath = filePath;
                Line = line;
                RuleName = ruleName;
                Reason = reason;
                Source = source;
            }
        }

        // Violation(= CI で Error)とは別の、許可まわりの報告(Warning / Info)。CI を fail させない。
        public readonly struct Notice
        {
            public readonly ValidationSeverity Severity;
            public readonly string Code;
            public readonly string FilePath;
            public readonly int Line;
            public readonly string Message;

            public Notice(ValidationSeverity severity, string code, string filePath, int line, string message)
            {
                Severity = severity;
                Code = code;
                FilePath = filePath;
                Line = line;
                Message = message;
            }
        }

        public sealed class ScanReport
        {
            public readonly List<Violation> Violations = new();
            public readonly List<Notice> Notices = new();
            public readonly List<AllowedHit> Allowed = new();

            public int CommentAllowedCount
            {
                get
                {
                    var n = 0;
                    foreach (var a in Allowed)
                    {
                        if (a.Source == AllowSource.Comment) n++;
                    }

                    return n;
                }
            }

            public int SettingsAllowedCount => Allowed.Count - CommentAllowedCount;
        }

        // 使える規則名(互換の契約。追加のみ)。
        public static IReadOnlyList<string> RuleNames
        {
            get
            {
                var names = new string[Rules.Length];
                for (var i = 0; i < Rules.Length; i++)
                {
                    names[i] = Rules[i].Name;
                }

                return names;
            }
        }

        private sealed class Directive
        {
            public int Line;
            public string RuleText;
            public Rule Rule;
            public string Reason;
            public bool Used;
            public bool Flagged;

            public bool HasReason => !string.IsNullOrEmpty(Reason);
        }

        private sealed class SettingsAllow
        {
            public string Path;
            public Rule Rule; // null = 全規則
            public string Reason;
        }

        private static Rule FindRule(string name)
        {
            foreach (var rule in Rules)
            {
                if (string.Equals(rule.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return rule;
                }
            }

            return null;
        }

        private static string RuleNameList() => string.Join(" / ", RuleNames);

        // 既存の挙動と同じ(許可コメントは効くが、設定の許可リストは渡さない = 実設定に依存しない)。
        public static List<Violation> Scan(string rootFolder) => ScanDetailed(rootFolder, null).Violations;

        // [42_distribution.md] §2.3-2(P-4、2026-09-20) — 走査ルートが見つからない/`.cs` が 0 件のときは
        // 「違反 0 件」として静かに通さず、専用の Violation を 1 件返して CI.ValidateAll を Error にする
        // (パッケージ化でルートが変わって走査対象が消えると、禁止 API チェックが恒久的に無効化されて
        // しまう事故を防ぐ。CLAUDE.md §0-3)。
        public static ScanReport ScanDetailed(string rootFolder, IReadOnlyList<ForbiddenApiAllowEntry> settingsEntries)
        {
            var report = new ScanReport();
            var violations = report.Violations;

            if (!Directory.Exists(rootFolder))
            {
                violations.Add(new Violation(rootFolder, 0,
                    $"ForbiddenApiScanner: 走査対象フォルダが見つかりません('{rootFolder}')。禁止 API チェックが無効化されています。"));
                return report;
            }

            var files = Directory.GetFiles(rootFolder, "*.cs", SearchOption.AllDirectories);

            if (files.Length == 0)
            {
                violations.Add(new Violation(rootFolder, 0,
                    $"ForbiddenApiScanner: 走査対象の .cs ファイルが 0 件です('{rootFolder}')。禁止 API チェックが無効化されています。"));
                return report;
            }

            var settingsAllows = ResolveSettingsEntries(settingsEntries, report.Notices);

            foreach (var file in files)
            {
                var normalized = file.Replace('\\', '/');

                // [47_review_p_tickets_2026-09-20.md] P1-2 — Samples(通常配置の "/Samples/" と、UPM の
                // Unity 非可視フォルダ "/Samples~/")・Tests・Tools~・Documentation~ は製品コードの規約対象外
                // (手動確認用のデモスクリプト・テストの意図的な禁止パターン文字列・付属ツール・ドキュメント置き場)。
                // 開発リポジトリでは CI.ResolveForbiddenApiScanRoot() がパッケージ自身を走査するため、
                // これらのフォルダを除外しないと Samples~/Tests 内の既知の当たりが常に混ざる
                // ([42_distribution.md] §2.3-2 の実測 26 件のうち 14 件はこの除外漏れが原因)。
                // [14_networking.md] §16(N-3、2026-09-22) — `Samples~/NetCheck/` の手動確認用デモ
                // (NetCheckRunner/NetBridgeSmokeTest)を `Runtime/Ngo/NetCheck/` へ移設した際、Samples の
                // 除外パスに乗らなくなったため個別に追加した。これらは実プレイの定常経路(Tick/Spawn/Play)
                // ではなく実機確認専用のヘッドレス自動テストコードで、以前から Samples 扱いとして本規約の
                // 対象外だった経緯を維持する(禁止パターンの意図=製品コードの定常経路保護、であってこの
                // 確認用コードは対象ではない)。
                if (normalized.Contains("/Samples/") || normalized.Contains("/Samples~/") ||
                    normalized.Contains("/Tests/") || normalized.Contains("/Tools~/") ||
                    normalized.Contains("/Documentation~/") || normalized.Contains("/Runtime/Ngo/NetCheck/"))
                {
                    continue;
                }

                ScanFile(normalized, settingsAllows, report);
            }

            if (report.Allowed.Count > 0)
            {
                report.Notices.Add(new Notice(ValidationSeverity.Info, CodeAllowSummary, rootFolder, 0,
                    $"禁止 API の許可: {report.Allowed.Count} 件(コメント {report.CommentAllowedCount}、設定 {report.SettingsAllowedCount})。" +
                    "一覧は Tools > D-Drive > Validation > Forbidden API 許可一覧 で確認できます。"));
            }

            return report;
        }

        private static void ScanFile(string normalized, List<SettingsAllow> settingsAllows, ScanReport report)
        {
            var lines = File.ReadAllLines(normalized);
            var isSelf = normalized.EndsWith("ForbiddenApiScanner.cs", StringComparison.Ordinal);
            var relative = settingsAllows.Count > 0 ? ToProjectRelative(normalized) : normalized;

            List<Directive> all = null;
            var prev = new List<Directive>();
            var cur = new List<Directive>();

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                cur.Clear();
                if (!isSelf && line.IndexOf(AllowPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ParseDirectives(line, lineIndex + 1, cur);
                    if (cur.Count > 0)
                    {
                        all ??= new List<Directive>();
                        all.AddRange(cur);
                    }
                }

                if (line.TrimStart().StartsWith("//"))
                {
                    // コメントだけの行の許可は「次の 1 行」にだけ効く。
                    prev.Clear();
                    prev.AddRange(cur);
                    continue;
                }

                foreach (var rule in Rules)
                {
                    if (IsAllowedFile(normalized, rule.AllowedFileSuffixes))
                    {
                        continue;
                    }

                    if (!Regex.IsMatch(line, rule.Pattern))
                    {
                        continue;
                    }

                    // 1. 許可コメント(同じ行 + 直前の行)
                    string reason = null;
                    Directive invalid = null;
                    MarkDirectives(cur, rule, ref reason, ref invalid);
                    MarkDirectives(prev, rule, ref reason, ref invalid);
                    if (reason != null)
                    {
                        report.Allowed.Add(new AllowedHit(normalized, lineIndex + 1, rule.Name, reason, AllowSource.Comment));
                        continue;
                    }

                    // 2. 設定の許可リスト
                    var settingsReason = FindSettingsAllow(settingsAllows, normalized, relative, rule);
                    if (settingsReason != null)
                    {
                        report.Allowed.Add(new AllowedHit(normalized, lineIndex + 1, rule.Name, settingsReason, AllowSource.Settings));
                        continue;
                    }

                    var message = rule.Message;
                    if (invalid != null)
                    {
                        invalid.Flagged = true;
                        message += " [許可コメントに理由が必要です。`" + AllowPrefix + " " + rule.Name + "(理由)` の形で括弧内に理由を書いてください]";
                    }

                    report.Violations.Add(new Violation(normalized, lineIndex + 1, message));
                }

                prev.Clear();
            }

            if (all == null)
            {
                return;
            }

            foreach (var d in all)
            {
                if (d.Rule == null)
                {
                    report.Notices.Add(new Notice(ValidationSeverity.Warning, CodeAllowUnknownRule, normalized, d.Line,
                        $"許可コメントの規則名 '{d.RuleText}' は存在しません(使える規則名: {RuleNameList()})。この許可は無効です。"));
                }
                else if (!d.HasReason)
                {
                    if (!d.Flagged)
                    {
                        report.Notices.Add(new Notice(ValidationSeverity.Warning, CodeAllowNoReason, normalized, d.Line,
                            $"許可コメント({d.Rule.Name})に理由がありません。`{AllowPrefix} {d.Rule.Name}(理由)` の形で括弧内に理由を書いてください。この許可は無効です。"));
                    }
                }
                else if (!d.Used)
                {
                    report.Notices.Add(new Notice(ValidationSeverity.Info, CodeAllowUnused, normalized, d.Line,
                        $"使われていない許可コメントです({d.Rule.Name})。その行(または次の行)に該当する当たりがありません。不要なら削除してください。"));
                }
            }
        }

        private static void MarkDirectives(List<Directive> list, Rule rule, ref string reason, ref Directive invalid)
        {
            foreach (var d in list)
            {
                if (d.Rule != rule)
                {
                    continue;
                }

                if (d.HasReason)
                {
                    d.Used = true;
                    reason ??= d.Reason;
                }
                else
                {
                    invalid ??= d;
                }
            }
        }

        // 行の `//` コメントから許可コメントを取り出す。1 つのコメントに接頭辞が複数あれば全部拾う。
        private static void ParseDirectives(string line, int lineNumber, List<Directive> into)
        {
            var commentStart = FindLineCommentStart(line);
            if (commentStart < 0)
            {
                return;
            }

            var comment = line.Substring(commentStart + 2);
            var pos = comment.IndexOf(AllowPrefix, StringComparison.OrdinalIgnoreCase);
            while (pos >= 0)
            {
                var segStart = pos + AllowPrefix.Length;
                var next = comment.IndexOf(AllowPrefix, segStart, StringComparison.OrdinalIgnoreCase);
                var segment = (next >= 0 ? comment.Substring(segStart, next - segStart) : comment.Substring(segStart)).Trim();

                var n = 0;
                while (n < segment.Length && (char.IsLetterOrDigit(segment[n]) || segment[n] == '_'))
                {
                    n++;
                }

                var ruleText = segment.Substring(0, n);
                var rest = segment.Substring(n).TrimStart();
                var reason = string.Empty;
                if (rest.Length > 0 && (rest[0] == '(' || rest[0] == '（'))
                {
                    var close = Math.Max(rest.LastIndexOf(')'), rest.LastIndexOf('）'));
                    if (close > 0)
                    {
                        reason = rest.Substring(1, close - 1).Trim();
                    }
                }

                into.Add(new Directive
                {
                    Line = lineNumber,
                    RuleText = ruleText,
                    Rule = FindRule(ruleText),
                    Reason = reason,
                });

                pos = next;
            }
        }

        // 文字列/文字リテラルの外にある最初の `//` の位置(無ければ -1)。過剰な構文解析はしない
        // (複数行にまたがる verbatim 文字列・raw 文字列は追わない)。
        private static int FindLineCommentStart(string line)
        {
            var inString = false;
            var verbatim = false;
            var inChar = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inString)
                {
                    if (verbatim)
                    {
                        if (c == '"')
                        {
                            if (i + 1 < line.Length && line[i + 1] == '"') i++;
                            else inString = false;
                        }
                    }
                    else if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (inChar)
                {
                    if (c == '\\') i++;
                    else if (c == '\'') inChar = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    verbatim = i > 0 && line[i - 1] == '@';
                }
                else if (c == '\'')
                {
                    inChar = true;
                }
                else if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    return i;
                }
            }

            return -1;
        }

        // 設定の許可リストの要素を検査して有効なものだけ返す(無効な要素は Notice を積む)。
        private static List<SettingsAllow> ResolveSettingsEntries(IReadOnlyList<ForbiddenApiAllowEntry> entries, List<Notice> notices)
        {
            var result = new List<SettingsAllow>();
            if (entries == null)
            {
                return result;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null)
                {
                    continue;
                }

                var problem = DescribeEntryProblem(e, out var rule);
                if (problem != null)
                {
                    notices.Add(new Notice(ValidationSeverity.Warning, CodeAllowSettingsInvalid,
                        "ProjectSettings/DDriveProjectSettings.asset", 0,
                        $"禁止 API の除外設定(#{i + 1}、パス '{e.Path}')が無効です: {problem}"));
                    continue;
                }

                var path = e.Path.Trim().Replace('\\', '/');
                if (path.StartsWith("./", StringComparison.Ordinal))
                {
                    path = path.Substring(2);
                }

                result.Add(new SettingsAllow { Path = path, Rule = rule, Reason = e.Reason.Trim() });
            }

            return result;
        }

        // 設定の 1 要素の問題点(有効なら null)。ProjectSetupValidator からも使う。
        public static string DescribeEntryProblem(ForbiddenApiAllowEntry entry)
            => DescribeEntryProblem(entry, out _);

        private static string DescribeEntryProblem(ForbiddenApiAllowEntry entry, out Rule rule)
        {
            rule = null;
            if (entry == null || string.IsNullOrWhiteSpace(entry.Path))
            {
                return "パスが空です。";
            }

            if (string.IsNullOrWhiteSpace(entry.Reason))
            {
                return "理由が空です(理由は必須。この除外は無効です)。";
            }

            if (!string.IsNullOrWhiteSpace(entry.Rule))
            {
                rule = FindRule(entry.Rule.Trim());
                if (rule == null)
                {
                    return $"規則名 '{entry.Rule}' は存在しません(使える規則名: {RuleNameList()}。空欄なら全規則)。";
                }
            }

            return null;
        }

        private static string FindSettingsAllow(List<SettingsAllow> allows, string normalized, string relative, Rule rule)
        {
            foreach (var a in allows)
            {
                if (a.Rule != null && a.Rule != rule)
                {
                    continue;
                }

                if (normalized.StartsWith(a.Path, StringComparison.Ordinal) || relative.StartsWith(a.Path, StringComparison.Ordinal))
                {
                    return a.Reason;
                }
            }

            return null;
        }

        private static string ToProjectRelative(string normalized)
        {
            try
            {
                var full = Path.GetFullPath(normalized).Replace('\\', '/');
                var root = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return full.Substring(root.Length);
                }
            }
            catch (Exception)
            {
                // 解決できなければ元のパスのまま比較する
            }

            return normalized;
        }

        // 許可した箇所の一覧(Tools > D-Drive > Validation > Forbidden API 許可一覧 のログ用)。
        public static string FormatAllowedList(ScanReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"禁止 API の許可: {report.Allowed.Count} 件(コメント {report.CommentAllowedCount}、設定 {report.SettingsAllowedCount})");
            foreach (var a in report.Allowed)
            {
                sb.AppendLine($"  [{(a.Source == AllowSource.Comment ? "comment" : "settings")}] {a.FilePath}:{a.Line} {a.RuleName} — {a.Reason}");
            }

            return sb.ToString();
        }

        private static bool IsAllowedFile(string path, string[] suffixes)
        {
            foreach (var suffix in suffixes)
            {
                if (path.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
