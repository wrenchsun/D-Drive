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
    // 形式の拡張規則(2026-10-03、レビュー PC-R-07。[42_distribution.md] §4.2.1 で v1.4.0 に固定):
    //   ・値は `X.Y.Z` の文字列だけ(`1.4` と `1.4.0-rc.1` も読める)。範囲・上限・条件は将来**新しいキー**で足す(値の書式は広げない)。
    //   ・未知のキー(`ddriveUpdate` 直下も `requires` / `compatibleWith` の中身の他のキーも)は黙って無視する(警告しない)。
    //   ・値が文字列でない項目(オブジェクト・配列・数値・真偽・null)は将来用の予約として黙って無視する(BAD-DECLARATION にしない)。
    //   ・値が文字列だが版として読めないもの(範囲指定など)だけ BAD-DECLARATION(Warning)で知らせる。
    //   ・比較ではプレリリース(`-rc.1`)を無視して X.Y.Z で比べる(宣言側も導入済みの版も)。
    //   ・同じリポジトリの複数パッケージは同じタグ `vX.Y.Z` で揃える前提(タグはリポジトリ単位)。
    public sealed class DdriveUpdateDeclaration
    {
        public const string FieldName = "ddriveUpdate";
        public const string RequiresKey = "requires";
        public const string CompatibleWithKey = "compatibleWith";

        public static readonly DdriveUpdateDeclaration Empty = new(
            new Dictionary<string, string>(), new Dictionary<string, string>());

        // 2026-10-06(P-15 確認 BUG-1): package.json が JSON として読めない / `ddriveUpdate` の形が読めないとき(= 宣言が「無い」のではなく
        // 「読めなかった」)を表す。Requires / CompatibleWith は空。`Parse` は従来どおり Empty 相当(例外を投げない)を返すが、
        // 呼び出し側が「宣言なし」と「読めなかった」を区別できるよう `TryParse` と `IsUnreadable` を足した。
        public static readonly DdriveUpdateDeclaration Unreadable = new(
            new Dictionary<string, string>(), new Dictionary<string, string>(), true);

        public readonly IReadOnlyDictionary<string, string> Requires;
        public readonly IReadOnlyDictionary<string, string> CompatibleWith;

        // true: package.json を読めなかった(壊れた JSON・根がオブジェクトでない・`ddriveUpdate` がオブジェクトでない)。
        public readonly bool IsUnreadable;

        public DdriveUpdateDeclaration(IReadOnlyDictionary<string, string> requires, IReadOnlyDictionary<string, string> compatibleWith)
            : this(requires, compatibleWith, false)
        {
        }

        private DdriveUpdateDeclaration(IReadOnlyDictionary<string, string> requires, IReadOnlyDictionary<string, string> compatibleWith, bool unreadable)
        {
            Requires = requires ?? new Dictionary<string, string>();
            CompatibleWith = compatibleWith ?? new Dictionary<string, string>();
            IsUnreadable = unreadable;
        }

        public bool IsEmpty => Requires.Count == 0 && CompatibleWith.Count == 0;

        // package.json のテキストから宣言を読む。フィールド無し・壊れた JSON・想定外の型は Empty(例外を投げない)。
        // 「無い」と「読めなかった」を区別したいときは `TryParse` を使う。
        public static DdriveUpdateDeclaration Parse(string packageJsonText)
        {
            TryParse(packageJsonText, out var declaration);
            return declaration.IsUnreadable ? Empty : declaration;
        }

        // package.json のテキストから宣言を読み、読めたかどうかを返す(2026-10-06、BUG-1)。
        //   ・true: JSON オブジェクトとして読めた。`ddriveUpdate` が無い(または null)ときは Empty(= 宣言なし。従来どおり)。
        //   ・false: 空文字・壊れた JSON・根がオブジェクトでない・`ddriveUpdate` がオブジェクトでない。declaration は `Unreadable`。
        // 取得した package.json の文字化け(文字コードの取り違え)はたいてい JSON の壊れとして現れるので、事前確認はこれで見逃さない。
        public static bool TryParse(string packageJsonText, out DdriveUpdateDeclaration declaration)
        {
            declaration = Unreadable;
            if (string.IsNullOrWhiteSpace(packageJsonText))
            {
                return false;
            }

            try
            {
                // 先頭の BOM(U+FEFF)は取り除く(git show の非同期読み取りでは BOM が読み捨てられず残ることがある。GA-R-06)。
                if (JToken.Parse(packageJsonText.TrimStart('\uFEFF')) is not JObject root)
                {
                    return false;
                }

                var token = root[FieldName];
                if (token == null || token.Type == JTokenType.Null)
                {
                    declaration = Empty;
                    return true;
                }

                if (token is not JObject field)
                {
                    return false;
                }

                var requires = ReadMap(field[RequiresKey]);
                var compat = ReadMap(field[CompatibleWithKey]);
                declaration = requires.Count == 0 && compat.Count == 0 ? Empty : new DdriveUpdateDeclaration(requires, compat);
                return true;
            }
            catch (Exception)
            {
                return false;
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

        // 2026-10-06(P-15 確認 Q-2): 一覧の行に「どちらの宣言が原因か」を短く出すための付帯情報(未設定のものは null)。
        // 宣言した側の表示名 / 相手側の表示名 / 宣言された最低版(X.Y.Z)/ 相手の導入済みの版(未導入なら null)。
        public readonly string OwnerDisplayName;
        public readonly string TargetDisplayName;
        public readonly string MinimumVersion;
        public readonly string ActualVersion;

        public PackageDependencyIssue(DependencyIssueSeverity severity, string code, string packageId, string targetId, string message)
            : this(severity, code, packageId, targetId, message, null, null, null, null)
        {
        }

        public PackageDependencyIssue(
            DependencyIssueSeverity severity, string code, string packageId, string targetId, string message,
            string ownerDisplayName, string targetDisplayName, string minimumVersion, string actualVersion)
        {
            Severity = severity;
            Code = code;
            PackageId = packageId;
            TargetId = targetId;
            Message = message;
            OwnerDisplayName = ownerDisplayName;
            TargetDisplayName = targetDisplayName;
            MinimumVersion = minimumVersion;
            ActualVersion = actualVersion;
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

                if (owner.Declaration.IsUnreadable)
                {
                    // 2026-10-06(BUG-1): 壊れた package.json を「宣言なし」として黙って通さない(BAD-DECLARATION の範囲。Warning)。
                    issues.Add(new PackageDependencyIssue(
                        DependencyIssueSeverity.Warning,
                        PackageDependencyIssue.CodeBadDeclaration,
                        owner.Id,
                        string.Empty,
                        $"{owner.DisplayName} の package.json を読めませんでした(JSON として壊れている、または ddriveUpdate の形が不正です)。依存の宣言は確認できていません。"));
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

            if (!IsDeclaredVersion(minimum) || !TryParse(minimum, out var min))
            {
                issues.Add(new PackageDependencyIssue(
                    DependencyIssueSeverity.Warning,
                    PackageDependencyIssue.CodeBadDeclaration,
                    owner.Id,
                    targetId,
                    $"{owner.DisplayName} の package.json の ddriveUpdate に書かれた {targetId} の版「{minimum}」を X.Y.Z として読めませんでした(無視します)。",
                    owner.DisplayName, targetId, null, null));
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
                        $"{owner.DisplayName} は {targetId} v{min} 以降が必要ですが、導入されていません。",
                        owner.DisplayName, targetId, min.ToString(), null));
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
                        $"{owner.DisplayName} は {target.DisplayName} v{min} 以降が必要ですが、v{actual} が入っています。",
                        owner.DisplayName, target.DisplayName, min.ToString(), actual.ToString())
                    : new PackageDependencyIssue(
                        DependencyIssueSeverity.Warning,
                        PackageDependencyIssue.CodeCompatibleOld,
                        owner.Id,
                        targetId,
                        $"{owner.DisplayName} は {target.DisplayName} v{min} 以降に対応していますが、v{actual} が入っています。",
                        owner.DisplayName, target.DisplayName, min.ToString(), actual.ToString()));
                return;
            }

            if (actual.Major > min.Major)
            {
                issues.Add(new PackageDependencyIssue(
                    DependencyIssueSeverity.Info,
                    PackageDependencyIssue.CodeMajorAhead,
                    owner.Id,
                    targetId,
                    $"{target.DisplayName} の MAJOR が上がっています(v{actual}。{owner.DisplayName} が宣言しているのは v{min} 以降)。CHANGELOG の「互換性」を確認してください。",
                    owner.DisplayName, target.DisplayName, min.ToString(), actual.ToString()));
            }
        }

        // 宣言の値として許す書式: X.Y(.Z)(-プレリリース)。範囲(">=1.4.0 <2.0.0")や "1.4.0 - 1.x" のような将来の書式は読めない
        // ものとして BAD-DECLARATION にする(SemVer.TryParse は "-" 以降を捨てるため、そのままだと範囲の前半だけを黙って読んでしまう)。
        public static bool IsDeclaredVersion(string text)
            => !string.IsNullOrEmpty(text)
               && System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+\.\d+(\.\d+)?(-[0-9A-Za-z.-]+)?$");

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
