using DDrive.Editor.Spec;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [32_spec_web.md] §5.2/§7 W-9 — API トークンは .asset(git 管理)ではなく EditorPrefs
    // (マシンごと)に保存する。実機の EditorPrefs を汚さないよう、SetUp/TearDown で
    // 元の値を保存・復元する(このテストは開発者の実際のマシンで走る)。
    public class DDriveSpecSettingsTests
    {
        private string _prevReadToken;
        private string _prevWriteToken;

        [SetUp]
        public void SetUp()
        {
            _prevReadToken = DDriveSpecSettings.ReadToken;
            _prevWriteToken = DDriveSpecSettings.WriteToken;
        }

        [TearDown]
        public void TearDown()
        {
            DDriveSpecSettings.ReadToken = _prevReadToken;
            DDriveSpecSettings.WriteToken = _prevWriteToken;
        }

        [Test]
        public void ReadToken_RoundTrips()
        {
            DDriveSpecSettings.ReadToken = "test-read-token-value";
            Assert.AreEqual("test-read-token-value", DDriveSpecSettings.ReadToken);
        }

        [Test]
        public void WriteToken_RoundTrips()
        {
            DDriveSpecSettings.WriteToken = "test-write-token-value";
            Assert.AreEqual("test-write-token-value", DDriveSpecSettings.WriteToken);
        }

        [Test]
        public void ReadToken_And_WriteToken_AreIndependent()
        {
            DDriveSpecSettings.ReadToken = "read-value";
            DDriveSpecSettings.WriteToken = "write-value";

            Assert.AreEqual("read-value", DDriveSpecSettings.ReadToken);
            Assert.AreEqual("write-value", DDriveSpecSettings.WriteToken);
        }

        [Test]
        public void ReadToken_Unset_DefaultsToEmptyString()
        {
            DDriveSpecSettings.ReadToken = null;
            Assert.AreEqual(string.Empty, DDriveSpecSettings.ReadToken);
        }
    }
}
