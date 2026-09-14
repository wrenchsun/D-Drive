using DDrive.Editor.Manual;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [09_editor_tools.md] §6.1 — マニュアルボタンの URL 組み立て(純粋関数)のテスト。
    public class ManualUrlBuilderTests
    {
        [Test]
        public void BuildWebUrl_EmptyHumanAppUrl_ReturnsNull()
        {
            Assert.IsNull(ManualUrlBuilder.BuildWebUrl(string.Empty, "asset-browser"));
            Assert.IsNull(ManualUrlBuilder.BuildWebUrl(null, "asset-browser"));
        }

        [Test]
        public void BuildWebUrl_NoExistingQuery_UsesQuestionMark()
        {
            var url = ManualUrlBuilder.BuildWebUrl("https://example.com/app", "asset-browser");

            Assert.AreEqual("https://example.com/app?page=manual&p=asset-browser", url);
        }

        [Test]
        public void BuildWebUrl_ExistingQuery_AppendsWithAmpersand()
        {
            var url = ManualUrlBuilder.BuildWebUrl("https://example.com/app?foo=1", "asset-browser");

            Assert.AreEqual("https://example.com/app?foo=1&page=manual&p=asset-browser", url);
        }

        [Test]
        public void BuildWebUrl_PageNameIsEscaped()
        {
            var url = ManualUrlBuilder.BuildWebUrl("https://example.com/app", "テスト ページ");

            StringAssert.Contains("p=" + System.Uri.EscapeDataString("テスト ページ"), url);
            StringAssert.DoesNotContain(" ", url);
        }

        [Test]
        public void BuildLocalFileUrl_ReturnsFileUri_ContainingPage()
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DDriveManualTest");
            var url = ManualUrlBuilder.BuildLocalFileUrl(folder, "asset-browser");

            StringAssert.StartsWith("file://", url);
            var localPath = new System.Uri(url).LocalPath;
            Assert.AreEqual("asset-browser.html", System.IO.Path.GetFileName(localPath));
        }

        [Test]
        public void ResolveUseWeb_PreferWebAndUrlSet_ReturnsTrue()
        {
            Assert.IsTrue(ManualUrlBuilder.ResolveUseWeb(true, "https://example.com/app"));
        }

        [Test]
        public void ResolveUseWeb_PreferWebButUrlEmpty_ReturnsFalse()
        {
            Assert.IsFalse(ManualUrlBuilder.ResolveUseWeb(true, string.Empty));
            Assert.IsFalse(ManualUrlBuilder.ResolveUseWeb(true, null));
        }

        [Test]
        public void ResolveUseWeb_PreferLocal_ReturnsFalseEvenIfUrlSet()
        {
            Assert.IsFalse(ManualUrlBuilder.ResolveUseWeb(false, "https://example.com/app"));
        }
    }
}
