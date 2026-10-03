using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 P-15(2026-10-03) — パッケージの package.json に書ける独自フィールド
    // `ddriveUpdate`(UPM は未知のフィールドを無視する)の宣言と、導入済みパッケージ同士の
    // 「vX.Y.Z 以降対応」の検査(純関数)。
    //   "ddriveUpdate": {
    //     "requires":       { "<パッケージ ID>": "X.Y.Z" },   // 必須。入っていて、版が X.Y.Z 以上であること
    //     "compatibleWith": { "<パッケージ ID>": "X.Y.Z" }    // 任意の相手。入っている場合だけ X.Y.Z 以上であること
    //   }
    // 値は最低版(上限は書かない)。Unity API に依存しないので EditMode テストから直接検証できる。
    public sealed class DdriveUpdateDeclaration
    {
        public const string FieldName = "ddriveUpdate";
        public const string RequiresKey = "requires";
        public const string CompatibleWithKey = "compatibleWith";

        public static readonly DdriveUpdateDeclaration Empty = new(
            new Dictionary<string, string>(), new Dictionary<string, string>());

        public readonly IReadOnlyDictionary<string, string> Requires;
        public readonly IReadOnlyDictionary<string, string> CompatibleWith;

        public DdriveUpdateDeclaration(IReadOnlyDictionary<string, string> requires, IReadOnlyDictionary<string, string> compatibleWith)
        {
            Requires = requires ?? new Dictionary<string, string>();
            CompatibleWith = compatibleWith ?? new Dictionary<string, string>();
        }

        public bool IsEmpty => Requires.Count == 0 && CompatibleWith.Count == 0;

        // package.json のテキストから宣言を読む。フィールド無し・壊れた JSON・想定外の型は Empty(例外を投げない)。
        public static DdriveUpdateDeclaration Parse(string packageJsonText)
        {
            if (string.IsNullOrWhiteSpace(packageJsonText))
            {
                return Empty;
            }

            try
            {
                if (JToken.Parse(packageJsonText) is not JObject root
                    || root[FieldName] is not JObject field)
                {
                    return Empty;
                }

                var requires = ReadMap(field[RequiresKey]);
                var compat = ReadMap(field[CompatibleWithKey]);
                return requires.Count == 0 && compat.Count == 0 ? Empty : new DdriveUpdateDeclaration(requires, compat);
            }
            catch (Exception)
            {
                return Empty;
            }
        }

        private static Dictionary<string, string> ReadMap(JToken token)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (token is not JObject obj)
            {
                return map;
            }

            foreach (var prop in obj.Properties())
            {
                if (string.IsNullOrWhiteSpace(prop.Name) || prop.Value == null || prop.Value.Type != JTokenType.String)
                {
                    continue;
                }

                map[prop.Name] = (string)prop.Value;
            }

            return map;
        }

        // package.json の "version" を読む(無い・壊れているときは null)。
        public static string ParseVersion(string packageJsonText)
        {
            if (string.IsNullOrWhiteSpace(packageJsonText))
            {
                return null;
            }

            try
            {
                return JToken.Parse(packageJsonText) is JObject root && root["version"]?.Type == JTokenType.String
                    ? (string)root["version"]
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    // 導入済み(または導入予定)のパッケージ 1 件の状態。
    public sealed class PackageState
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string Version;
        public readonly string ResolvedPath;
        public readonly DdriveUpdateDeclaration Declaration;

        public PackageState(string id, string displayName, string version, string resolvedPath, DdriveUpdateDeclaration declaration)
        {
            Id = id ?? string.Empty;
            DisplayName = string.IsNullOrEmpty(displayName) ? Id : displayName;
            Version = version ?? string.Empty;
            ResolvedPath = resolvedPath;
            Declaration = declaration ?? DdriveUpdateDeclaration.Empty;
        }
    }

    public enum DependencyIssueSeverity
    {
        Info,
        Warning,
        Error,
    }

    public readonly struct PackageDependencyIssue
    {
        public const string CodeRequiresMissing = "DD-PKGDEP-REQUIRES-MISSING";
        public const string CodeRequiresOld = "DD-PKGDEP-REQUIRES-OLD";
        public const string CodeCompatibleOld = "DD-PKGDEP-COMPAT-OLD";
        public const string CodeMajorAhead = "DD-PKGDEP-MAJOR-AHEAD";
        public const string CodeBadDeclaration = "DD-PKGDEP-BAD-DECLARATION";

        public readonly DependencyIssueSeverity Severity;
        public readonly string Code;

        // 宣言を持っているパッケージ(「〜は」の主語)。
        public readonly string PackageId;

        // 宣言された相手。
        public readonly string TargetId;
        public readonly string Message;

        public PackageDependencyIssue(DependencyIssueSeverity severity, string code, string packageId, string targetId, string message)
        {
            Severity = severity;
            Code = code;
            PackageId = packageId;
            TargetId = targetId;
            Message = message;
        }

        public bool SameAs(PackageDependencyIssue other)
            => Code == other.Code && PackageId == other.PackageId && TargetId == other.TargetId;

        public bool Involves(string packageId)
            => string.Equals(PackageId, packageId, StringComparison.Ordinal) || string.Equals(TargetId, packageId, StringComparison.Ordinal);
    }

    public static class PackageDependencyChecker
    {
        // 今の組み合わせを検査する。自己参照・相互参照(循環)・壊れた宣言で落ちない(再帰しないので循環は問題にならない)。
        public static List<PackageDependencyIssue> Check(IReadOnlyList<PackageState> packages)
        {
            var issues = new List<PackageDependencyIssue>();
            if (packages == null)
            {
                return issues;
            }

            for (var i = 0; i < packages.Count; i++)
            {
                var owner = packages[i];
                if (owner == null || string.IsNullOrEmpty(owner.Id))
                {
                    continue;
                }

                foreach (var kv in owner.Declaration.Requires)
                {
                    CheckOne(packages, owner, kv.Key, kv.Value, true, issues);
                }

                foreach (var kv in owner.Declaration.CompatibleWith)
                {
                    CheckOne(packages, owner, kv.Key, kv.Value, false, issues);
                }
            }

            return issues;
        }

        // `packageId` を `newVersion`(宣言 = `newDeclaration`。取得できなかったときは null)に差し替えた場合の
        // 組み合わせを検査し、「今の組み合わせには無かった問題」だけを返す(版上げ前の事前確認)。
        public static List<PackageDependencyIssue> CheckPlanned(
            IReadOnlyList<PackageState> current,
            string packageId,
            string newVersion,
            DdriveUpdateDeclaration newDeclaration)
        {
            var before = Check(current);
            var planned = new List<PackageState>();
            var replaced = false;
            if (current != null)
            {
                for (var i = 0; i < current.Count; i++)
                {
                    var p = current[i];
                    if (p != null && string.Equals(p.Id, packageId, StringComparison.Ordinal))
                    {
                        planned.Add(new PackageState(p.Id, p.DisplayName, newVersion, p.ResolvedPath, newDeclaration));
                        replaced = true;
                    }
                    else
                    {
                        planned.Add(p);
                    }
                }
            }

            if (!replaced)
            {
                planned.Add(new PackageState(packageId, packageId, newVersion, null, newDeclaration));
            }

            var after = Check(planned);
            var result = new List<PackageDependencyIssue>();
            foreach (var issue in after)
            {
                var existed = false;
                foreach (var b in before)
                {
                    if (b.SameAs(issue))
                    {
                        existed = true;
                        break;
                    }
                }

                if (!existed)
                {
                    result.Add(issue);
                }
            }

            return result;
        }

        public static bool HasAtLeast(IReadOnlyList<PackageDependencyIssue> issues, DependencyIssueSeverity severity)
        {
            if (issues == null)
            {
                return false;
            }

            for (var i = 0; i < issues.Count; i++)
            {
                if (issues[i].Severity >= severity)
                {
                    return true;
                }
            }

            return false;
        }

        private static void CheckOne(
            IReadOnlyList<PackageState> packages,
            PackageState owner,
            string targetId,
            string minimum,
            bool required,
            List<PackageDependencyIssue> issues)
        {
            if (string.IsNullOrEmpty(targetId) || string.Equals(targetId, owner.Id, StringComparison.Ordinal))
            {
                return; // 自己参照は無視する。
            }

            if (!TryParse(minimum, out var min))
            {
                issues.Add(new PackageDependencyIssue(
                    DependencyIssueSeverity.Warning,
                    PackageDependencyIssue.CodeBadDeclaration,
                    owner.Id,
                    targetId,
                    $"{owner.DisplayName} の package.json の ddriveUpdate に書かれた {targetId} の版「{minimum}」を X.Y.Z として読めませんでした(無視します)。"));
                return;
            }

            PackageState target = null;
            for (var i = 0; i < packages.Count; i++)
            {
                if (packages[i] != null && string.Equals(packages[i].Id, targetId, StringComparison.Ordinal))
                {
                    target = packages[i];
                    break;
                }
            }

            if (target == null)
            {
                if (required)
                {
                    issues.Add(new PackageDependencyIssue(
                        DependencyIssueSeverity.Error,
                        PackageDependencyIssue.CodeRequiresMissing,
                        owner.Id,
                        targetId,
                        $"{owner.DisplayName} は {targetId} v{min} 以降が必要ですが、導入されていません。"));
                }

                return; // compatibleWith は相手が入っていなければ何も言わない。
            }

            if (!TryParse(target.Version, out var actual))
            {
                return; // 相手の版が読めないときは判定しない。
            }

            if (actual.CompareTo(min) < 0)
            {
                issues.Add(required
                    ? new PackageDependencyIssue(
                        DependencyIssueSeverity.Error,
                        PackageDependencyIssue.CodeRequiresOld,
                        owner.Id,
                        targetId,
                        $"{owner.DisplayName} は {target.DisplayName} v{min} 以降が必要ですが、v{actual} が入っています。")
                    : new PackageDependencyIssue(
                        DependencyIssueSeverity.Warning,
                        PackageDependencyIssue.CodeCompatibleOld,
                        owner.Id,
                        targetId,
                        $"{owner.DisplayName} は {target.DisplayName} v{min} 以降に対応していますが、v{actual} が入っています。"));
                return;
            }

            if (actual.Major > min.Major)
            {
                issues.Add(new PackageDependencyIssue(
                    DependencyIssueSeverity.Info,
                    PackageDependencyIssue.CodeMajorAhead,
                    owner.Id,
                    targetId,
                    $"{target.DisplayName} の MAJOR が上がっています(v{actual}。{owner.DisplayName} が宣言しているのは v{min} 以降)。CHANGELOG の「互換性」を確認してください。"));
            }
        }

        // "1.4" のような 2 桁も 3 桁(X.Y.Z)に揃える(System.Version は Build 未指定を -1 として扱い、1.4 < 1.4.0 になるため)。
        private static bool TryParse(string text, out Version version)
        {
            if (!SemVer.TryParse(text, out var v))
            {
                version = null;
                return false;
            }

            version = new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            return true;
        }
    }
}
