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
            // spec-sync は 2026-09-15 のマニュアル改訂でタイトルが「発注ツール（Web）と仕様書同期」に変わり、
            // 括弧を含むためこのテスト（括弧なしのページ）の対象から外した。
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

        // 2026-09-17 追記 — プログラマーマニュアル(docs/ProgrammerManual)側。
        // デザイナーマニュアル側のテストは変更せず、同じ照合を ManualKind.Programmer で行う。
        [Test]
        public void DiscoverPages_MatchesActualFilesOnDisk_Programmer()
        {
            var projectRoot = ManualPages.GetProjectRoot();
            var folder = ManualPages.GetManualFolder(projectRoot, ManualKind.Programmer);
            Assert.IsTrue(Directory.Exists(folder), "docs/ProgrammerManual が見つからない: " + folder);

            var actualFileNames = Directory.GetFiles(folder, "*.html")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.Equals(name, ManualPages.TopPageName, StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var pages = ManualPages.DiscoverPages(projectRoot, ManualKind.Programmer);

            Assert.AreEqual(actualFileNames, pages.Select(p => p.FileName).ToArray());
            Assert.IsTrue(pages.All(p => !string.IsNullOrEmpty(p.DisplayName)));
            CollectionAssert.Contains(actualFileNames, "concepts");
            CollectionAssert.Contains(actualFileNames, "extending");
        }

        [Test]
        public void DiscoverPages_ExcludesTopPage_Programmer()
        {
            var projectRoot = ManualPages.GetProjectRoot();
            var pages = ManualPages.DiscoverPages(projectRoot, ManualKind.Programmer);

            Assert.IsFalse(pages.Any(p => p.FileName == ManualPages.TopPageName));
        }

        [Test]
        public void ResolveDisplayName_KnownPagesWithoutParens_MatchesActualTitle_Programmer()
        {
            var projectRoot = ManualPages.GetProjectRoot();
            var folder = ManualPages.GetManualFolder(projectRoot, ManualKind.Programmer);

            Assert.AreEqual("基本概念", ManualPages.ResolveDisplayName(folder, "concepts", ManualKind.Programmer));
            Assert.AreEqual("VFX API", ManualPages.ResolveDisplayName(folder, "vfx-api", ManualKind.Programmer));
        }

        [Test]
        public void ResolveDisplayName_TopPage_MatchesReadmeTitle_Programmer()
        {
            var projectRoot = ManualPages.GetProjectRoot();
            var folder = ManualPages.GetManualFolder(projectRoot, ManualKind.Programmer);

            Assert.AreEqual("D-Drive プログラマーマニュアル", ManualPages.ResolveDisplayName(folder, ManualPages.TopPageName, ManualKind.Programmer));
        }

        [Test]
        public void ResolveDisplayName_MissingFile_FallsBackToFileName_Programmer()
        {
            var projectRoot = ManualPages.GetProjectRoot();
            var folder = ManualPages.GetManualFolder(projectRoot, ManualKind.Programmer);

            Assert.AreEqual("no-such-page", ManualPages.ResolveDisplayName(folder, "no-such-page", ManualKind.Programmer));
        }

        [Test]
        public void StripManualSuffix_RemovesKnownSuffix_Programmer()
        {
            Assert.AreEqual("基本概念", ManualPages.StripManualSuffix("基本概念 | D-Drive プログラマーマニュアル", ManualKind.Programmer));
        }

        [Test]
        public void StripManualSuffix_NoSuffix_ReturnsAsIs_Programmer()
        {
            Assert.AreEqual("D-Drive プログラマーマニュアル", ManualPages.StripManualSuffix("D-Drive プログラマーマニュアル", ManualKind.Programmer));
        }

        [Test]
        public void StripManualSuffix_DoesNotStripOtherKindSuffix()
        {
            // デザイナー側の接尾辞はプログラマー側の StripManualSuffix では剥がれない(種別の取り違え防止)。
            const string title = "用語集 | D-Drive デザイナーマニュアル";
            Assert.AreEqual(title, ManualPages.StripManualSuffix(title, ManualKind.Programmer));
        }

        // [47_review_p_tickets_2026-09-20.md] P2-2(2026-09-20) — 開発リポジトリでは docs/ を優先する。
        // ProjectSetupValidatorTests と同じ流儀でフィールドを直接書き換えて模擬する(Save を呼ばない)。
        [Test]
        public void GetManualFolder_DevelopmentRepo_PrefersDocsFolder_OverDocumentationTilde()
        {
            var settings = DDrive.Editor.Settings.DDriveProjectSettings.instance;
            var isDevField = typeof(DDrive.Editor.Settings.DDriveProjectSettings).GetField(
                "_isDevelopmentRepo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var originalIsDev = (bool)isDevField.GetValue(settings);

            try
            {
                isDevField.SetValue(settings, true);
                var projectRoot = ManualPages.GetProjectRoot();
                var folder = ManualPages.GetManualFolder(projectRoot);

                var expectedDocsFolder = Path.Combine(projectRoot, "docs/DesignerManual".Replace('/', Path.DirectorySeparatorChar));
                Assert.AreEqual(expectedDocsFolder, folder);
            }
            finally
            {
                isDevField.SetValue(settings, originalIsDev);
            }
        }

        [Test]
        public void GetManualFolder_NotDevelopmentRepo_PrefersDocumentationTilde_WhenBundled()
        {
            var settings = DDrive.Editor.Settings.DDriveProjectSettings.instance;
            var isDevField = typeof(DDrive.Editor.Settings.DDriveProjectSettings).GetField(
                "_isDevelopmentRepo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var originalIsDev = (bool)isDevField.GetValue(settings);

            try
            {
                isDevField.SetValue(settings, false);
                var projectRoot = ManualPages.GetProjectRoot();
                var folder = ManualPages.GetManualFolder(projectRoot);

                StringAssert.Contains("Documentation~", folder,
                    "このリポジトリは Documentation~/DesignerManual を同梱済み(P-9)のため、持ち込み先扱いではこちらが正本");
            }
            finally
            {
                isDevField.SetValue(settings, originalIsDev);
            }
        }
    }
}
