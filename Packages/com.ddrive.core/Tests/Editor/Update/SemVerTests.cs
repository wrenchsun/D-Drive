using DDrive.Editor.Update;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // [42_distribution.md] §4.1/§6 P-8(2026-09-20) — SemVer(MAJOR.MINOR.PATCH の比較)の EditMode テスト。
    public class SemVerTests
    {
        [TestCase("1.0.0", "1.0.1", true)]
        [TestCase("1.0.1", "1.0.0", false)]
        [TestCase("1.0.0", "1.0.0", false)]
        [TestCase("0.9.0", "1.0.0", true)]
        [TestCase("1.1.0", "1.0.9", false)]
        public void IsOlderThan_ComparesCorrectly(string a, string b, bool expected)
        {
            Assert.AreEqual(expected, SemVer.IsOlderThan(a, b));
        }

        [Test]
        public void IsOlderThan_WithPreReleaseSuffix_IgnoresSuffix()
        {
            Assert.IsFalse(SemVer.IsOlderThan("1.0.0-dev", "1.0.0"), "pre-release サフィックスは無視して比較する");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not-a-version")]
        public void IsOlderThan_UnparsableInput_ReturnsFalse(string value)
        {
            Assert.IsFalse(SemVer.IsOlderThan(value, "1.0.0"));
            Assert.IsFalse(SemVer.IsOlderThan("1.0.0", value));
        }

        [Test]
        public void Compare_UnparsableInput_ReturnsNull()
        {
            Assert.IsNull(SemVer.Compare("not-a-version", "1.0.0"));
        }

        [Test]
        public void TryParse_ValidVersion_ReturnsTrue()
        {
            Assert.IsTrue(SemVer.TryParse("1.2.3", out var version));
            Assert.AreEqual(new System.Version(1, 2, 3), version);
        }
    }
}
