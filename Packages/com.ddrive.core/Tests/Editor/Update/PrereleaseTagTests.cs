using System.Collections.Generic;
using DDrive.Editor.Update;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // [42_distribution.md] §4.2.1 P-15(2026-10-03、レビュー PC-R-02) — 元のタグ名を保つ・プレリリースは既定で「最新」にしない。
    // 実 git・実 manifest には触れない。
    public class PrereleaseTagTests
    {
        private const string Sha = "a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4";

        private static string Tags(params string[] names)
        {
            var s = string.Empty;
            foreach (var n in names)
            {
                s += $"{Sha}\trefs/tags/{n}\n";
            }

            return s;
        }

        private static List<GitTag> Parse(bool requireV, params string[] names) => GitTagListParser.ParseTags(Tags(names), requireV);

        // ── GitTag ──

        [TestCase("v1.5.0", true, "1.5.0", "")]
        [TestCase("v1.5.0-rc.1", true, "1.5.0", "rc.1")]
        [TestCase("V1.5.0", true, "1.5.0", "")]
        [TestCase("v01.2.0", true, "1.2.0", "")]
        [TestCase("1.5.0", false, "1.5.0", "")]
        [TestCase("1.5.0-beta", false, "1.5.0", "beta")]
        public void GitTag_TryParse_KeepsOriginalNameAndSplitsPrerelease(string name, bool requireV, string version, string pre)
        {
            Assert.IsTrue(GitTag.TryParse(name, requireV, out var tag));
            Assert.AreEqual(name, tag.Name);
            Assert.AreEqual(version, tag.Version.ToString());
            Assert.AreEqual(pre, tag.Prerelease);
        }

        [TestCase("1.5.0", true)] // v 必須で v 無し
        [TestCase("latest", false)]
        [TestCase("v1.5.0-", false)] // 空のプレリリース
        [TestCase("v1.5.0-rc 1", false)]
        [TestCase("v1.5.0+build", false)]
        [TestCase("vX", false)]
        [TestCase("v1.5", false)] // 2 区間(FX-R-08)
        [TestCase("v1.5.0.1", false)] // 4 区間(FX-R-08)
        [TestCase("", false)]
        [TestCase(null, false)]
        public void GitTag_TryParse_RejectsNonVersionTags(string name, bool requireV)
            => Assert.IsFalse(GitTag.TryParse(name, requireV, out _));

        [Test]
        public void GitTag_Compare_FollowsSemVerPrecedence()
        {
            GitTag T(string n)
            {
                Assert.IsTrue(GitTag.TryParse(n, false, out var t));
                return t;
            }

            Assert.Less(GitTag.Compare(T("1.5.0-rc.1"), T("1.5.0")), 0, "rc < 正式版");
            Assert.Less(GitTag.Compare(T("1.5.0-rc.1"), T("1.5.0-rc.2")), 0);
            Assert.Less(GitTag.Compare(T("1.5.0-rc.2"), T("1.5.0-rc.10")), 0, "数字は数値比較");
            Assert.Less(GitTag.Compare(T("1.5.0-alpha"), T("1.5.0-beta")), 0);
            Assert.Less(GitTag.Compare(T("1.5.0-1"), T("1.5.0-alpha")), 0, "数字 < 英字");
            Assert.Less(GitTag.Compare(T("1.5.0-rc"), T("1.5.0-rc.1")), 0, "短い方が小さい");
            Assert.Less(GitTag.Compare(T("1.4.9"), T("1.5.0-rc.1")), 0, "X.Y.Z が先");
            Assert.AreEqual(0, GitTag.Compare(T("1.2.0"), T("01.2.0")));
        }

        // ── ParseTags ──

        [Test]
        public void ParseTags_KeepsOriginalNames_DescendingBySemVer_StableBeforePrerelease()
        {
            var tags = Parse(true, "v1.4.0", "v1.5.0-rc.1", "v1.5.0", "v1.5.0-rc.2", "v01.2.0", "latest", "1.9.9");

            CollectionAssert.AreEqual(
                new[] { "v1.5.0", "v1.5.0-rc.2", "v1.5.0-rc.1", "v1.4.0", "v01.2.0" },
                tags.ConvertAll(t => t.Name));
        }

        [Test]
        public void ParseTags_OnlyPrerelease_DoesNotInventStableTag()
        {
            var tags = Parse(true, "v1.5.0-rc.1");

            Assert.AreEqual(1, tags.Count);
            Assert.AreEqual("v1.5.0-rc.1", tags[0].Name);
            Assert.IsTrue(tags[0].IsPrerelease);
        }

        [Test]
        public void ParseTags_WithoutVPrefixAllowed_KeepsDRiveStyleNames()
        {
            var tags = Parse(false, "1.5.0", "v1.4.0");

            CollectionAssert.AreEqual(new[] { "1.5.0", "v1.4.0" }, tags.ConvertAll(t => t.Name));
        }

        [Test]
        public void ParseTags_SkipsPeeledLines_AndDuplicates()
        {
            var tags = GitTagListParser.ParseTags(Tags("v1.0.0", "v1.0.0^{}", "v1.0.0"), true);

            Assert.AreEqual(1, tags.Count);
        }

        // 既存の List<Version> 版は変わらない(プレリリースは X.Y.Z に丸めて返す)。
        [Test]
        public void LegacyParse_StillReturnsVersions()
        {
            var versions = GitTagListParser.ParseVersionTags(Tags("v1.5.0-rc.1", "v1.4.0"));

            Assert.AreEqual(2, versions.Count);
            Assert.AreEqual(new System.Version(1, 5, 0), versions[0]);
        }

        // ── EvaluateTags ──

        [Test]
        public void Evaluate_StableCurrent_IgnoresPrereleaseAsLatest()
        {
            var result = UpdateCheckLogic.EvaluateTags("v1.4.0", "1.4.0", Parse(true, "v1.4.0", "v1.5.0-rc.1"));

            Assert.AreEqual(UpdateCheckLogic.BumpKind.UpToDate, result.Bump);
            Assert.AreEqual("v1.4.0", result.LatestTag);
            Assert.AreEqual("v1.5.0-rc.1", result.NewerPrereleaseTag, "勧めないが知らせる");
        }

        [Test]
        public void Evaluate_StableCurrent_OnlyPrereleaseAvailable_HasNoLatest()
        {
            var result = UpdateCheckLogic.EvaluateTags("v1.4.0", "1.4.0", Parse(true, "v1.5.0-rc.1"));

            Assert.IsNull(result.LatestTag);
            Assert.AreEqual(UpdateCheckLogic.BumpKind.Unknown, result.Bump);
            Assert.AreEqual("v1.5.0-rc.1", result.NewerPrereleaseTag);
        }

        [Test]
        public void Evaluate_StableCurrent_NewerStable_UsesOriginalTagName()
        {
            var result = UpdateCheckLogic.EvaluateTags("v1.4.0", "1.4.0", Parse(true, "v1.4.0", "v1.5.0", "v1.6.0-rc.1"));

            Assert.AreEqual("v1.5.0", result.LatestTag);
            Assert.AreEqual(UpdateCheckLogic.BumpKind.Minor, result.Bump);
            Assert.AreEqual("v1.6.0-rc.1", result.NewerPrereleaseTag);
        }

        [Test]
        public void Evaluate_PrereleaseCurrent_ConsidersPrereleaseAndStable()
        {
            var tags = Parse(true, "v1.5.0-rc.1", "v1.5.0-rc.2", "v1.5.0");

            var result = UpdateCheckLogic.EvaluateTags("v1.5.0-rc.1", "1.5.0", tags);

            Assert.AreEqual("v1.5.0", result.LatestTag, "同じ版の正式版へ進める");
            Assert.AreEqual(UpdateCheckLogic.BumpKind.Patch, result.Bump);
            Assert.IsNull(result.NewerPrereleaseTag);
        }

        [Test]
        public void Evaluate_PrereleaseCurrent_NewerRc_IsRecommended()
        {
            var result = UpdateCheckLogic.EvaluateTags("v1.5.0-rc.1", "1.5.0", Parse(true, "v1.5.0-rc.1", "v1.5.0-rc.2"));

            Assert.AreEqual("v1.5.0-rc.2", result.LatestTag);
            Assert.AreEqual(UpdateCheckLogic.BumpKind.Patch, result.Bump);
        }

        [Test]
        public void Evaluate_PrereleaseCurrent_AlreadyNewest_IsUpToDate()
        {
            var result = UpdateCheckLogic.EvaluateTags("v1.5.0-rc.2", "1.5.0", Parse(true, "v1.5.0-rc.1", "v1.5.0-rc.2"));

            Assert.AreEqual(UpdateCheckLogic.BumpKind.UpToDate, result.Bump);
        }

        [Test]
        public void Evaluate_CommitHashRef_FallsBackToPackageJsonVersion()
        {
            var result = UpdateCheckLogic.EvaluateTags("a1b2c3d", "1.4.0", Parse(true, "v1.5.0"));

            Assert.IsTrue(result.CurrentIsFromPackageJson);
            Assert.AreEqual("v1.5.0", result.LatestTag);
            Assert.AreEqual(UpdateCheckLogic.BumpKind.Minor, result.Bump);
        }

        [Test]
        public void Evaluate_NoTags_IsUnknown()
        {
            var result = UpdateCheckLogic.EvaluateTags("v1.4.0", "1.4.0", new List<GitTag>());

            Assert.AreEqual(UpdateCheckLogic.BumpKind.Unknown, result.Bump);
            Assert.IsNull(result.LatestTag);
        }

        // ── 導入計画(URL 入力) ──

        private sealed class FakeLister : IGitTagLister
        {
            public string Output;

            public string ListTags(string repoUrl, out string warningMessage)
            {
                warningMessage = null;
                return Output;
            }
        }

        private const string ToonUrl = "https://github.com/example/T-Drive.git?path=unity/com.tdrive.toon";

        private static JObject Manifest() => JObject.Parse("{ \"dependencies\": { \"com.unity.ugui\": \"2.0.0\" } }");

        [Test]
        public void AddPlan_PrefersStableTag_AndNeverInventsTagName()
        {
            var plan = new PackageAddPlanner(new FakeLister { Output = Tags("v1.0.0", "v1.1.0-rc.1") }).Plan(ToonUrl, Manifest());

            Assert.AreEqual(PackageAddOutcome.AddNew, plan.Outcome);
            StringAssert.EndsWith("#v1.0.0", plan.ManifestValue);
        }

        [Test]
        public void AddPlan_OnlyPrerelease_IsNoTags_AndNamesTheRealTag()
        {
            var plan = new PackageAddPlanner(new FakeLister { Output = Tags("v0.6.0-rc.1") }).Plan(ToonUrl, Manifest());

            Assert.AreEqual(PackageAddOutcome.NoTags, plan.Outcome);
            StringAssert.Contains("v0.6.0-rc.1", plan.Message);
            Assert.IsNull(plan.ManifestValue);
        }

        [Test]
        public void AddPlan_ExplicitPrereleaseRef_IsKeptAsWritten()
        {
            var plan = new PackageAddPlanner(null).Plan(ToonUrl + "#v0.6.0-rc.1", Manifest());

            Assert.AreEqual(PackageAddOutcome.AddNew, plan.Outcome);
            StringAssert.EndsWith("#v0.6.0-rc.1", plan.ManifestValue);
        }

        // ── 事前確認(UpdatePreflight) ──

        private sealed class ThrowingFetcher : IRemotePackageJsonFetcher
        {
            public int Calls;

            public string FetchPackageJson(string cloneUrl, string subPath, string reference, out string warningMessage)
            {
                Calls++;
                warningMessage = "should not be called";
                return null;
            }
        }

        [Test]
        public void Preflight_DDrive_DoesNotFetch_AndStillChecksOtherPackagesAgainstTagVersion()
        {
            var installed = new List<PackageState>
            {
                new("com.ddrive.core", "D-Drive", "1.4.0", null, DdriveUpdateDeclaration.Empty),
                new("com.tdrive.toon", "Toon", "0.5.0", null,
                    DdriveUpdateDeclaration.Parse("{ \"ddriveUpdate\": { \"requires\": { \"com.ddrive.core\": \"1.4.0\" } } }")),
            };
            var fetcher = new ThrowingFetcher();
            var url = GitPackageUrl.Parse("https://github.com/example/D-Drive.git?path=Packages/com.ddrive.core#v1.4.0");

            var result = UpdatePreflight.Run(fetcher, url, "com.ddrive.core", "v1.3.1", installed, false);

            Assert.AreEqual(0, fetcher.Calls, "D-Drive の版上げはネットワークを待たない");
            Assert.IsTrue(PackageDependencyChecker.HasAtLeast(result.Issues, DependencyIssueSeverity.Error), "下げると Toon の requires を割る");
        }

        [Test]
        public void Preflight_PrereleaseTargetRef_ComparesAsXYZ()
        {
            var installed = new List<PackageState> { new("com.ddrive.core", "D-Drive", "1.4.0", null, DdriveUpdateDeclaration.Empty) };
            var url = GitPackageUrl.Parse("https://github.com/example/T-Drive.git?path=unity/com.tdrive.toon#v0.5.0");

            var result = UpdatePreflight.Run(null, url, "com.tdrive.toon", "v0.6.0-rc.1", installed);

            Assert.IsFalse(result.Fetched);
            Assert.IsNotEmpty(result.Message);
        }

        // ── 同じリポジトリの別パッケージ ──

        [Test]
        public void Siblings_SameRepoDifferentPathAndRef_AreReported()
        {
            var manifest = JObject.Parse(@"{ ""dependencies"": {
                ""com.tdrive.toon"": ""https://github.com/example/T-Drive.git?path=unity/com.tdrive.toon#v0.5.0"",
                ""com.tdrive.facial"": ""https://github.com/example/T-Drive.git?path=unity/com.tdrive.facial#v0.5.0"",
                ""com.other"": ""https://github.com/example/Other.git#v0.5.0"" } }");

            var siblings = PackageManifestOps.FindSiblingsAtOtherRef(manifest, "com.tdrive.toon", "v0.6.0");

            Assert.AreEqual(1, siblings.Count);
            Assert.AreEqual("com.tdrive.facial", siblings[0].Key);
            Assert.AreEqual("v0.5.0", siblings[0].Value);
            Assert.AreEqual(0, PackageManifestOps.FindSiblingsAtOtherRef(manifest, "com.tdrive.toon", "v0.5.0").Count, "同じタグなら案内しない");
        }
    }
}
