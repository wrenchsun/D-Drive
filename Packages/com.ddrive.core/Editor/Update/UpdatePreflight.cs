using System;
using System.Collections.Generic;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 P-15(2026-10-03) — 版を上げる前に、上げ先の版の package.json を取得して
    // 「依存(requires / compatibleWith)が満たされなくなる組み合わせ」を事前に検査するための入口。
    // 取得は `IRemotePackageJsonFetcher`(実装 = `GitSparsePackageJsonFetcher`)越しにしてテストでは差し替える。
    public interface IRemotePackageJsonFetcher
    {
        // 成功時は package.json の本文(失敗時は null、`warningMessage` にユーザーへ見せる 1 行の理由)。
        // 例外で呼び出し元を止めない(CLAUDE.md §0-4)。
        string FetchPackageJson(string cloneUrl, string subPath, string reference, out string warningMessage);
    }

    public readonly struct PreflightResult
    {
        // true: 上げ先の package.json を取得して宣言まで含めて検査した。
        // false: 取得できなかった(版の比較だけ行った。「更新後に確認します」と表示して続行できる)。
        public readonly bool Fetched;
        public readonly string Message;
        public readonly IReadOnlyList<PackageDependencyIssue> Issues;

        public PreflightResult(bool fetched, string message, IReadOnlyList<PackageDependencyIssue> issues)
        {
            Fetched = fetched;
            Message = message;
            Issues = issues ?? Array.Empty<PackageDependencyIssue>();
        }
    }

    public static class UpdatePreflight
    {
        public static PreflightResult Run(
            IRemotePackageJsonFetcher fetcher,
            GitPackageUrl url,
            string packageId,
            string targetRef,
            IReadOnlyList<PackageState> current,
            bool fetchRemote = true)
        {
            // 元のタグ名(`v1.5.0-rc.1` 等)から X.Y.Z を取る(プレリリース部は比較では無視する。[42] §4.2.1)。
            var tagVersion = GitTag.TryParseRef(targetRef, out var targetTag) ? targetTag.Version.ToString() : null;
            string fetchedText = null;
            string warning = null;

            // fetchRemote = false(D-Drive の版上げ): D-Drive は ddriveUpdate を宣言しない規約([42] §4.2.1)ので上げ先の package.json は
            // 取得しない(= ネットワークの待ちが入らない)。他パッケージの requires / compatibleWith との照合はタグの版だけで足りる。
            if (!fetchRemote)
            {
                if (tagVersion == null)
                {
                    return new PreflightResult(
                        false,
                        "事前確認できませんでした(上げ先の版を判定できません)。更新後に確認します。",
                        Array.Empty<PackageDependencyIssue>());
                }

                return new PreflightResult(
                    true,
                    "他のパッケージの宣言(requires / compatibleWith)との照合を行いました。",
                    PackageDependencyChecker.CheckPlanned(current, packageId, tagVersion, null));
            }

            if (fetcher != null && url.IsGitUrl && !string.IsNullOrEmpty(targetRef))
            {
                fetchedText = fetcher.FetchPackageJson(url.CloneUrl, url.Path, targetRef, out warning);
            }
            else
            {
                warning = "取得先を解決できませんでした。";
            }

            var fetched = fetchedText != null;
            var fetchedVersion = fetched ? DdriveUpdateDeclaration.ParseVersion(fetchedText) : null;
            var newVersion = SemVer.TryParse(fetchedVersion, out _) ? fetchedVersion : tagVersion;

            if (!SemVer.TryParse(newVersion, out _))
            {
                return new PreflightResult(
                    false,
                    "事前確認できませんでした(上げ先の版を判定できません)。更新後に確認します。",
                    Array.Empty<PackageDependencyIssue>());
            }

            var declaration = fetched ? DdriveUpdateDeclaration.Parse(fetchedText) : null;
            var issues = PackageDependencyChecker.CheckPlanned(current, packageId, newVersion, declaration);

            if (!fetched)
            {
                return new PreflightResult(
                    false,
                    "上げ先の package.json を取得できなかったため、他のパッケージの宣言との照合だけ行いました" +
                    (string.IsNullOrEmpty(warning) ? "。" : $"({warning})。") + "更新後に改めて確認します。",
                    issues);
            }

            var note = string.Empty;
            if (tagVersion != null && fetchedVersion != null && SemVer.Compare(tagVersion, fetchedVersion) != 0)
            {
                note = $" ⚠ タグ {targetRef} の package.json の version は {fetchedVersion} です(タグと一致していません)。";
            }

            return new PreflightResult(true, "上げ先の package.json を確認しました。" + note, issues);
        }
    }
}
