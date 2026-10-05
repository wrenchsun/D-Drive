using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.Update;
using DDrive.Editor.Validation;
using DDrive.Foundation.Validation;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // [42_distribution.md] §4.2 P-15(2026-10-03) — `ddriveUpdate` の読み取りと依存検査(純関数)を固定する。
    // 実 PackageManager・実ファイルには触れない。
    public class PackageDependencyCheckerTests
    {
        private static PackageState Pkg(string id, string version, string packageJson = null)
            => new(id, id, version, null, DdriveUpdateDeclaration.Parse(packageJson));

        private static string Json(string requires = null, string compat = null)
        {
            var parts = new List<string>();
            if (requires != null)
            {
                parts.Add($"\"requires\": {{ {requires} }}");
            }

            if (compat != null)
            {
                parts.Add($"\"compatibleWith\": {{ {compat} }}");
            }

            return "{ \"name\": \"x\", \"version\": \"1.0.0\", \"ddriveUpdate\": { " + string.Join(", ", parts) + " } }";
        }

        // ── Parse ──

        [Test]
        public void Parse_ReadsRequiresAndCompatibleWith()
        {
            var d = DdriveUpdateDeclaration.Parse(Json("\"com.a\": \"0.5.0\"", "\"com.b\": \"1.4.0\""));

            Assert.AreEqual("0.5.0", d.Requires["com.a"]);
            Assert.AreEqual("1.4.0", d.CompatibleWith["com.b"]);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("{ broken json")]
        [TestCase("[]")]
        [TestCase("{ \"name\": \"x\" }")]
        [TestCase("{ \"ddriveUpdate\": 5 }")]
        [TestCase("{ \"ddriveUpdate\": { \"requires\": [1,2], \"compatibleWith\": \"x\" } }")]
        public void Parse_MissingOrBroken_ReturnsEmptyWithoutThrowing(string text)
        {
            var d = DdriveUpdateDeclaration.Parse(text);

            Assert.IsTrue(d.IsEmpty);
        }

        [Test]
        public void Parse_NonStringValues_AreSkipped()
        {
            var d = DdriveUpdateDeclaration.Parse("{ \"ddriveUpdate\": { \"requires\": { \"com.a\": 1, \"com.b\": \"2.0.0\" } } }");

            Assert.AreEqual(1, d.Requires.Count);
            Assert.AreEqual("2.0.0", d.Requires["com.b"]);
        }

        [Test]
        public void ParseVersion_ReadsVersionOrNull()
        {
            Assert.AreEqual("1.2.3", DdriveUpdateDeclaration.ParseVersion("{ \"version\": \"1.2.3\" }"));
            Assert.IsNull(DdriveUpdateDeclaration.ParseVersion("{ }"));
            Assert.IsNull(DdriveUpdateDeclaration.ParseVersion("not json"));
            Assert.IsNull(DdriveUpdateDeclaration.ParseVersion(null));
            Assert.AreEqual("1.2.3", DdriveUpdateDeclaration.ParseVersion("\uFEFF{ \"version\": \"1.2.3\" }"), "BOM 付きでも読める(GB-R-08)");
        }

        // ── requires ──

        [Test]
        public void Requires_Satisfied_NoIssues()
        {
            var issues = PackageDependencyChecker.Check(new[]
            {
                Pkg("com.t", "1.0.0", Json("\"com.d\": \"1.4.0\"")),
                Pkg("com.d", "1.4.0"),
            });

            Assert.IsEmpty(issues);
        }

        [Test]
        public void Requires_NotInstalled_IsError()
        {
            var issues = PackageDependencyChecker.Check(new[] { Pkg("com.t", "1.0.0", Json("\"com.d\": \"1.4.0\"")) });

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(PackageDependencyIssue.CodeRequiresMissing, issues[0].Code);
            Assert.AreEqual(DependencyIssueSeverity.Error, issues[0].Severity);
        }

        [Test]
        public void Requires_Older_IsError()
        {
            var issues = PackageDependencyChecker.Check(new[]
            {
                Pkg("com.t", "1.0.0", Json("\"com.d\": \"1.4.0\"")),
                Pkg("com.d", "1.3.1"),
            });

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(PackageDependencyIssue.CodeRequiresOld, issues[0].Code);
            Assert.AreEqual(DependencyIssueSeverity.Error, issues[0].Severity);
        }

        [Test]
        public void TwoPartMinimum_IsTreatedAsXYZero()
        {
            // "1.4" と 1.4.0 が等しい扱い(System.Version の既定比較だと 1.4 < 1.4.0 になる)
            var issues = PackageDependencyChecker.Check(new[]
            {
                Pkg("com.t", "1.0.0", Json("\"com.d\": \"1.4\"")),
                Pkg("com.d", "1.4.0"),
            });

            Assert.IsEmpty(issues);
        }

        // ── compatibleWith ──

        [Test]
        public void CompatibleWith_PeerNotInstalled_SaysNothing()
        {
            var issues = PackageDependencyChecker.Check(new[] { Pkg("com.t", "1.0.0", Json(null, "\"com.d\": \"1.4.0\"")) });

            Assert.IsEmpty(issues);
        }

        [Test]
        public void CompatibleWith_PeerOlder_IsWarning()
        {
            var issues = PackageDependencyChecker.Check(new[]
            {
                Pkg("com.t", "1.0.0", Json(null, "\"com.d\": \"1.4.0\"")),
                Pkg("com.d", "1.3.1"),
            });

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(PackageDependencyIssue.CodeCompatibleOld, issues[0].Code);
            Assert.AreEqual(DependencyIssueSeverity.Warning, issues[0].Severity);
            StringAssert.Contains("1.4.0", issues[0].Message);
            StringAssert.Contains("1.3.1", issues[0].Message);
        }

        [Test]
        public void CompatibleWith_PeerEnough_NoIssues()
        {
            var issues = PackageDependencyChecker.Check(new[]
            {
                Pkg("com.t", "1.0.0", Json(null, "\"com.d\": \"1.4.0\"")),
                Pkg("com.d", "1.5.2"),
            });

            Assert.IsEmpty(issues);
        }

        // ── MAJOR 差 ──

        [Test]
        public void PeerMajorAhead_IsInfo_ForBothKinds()
        {
            var issues = PackageDependencyChecker.Check(new[]
            {
                Pkg("com.t", "1.0.0", Json("\"com.r\": \"1.0.0\"", "\"com.d\": \"1.4.0\"")),
                Pkg("com.r", "2.0.0"),
                Pkg("com.d", "2.0.0"),
            });

            Assert.AreEqual(2, issues.Count);
            Assert.IsTrue(issues.All(i => i.Code == PackageDependencyIssue.CodeMajorAhead && i.Severity == DependencyIssueSeverity.Info));
        }

        // ── 落ちない ──

        [Test]
        public void SelfReference_IsIgnored()
        {
            var issues = PackageDependencyChecker.Check(new[] { Pkg("com.t", "1.0.0", Json("\"com.t\": \"9.0.0\"", "\"com.t\": \"9.0.0\"")) });

            Assert.IsEmpty(issues);
        }

        [Test]
        public void MutualReference_DoesNotLoop()
        {
            var issues = PackageDependencyChecker.Check(new[]
            {
                Pkg("com.a", "1.0.0", Json("\"com.b\": \"1.0.0\"")),
                Pkg("com.b", "1.0.0", Json("\"com.a\": \"1.0.0\"")),
            });

            Assert.IsEmpty(issues);
        }

        [Test]
        public void UnparsableMinimum_IsWarning_AndUnparsablePeerVersion_IsSkipped()
        {
            var issues = PackageDependencyChecker.Check(new[]
            {
                Pkg("com.t", "1.0.0", Json("\"com.d\": \"latest\", \"com.e\": \"1.0.0\"")),
                Pkg("com.d", "1.0.0"),
                Pkg("com.e", "abc"),
            });

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(PackageDependencyIssue.CodeBadDeclaration, issues[0].Code);
        }

        [Test]
        public void NullAndEmptyInput_ReturnEmpty()
        {
            Assert.IsEmpty(PackageDependencyChecker.Check(null));
            Assert.IsEmpty(PackageDependencyChecker.Check(new PackageState[] { null }));
            Assert.IsEmpty(PackageDependencyChecker.Check(new List<PackageState>()));
        }

        // ── 版上げ前の検査(差し替え) ──

        [Test]
        public void CheckPlanned_DowngradingDDrive_WarnsAboutPeerThatNeedsNewer()
        {
            var current = new[]
            {
                Pkg("com.t", "1.0.0", Json(null, "\"com.ddrive.core\": \"1.4.0\"")),
                Pkg("com.ddrive.core", "1.4.0"),
            };

            var issues = PackageDependencyChecker.CheckPlanned(current, "com.ddrive.core", "1.3.1", null);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(PackageDependencyIssue.CodeCompatibleOld, issues[0].Code);
        }

        [Test]
        public void CheckPlanned_PreExistingIssues_AreNotReported()
        {
            var current = new[]
            {
                Pkg("com.t", "1.0.0", Json(null, "\"com.ddrive.core\": \"1.4.0\"")),
                Pkg("com.ddrive.core", "1.3.0"),
            };

            var issues = PackageDependencyChecker.CheckPlanned(current, "com.ddrive.core", "1.3.1", null);

            Assert.IsEmpty(issues, "既に満たされていなかった組み合わせは「新しい問題」ではない");
        }

        [Test]
        public void CheckPlanned_NewDeclaration_IsChecked()
        {
            var current = new[] { Pkg("com.t", "1.0.0"), Pkg("com.ddrive.core", "1.3.1") };
            var newDecl = DdriveUpdateDeclaration.Parse(Json(null, "\"com.ddrive.core\": \"1.4.0\""));

            var issues = PackageDependencyChecker.CheckPlanned(current, "com.t", "1.1.0", newDecl);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("com.t", issues[0].PackageId);
        }

        [Test]
        public void CheckPlanned_UnknownPackage_IsAddedAndChecked()
        {
            var current = new[] { Pkg("com.ddrive.core", "1.3.1") };
            var newDecl = DdriveUpdateDeclaration.Parse(Json("\"com.ddrive.core\": \"1.4.0\""));

            var issues = PackageDependencyChecker.CheckPlanned(current, "com.new", "1.0.0", newDecl);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(PackageDependencyIssue.CodeRequiresOld, issues[0].Code);
        }

        // ── Validator への変換 ──

        [Test]
        public void ValidatorToResults_ErrorBecomesWarning_InfoStaysInfo_AllHaveCode()
        {
            var issues = PackageDependencyChecker.Check(new[]
            {
                Pkg("com.t", "1.0.0", Json("\"com.missing\": \"1.0.0\"", "\"com.d\": \"1.0.0\"")),
                Pkg("com.d", "2.0.0"),
            });

            var results = PackageDependencyValidator.ToResults(issues).ToList();

            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results.Any(r => r.Code == PackageDependencyIssue.CodeRequiresMissing && r.Severity == ValidationSeverity.Warning));
            Assert.IsTrue(results.Any(r => r.Code == PackageDependencyIssue.CodeMajorAhead && r.Severity == ValidationSeverity.Info));
            Assert.IsFalse(results.Any(r => r.Severity == ValidationSeverity.Error), "新規検査は Warning 始まり([42] §5.8)");
        }
    }
}
