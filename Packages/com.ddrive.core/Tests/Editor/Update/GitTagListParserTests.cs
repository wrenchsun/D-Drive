using System;
using DDrive.Editor.Update;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // [42_distribution.md] §4.2/§6 P-14(2026-09-20) — `git ls-remote --tags` の標準出力の解析
    // (`GitTagListParser.Parse`)を固定する。実プロセスには一切触れない。
    public class GitTagListParserTests
    {
        [Test]
        public void Parse_ExcludesPeeledLines_KeepsOnlyPlainTagRef()
        {
            const string output =
                "a1b2c3\trefs/tags/v1.0.0\n" +
                "d4e5f6\trefs/tags/v1.0.0^{}\n";

            var result = GitTagListParser.Parse(output);

            CollectionAssert.AreEqual(new[] { new Version(1, 0, 0) }, result);
        }

        [Test]
        public void Parse_IgnoresNonSemVerTags()
        {
            const string output =
                "a1b2c3\trefs/tags/v1.0.0\n" +
                "d4e5f6\trefs/tags/latest\n" +
                "778899\trefs/tags/not-a-version\n";

            var result = GitTagListParser.Parse(output);

            CollectionAssert.AreEqual(new[] { new Version(1, 0, 0) }, result);
        }

        [Test]
        public void Parse_SortsDescending()
        {
            const string output =
                "a1\trefs/tags/v1.0.0\n" +
                "a2\trefs/tags/v1.1.0\n" +
                "a3\trefs/tags/v0.9.0\n" +
                "a4\trefs/tags/v2.0.0\n";

            var result = GitTagListParser.Parse(output);

            CollectionAssert.AreEqual(
                new[] { new Version(2, 0, 0), new Version(1, 1, 0), new Version(1, 0, 0), new Version(0, 9, 0) },
                result);
        }

        [Test]
        public void Parse_IgnoresNonRefsTagsLines()
        {
            const string output =
                "a1\trefs/heads/main\n" +
                "a2\trefs/tags/v1.0.0\n";

            var result = GitTagListParser.Parse(output);

            CollectionAssert.AreEqual(new[] { new Version(1, 0, 0) }, result);
        }

        [Test]
        public void Parse_TagWithoutVPrefix_IsStillParsed()
        {
            const string output = "a1\trefs/tags/1.2.3\n";

            var result = GitTagListParser.Parse(output);

            CollectionAssert.AreEqual(new[] { new Version(1, 2, 3) }, result);
        }

        [TestCase(null)]
        [TestCase("")]
        public void Parse_NullOrEmptyInput_ReturnsEmptyList(string input)
        {
            var result = GitTagListParser.Parse(input);

            Assert.IsEmpty(result);
        }
    }
}
