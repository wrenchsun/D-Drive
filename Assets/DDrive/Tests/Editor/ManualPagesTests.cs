using System;
using System.IO;
using System.Linq;
using DDrive.Editor.Manual;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [09_editor_tools.md] §6.1 — マニュアルのページ一覧(docs/DesignerManual/*.html)。
    // 実ファイルとハードコード/検出結果がずれていないかをここで照合する。
    public class ManualPagesTests
    {
        [Test]
        public void DiscoverPages_MatchesActualFilesOnDisk()
        {
            var projectRoot = ManualPages.GetProjectRoot();
            var folder = ManualPages.GetManualFolder(projectRoot);
            Assert.IsTrue(Directory.Exists(folder), "docs/DesignerManual が見つからない: " + folder);

            var actualFileNames = Directory.GetFiles(folder, "*.html")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.Equals(name, ManualPages.TopPageName, StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var pages = ManualPages.DiscoverPages(projectRoot);

            Assert.AreEqual(actualFileNames, pages.Select(p => p.FileName).ToArray());
            Assert.IsTrue(pages.All(p => !string.IsNullOrEmpty(p.DisplayName)));
            CollectionAssert.Contains(actualFileNames, "asset-browser");
            CollectionAssert.Contains(actualFileNames, "glossary");
        }

        [Test]
        public void DiscoverPages_ExcludesTopPage()
        {
            var projectRoot = ManualPages.GetProjectRoot();
            var pages = ManualPages.DiscoverPages(projectRoot);

            Assert.IsFalse(pages.Any(p => p.FileName == ManualPages.TopPageName));
        }

        [Test]
        public void ResolveDisplayName_KnownPagesWithoutParens_MatchesActualTitle()
        {
            var projectRoot = ManualPages.GetProjectRoot();
            var folder = ManualPages.GetManualFolder(projectRoot);

            Assert.AreEqual("用語集", ManualPages.ResolveDisplayName(folder, "glossary"));
            Assert.AreEqual("Asset Browser の使い方", ManualPages.ResolveDisplayName(folder, "asset-browser"));
            Assert.AreEqual("仕様書との同期", ManualPages.ResolveDisplayName(folder, "spec-sync"));
        }

        [Test]
        public void ResolveDisplayName_TopPage_MatchesReadmeTitle()
        {
            var projectRoot = ManualPages.GetProjectRoot();
            var folder = ManualPages.GetManualFolder(projectRoot);

            Assert.AreEqual("D-Drive デザイナーマニュアル", ManualPages.ResolveDisplayName(folder, ManualPages.TopPageName));
        }

        [Test]
        public void ResolveDisplayName_MissingFile_FallsBackToFileName()
        {
            var projectRoot = ManualPages.GetProjectRoot();
            var folder = ManualPages.GetManualFolder(projectRoot);

            Assert.AreEqual("no-such-page", ManualPages.ResolveDisplayName(folder, "no-such-page"));
        }

        [Test]
        public void ExtractTitle_ReturnsTrimmedContent()
        {
            const string html = "<html><head><title>  Example Title  </title></head></html>";

            Assert.AreEqual("Example Title", ManualPages.ExtractTitle(html));
        }

        [Test]
        public void ExtractTitle_NoTitleTag_ReturnsNull()
        {
            Assert.IsNull(ManualPages.ExtractTitle("<html><head></head></html>"));
            Assert.IsNull(ManualPages.ExtractTitle(string.Empty));
            Assert.IsNull(ManualPages.ExtractTitle(null));
        }

        [Test]
        public void StripManualSuffix_RemovesKnownSuffix()
        {
            Assert.AreEqual("用語集", ManualPages.StripManualSuffix("用語集 | D-Drive デザイナーマニュアル"));
        }

        [Test]
        public void StripManualSuffix_NoSuffix_ReturnsAsIs()
        {
            Assert.AreEqual("D-Drive デザイナーマニュアル", ManualPages.StripManualSuffix("D-Drive デザイナーマニュアル"));
        }
    }
}
