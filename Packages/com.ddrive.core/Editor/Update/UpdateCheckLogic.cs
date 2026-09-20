using System;
using System.Collections.Generic;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2/§6 P-14(2026-09-20) — 「更新チェック」の現在 vs 最新の判定(純関数)。
    // `GitPackageUrl.Ref`(manifest の "#" 以降)と `GitTagListParser.Parse` の結果を受け取り、
    // 「最新です / MINOR / MAJOR」等を判定する。Unity API に依存しないので EditMode テストから
    // 直接検証できる。実 git 呼び出し(`IGitTagLister`)・manifest 読み書き(`ManifestJson`)とは分離している。
    public static class UpdateCheckLogic
    {
        public enum BumpKind
        {
            Unknown,  // 比較できない(現在・最新のいずれかが版として解釈できない)
            UpToDate, // 最新のタグが現在の版以下
            Patch,
            Minor,
            Major,
        }

        public readonly struct Result
        {
            // 比較に使った「現在の版」。null なら比較不能。
            public readonly Version CurrentVersion;

            // true: manifest の "#ref" がタグとして解釈できず、package.json の版で比較した
            // (現在の参照がコミットハッシュ指定のとき)。
            public readonly bool CurrentIsFromPackageJson;

            // 取得したタグのうち最も新しい版。null なら 1 件も取得できなかった。
            public readonly Version LatestVersion;

            public readonly BumpKind Bump;

            public Result(Version currentVersion, bool currentIsFromPackageJson, Version latestVersion, BumpKind bump)
            {
                CurrentVersion = currentVersion;
                CurrentIsFromPackageJson = currentIsFromPackageJson;
                LatestVersion = latestVersion;
                Bump = bump;
            }
        }

        // currentRef: `GitPackageUrl.Ref`(manifest の "#" 以降。タグ名 or コミットハッシュ、null/空も許容)。
        // currentPackageJsonVersion: package.json の "version"(currentRef がタグとして解釈できないときの
        // フォールバック)。availableTags: `GitTagListParser.Parse` の戻り値(順不同で渡してよい。最大値を取る)。
        public static Result Evaluate(string currentRef, string currentPackageJsonVersion, IReadOnlyList<Version> availableTags)
        {
            var currentIsTag = TryParseRefAsVersion(currentRef, out var refVersion);
            Version current = null;
            if (currentIsTag)
            {
                current = refVersion;
            }
            else if (SemVer.TryParse(currentPackageJsonVersion, out var pkgVersion))
            {
                current = pkgVersion;
            }

            var usedPackageJson = !currentIsTag;
            var latest = FindLatest(availableTags);

            if (current == null || latest == null)
            {
                return new Result(current, usedPackageJson, latest, BumpKind.Unknown);
            }

            if (latest.CompareTo(current) <= 0)
            {
                return new Result(current, usedPackageJson, latest, BumpKind.UpToDate);
            }

            BumpKind bump;
            if (latest.Major != current.Major)
            {
                bump = BumpKind.Major;
            }
            else if (latest.Minor != current.Minor)
            {
                bump = BumpKind.Minor;
            }
            else
            {
                bump = BumpKind.Patch;
            }

            return new Result(current, usedPackageJson, latest, bump);
        }

        private static Version FindLatest(IReadOnlyList<Version> availableTags)
        {
            if (availableTags == null)
            {
                return null;
            }

            Version latest = null;
            for (var i = 0; i < availableTags.Count; i++)
            {
                var candidate = availableTags[i];
                if (candidate == null)
                {
                    continue;
                }

                if (latest == null || candidate.CompareTo(latest) > 0)
                {
                    latest = candidate;
                }
            }

            return latest;
        }

        // "v1.1.0" 等のタグ表記を Version に変換できるかどうか(コミットハッシュ指定なら false になる)。
        private static bool TryParseRefAsVersion(string currentRef, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(currentRef))
            {
                return false;
            }

            var body = currentRef.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? currentRef.Substring(1) : currentRef;
            return SemVer.TryParse(body, out version);
        }
    }
}
