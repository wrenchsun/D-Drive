using System.IO;
using System.Text.RegularExpressions;
using DDrive.Editor.Settings;
using DDrive.Editor.Update;
using DDrive.Runtime;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace DDrive.Tests.Editor.Compat
{
    // [42_distribution.md] §4.1 / §5.11-9(P-3、2026-09-20) — 版の 3 者一致を検査する。
    //   (1) `package.json` の "version"
    //   (2) `DDriveVersion.Value`(Runtime から読める写し)
    //   (3) `CHANGELOG.md` の最新のバージョン見出し(`[Unreleased]` は除く)
    //
    // [47_review_p_tickets_2026-09-20.md] P1-3(b)(2026-09-20 修正) — 旧実装は
    // `Directory.GetParent(Application.dataPath)`(= プロジェクト直下)からの相対パスで
    // `CHANGELOG.md`/`package.json` を探していたため、持ち込み先(git URL 解決。
    // `Library/PackageCache/com.ddrive.core@<hash>` に展開される)では見つからず、
    // `package.json` の検査は常に `Assert.Ignore` で無効化されたままだった。
    // `PackageInfo.FindForAssembly` の `resolvedPath` を起点に `ChangelogLocator.ResolvePath`
    // (P-9 で同梱されたパッケージ内 CHANGELOG.md を先に見る)を使い、`package.json` も
    // `resolvedPath` 直下から読む(どちらの配置でも見つかる)。
    public class PackageVersionConsistencyTests
    {
        [Test]
        public void DDriveVersion_BaseVersion_MatchesLatestChangelogHeading()
        {
            var packageInfo = PackageInfo.FindForAssembly(typeof(DDriveVersion).Assembly);
            Assert.IsNotNull(packageInfo, "com.ddrive.core の PackageInfo が解決できません。");

            var preferDevRepoRoot = DDriveProjectSettings.instance.IsDevelopmentRepo;
            var changelogPath = ChangelogLocator.ResolvePath(packageInfo.resolvedPath, preferDevRepoRoot);
            Assert.IsNotNull(changelogPath, $"CHANGELOG.md が見つかりません(resolvedPath={packageInfo.resolvedPath})。");

            var changelogVersion = FindLatestChangelogVersion(File.ReadAllText(changelogPath));
            Assert.IsNotNull(changelogVersion, "CHANGELOG.md に `## [X.Y.Z]` 形式の見出しが見つかりません(`[Unreleased]` 以外)。");

            var runtimeBaseVersion = StripPrerelease(DDriveVersion.Value);

            Assert.AreEqual(changelogVersion, runtimeBaseVersion,
                $"DDriveVersion.Value(base={runtimeBaseVersion})と CHANGELOG.md({changelogPath})の最新見出し({changelogVersion})が一致しません。" +
                "[42_distribution.md] §4.1 のとおり、リリース時は両方を同じ版に揃えてください。");
        }

        [Test]
        public void PackageJson_MatchesDDriveVersionBaseVersion()
        {
            var packageInfo = PackageInfo.FindForAssembly(typeof(DDriveVersion).Assembly);
            Assert.IsNotNull(packageInfo, "com.ddrive.core の PackageInfo が解決できません。");

            var packageJsonPath = packageInfo.resolvedPath.TrimEnd('/', '\\') + "/package.json";
            Assert.IsTrue(File.Exists(packageJsonPath), $"{packageJsonPath} が見つかりません。");

            var json = File.ReadAllText(packageJsonPath);
            var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
            Assert.IsTrue(match.Success, $"{packageJsonPath} に \"version\" フィールドが見つかりません。");

            var packageBaseVersion = StripPrerelease(match.Groups[1].Value);
            var runtimeBaseVersion = StripPrerelease(DDriveVersion.Value);
            Assert.AreEqual(runtimeBaseVersion, packageBaseVersion, "package.json の version と DDriveVersion.Value が一致しません。");
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
