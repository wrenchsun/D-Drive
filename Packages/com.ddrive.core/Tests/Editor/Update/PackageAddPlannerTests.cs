using DDrive.Editor.Update;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // [42_distribution.md] §4.2 P-15(2026-10-03) — 「URL を入力して追加」の解釈(`PackageAddPlanner`)と
    // manifest 操作(`PackageManifestOps`)を固定する。実 git・実 PackageManager・実 manifest には触れない
    // (manifest は文字列から作った JObject、git は偽の `IGitTagLister`)。
    public class PackageAddPlannerTests
    {
        private sealed class FakeLister : IGitTagLister
        {
            public string Output;
            public string Warning;
            public int Calls;
            public string LastUrl;

            public string ListTags(string repoUrl, out string warningMessage)
            {
                Calls++;
                LastUrl = repoUrl;
                warningMessage = Warning;
                return Warning == null ? Output : null;
            }
        }

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

        private static JObject Manifest(string deps = "")
            => JObject.Parse("{ \"dependencies\": { \"com.unity.ugui\": \"2.0.0\"" + (deps.Length > 0 ? ", " + deps : string.Empty) + " } }");

        private const string ToonUrl = "https://github.com/example/T-Drive.git?path=unity/com.tdrive.toon";

        // ── 入力の解釈 ──

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not a url")]
        [TestCase("1.0.0")]
        [TestCase("file:../local")]
        [TestCase("com.unknown.package")]
        public void Plan_InvalidInput_IsInvalid_AndDoesNotCallGit(string input)
        {
            var lister = new FakeLister { Output = Tags("v1.0.0") };

            var plan = new PackageAddPlanner(lister).Plan(input, Manifest());

            Assert.AreEqual(PackageAddOutcome.Invalid, plan.Outcome);
            Assert.AreEqual(0, lister.Calls);
        }

        [Test]
        public void Plan_PackageIdOfNonGitDependency_IsInvalid()
        {
            var plan = new PackageAddPlanner(new FakeLister()).Plan("com.unity.ugui", Manifest());

            Assert.AreEqual(PackageAddOutcome.Invalid, plan.Outcome);
        }

        [Test]
        public void Plan_PackageIdOfGitDependency_RegistersExisting()
        {
            var manifest = Manifest($"\"com.tdrive.toon\": \"git+{ToonUrl}#v0.5.0\"");

            var plan = new PackageAddPlanner(new FakeLister()).Plan("com.tdrive.toon", manifest);

            Assert.AreEqual(PackageAddOutcome.RegisterExisting, plan.Outcome);
            Assert.AreEqual("com.tdrive.toon", plan.PackageId);
        }

        [TestCase(ToonUrl)]
        [TestCase("git+" + ToonUrl)]
        [TestCase("git+https://github.com/example/T-Drive.git?path=unity/com.tdrive.toon#v0.9.0")]
        public void Plan_UrlAlreadyInManifest_RegistersExisting_RegardlessOfPrefixOrRef(string input)
        {
            var manifest = Manifest($"\"com.tdrive.toon\": \"git+{ToonUrl}#v0.5.0\"");
            var lister = new FakeLister { Output = Tags("v1.0.0") };

            var plan = new PackageAddPlanner(lister).Plan(input, manifest);

            Assert.AreEqual(PackageAddOutcome.RegisterExisting, plan.Outcome);
            Assert.AreEqual("com.tdrive.toon", plan.PackageId);
            Assert.AreEqual(0, lister.Calls);
        }

        [Test]
        public void Plan_SameRepoDifferentPath_IsNotTheSamePackage()
        {
            var manifest = Manifest($"\"com.tdrive.toon\": \"git+{ToonUrl}#v0.5.0\"");
            var lister = new FakeLister { Output = Tags("v0.2.0") };

            var plan = new PackageAddPlanner(lister).Plan("https://github.com/example/T-Drive.git?path=unity/com.tdrive.facial", manifest);

            Assert.AreEqual(PackageAddOutcome.AddNew, plan.Outcome);
        }

        // ── 新規導入 ──

        [Test]
        public void Plan_NewUrlWithoutRef_UsesLatestVTag()
        {
            var lister = new FakeLister { Output = Tags("v0.9.0", "v0.10.0", "v0.2.0", "not-a-version", "2.5.11") };

            var plan = new PackageAddPlanner(lister).Plan(ToonUrl, Manifest());

            Assert.AreEqual(PackageAddOutcome.AddNew, plan.Outcome);
            Assert.AreEqual(ToonUrl + "#v0.10.0", plan.ManifestValue);
            Assert.AreEqual("https://github.com/example/T-Drive.git", lister.LastUrl);
        }

        [Test]
        public void Plan_NewGitPlusUrl_KeepsPrefixAndPath()
        {
            var lister = new FakeLister { Output = Tags("v1.2.3") };

            var plan = new PackageAddPlanner(lister).Plan("git+ssh://git@github.com/example/T-Drive.git?path=unity/com.tdrive.toon", Manifest());

            Assert.AreEqual(PackageAddOutcome.AddNew, plan.Outcome);
            Assert.AreEqual("git+ssh://git@github.com/example/T-Drive.git?path=unity/com.tdrive.toon#v1.2.3", plan.ManifestValue);
        }

        [Test]
        public void Plan_NewUrlWithRef_UsesRefAsIs_AndSkipsTagListing()
        {
            var lister = new FakeLister { Output = Tags("v9.0.0") };

            var plan = new PackageAddPlanner(lister).Plan(ToonUrl + "#v0.5.0", Manifest());

            Assert.AreEqual(PackageAddOutcome.AddNew, plan.Outcome);
            Assert.AreEqual(ToonUrl + "#v0.5.0", plan.ManifestValue);
            Assert.AreEqual(0, lister.Calls);
        }

        [Test]
        public void Plan_NoTags_IsNoTags()
        {
            var plan = new PackageAddPlanner(new FakeLister { Output = Tags() }).Plan(ToonUrl, Manifest());

            Assert.AreEqual(PackageAddOutcome.NoTags, plan.Outcome);
        }

        [Test]
        public void Plan_OnlyNonVTags_IsNoTags()
        {
            // UniTask のような "2.5.11"(v 無し)のタグしか無い URL は、版上げに使えないので何も変えない。
            var plan = new PackageAddPlanner(new FakeLister { Output = Tags("2.5.11", "2.5.10") }).Plan(ToonUrl, Manifest());

            Assert.AreEqual(PackageAddOutcome.NoTags, plan.Outcome);
        }

        [Test]
        public void Plan_GitFailure_IsTagListFailed_WithReason()
        {
            var plan = new PackageAddPlanner(new FakeLister { Warning = "fatal: repository not found" }).Plan(ToonUrl, Manifest());

            Assert.AreEqual(PackageAddOutcome.TagListFailed, plan.Outcome);
            StringAssert.Contains("repository not found", plan.Message);
        }

        [Test]
        public void Plan_NullManifest_DoesNotThrow()
        {
            var lister = new FakeLister { Output = Tags("v1.0.0") };

            var plan = new PackageAddPlanner(lister).Plan(ToonUrl, null);

            Assert.AreEqual(PackageAddOutcome.AddNew, plan.Outcome);
        }

        // ── PackageManifestOps ──

        [Test]
        public void TryBumpRef_ReplacesOnlyTheRef_AndReturnsPrevious()
        {
            var manifest = Manifest($"\"com.tdrive.toon\": \"git+{ToonUrl}#v0.5.0\"");

            var ok = PackageManifestOps.TryBumpRef(manifest, "com.tdrive.toon", "v0.6.0", out var previous, out var now);

            Assert.IsTrue(ok);
            Assert.AreEqual($"git+{ToonUrl}#v0.5.0", previous);
            Assert.AreEqual($"git+{ToonUrl}#v0.6.0", now);
            Assert.AreEqual(now, (string)manifest["dependencies"]["com.tdrive.toon"]);
        }

        [Test]
        public void TryBumpRef_NonGitOrMissing_DoesNothing()
        {
            var manifest = Manifest();

            Assert.IsFalse(PackageManifestOps.TryBumpRef(manifest, "com.unity.ugui", "v1.0.0", out _, out _));
            Assert.IsFalse(PackageManifestOps.TryBumpRef(manifest, "com.nothing", "v1.0.0", out _, out _));
            Assert.AreEqual("2.0.0", (string)manifest["dependencies"]["com.unity.ugui"]);
            Assert.IsNull(manifest["dependencies"]["com.nothing"]);
        }

        [Test]
        public void TryRestore_SwapsValues_SoTwoCallsRoundTrip()
        {
            var manifest = Manifest($"\"com.tdrive.toon\": \"git+{ToonUrl}#v0.6.0\"");
            var prev = $"git+{ToonUrl}#v0.5.0";

            Assert.IsTrue(PackageManifestOps.TryRestore(manifest, "com.tdrive.toon", prev, out var replaced));
            Assert.AreEqual(prev, (string)manifest["dependencies"]["com.tdrive.toon"]);

            Assert.IsTrue(PackageManifestOps.TryRestore(manifest, "com.tdrive.toon", replaced, out _));
            Assert.AreEqual($"git+{ToonUrl}#v0.6.0", (string)manifest["dependencies"]["com.tdrive.toon"]);
        }

        [Test]
        public void TryRestore_EmptyPrevious_DoesNothing()
        {
            var manifest = Manifest($"\"com.tdrive.toon\": \"git+{ToonUrl}#v0.6.0\"");

            Assert.IsFalse(PackageManifestOps.TryRestore(manifest, "com.tdrive.toon", string.Empty, out _));
            Assert.AreEqual($"git+{ToonUrl}#v0.6.0", (string)manifest["dependencies"]["com.tdrive.toon"]);
        }

        [TestCase("v1.2.3", true)]
        [TestCase("V1.2.3", true)]
        [TestCase("2.5.11", false)]
        [TestCase("main", false)]
        [TestCase("6c65a8912345678901234567890123456789abcd", false)]
        [TestCase("v", false)]
        [TestCase(null, false)]
        public void IsVersionTagRef(string reference, bool expected)
        {
            Assert.AreEqual(expected, PackageManifestOps.IsVersionTagRef(reference));
        }
    }
}
