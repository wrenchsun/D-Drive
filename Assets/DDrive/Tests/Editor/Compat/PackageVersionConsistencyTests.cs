using System.IO;
using System.Text.RegularExpressions;
using DDrive.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor.Compat
{
    // [42_distribution.md] §4.1 / §5.11-9(P-3、2026-09-20) — 版の 3 者一致を検査する。
    //   (1) `package.json` の "version"(P-5 でパッケージ化するまでは存在しない。存在すれば比較する)
    //   (2) `DDriveVersion.Value`(Runtime から読める写し)
    //   (3) `CHANGELOG.md` の最新のバージョン見出し(`[Unreleased]` は除く)
    //
    // `DDriveVersion.Value` は現時点で "1.0.0-dev"(パッケージ化前を示すプレリリースサフィックス付き)。
    // CHANGELOG の見出しは "1.0.0"(パッケージ化=P-5 で発効予定、まだ未リリース)。両者は
    // MAJOR.MINOR.PATCH の部分だけを比較する(サフィックスの有無は「発効済みか」を表すだけで、
    // 3 者の意味的な版が食い違っていないかを見るのが本テストの目的のため)。
    public class PackageVersionConsistencyTests
    {
        [Test]
        public void DDriveVersion_BaseVersion_MatchesLatestChangelogHeading()
        {
            var changelogPath = FindRepoFile("CHANGELOG.md");
            Assert.IsTrue(File.Exists(changelogPath), $"{changelogPath} が見つかりません。");

            var changelogVersion = FindLatestChangelogVersion(File.ReadAllText(changelogPath));
            Assert.IsNotNull(changelogVersion, "CHANGELOG.md に `## [X.Y.Z]` 形式の見出しが見つかりません(`[Unreleased]` 以外)。");

            var runtimeBaseVersion = StripPrerelease(DDriveVersion.Value);

            Assert.AreEqual(changelogVersion, runtimeBaseVersion,
                $"DDriveVersion.Value(base={runtimeBaseVersion})と CHANGELOG.md の最新見出し({changelogVersion})が一致しません。" +
                "[42_distribution.md] §4.1 のとおり、リリース時は両方を同じ版に揃えてください。");
        }

        [Test]
        public void PackageJson_IfExists_MatchesDDriveVersionBaseVersion()
        {
            var packageJsonPath = FindRepoFile("Packages/com.ddrive.core/package.json");
            if (!File.Exists(packageJsonPath))
            {
                Assert.Ignore("package.json はまだ存在しません(P-5 でパッケージ化するまでは無くてよい、[42] §5.11-9)。");
                return;
            }

            var json = File.ReadAllText(packageJsonPath);
            var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
            Assert.IsTrue(match.Success, $"{packageJsonPath} に \"version\" フィールドが見つかりません。");

            var packageBaseVersion = StripPrerelease(match.Groups[1].Value);
            var runtimeBaseVersion = StripPrerelease(DDriveVersion.Value);
            Assert.AreEqual(runtimeBaseVersion, packageBaseVersion, "package.json の version と DDriveVersion.Value が一致しません。");
        }

        private static string FindRepoFile(string relativePath)
        {
            var repoRoot = Directory.GetParent(Application.dataPath)!.FullName;
            return Path.Combine(repoRoot, relativePath).Replace('\\', '/');
        }

        // "## [1.0.0] - 未リリース" のような見出しから最初に見つかったバージョン番号を返す。
        // "[Unreleased]" は数字にマッチしないため自然に読み飛ばされる。
        private static string FindLatestChangelogVersion(string changelog)
        {
            var match = Regex.Match(changelog, @"^##\s*\[(\d+\.\d+\.\d+)\]", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string StripPrerelease(string version)
        {
            var dash = version.IndexOf('-');
            return dash >= 0 ? version.Substring(0, dash) : version;
        }
    }
}
