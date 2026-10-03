using DDrive.Editor.Update;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // [42_distribution.md] §4.2 P-15 追加修正(2026-10-03) — git CLI に渡す引数の組み立て(実 git は呼ばない)。
    // 外から来る URL・タグ名・パスが `-` で始まってもオプションとして解釈されないこと(`--` 区切り + 実行前の拒否)。
    // 2026-10-03(レビュー PC-R-01): 取得は作業ツリーを作らない(`--no-checkout` + `git show HEAD:<path>/package.json`)。
    public class GitArgumentsTests
    {
        [TestCase("https://github.com/o/r.git", true)]
        [TestCase("git@github.com:o/r.git", true)]
        [TestCase("C:/repos/pkg", true)]
        [TestCase("v1.2.3", true)]
        [TestCase("Packages/com.ddrive.core", true)]
        [TestCase(".", true)]
        [TestCase("-v", false)]
        [TestCase("--upload-pack=calc", false)]
        [TestCase("-", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("v1\nrm", false)]
        [TestCase("v1\0x", false)]
        public void IsSafeValue_RejectsOptionLikeAndControlCharacters(string value, bool expected)
            => Assert.AreEqual(expected, GitArguments.IsSafeValue(value));

        [Test]
        public void LsRemoteTags_PutsUrlAfterDoubleDash()
            => CollectionAssert.AreEqual(new[] { "ls-remote", "--tags", "--", "https://example.com/r.git" }, GitArguments.LsRemoteTags("https://example.com/r.git"));

        [Test]
        public void CloneNoCheckout_NeverChecksOutAWorkingTree_PutsPositionalArgumentsAfterDoubleDash_AndKeepsEachValueAsOneArgument()
        {
            var args = GitArguments.CloneNoCheckout("https://example.com/my repo.git", "v1.0.0", "C:/proj/Temp/DDriveUpdate/x y");

            CollectionAssert.AreEqual(
                new[]
                {
                    "clone", "--depth", "1", "--filter=blob:none", "--no-checkout", "--no-tags", "--branch", "v1.0.0", "--",
                    "https://example.com/my repo.git", "C:/proj/Temp/DDriveUpdate/x y",
                },
                args,
                "空白を含む値も 1 引数のまま(引用符を付けない)");
            CollectionAssert.DoesNotContain(args, "--sparse");
            Assert.Less(System.Array.IndexOf(args, "--"), System.Array.IndexOf(args, "https://example.com/my repo.git"));
        }

        [Test]
        public void ShowPackageJson_UsesHeadAndNeverAUserSuppliedRevision()
        {
            CollectionAssert.AreEqual(new[] { "show", "HEAD:Packages/com.ddrive.core/package.json" }, GitArguments.ShowPackageJson("Packages/com.ddrive.core"));
            CollectionAssert.AreEqual(new[] { "show", "HEAD:package.json" }, GitArguments.ShowPackageJson(null));
            CollectionAssert.AreEqual(new[] { "show", "HEAD:package.json" }, GitArguments.ShowPackageJson(string.Empty));
        }

        [TestCase(null, true, null)]
        [TestCase("", true, null)]
        [TestCase("/", true, null)]
        [TestCase("unity/com.tdrive.toon", true, "unity/com.tdrive.toon")]
        [TestCase("/unity/com.tdrive.toon", true, "unity/com.tdrive.toon")] // UPM の ?path=/sub(リポジトリ直下基準)
        [TestCase("unity\\com.tdrive.toon/", true, "unity/com.tdrive.toon")]
        [TestCase("../outside", false, null)]
        [TestCase("a/../../b", false, null)]
        [TestCase("a/./b", false, null)]
        [TestCase("a//b", false, null)]
        [TestCase("..", false, null)]
        [TestCase("C:/Windows", false, null)]
        [TestCase("C:\\Windows", false, null)]
        [TestCase("//server/share", false, null)]
        [TestCase("-x", false, null)]
        [TestCase("--stdin", false, null)]
        [TestCase("a\nb", false, null)]
        [TestCase("a\0b", false, null)]
        public void TryNormalizePackagePath_AcceptsOnlyRepositoryRelativePaths(string input, bool ok, string expected)
        {
            Assert.AreEqual(ok, GitArguments.TryNormalizePackagePath(input, out var normalized));
            if (ok)
            {
                Assert.AreEqual(expected, normalized);
            }
        }

        [Test]
        public void FirstUnsafe_ReturnsTheNameOfTheFirstBadValue_OrNull()
        {
            var names = new[] { "url", "ref", "path" };
            Assert.IsNull(GitArguments.FirstUnsafe(names, new[] { "https://x/y.git", "v1", "." }));
            Assert.AreEqual("ref", GitArguments.FirstUnsafe(names, new[] { "https://x/y.git", "--upload-pack=x", "." }));
            Assert.AreEqual("url", GitArguments.FirstUnsafe(names, new[] { "-oProxyCommand=x", "--bad", "." }));
        }

        [Test]
        public void Fetchers_RejectOptionLikeValues_WithWarning_AndNeverThrow()
        {
            // 実 git は起動しない(引数の検査が先に弾く)。
            var lister = new GitCliTagLister();
            Assert.IsNull(lister.ListTags("--upload-pack=calc", out var warning));
            StringAssert.Contains("-", warning);

            var fetcher = new GitPackageJsonFetcher("C:/proj/Temp/DDriveUpdate");
            Assert.IsNull(fetcher.FetchPackageJson("https://example.com/r.git", "Packages/x", "--branch=evil", out warning));
            Assert.IsNotEmpty(warning);
            Assert.IsNull(fetcher.FetchPackageJson("-c", "Packages/x", "v1.0.0", out warning));
            Assert.IsNotEmpty(warning);
            Assert.IsNull(fetcher.FetchPackageJson("https://example.com/r.git", "-x", "v1.0.0", out warning));
            Assert.IsNotEmpty(warning);
            Assert.IsNull(fetcher.FetchPackageJson("https://example.com/r.git", "../../etc", "v1.0.0", out warning));
            StringAssert.Contains("?path=", warning);
        }
    }
}
