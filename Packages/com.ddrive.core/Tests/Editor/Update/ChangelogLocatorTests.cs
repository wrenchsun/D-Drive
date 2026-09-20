using System.Collections.Generic;
using DDrive.Editor.Update;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // [42_distribution.md] §4.2 手順 1/§6 P-8(2026-09-20) — CHANGELOG.md の探索順(開発リポジトリの
    // ルート → パッケージ直下)の EditMode テスト。実ファイルシステムには一切触れない
    // (FileExists 判定を差し替えられるようにしてあるため)。
    public class ChangelogLocatorTests
    {
        [Test]
        public void ResolvePath_FindsDevRepoRootChangelog_TwoLevelsAbovePackage()
        {
            const string resolvedPath = "/repo/Packages/com.ddrive.core";
            var existing = new HashSet<string> { "/repo/CHANGELOG.md" };

            var result = ChangelogLocator.ResolvePath(resolvedPath, existing.Contains);

            Assert.AreEqual("/repo/CHANGELOG.md", result);
        }

        [Test]
        public void ResolvePath_FallsBackToPackageRoot_WhenDevRepoChangelogMissing()
        {
            const string resolvedPath = "/repo/Packages/com.ddrive.core";
            var existing = new HashSet<string> { "/repo/Packages/com.ddrive.core/CHANGELOG.md" };

            var result = ChangelogLocator.ResolvePath(resolvedPath, existing.Contains);

            Assert.AreEqual("/repo/Packages/com.ddrive.core/CHANGELOG.md", result);
        }

        [Test]
        public void ResolvePath_NeitherExists_ReturnsNull()
        {
            const string resolvedPath = "/repo/Packages/com.ddrive.core";
            var existing = new HashSet<string>();

            var result = ChangelogLocator.ResolvePath(resolvedPath, existing.Contains);

            Assert.IsNull(result);
        }

        [Test]
        public void ResolvePath_NullOrEmptyResolvedPath_ReturnsNull()
        {
            Assert.IsNull(ChangelogLocator.ResolvePath(null, _ => true));
            Assert.IsNull(ChangelogLocator.ResolvePath(string.Empty, _ => true));
        }

        [Test]
        public void ResolvePath_PrefersDevRepoOverPackageRoot_WhenBothExist()
        {
            const string resolvedPath = "/repo/Packages/com.ddrive.core";
            var existing = new HashSet<string>
            {
                "/repo/CHANGELOG.md",
                "/repo/Packages/com.ddrive.core/CHANGELOG.md",
            };

            var result = ChangelogLocator.ResolvePath(resolvedPath, existing.Contains);

            Assert.AreEqual("/repo/CHANGELOG.md", result);
        }
    }
}
