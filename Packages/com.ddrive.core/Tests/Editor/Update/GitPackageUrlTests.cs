using DDrive.Editor.Update;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // [42_distribution.md] §4.2/§6 P-14(2026-09-20) — `Packages/manifest.json` の `com.ddrive.core` の値の
    // 解析(`GitPackageUrl.Parse`)と `#ref` だけの差し替え(`WithRef`)を固定する。実ファイル・実プロセスに
    // 一切触れない。
    public class GitPackageUrlTests
    {
        [Test]
        public void Parse_GitHttps_ExtractsPrefixCloneUrlPathAndRef()
        {
            var result = GitPackageUrl.Parse("git+https://github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#v1.0.0");

            Assert.IsTrue(result.IsGitUrl);
            Assert.AreEqual("git+https", result.Prefix);
            Assert.AreEqual("https://github.com/wrenchsun/D-Drive.git", result.CloneUrl);
            Assert.AreEqual("Packages/com.ddrive.core", result.Path);
            Assert.AreEqual("v1.0.0", result.Ref);
        }

        [Test]
        public void Parse_GitSsh_ExtractsPrefixAndCloneUrl()
        {
            var result = GitPackageUrl.Parse("git+ssh://git@github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#v1.0.0");

            Assert.IsTrue(result.IsGitUrl);
            Assert.AreEqual("git+ssh", result.Prefix);
            Assert.AreEqual("ssh://git@github.com/wrenchsun/D-Drive.git", result.CloneUrl);
            Assert.AreEqual("Packages/com.ddrive.core", result.Path);
            Assert.AreEqual("v1.0.0", result.Ref);
        }

        [Test]
        public void Parse_CommitHashRef_IsGitUrl_WithFullHashAsRef()
        {
            const string hash = "6c65a8912345678901234567890123456789abcd";
            var result = GitPackageUrl.Parse($"git+https://github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#{hash}");

            Assert.IsTrue(result.IsGitUrl);
            Assert.AreEqual(hash, result.Ref);
        }

        [Test]
        public void Parse_NoPath_PathIsNull()
        {
            var result = GitPackageUrl.Parse("git+https://github.com/wrenchsun/D-Drive.git#v1.0.0");

            Assert.IsTrue(result.IsGitUrl);
            Assert.IsNull(result.Path);
            Assert.AreEqual("v1.0.0", result.Ref);
            Assert.AreEqual("https://github.com/wrenchsun/D-Drive.git", result.CloneUrl);
        }

        [Test]
        public void Parse_NoRef_RefIsNull()
        {
            var result = GitPackageUrl.Parse("git+https://github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core");

            Assert.IsTrue(result.IsGitUrl);
            Assert.IsNull(result.Ref);
            Assert.AreEqual("Packages/com.ddrive.core", result.Path);
        }

        [Test]
        public void Parse_BareHttpsGitUrl_WithoutGitPlusPrefix_IsRecognizedAsGitUrl()
        {
            // README の UniTask/R3 の依存記述と同じ形式("git+" 無しでも ".git" で終わる https)。
            var result = GitPackageUrl.Parse("https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11");

            Assert.IsTrue(result.IsGitUrl);
            Assert.AreEqual(string.Empty, result.Prefix);
            Assert.AreEqual("https://github.com/Cysharp/UniTask.git", result.CloneUrl);
            Assert.AreEqual("2.5.11", result.Ref);
        }

        [TestCase("1.0.0")]
        [TestCase("1.3.1")]
        public void Parse_RegistryVersionValue_IsNotGitUrl(string registryValue)
        {
            var result = GitPackageUrl.Parse(registryValue);

            Assert.IsFalse(result.IsGitUrl);
        }

        [Test]
        public void Parse_FileUrl_IsNotGitUrl()
        {
            var result = GitPackageUrl.Parse("file:../com.ddrive.core");

            Assert.IsFalse(result.IsGitUrl);
        }

        [TestCase(null)]
        [TestCase("")]
        public void Parse_NullOrEmpty_IsNotGitUrl(string value)
        {
            var result = GitPackageUrl.Parse(value);

            Assert.IsFalse(result.IsGitUrl);
        }

        [Test]
        public void WithRef_ReplacesOnlyTheRef_KeepsUrlPathAndPrefixFormat()
        {
            var original = "git+ssh://git@github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#v1.0.0";
            var parsed = GitPackageUrl.Parse(original);

            var updated = parsed.WithRef("v1.1.0");

            Assert.AreEqual("git+ssh://git@github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#v1.1.0", updated);
        }

        [Test]
        public void WithRef_OriginalHadNoRef_AppendsRef()
        {
            var parsed = GitPackageUrl.Parse("git+https://github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core");

            var updated = parsed.WithRef("v1.1.0");

            Assert.AreEqual("git+https://github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#v1.1.0", updated);
        }

        [Test]
        public void WithRef_NotGitUrl_ReturnsRawValueUnchanged()
        {
            var parsed = GitPackageUrl.Parse("1.0.0");

            var updated = parsed.WithRef("v1.1.0");

            Assert.AreEqual("1.0.0", updated);
        }
    }
}
