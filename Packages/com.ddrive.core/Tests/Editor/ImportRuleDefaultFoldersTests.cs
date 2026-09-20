using System.IO;
using DDrive.Editor.Import;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] 5-11 追加分(2026-09-14) — ImportRuleDefaultFolders(SourceAssets の既定フォルダ + README 生成)のテスト。
    // 実データ(Assets/SourceAssets)を汚さないため、一時フォルダに対して EnsureDefaultFolders を直接呼ぶ。
    public class ImportRuleDefaultFoldersTests
    {
        private const string TempRoot = TestTempFolder.Root + "/TempDefaultFoldersSourceAssets";

        [SetUp]
        public void SetUp()
        {
            ImportRulePostprocessor.Suppress = true;
            ImportRuleService.ResetImportHintStateForTests();
            CleanupTempRoot();
        }

        [TearDown]
        public void TearDown()
        {
            ImportRulePostprocessor.Suppress = false;
            CleanupTempRoot();
        }

        private static void CleanupTempRoot()
        {
            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }
        }

        private static void EnsureDiskFolder(string assetFolderPath)
        {
            var absolute = Path.GetFullPath(assetFolderPath);
            if (!Directory.Exists(absolute))
            {
                Directory.CreateDirectory(absolute);
            }
        }

        [Test]
        public void EnsureDefaultFolders_CreatesAllTypeFolders_WithReadme()
        {
            var report = ImportRuleDefaultFolders.EnsureDefaultFolders(TempRoot);

            // 種別フォルダの数 + ルートフォルダ自体 + Cutscene(6-10c、Handlers に無い専用フォルダ) = Handlers.Count + 2
            Assert.AreEqual(ImportRuleService.Handlers.Count + 2, report.CreatedFolders);
            // README: ルート 1 つ + 種別フォルダ分 + Cutscene
            Assert.AreEqual(ImportRuleService.Handlers.Count + 2, report.CreatedReadmes);

            Assert.IsTrue(AssetDatabase.IsValidFolder(TempRoot));
            Assert.IsTrue(File.Exists(Path.GetFullPath($"{TempRoot}/README.md")));

            foreach (var handler in ImportRuleService.Handlers)
            {
                var folder = $"{TempRoot}/{handler.TypeFolder}";
                Assert.IsTrue(AssetDatabase.IsValidFolder(folder), $"{folder} should exist");
                var readme = $"{folder}/README.md";
                Assert.IsTrue(File.Exists(Path.GetFullPath(readme)), $"{readme} should exist");

                var content = File.ReadAllText(Path.GetFullPath(readme));
                StringAssert.Contains(handler.TypeFolder, content);
                foreach (var ext in handler.Extensions)
                {
                    StringAssert.Contains(ext, content);
                }
            }

            // Cutscene(6-10c): ImportRuleService.Handlers には無い専用フォルダ([26_timeline.md] §6)。
            var cutsceneFolder = $"{TempRoot}/{DDrive.Editor.Cutscene.CutsceneImportService.TypeFolder}";
            Assert.IsTrue(AssetDatabase.IsValidFolder(cutsceneFolder), $"{cutsceneFolder} should exist");
            var cutsceneReadme = $"{cutsceneFolder}/README.md";
            Assert.IsTrue(File.Exists(Path.GetFullPath(cutsceneReadme)), $"{cutsceneReadme} should exist");
            StringAssert.Contains(".fbx", File.ReadAllText(Path.GetFullPath(cutsceneReadme)));
        }

        [Test]
        public void EnsureDefaultFolders_IsIdempotent()
        {
            ImportRuleDefaultFolders.EnsureDefaultFolders(TempRoot);

            var second = ImportRuleDefaultFolders.EnsureDefaultFolders(TempRoot);
            Assert.AreEqual(0, second.CreatedFolders);
            Assert.AreEqual(0, second.CreatedReadmes);
        }

        [Test]
        public void EnsureDefaultFolders_DoesNotOverwriteExistingReadme()
        {
            ImportRuleDefaultFolders.EnsureDefaultFolders(TempRoot);

            var seReadme = Path.GetFullPath($"{TempRoot}/Se/README.md");
            const string customContent = "デザイナーが書いたメモ";
            File.WriteAllText(seReadme, customContent);
            AssetDatabase.ImportAsset($"{TempRoot}/Se/README.md", ImportAssetOptions.ForceSynchronousImport);

            var report = ImportRuleDefaultFolders.EnsureDefaultFolders(TempRoot);
            Assert.AreEqual(0, report.CreatedReadmes);
            Assert.AreEqual(customContent, File.ReadAllText(seReadme));
        }

        [Test]
        public void EnsureDefaultFolders_SkipsReadme_WhenExistingFolderCollidesByCaseOnly()
        {
            // Windows 等の大文字小文字を区別しないファイルシステムでは、"Se" のつもりで作ったフォルダが
            // 既存の "se" に解決されてしまうことがある(実プロジェクトの Assets/SourceAssets/model と
            // ImportRuleService の "Model" 種別フォルダで実際に起きた事故の再現)。
            // その場合は既存フォルダ(無関係なファイルが入っている可能性がある)を README で汚さないこと。
            var lowerCaseFolder = $"{TempRoot}/se";
            EnsureDiskFolder(lowerCaseFolder);
            AssetDatabase.Refresh();

            if (!AssetDatabase.IsValidFolder($"{TempRoot}/Se"))
            {
                // 大文字小文字を区別するファイルシステム(Linux 等)では "se" と "Se" は別フォルダなので、
                // このテストが再現したい衝突自体が起きない。実行環境依存のため素直に無視する。
                Assert.Ignore("このファイルシステムは大文字小文字を区別するため、衝突を再現できない");
            }

            var report = ImportRuleDefaultFolders.EnsureDefaultFolders(TempRoot);

            var seReadme = Path.GetFullPath($"{TempRoot}/Se/README.md");
            Assert.IsFalse(File.Exists(seReadme), "README should not be written into the colliding folder");
            StringAssert.Contains("衝突", report.ToString());
        }

        [Test]
        public void GeneratedReadme_IsNotPickedUpByImportRule_AndDoesNotWarn()
        {
            ImportRuleDefaultFolders.EnsureDefaultFolders(TempRoot);

            // README.md は拡張子的にもどの種別にも一致しないが、案内ログの対象にもならないこと
            // (「置き方を間違えたファイル」ではなくツール自身が置いた説明書のため)。
            var report = ImportRuleService.ScanAll(TempRoot, TestTempFolder.Root + "/TempDefaultFoldersGameData");
            Assert.AreEqual(0, report.Created);
            LogAssert.NoUnexpectedReceived();

            if (AssetDatabase.IsValidFolder(TestTempFolder.Root + "/TempDefaultFoldersGameData"))
            {
                AssetDatabase.DeleteAsset(TestTempFolder.Root + "/TempDefaultFoldersGameData");
            }
        }
    }
}
