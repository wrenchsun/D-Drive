using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DDrive.Editor.Settings;
using DDrive.Editor.Validation;
using DDrive.Foundation.Validation;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] M-4(2026-10-05) — 禁止 API の許可(行単位の許可コメント + 設定の許可リスト)。
    // 一時フォルダにだけ書く(実プロジェクトのソース・実 DDriveProjectSettings には触れない)。
    public class ForbiddenApiAllowanceTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ddrive_forbidden_allow_" + Guid.NewGuid().ToString("N"));
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

        private string WriteFile(string name, params string[] lines)
        {
            var path = Path.Combine(_tempDir, name);
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, string.Join("\n", lines));
            return path;
        }

        private ForbiddenApiScanner.ScanReport Scan(IReadOnlyList<ForbiddenApiAllowEntry> entries = null)
            => ForbiddenApiScanner.ScanDetailed(_tempDir, entries);

        private static int Count(ForbiddenApiScanner.ScanReport r, string code) => r.Notices.Count(n => n.Code == code);

        [Test]
        public void SameLineComment_AllowsThatHit()
        {
            WriteFile("A.cs", "var t = Time.time; // ddrive-allow: Time(Host 引き継ぎのタイムアウトは実時間で測る)");
            var r = Scan();
            Assert.AreEqual(0, r.Violations.Count);
            Assert.AreEqual(1, r.Allowed.Count);
            Assert.AreEqual(ForbiddenApiScanner.AllowSource.Comment, r.Allowed[0].Source);
            Assert.AreEqual(1, Count(r, ForbiddenApiScanner.CodeAllowSummary));
        }

        [Test]
        public void PreviousLineComment_AllowsNextLineOnly()
        {
            WriteFile("A.cs",
                "// ddrive-allow: Instantiate(NGO の NetworkObject は Instantiate → Spawn が正規手順)",
                "var a = Instantiate(prefab);",
                "var b = Instantiate(prefab);");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            Assert.AreEqual(3, r.Violations[0].Line);
            Assert.AreEqual(1, r.Allowed.Count);
        }

        [Test]
        public void PreviousLineComment_DoesNotReachTwoLinesAhead()
        {
            WriteFile("A.cs",
                "// ddrive-allow: Time(理由)",
                "var x = 1;",
                "var t = Time.time;");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            Assert.AreEqual(1, Count(r, ForbiddenApiScanner.CodeAllowUnused));
        }

        [Test]
        public void OtherCommentLineBetween_BreaksTheAllow()
        {
            WriteFile("A.cs",
                "// ddrive-allow: Time(理由)",
                "// 補足",
                "var t = Time.time;");
            Assert.AreEqual(1, Scan().Violations.Count);
        }

        [Test]
        public void DifferentRuleName_DoesNotAllow()
        {
            WriteFile("A.cs", "var go = Instantiate(p); // ddrive-allow: Time(理由)");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            Assert.AreEqual(1, Count(r, ForbiddenApiScanner.CodeAllowUnused));
        }

        [Test]
        public void MissingReason_StaysViolation_WithMessage()
        {
            WriteFile("A.cs", "var t = Time.time; // ddrive-allow: Time");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            StringAssert.Contains("理由が必要", r.Violations[0].Message);
            Assert.AreEqual(0, r.Allowed.Count);
        }

        [Test]
        public void EmptyParentheses_AreInvalid()
        {
            WriteFile("A.cs", "var t = Time.time; // ddrive-allow: Time()");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            StringAssert.Contains("理由が必要", r.Violations[0].Message);
        }

        [Test]
        public void MissingReasonWithoutHit_GivesWarningNotice()
        {
            WriteFile("A.cs", "var x = 1; // ddrive-allow: Time");
            var r = Scan();
            Assert.AreEqual(0, r.Violations.Count);
            Assert.AreEqual(1, Count(r, ForbiddenApiScanner.CodeAllowNoReason));
            Assert.AreEqual(ValidationSeverity.Warning, r.Notices.First(n => n.Code == ForbiddenApiScanner.CodeAllowNoReason).Severity);
        }

        [Test]
        public void UnknownRuleName_IsInvalid_AndReported()
        {
            WriteFile("A.cs", "var t = Time.time; // ddrive-allow: Timez(理由)");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            Assert.AreEqual(1, Count(r, ForbiddenApiScanner.CodeAllowUnknownRule));
        }

        [Test]
        public void UnusedAllow_IsInfo()
        {
            WriteFile("A.cs", "var x = 1; // ddrive-allow: Time(もう要らない)");
            var r = Scan();
            var n = r.Notices.Single(x => x.Code == ForbiddenApiScanner.CodeAllowUnused);
            Assert.AreEqual(ValidationSeverity.Info, n.Severity);
            Assert.AreEqual(1, n.Line);
        }

        [Test]
        public void FullWidthParentheses_AndCaseInsensitiveRule_Work()
        {
            WriteFile("A.cs", "var t = Time.time; // DDRIVE-ALLOW: time（実時間で測る）");
            var r = Scan();
            Assert.AreEqual(0, r.Violations.Count);
            Assert.AreEqual(1, r.Allowed.Count);
            Assert.AreEqual("実時間で測る", r.Allowed[0].Reason);
        }

        [Test]
        public void ReasonMayContainParentheses()
        {
            WriteFile("A.cs", "var go = Instantiate(p); // ddrive-allow: Instantiate(NGO の Spawn(正規手順))");
            var r = Scan();
            Assert.AreEqual(0, r.Violations.Count);
            Assert.AreEqual("NGO の Spawn(正規手順)", r.Allowed[0].Reason);
        }

        [Test]
        public void TwoRulesOnOneLine_NeedTwoDirectives()
        {
            WriteFile("A.cs", "var t = Time.time + Instantiate(p).x; // ddrive-allow: Time(a) ddrive-allow: Instantiate(b)");
            var r = Scan();
            Assert.AreEqual(0, r.Violations.Count);
            Assert.AreEqual(2, r.Allowed.Count);
        }

        [Test]
        public void TwoRulesOnOneLine_OnlyOneDirective_LeavesTheOther()
        {
            WriteFile("A.cs", "var t = Time.time + Instantiate(p).x; // ddrive-allow: Time(a)");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            Assert.AreEqual(1, r.Allowed.Count);
        }

        [Test]
        public void DirectiveInsideStringLiteral_IsIgnored()
        {
            WriteFile("A.cs", "var s = \"// ddrive-allow: Time(x)\"; var t = Time.time;");
            var r = Scan();
            Assert.AreEqual(1, r.Violations.Count);
            Assert.AreEqual(0, r.Allowed.Count);
        }

        [Test]
        public void BlockCommentForm_IsNotRecognized()
        {
            WriteFile("A.cs", "var t = Time.time; /* ddrive-allow: Time(x) */");
            Assert.AreEqual(1, Scan().Violations.Count);
        }

        [Test]
        public void Settings_AllowsFolder()
        {
            WriteFile("ThirdParty/Lib.cs", "var t = Time.time;");
            WriteFile("Mine.cs", "var u = Time.time;");
            var entries = new List<ForbiddenApiAllowEntry>
            {
                new() { Path = (_tempDir + "/ThirdParty/").Replace('\\', '/'), Rule = "", Reason = "外部ライブラリ" },
            };
            var r = Scan(entries);
            Assert.AreEqual(1, r.Violations.Count);
            StringAssert.EndsWith("Mine.cs", r.Violations[0].FilePath);
            Assert.AreEqual(1, r.SettingsAllowedCount);
            Assert.AreEqual(0, r.CommentAllowedCount);
        }

        [Test]
        public void Settings_SingleFile_WithRule_OnlyThatRule()
        {
            var path = WriteFile("Gen.cs", "var t = Time.time; var go = Instantiate(p);");
            var entries = new List<ForbiddenApiAllowEntry>
            {
                new() { Path = path.Replace('\\', '/'), Rule = "time", Reason = "生成コード" },
            };
            var r = Scan(entries);
            Assert.AreEqual(1, r.Violations.Count);
            StringAssert.Contains("Instantiate", r.Violations[0].Message);
        }

        [Test]
        public void Settings_WithoutReasonOrWithUnknownRule_AreInvalid()
        {
            WriteFile("Lib.cs", "var t = Time.time;");
            var entries = new List<ForbiddenApiAllowEntry>
            {
                new() { Path = _tempDir.Replace('\\', '/'), Rule = "", Reason = "  " },
                new() { Path = _tempDir.Replace('\\', '/'), Rule = "Nope", Reason = "x" },
            };
            var r = Scan(entries);
            Assert.AreEqual(1, r.Violations.Count);
            Assert.AreEqual(2, Count(r, ForbiddenApiScanner.CodeAllowSettingsInvalid));
        }

        [Test]
        public void NoAllowances_ResultIsSameAsLegacyScan_NoNotices()
        {
            WriteFile("A.cs", "var t = Time.time;", "var go = Instantiate(p);");
            var detailed = Scan();
            var legacy = ForbiddenApiScanner.Scan(_tempDir);
            Assert.AreEqual(2, legacy.Count);
            Assert.AreEqual(legacy.Count, detailed.Violations.Count);
            Assert.AreEqual(0, detailed.Notices.Count);
            Assert.AreEqual(0, detailed.Allowed.Count);
            StringAssert.DoesNotContain("許可コメント", legacy[0].Message);
        }

        [Test]
        public void CommentAndSettings_AreCountedSeparately()
        {
            WriteFile("A.cs", "var t = Time.time; // ddrive-allow: Time(a)");
            WriteFile("Ext/B.cs", "var u = Time.time;");
            var entries = new List<ForbiddenApiAllowEntry>
            {
                new() { Path = (_tempDir + "/Ext/").Replace('\\', '/'), Reason = "外部" },
            };
            var r = Scan(entries);
            Assert.AreEqual(0, r.Violations.Count);
            Assert.AreEqual(1, r.CommentAllowedCount);
            Assert.AreEqual(1, r.SettingsAllowedCount);
            StringAssert.Contains("2 件(コメント 1、設定 1)", r.Notices.Single(n => n.Code == ForbiddenApiScanner.CodeAllowSummary).Message);
        }

        // D-Drive 自身のソースに、許可コメントの書式に見える誤認(説明文中の例など)が無いこと(読み取りのみ)。
        [Test]
        public void ThisPackage_HasNoAllowNotices()
        {
            var root = DDrive.Editor.CI.ResolveForbiddenApiScanRoot();
            if (!Directory.Exists(root))
            {
                Assert.Ignore("走査ルートがありません: " + root);
            }

            var r = ForbiddenApiScanner.ScanDetailed(root, null);
            Assert.AreEqual(0, r.Notices.Count, string.Join(", ", r.Notices.Select(n => n.FilePath + ":" + n.Line + " " + n.Code)));
        }

        [Test]
        public void RuleNames_AreStable()
        {
            CollectionAssert.AreEqual(
                new[] { "Time", "ResourcesLoad", "AddressablesLoad", "Instantiate", "AudioSourcePlay" },
                ForbiddenApiScanner.RuleNames);
        }
    }
}
