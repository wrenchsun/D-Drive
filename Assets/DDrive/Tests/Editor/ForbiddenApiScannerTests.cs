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
    }
}
