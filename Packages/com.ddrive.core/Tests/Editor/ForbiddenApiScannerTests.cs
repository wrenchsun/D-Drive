using System;
using System.IO;
using DDrive.Editor.Validation;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    public class ForbiddenApiScannerTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ddrive_forbidden_api_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        private string WriteFile(string name, string content)
        {
            var path = Path.Combine(_tempDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        [Test]
        public void Scan_DetectsDirectTimeTimeUsage()
        {
            WriteFile("SomeManager.cs", "var t = UnityEngine.Time.time;");

            var violations = ForbiddenApiScanner.Scan(_tempDir);

            Assert.AreEqual(1, violations.Count);
            Assert.AreEqual(1, violations[0].Line);
        }

        [Test]
        public void Scan_AllowsTimeUsageInAllowlistedFile()
        {
            WriteFile("LocalTimeSource.cs", "public double Time => UnityEngine.Time.timeAsDouble;");

            var violations = ForbiddenApiScanner.Scan(_tempDir);

            Assert.AreEqual(0, violations.Count);
        }

        [Test]
        public void Scan_DetectsInstantiateOutsidePoolService()
        {
            WriteFile("SomeSpawner.cs", "var go = Instantiate(prefab);");

            var violations = ForbiddenApiScanner.Scan(_tempDir);

            Assert.AreEqual(1, violations.Count);
        }

        [Test]
        public void Scan_DetectsQualifiedObjectInstantiateOutsidePoolService()
        {
            WriteFile("SomeSpawner.cs", "var go = Object.Instantiate(prefab);");

            var violations = ForbiddenApiScanner.Scan(_tempDir);

            Assert.AreEqual(1, violations.Count);
        }

        [Test]
        public void Scan_AllowsInstantiateInPoolService()
        {
            WriteFile("PoolService.cs", "var go = Instantiate(prefab);");

            var violations = ForbiddenApiScanner.Scan(_tempDir);

            Assert.AreEqual(0, violations.Count);
        }

        [Test]
        public void Scan_IgnoresCommentedOutLines()
        {
            WriteFile("SomeManager.cs", "// var t = Time.time;");

            var violations = ForbiddenApiScanner.Scan(_tempDir);

            Assert.AreEqual(0, violations.Count);
        }

        [Test]
        public void Scan_CleanFile_HasNoViolations()
        {
            WriteFile("Clean.cs", "public sealed class Clean { public void Do() { } }");

            var violations = ForbiddenApiScanner.Scan(_tempDir);

            Assert.AreEqual(0, violations.Count);
        }

        // [42_distribution.md] §2.3-2(P-4、2026-09-20) — 走査対象フォルダが存在しない/`.cs` が
        // 0 件のときは「違反 0 件」で静かに通さず、Error 扱いの Violation を返す(禁止 API チェックの
        // 恒久的な無効化を防ぐ)。
        [Test]
        public void Scan_MissingFolder_ReturnsErrorViolation()
        {
            var missing = Path.Combine(_tempDir, "does_not_exist");

            var violations = ForbiddenApiScanner.Scan(missing);

            Assert.AreEqual(1, violations.Count);
            StringAssert.Contains("見つかりません", violations[0].Message);
        }

        [Test]
        public void Scan_EmptyFolder_ReturnsErrorViolation()
        {
            var violations = ForbiddenApiScanner.Scan(_tempDir);

            Assert.AreEqual(1, violations.Count);
            StringAssert.Contains("0 件", violations[0].Message);
        }

        // [47_review_p_tickets_2026-09-20.md] P1-2 — Samples~/Tests/Tools~/Documentation~ の除外を
        // "/Samples/" だけでなく広げたことの回帰確認(P-5 の Samples~ 移設で増えた回帰の再発防止)。
        [Test]
        public void Scan_ExcludesTildeSamplesFolder()
        {
            var samplesDir = Path.Combine(_tempDir, "Samples~", "Demo");
            Directory.CreateDirectory(samplesDir);
            File.WriteAllText(Path.Combine(samplesDir, "Demo.cs"), "var t = UnityEngine.Time.time;");

            var violations = ForbiddenApiScanner.Scan(_tempDir);

            Assert.AreEqual(0, violations.Count);
        }

        [Test]
        public void Scan_ExcludesTestsFolder()
        {
            var testsDir = Path.Combine(_tempDir, "Tests", "Editor");
            Directory.CreateDirectory(testsDir);
            File.WriteAllText(Path.Combine(testsDir, "SomeTests.cs"), "var t = UnityEngine.Time.time;");

            var violations = ForbiddenApiScanner.Scan(_tempDir);

            Assert.AreEqual(0, violations.Count);
        }

        [Test]
        public void Scan_ExcludesToolsAndDocumentationTildeFolders()
        {
            var toolsDir = Path.Combine(_tempDir, "Tools~");
            var docsDir = Path.Combine(_tempDir, "Documentation~");
            Directory.CreateDirectory(toolsDir);
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(toolsDir, "Tool.cs"), "var t = UnityEngine.Time.time;");
            File.WriteAllText(Path.Combine(docsDir, "Snippet.cs"), "var t = UnityEngine.Time.time;");

            var violations = ForbiddenApiScanner.Scan(_tempDir);

            Assert.AreEqual(0, violations.Count);
        }
    }
}
