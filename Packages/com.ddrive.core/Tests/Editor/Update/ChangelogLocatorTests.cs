using System.Collections.Generic;
using DDrive.Editor.Update;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // [42_distribution.md] §4.2 手順 1/§6 P-8(2026-09-20) — CHANGELOG.md の探索順の EditMode テスト。
    // [47_review_p_tickets_2026-09-20.md] P2-4(2026-09-20 修正) — 既定(持ち込み先、preferDevRepoRoot=false)
    // はパッケージ直下を先に見る。開発リポジトリ(preferDevRepoRoot=true)だけ 2 階層上(リポジトリ直下)を
    // 先に見る。実ファイルシステムには一切触れない(FileExists 判定を差し替えられるようにしてあるため)。
    public class ChangelogLocatorTests
    {
        [Test]
        public void ResolvePath_PreferDevRepoRoot_FindsDevRepoRootChangelog_TwoLevelsAbovePackage()
        {
            const string resolvedPath = "/repo/Packages/com.ddrive.core";
            var existing = new HashSet<string> { "/repo/CHANGELOG.md" };

            var result = ChangelogLocator.ResolvePath(resolvedPath, preferDevRepoRoot: true, fileExists: existing.Contains);

            Assert.AreEqual("/repo/CHANGELOG.md", result);
        }

        [Test]
        public void ResolvePath_PreferDevRepoRoot_FallsBackToPackageRoot_WhenDevRepoChangelogMissing()
        {
            const string resolvedPath = "/repo/Packages/com.ddrive.core";
            var existing = new HashSet<string> { "/repo/Packages/com.ddrive.core/CHANGELOG.md" };

            var result = ChangelogLocator.ResolvePath(resolvedPath, preferDevRepoRoot: true, fileExists: existing.Contains);

            Assert.AreEqual("/repo/Packages/com.ddrive.core/CHANGELOG.md", result);
        }

        [Test]
        public void ResolvePath_NeitherExists_ReturnsNull()
        {
            const string resolvedPath = "/repo/Packages/com.ddrive.core";
            var existing = new HashSet<string>();

            var result = ChangelogLocator.ResolvePath(resolvedPath, preferDevRepoRoot: false, fileExists: existing.Contains);

            Assert.IsNull(result);
        }

        [Test]
        public void ResolvePath_NullOrEmptyResolvedPath_ReturnsNull()
        {
            Assert.IsNull(ChangelogLocator.ResolvePath(null, preferDevRepoRoot: false, fileExists: _ => true));
            Assert.IsNull(ChangelogLocator.ResolvePath(string.Empty, preferDevRepoRoot: false, fileExists: _ => true));
        }

        [Test]
        public void ResolvePath_PreferDevRepoRoot_PrefersDevRepoOverPackageRoot_WhenBothExist()
        {
            const string resolvedPath = "/repo/Packages/com.ddrive.core";
            var existing = new HashSet<string>
            {
                "/repo/CHANGELOG.md",
                "/repo/Packages/com.ddrive.core/CHANGELOG.md",
            };

            var result = ChangelogLocator.ResolvePath(resolvedPath, preferDevRepoRoot: true, fileExists: existing.Contains);

            Assert.AreEqual("/repo/CHANGELOG.md", result);
        }

        // [47] P2-4 — 既定(持ち込み先)はパッケージ直下を先に見る。両方存在してもパッケージ直下を返す。
        [Test]
        public void ResolvePath_DefaultDoesNotPreferDevRepoRoot_PrefersPackageRoot_WhenBothExist()
        {
            const string resolvedPath = "/consumer-project/Packages/com.ddrive.core";
            var existing = new HashSet<string>
            {
                "/consumer-project/CHANGELOG.md", // 持ち込み先自身の CHANGELOG.md(誤認してはいけない)
                "/consumer-project/Packages/com.ddrive.core/CHANGELOG.md", // D-Drive 本体の CHANGELOG.md(正本)
            };

            var result = ChangelogLocator.ResolvePath(resolvedPath, preferDevRepoRoot: false, fileExists: existing.Contains);

            Assert.AreEqual("/consumer-project/Packages/com.ddrive.core/CHANGELOG.md", result);
        }

        // [47] P2-4 の再現条件そのもの: 埋め込み配置の持ち込み先で、D-Drive 自身に CHANGELOG.md が
        // 無い(まだ同梱していない旧版)場合は、持ち込み先自身の CHANGELOG.md にフォールバックしてしまうが、
        // これは「見つからない」よりはまし(かつ P-9 以降は D-Drive 側に必ず同梱されるため実際には
        // 発生しない)。少なくとも「両方あるときにパッケージ直下を優先する」ことを上のテストで固定する。
        [Test]
        public void ResolvePath_DefaultDoesNotPreferDevRepoRoot_FallsBackToDevRepoRoot_WhenPackageChangelogMissing()
        {
            const string resolvedPath = "/consumer-project/Packages/com.ddrive.core";
            var existing = new HashSet<string> { "/consumer-project/CHANGELOG.md" };

            var result = ChangelogLocator.ResolvePath(resolvedPath, preferDevRepoRoot: false, fileExists: existing.Contains);

            Assert.AreEqual("/consumer-project/CHANGELOG.md", result);
        }
    }
}
