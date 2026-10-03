using System.Collections.Generic;
using DDrive.Editor.Update;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // [42_distribution.md] §4.2.1 P-15(2026-10-03、レビュー PC-R-07) — `ddriveUpdate` 形式の拡張規則を固定する。
    // 旧版の D-Drive が将来の拡張された宣言(新しいキー・オブジェクト値・範囲指定)を読んでも壊れない(例外・誤検出が無い)こと。
    public class DdriveUpdateFormatCompatTests
    {
        private static PackageState Pkg(string id, string version, string packageJson = null)
            => new(id, id, version, null, DdriveUpdateDeclaration.Parse(packageJson));

        // 将来の宣言の例: 未知のキー(below / platforms)・値がオブジェクト・配列・数値・真偽・null の項目。
        private const string FutureJson = @"{
            ""name"": ""com.t"", ""version"": ""2.0.0"",
            ""ddriveUpdate"": {
                ""requires"": {
                    ""com.a"": ""1.4.0"",
                    ""com.future1"": { ""min"": ""1.0.0"", ""max"": ""2.0.0"" },
                    ""com.future2"": [""1.0.0""],
                    ""com.future3"": 7,
                    ""com.future4"": true,
                    ""com.future5"": null
                },
                ""compatibleWith"": { ""com.b"": ""1.0.0"", ""com.future6"": { ""x"": 1 } },
                ""below"": { ""com.a"": ""2.0.0"" },
                ""platforms"": [""win"", ""mac""],
                ""schema"": 2
            }
        }";

        [Test]
        public void FutureDeclaration_IsReadWithoutThrowing_UnknownKeysAndNonStringValuesAreIgnored()
        {
            var d = DdriveUpdateDeclaration.Parse(FutureJson);

            Assert.AreEqual(1, d.Requires.Count, "文字列の値だけ読む");
            Assert.AreEqual("1.4.0", d.Requires["com.a"]);
            Assert.AreEqual(1, d.CompatibleWith.Count);
            Assert.AreEqual("1.0.0", d.CompatibleWith["com.b"]);
        }

        [Test]
        public void FutureDeclaration_ProducesNoIssues_WhenKnownPartIsSatisfied()
        {
            var issues = PackageDependencyChecker.Check(new[]
            {
                Pkg("com.t", "2.0.0", FutureJson),
                Pkg("com.a", "1.4.0"),
                Pkg("com.b", "1.0.0"),
            });

            Assert.IsEmpty(issues, "未知のキー・オブジェクト値は警告も BAD-DECLARATION も出さない");
        }

        [Test]
        public void FutureDeclaration_KnownPartIsStillChecked()
        {
            var issues = PackageDependencyChecker.Check(new[]
            {
                Pkg("com.t", "2.0.0", FutureJson),
                Pkg("com.a", "1.3.0"),
                Pkg("com.b", "1.0.0"),
            });

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(PackageDependencyIssue.CodeRequiresOld, issues[0].Code);
        }

        [TestCase(">=1.4.0 <2.0.0")]
        [TestCase("1.4.0 - 1.x")]
        [TestCase("^1.4.0")]
        [TestCase("latest")]
        [TestCase("1.x")]
        public void StringValueThatIsNotAVersion_IsBadDeclaration_NotSilentlyTruncated(string value)
        {
            var json = "{ \"ddriveUpdate\": { \"requires\": { \"com.a\": \"" + value + "\" } } }";

            var issues = PackageDependencyChecker.Check(new[] { Pkg("com.t", "1.0.0", json), Pkg("com.a", "9.0.0") });

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(PackageDependencyIssue.CodeBadDeclaration, issues[0].Code);
        }

        [TestCase("1.4.0", true)]
        [TestCase("1.4", true)]
        [TestCase("1.4.0-rc.1", true)]
        [TestCase("1.4.0 ", false)]
        [TestCase(">=1.4.0", false)]
        [TestCase("1.4.0 - 2.0.0", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsDeclaredVersion(string text, bool expected)
            => Assert.AreEqual(expected, PackageDependencyChecker.IsDeclaredVersion(text));

        [Test]
        public void Prerelease_IsIgnoredInComparison_ForDeclarationAndInstalledVersion()
        {
            // 導入済みが 1.4.0-rc.1 でも requires 1.4.0 を満たす(比較は X.Y.Z)。宣言側が 1.5.0-rc.1 なら 1.5.0 として比べる。
            var ok = PackageDependencyChecker.Check(new[]
            {
                Pkg("com.t", "1.0.0", "{ \"ddriveUpdate\": { \"requires\": { \"com.a\": \"1.4.0\" } } }"),
                Pkg("com.a", "1.4.0-rc.1"),
            });
            var old = PackageDependencyChecker.Check(new[]
            {
                Pkg("com.t", "1.0.0", "{ \"ddriveUpdate\": { \"requires\": { \"com.a\": \"1.5.0-rc.1\" } } }"),
                Pkg("com.a", "1.4.9"),
            });

            Assert.IsEmpty(ok);
            Assert.AreEqual(1, old.Count);
            Assert.AreEqual(PackageDependencyIssue.CodeRequiresOld, old[0].Code);
        }

        [Test]
        public void DdriveUpdateItself_NotAnObject_IsEmpty()
        {
            foreach (var json in new[] { "{ \"ddriveUpdate\": [] }", "{ \"ddriveUpdate\": \"x\" }", "{ \"ddriveUpdate\": null }", "{ \"ddriveUpdate\": { \"requires\": \"x\" } }" })
            {
                Assert.IsTrue(DdriveUpdateDeclaration.Parse(json).IsEmpty, json);
            }
        }
    }
}
