using System;
using System.Collections.Generic;
using DDrive.Editor.Setup;
using Newtonsoft.Json.Linq;

namespace DDrive.Editor.Update
{
    // 更新ウィンドウの「パッケージ」一覧の 1 行。
    public sealed class PackageRow
    {
        public string Id;
        public string DisplayName;
        public bool IsDDrive;

        // manifest.dependencies の値(無ければ null)。
        public string ManifestValue;
        public GitPackageUrl Url;

        // 導入済みの状態(manifest にあるが未解決・未導入のときは null)。
        public PackageState Installed;

        public bool InManifest => ManifestValue != null;
        public bool IsGit => Url.IsGitUrl;
        public string CurrentVersion => Installed?.Version ?? string.Empty;
    }

    // manifest にある git URL 依存のうち、まだ管理対象でないもの(1 クリックで登録できる候補)。
    public sealed class PackageCandidate
    {
        public string Id;
        public string ManifestValue;

        // false: `#vX.Y.Z` 形式の ref ではない(UniTask / R3 のようにタグ運用が違う)ため、最新版の判定が当てにならない。
        public bool HasVersionTagRef;
    }

    // [42_distribution.md] §4.2 P-15(2026-10-03) — 一覧の行・候補の構築(純関数。manifest は JObject、導入済みの情報は
    // `PackageState` の一覧で受け取るので、実 manifest・実 PackageManager に触れずにテストできる)。
    public static class ManagedPackageRows
    {
        public const string DDrivePackageId = ProjectSetupActions.DDriveCorePackageId;

        // 1 行目は常に D-Drive。続けて `managedIds` の順(重複・D-Drive 自身は無視)。
        public static List<PackageRow> Build(JObject manifest, IReadOnlyList<string> managedIds, IReadOnlyList<PackageState> installed)
        {
            var rows = new List<PackageRow> { BuildRow(DDrivePackageId, true, manifest, installed) };
            if (managedIds == null)
            {
                return rows;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal) { DDrivePackageId };
            for (var i = 0; i < managedIds.Count; i++)
            {
                var id = managedIds[i];
                if (string.IsNullOrEmpty(id) || !seen.Add(id))
                {
                    continue;
                }

                rows.Add(BuildRow(id, false, manifest, installed));
            }

            return rows;
        }

        public static List<PackageCandidate> FindCandidates(JObject manifest, IReadOnlyList<string> managedIds)
        {
            var managed = new HashSet<string>(StringComparer.Ordinal) { DDrivePackageId };
            if (managedIds != null)
            {
                for (var i = 0; i < managedIds.Count; i++)
                {
                    managed.Add(managedIds[i]);
                }
            }

            var result = new List<PackageCandidate>();
            foreach (var dep in PackageManifestOps.ListGitDependencies(manifest))
            {
                if (managed.Contains(dep.Id))
                {
                    continue;
                }

                result.Add(new PackageCandidate { Id = dep.Id, ManifestValue = dep.Value, HasVersionTagRef = dep.HasVersionTagRef });
            }

            return result;
        }

        public static PackageState FindState(IReadOnlyList<PackageState> installed, string id)
        {
            if (installed == null)
            {
                return null;
            }

            for (var i = 0; i < installed.Count; i++)
            {
                if (installed[i] != null && string.Equals(installed[i].Id, id, StringComparison.Ordinal))
                {
                    return installed[i];
                }
            }

            return null;
        }

        // 一覧の行の依存の表示(2026-10-06、P-15 確認 Q-2。純関数)。原因がどちらの宣言にあるかが分かるように出し分ける:
        //   ・宣言した側の行(issue.PackageId == rowId): 主表示。Error = 「✗ 依存を満たしていません」、Warning = 「⚠ 依存に注意」、
        //     Info = 「ℹ 確認事項あり」+ 何が足りないか(「(D-Drive v1.4.0 以降が必要、現在 v1.3.1)」)。
        //   ・相手側の行(issue.TargetId == rowId で宣言していない側): 警告にはせず「ℹ <宣言した側> が v1.4.0 以降を要求しています(現在 v1.3.1)」。
        //     相手側を上げれば解消するという手がかりだけを残す(アイコン・色は警告より弱い)。
        //   ・`requires` の未導入は宣言した側の行だけ(相手は居ない)。
        //   ・どちらも無ければ「依存 OK」。両方あれば「 / 」でつなぐ。相手側が複数なら最初の 1 件 + 「ほか N 件」。
        public static string DescribeDependency(string rowId, IReadOnlyList<PackageDependencyIssue> issues)
        {
            if (issues == null || issues.Count == 0)
            {
                return "依存 OK";
            }

            var ownWorst = -1;
            PackageDependencyIssue? ownFirst = null;
            var counterpartCount = 0;
            PackageDependencyIssue? counterpartFirst = null;
            for (var i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                if (string.Equals(issue.PackageId, rowId, StringComparison.Ordinal))
                {
                    if ((int)issue.Severity > ownWorst)
                    {
                        ownWorst = (int)issue.Severity;
                        ownFirst = issue;
                    }
                }
                else if (string.Equals(issue.TargetId, rowId, StringComparison.Ordinal)
                         && issue.Code != PackageDependencyIssue.CodeBadDeclaration) // 宣言を読めない件は宣言した側だけの問題
                {
                    counterpartCount++;
                    counterpartFirst ??= issue;
                }
            }

            var parts = new List<string>();
            if (ownFirst.HasValue)
            {
                var first = ownFirst.Value;
                var head = first.Severity switch
                {
                    DependencyIssueSeverity.Error => "✗ 依存を満たしていません",
                    DependencyIssueSeverity.Warning => "⚠ 依存に注意",
                    _ => "ℹ 確認事項あり",
                };
                var detail = OwnDetail(first);
                parts.Add(detail == null ? head : head + "(" + detail + ")");
            }

            if (counterpartFirst.HasValue)
            {
                var first = counterpartFirst.Value;
                var text = "ℹ " + CounterpartDetail(first);
                if (counterpartCount > 1)
                {
                    text += $"(ほか {counterpartCount - 1} 件)";
                }

                parts.Add(text);
            }

            return parts.Count == 0 ? "依存 OK" : string.Join(" / ", parts);
        }

        // 宣言した側の行に付ける「何が足りないか」。
        private static string OwnDetail(PackageDependencyIssue issue)
        {
            var target = string.IsNullOrEmpty(issue.TargetDisplayName) ? issue.TargetId : issue.TargetDisplayName;
            switch (issue.Code)
            {
                case PackageDependencyIssue.CodeRequiresMissing:
                    return $"{target} v{issue.MinimumVersion} 以降が必要、未導入";
                case PackageDependencyIssue.CodeRequiresOld:
                    return $"{target} v{issue.MinimumVersion} 以降が必要、現在 v{issue.ActualVersion}";
                case PackageDependencyIssue.CodeCompatibleOld:
                    return $"{target} v{issue.MinimumVersion} 以降に対応、現在 v{issue.ActualVersion}";
                case PackageDependencyIssue.CodeBadDeclaration:
                    return string.IsNullOrEmpty(issue.TargetId) ? "package.json を読めません" : $"{target} の宣言を読めません";
                case PackageDependencyIssue.CodeMajorAhead:
                    return $"{target} の MAJOR が上がっています";
                default:
                    return null;
            }
        }

        // 相手側の行に出す文(宣言した側の名前 + 求める最低版 + 今の版)。
        private static string CounterpartDetail(PackageDependencyIssue issue)
        {
            var owner = string.IsNullOrEmpty(issue.OwnerDisplayName) ? issue.PackageId : issue.OwnerDisplayName;
            if (issue.Code == PackageDependencyIssue.CodeMajorAhead)
            {
                return $"{owner} が宣言しているのは v{issue.MinimumVersion} 以降です(現在 v{issue.ActualVersion}。MAJOR が上がっています)";
            }

            var verb = issue.Code == PackageDependencyIssue.CodeCompatibleOld ? "を想定しています" : "を要求しています";
            return $"{owner} が v{issue.MinimumVersion} 以降{verb}(現在 v{issue.ActualVersion})";
        }

        private static PackageRow BuildRow(string id, bool isDDrive, JObject manifest, IReadOnlyList<PackageState> installed)
        {
            var value = ManifestJson.GetDependencyValue(manifest, id);
            var state = FindState(installed, id);
            return new PackageRow
            {
                Id = id,
                DisplayName = isDDrive ? "D-Drive" : (state != null ? state.DisplayName : id),
                IsDDrive = isDDrive,
                ManifestValue = value,
                Url = GitPackageUrl.Parse(value),
                Installed = state,
            };
        }
    }
}
