using System.Collections.Generic;
using DDrive.Editor.Settings;
using DDrive.Editor.Update;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // [42_distribution.md] §4.2 P-15(2026-10-03) — 一覧の行・候補の構築、管理対象リストの純関数、
    // 事前確認(UpdatePreflight)、v タグだけの解析、CHANGELOG の探索を固定する。
    public class ManagedPackageRowsTests
    {
        private const string DDriveUrl = "git+https://github.com/example/D-Drive.git?path=Packages/com.ddrive.core#v1.3.1";
        private const string ToonUrl = "git+https://github.com/example/T-Drive.git?path=unity/com.tdrive.toon#v0.5.0";

        private static JObject Manifest(params string[] pairs)
        {
            var deps = new JObject();
            for (var i = 0; i + 1 < pairs.Length; i += 2)
            {
                deps[pairs[i]] = pairs[i + 1];
            }

            return new JObject { ["dependencies"] = deps };
        }

        private static PackageState State(string id, string version, string packageJson = null)
            => new(id, id + "-display", version, "C:/pkg/" + id, DdriveUpdateDeclaration.Parse(packageJson));

        // ── Build ──

        [Test]
        public void Build_FirstRowIsAlwaysDDrive_EvenWhenNothingIsRegistered()
        {
            var rows = ManagedPackageRows.Build(Manifest("com.ddrive.core", DDriveUrl), null, new[] { State("com.ddrive.core", "1.3.1") });

            Assert.AreEqual(1, rows.Count);
            Assert.IsTrue(rows[0].IsDDrive);
            Assert.AreEqual("D-Drive", rows[0].DisplayName);
            Assert.AreEqual("1.3.1", rows[0].CurrentVersion);
            Assert.IsTrue(rows[0].IsGit);
        }

        [Test]
        public void Build_DDriveNotInManifest_StillRow_NotGit()
        {
            var rows = ManagedPackageRows.Build(Manifest(), null, null);

            Assert.AreEqual(1, rows.Count);
            Assert.IsFalse(rows[0].InManifest);
            Assert.IsFalse(rows[0].IsGit);
            Assert.AreEqual(string.Empty, rows[0].CurrentVersion);
        }

        [Test]
        public void Build_ManagedRows_FollowOrder_SkipDuplicatesAndDDrive()
        {
            var manifest = Manifest("com.ddrive.core", DDriveUrl, "com.tdrive.toon", ToonUrl);
            var rows = ManagedPackageRows.Build(
                manifest,
                new[] { "com.tdrive.toon", "com.tdrive.toon", "com.ddrive.core", "", null, "com.gone" },
                new[] { State("com.tdrive.toon", "0.5.0") });

            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual("com.ddrive.core", rows[0].Id);
            Assert.AreEqual("com.tdrive.toon", rows[1].Id);
            Assert.AreEqual("com.tdrive.toon-display", rows[1].DisplayName);
            Assert.AreEqual("0.5.0", rows[1].CurrentVersion);
            Assert.AreEqual("com.gone", rows[2].Id);
            Assert.IsFalse(rows[2].InManifest, "manifest から消えたパッケージも行は残る(登録解除できる)");
            Assert.IsNull(rows[2].Installed);
        }

        // ── 候補 ──

        [Test]
        public void FindCandidates_ListsGitDependencies_NotManagedNotDDrive_NotRegistryOnes()
        {
            var manifest = Manifest(
                "com.unity.ugui", "2.0.0",
                "com.ddrive.core", DDriveUrl,
                "com.tdrive.toon", ToonUrl,
                "com.cysharp.unitask", "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11",
                "com.local", "file:../local");

            var all = ManagedPackageRows.FindCandidates(manifest, null);
            Assert.AreEqual(2, all.Count);
            Assert.AreEqual("com.tdrive.toon", all[0].Id);
            Assert.IsTrue(all[0].HasVersionTagRef);
            Assert.AreEqual("com.cysharp.unitask", all[1].Id);
            Assert.IsFalse(all[1].HasVersionTagRef, "UniTask の ref は v 無し(最新版の判定ができない旨を表示するための印)");

            var afterRegister = ManagedPackageRows.FindCandidates(manifest, new[] { "com.tdrive.toon" });
            Assert.AreEqual(1, afterRegister.Count);
            Assert.AreEqual("com.cysharp.unitask", afterRegister[0].Id);
        }

        [Test]
        public void FindCandidates_NullManifest_ReturnsEmpty()
        {
            Assert.IsEmpty(ManagedPackageRows.FindCandidates(null, null));
        }

        // ── ManagedPackageList(登録・解除) ──

        [Test]
        public void ManagedPackageList_Register_IsIdempotent_AndUnregister_Removes()
        {
            var list = new List<ManagedPackageEntry>();

            Assert.IsTrue(ManagedPackageList.Register(list, "com.a"));
            Assert.IsFalse(ManagedPackageList.Register(list, "com.a"));
            Assert.IsFalse(ManagedPackageList.Register(list, ""));
            Assert.IsFalse(ManagedPackageList.Register(list, null));
            Assert.AreEqual(1, list.Count);

            Assert.IsNotNull(ManagedPackageList.Find(list, "com.a"));
            Assert.IsTrue(ManagedPackageList.Unregister(list, "com.a"));
            Assert.IsFalse(ManagedPackageList.Unregister(list, "com.a"));
            Assert.IsNull(ManagedPackageList.Find(list, "com.a"));
        }

        [Test]
        public void ManagedPackageList_NullSafe()
        {
            Assert.IsNull(ManagedPackageList.Find(null, "x"));
            Assert.IsFalse(ManagedPackageList.Register(null, "x"));
            Assert.IsFalse(ManagedPackageList.Unregister(null, "x"));
        }

        [Test]
        public void ManagedPackageEntry_DefaultsAreEmptyStrings()
        {
            var e = new ManagedPackageEntry();

            Assert.AreEqual(string.Empty, e.PackageId);
            Assert.AreEqual(string.Empty, e.PreviousRef);
            Assert.AreEqual(string.Empty, e.LastAppliedVersion);
        }

        // ── 設定の往復(実設定ファイルに保存するため、DDriveProjectSettingsTests と同じく元に戻す) ──

        [Test]
        public void Settings_ManagedPackages_RoundTrip_ThenRestore()
        {
            var settings = DDriveProjectSettings.instance;
            const string id = "com.test.p15.roundtrip";
            var existed = settings.FindManagedPackage(id) != null;

            try
            {
                Assert.IsTrue(settings.RegisterManagedPackage(id) || existed);
                settings.SetManagedPackagePreviousRef(id, "git+https://example.com/x.git#v1.0.0");
                settings.SetManagedPackageLastAppliedVersion(id, "1.0.0");

                var entry = settings.FindManagedPackage(id);
                Assert.IsNotNull(entry);
                Assert.AreEqual("git+https://example.com/x.git#v1.0.0", entry.PreviousRef);
                Assert.AreEqual("1.0.0", entry.LastAppliedVersion);

                // 既存の単数フィールド(D-Drive 用)には影響しない
                var before = settings.PreviousPackageRef;
                settings.SetManagedPackagePreviousRef(id, null);
                Assert.AreEqual(string.Empty, settings.FindManagedPackage(id).PreviousRef);
                Assert.AreEqual(before, settings.PreviousPackageRef);
            }
            finally
            {
                if (!existed)
                {
                    settings.UnregisterManagedPackage(id);
                }
            }
        }

        [Test]
        public void Settings_SettersForUnknownPackage_DoNothing()
        {
            var settings = DDriveProjectSettings.instance;

            settings.SetManagedPackagePreviousRef("com.test.p15.unknown", "x");
            settings.SetManagedPackageLastAppliedVersion("com.test.p15.unknown", "1.0.0");

            Assert.IsNull(settings.FindManagedPackage("com.test.p15.unknown"));
        }

        // ── UpdatePreflight ──

        private sealed class FakeFetcher : IRemotePackageJsonFetcher
        {
            public string Json;
            public string Warning;
            public string LastUrl, LastPath, LastRef;

            public string FetchPackageJson(string cloneUrl, string subPath, string reference, out string warningMessage)
            {
                LastUrl = cloneUrl;
                LastPath = subPath;
                LastRef = reference;
                warningMessage = Json == null ? (Warning ?? "failed") : null;
                return Json;
            }
        }

        [Test]
        public void Preflight_Fetched_ChecksNewDeclarationAgainstInstalled()
        {
            var installed = new[] { State("com.ddrive.core", "1.3.1"), State("com.tdrive.toon", "0.5.0") };
            var fetcher = new FakeFetcher
            {
                Json = "{ \"version\": \"0.6.0\", \"ddriveUpdate\": { \"compatibleWith\": { \"com.ddrive.core\": \"1.4.0\" } } }",
            };

            var result = UpdatePreflight.Run(fetcher, GitPackageUrl.Parse(ToonUrl), "com.tdrive.toon", "v0.6.0", installed);

            Assert.IsTrue(result.Fetched);
            Assert.AreEqual(1, result.Issues.Count);
            Assert.AreEqual(PackageDependencyIssue.CodeCompatibleOld, result.Issues[0].Code);
            Assert.AreEqual("https://github.com/example/T-Drive.git", fetcher.LastUrl);
            Assert.AreEqual("unity/com.tdrive.toon", fetcher.LastPath);
            Assert.AreEqual("v0.6.0", fetcher.LastRef);
        }

        [Test]
        public void Preflight_FetchFailed_StillChecksOthersAgainstNewVersion_AndSaysSo()
        {
            var installed = new[]
            {
                State("com.ddrive.core", "1.4.0"),
                State("com.tdrive.toon", "0.5.0", "{ \"ddriveUpdate\": { \"compatibleWith\": { \"com.ddrive.core\": \"1.4.0\" } } }"),
            };
            var fetcher = new FakeFetcher { Json = null, Warning = "timeout" };

            var result = UpdatePreflight.Run(fetcher, GitPackageUrl.Parse(DDriveUrl), "com.ddrive.core", "v1.3.1", installed);

            Assert.IsFalse(result.Fetched);
            StringAssert.Contains("更新後に", result.Message);
            Assert.AreEqual(1, result.Issues.Count, "D-Drive を 1.3.1 に下げると T-Drive の対応版を割る");
        }

        [Test]
        public void Preflight_NoFetcher_OrUnparsableRef_DoesNotThrow()
        {
            var installed = new[] { State("com.tdrive.toon", "0.5.0") };

            var a = UpdatePreflight.Run(null, GitPackageUrl.Parse(ToonUrl), "com.tdrive.toon", "v0.6.0", installed);
            var b = UpdatePreflight.Run(new FakeFetcher(), GitPackageUrl.Parse(ToonUrl), "com.tdrive.toon", "main", installed);

            Assert.IsFalse(a.Fetched);
            Assert.IsFalse(b.Fetched);
            Assert.IsEmpty(b.Issues);
        }

        [Test]
        public void Preflight_TagAndPackageJsonVersionMismatch_IsNoted()
        {
            var fetcher = new FakeFetcher { Json = "{ \"version\": \"0.5.9\" }" };

            var result = UpdatePreflight.Run(fetcher, GitPackageUrl.Parse(ToonUrl), "com.tdrive.toon", "v0.6.0", new[] { State("com.tdrive.toon", "0.5.0") });

            Assert.IsTrue(result.Fetched);
            StringAssert.Contains("0.5.9", result.Message);
        }

        // ── GitTagListParser.ParseVersionTags ──

        [Test]
        public void ParseVersionTags_OnlyVPrefixed_DescendingAndNoPeeled()
        {
            const string sha = "a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4a1b2c3d4";
            var output =
                $"{sha}\trefs/tags/v1.0.0\n{sha}\trefs/tags/v1.0.0^{{}}\n{sha}\trefs/tags/v1.10.0\n{sha}\trefs/tags/2.5.11\n{sha}\trefs/tags/v1.2.0\n";

            var tags = GitTagListParser.ParseVersionTags(output);

            Assert.AreEqual(3, tags.Count);
            Assert.AreEqual("1.10.0", tags[0].ToString());
            Assert.AreEqual("1.0.0", tags[2].ToString());
            Assert.AreEqual(4, GitTagListParser.Parse(output).Count, "従来の Parse は v 無しも拾う(D-Drive 用、挙動不変)");
        }

        // ── ChangelogLocator.ResolvePackageOnlyPath ──

        [Test]
        public void ResolvePackageOnlyPath_FindsOnlyPackageRoot_NeverTwoLevelsUp()
        {
            var existing = new HashSet<string> { "C:/proj/CHANGELOG.md" };

            // 2 階層上(= 持ち込み先自身)の CHANGELOG は D-Drive 以外では使わない
            Assert.IsNull(ChangelogLocator.ResolvePackageOnlyPath("C:/proj/Packages/com.x", p => existing.Contains(p)));

            existing.Add("C:/proj/Packages/com.x/CHANGELOG.md");
            Assert.AreEqual("C:/proj/Packages/com.x/CHANGELOG.md", ChangelogLocator.ResolvePackageOnlyPath("C:/proj/Packages/com.x", p => existing.Contains(p)));
        }

        [Test]
        public void ResolvePackageOnlyPath_NullOrEmpty_ReturnsNull()
        {
            Assert.IsNull(ChangelogLocator.ResolvePackageOnlyPath(null));
            Assert.IsNull(ChangelogLocator.ResolvePackageOnlyPath(string.Empty));
        }
    }
}
