using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.Update;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Audio;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using static DDrive.Editor.Update.McpPackageSupport;

namespace DDrive.Tests.Editor.Update
{
    // [1002_ddrive_mcp.md] §11 MCP-14(2026-10-07) — isuzu 導入の純関数(走査・計画・推奨版比較・確認文面)と
    // Validator の DD-MCP-MULTIPLE / DD-MCP-ISUZU-OUTDATED。manifest は文字列から作った JObject で、
    // 実 Packages/manifest.json・実 PackageManager・`.mcp.json` には触れない。
    public class McpPackageSupportTests
    {
        private const string Isuzu = IsuzuPackageId;
        private const string CoplayDev = CoplayDevPackageId;
        private const string Unknown = "com.foo.mcp-bridge";

        private static string IsuzuAt(string reference) => IsuzuRepositoryUrl + "?path=" + Isuzu + (reference != null ? "#" + reference : string.Empty);

        private static JObject Manifest(params string[] entries)
        {
            var deps = new List<string> { "\"com.unity.ugui\": \"2.0.0\"" };
            deps.AddRange(entries);
            return JObject.Parse("{ \"dependencies\": { " + string.Join(", ", deps) + " }, \"testables\": [\"com.ddrive.core\"] }");
        }

        private static string Entry(string id, string value) => $"\"{id}\": \"{value}\"";

        // ── 走査 ──

        [Test]
        public void Scan_None()
        {
            var scan = ScanManifest(Manifest());
            Assert.IsFalse(scan.IsuzuInstalled);
            Assert.IsFalse(scan.HasOthers);
            Assert.IsNull(scan.IsuzuRef);
        }

        [Test]
        public void Scan_NullOrNoDependencies_IsEmpty()
        {
            Assert.IsFalse(ScanManifest(null).IsuzuInstalled);
            Assert.IsFalse(ScanManifest(new JObject()).HasOthers);
        }

        [Test]
        public void Scan_IsuzuOnly_RecommendedAndNotRecommended()
        {
            var ok = ScanManifest(Manifest(Entry(Isuzu, IsuzuAt(RecommendedIsuzuRef))));
            Assert.IsTrue(ok.IsuzuInstalled);
            Assert.AreEqual(RecommendedIsuzuRef, ok.IsuzuRef);
            Assert.IsTrue(ok.IsuzuIsRecommended);
            Assert.IsFalse(ok.HasOthers);

            var old = ScanManifest(Manifest(Entry(Isuzu, IsuzuAt("v4.2.0"))));
            Assert.AreEqual("v4.2.0", old.IsuzuRef);
            Assert.IsFalse(old.IsuzuIsRecommended);
        }

        [Test]
        public void Scan_CoplayDevOnly_IsKnownRemovableFixedPort()
        {
            var scan = ScanManifest(Manifest(Entry(CoplayDev, "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main")));
            Assert.IsFalse(scan.IsuzuInstalled);
            Assert.AreEqual(1, scan.Others.Count);
            var other = scan.Others[0];
            Assert.AreEqual(CoplayDev, other.Id);
            Assert.IsTrue(other.Known);
            Assert.IsTrue(other.Removable);
            Assert.IsTrue(other.FixedPort);
            Assert.IsNotNull(other.DisplayName);
            Assert.IsTrue(scan.HasRemovable);
        }

        [Test]
        public void Scan_UnknownMcpName_IsUnknownAndNotRemovable()
        {
            var scan = ScanManifest(Manifest(Entry(Unknown, "1.0.0"), Entry("com.foo.MCP-upper", "1.0.0"), Entry("com.foo.other", "1.0.0")));
            Assert.AreEqual(2, scan.Others.Count, "id に mcp を含む(大小無視)ものだけ");
            Assert.IsTrue(scan.Others.All(o => !o.Known && !o.Removable));
            Assert.IsFalse(scan.HasRemovable);
        }

        [Test]
        public void Scan_Both()
        {
            var scan = ScanManifest(Manifest(Entry(Isuzu, IsuzuAt("v4.4.2")), Entry(CoplayDev, "x"), Entry(Unknown, "1.0.0")));
            Assert.IsTrue(scan.IsuzuInstalled);
            Assert.AreEqual(new[] { CoplayDev, Unknown }, scan.Others.Select(o => o.Id).ToArray());
        }

        [Test]
        public void Scan_RegistryVersionValue_IsReturnedAsIs()
        {
            var scan = ScanManifest(Manifest(Entry(Isuzu, "4.4.2")));
            Assert.AreEqual("4.4.2", scan.IsuzuRef);
        }

        // ── 推奨版の比較 ──

        [TestCase("v4.4.2", RefComparison.Same)]
        [TestCase("v4.2.0", RefComparison.Older)]
        [TestCase("v4.10.0", RefComparison.Newer)]
        [TestCase("v5.0.0", RefComparison.Newer)]
        [TestCase("4.4.2", RefComparison.Same)]
        [TestCase("main", RefComparison.Unknown)]
        [TestCase("HEAD", RefComparison.Unknown)]
        [TestCase("a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2", RefComparison.Unknown)]
        [TestCase("1234567", RefComparison.Unknown)]
        [TestCase("", RefComparison.Unknown)]
        [TestCase(null, RefComparison.Unknown)]
        public void Compare_ToRecommended(string installed, RefComparison expected)
        {
            Assert.AreEqual("v4.4.2", RecommendedIsuzuRef, "推奨版を上げたらこのテストの期待値も見直す");
            Assert.AreEqual(expected, CompareToRecommended(installed));
        }

        // ── 計画 ──

        [Test]
        public void Plan_NotInstalled_NoOthers_AddsRecommendedUrl_AndRegisters()
        {
            var plan = BuildPlan(ScanManifest(Manifest()), McpChoice.KeepOthers);
            Assert.IsFalse(plan.Cancelled);
            Assert.AreEqual(Isuzu, plan.AddId);
            Assert.AreEqual(RecommendedIsuzuUrl, plan.AddValue);
            StringAssert.EndsWith("?path=jp.shiranui-isuzu.unity-mcp#v4.4.2", plan.AddValue);
            Assert.AreEqual(0, plan.RemoveIds.Count);
            Assert.AreEqual(Isuzu, plan.ManagedPackageId);
        }

        [Test]
        public void Plan_KeepOthers_RemovesNothing()
        {
            var plan = BuildPlan(ScanManifest(Manifest(Entry(CoplayDev, "x"), Entry(Unknown, "1"))), McpChoice.KeepOthers);
            Assert.AreEqual(0, plan.RemoveIds.Count);
            Assert.AreEqual(new[] { CoplayDev, Unknown }, plan.KeptIds.ToArray());
        }

        [Test]
        public void Plan_RemoveRemovable_RemovesOnlyKnownRemovable_NeverUnknown()
        {
            var plan = BuildPlan(ScanManifest(Manifest(Entry(CoplayDev, "x"), Entry(Unknown, "1"))), McpChoice.RemoveRemovable);
            Assert.AreEqual(new[] { CoplayDev }, plan.RemoveIds.ToArray());
            Assert.AreEqual(new[] { Unknown }, plan.KeptIds.ToArray());
        }

        [Test]
        public void Plan_RemoveRemovable_WithOnlyUnknown_RemovesNothing()
        {
            var plan = BuildPlan(ScanManifest(Manifest(Entry(Unknown, "1"))), McpChoice.RemoveRemovable);
            Assert.AreEqual(0, plan.RemoveIds.Count);
        }

        [Test]
        public void Plan_Cancel_DoesNothing()
        {
            var manifest = Manifest(Entry(CoplayDev, "x"));
            var plan = BuildPlan(ScanManifest(manifest), McpChoice.Cancel);
            Assert.IsTrue(plan.Cancelled);
            Assert.IsNull(plan.AddId);
            Assert.IsNull(plan.ManagedPackageId);
            Assert.AreEqual(0, plan.RemoveIds.Count);
            var before = manifest.ToString();
            ApplyToManifest(manifest, plan);
            Assert.AreEqual(before, manifest.ToString());
        }

        [Test]
        public void Plan_AlreadyInstalled_AddsNothing_ButStillRegistersManaged()
        {
            var plan = BuildPlan(ScanManifest(Manifest(Entry(Isuzu, IsuzuAt("v4.2.0")))), McpChoice.KeepOthers);
            Assert.IsNull(plan.AddId, "既にあるものを上書きしない(版上げは更新チェックで)");
            Assert.IsFalse(plan.ChangesManifest);
            Assert.AreEqual(Isuzu, plan.ManagedPackageId);
        }

        // ── manifest への適用 ──

        [Test]
        public void Apply_AddsIsuzuAndRemovesCoplayDev_OtherEntriesUnchanged()
        {
            var manifest = Manifest(Entry(CoplayDev, "x"), Entry(Unknown, "1.0.0"), Entry("com.other.pkg", "2.0.0"));
            var plan = BuildPlan(ScanManifest(manifest), McpChoice.RemoveRemovable);
            ApplyToManifest(manifest, plan);

            var deps = (JObject)manifest["dependencies"];
            Assert.AreEqual(RecommendedIsuzuUrl, (string)deps[Isuzu]);
            Assert.IsNull(deps[CoplayDev]);
            Assert.AreEqual("1.0.0", (string)deps[Unknown], "未知の MCP は外さない");
            Assert.AreEqual("2.0.0", (string)deps["com.other.pkg"]);
            Assert.AreEqual("2.0.0", (string)deps["com.unity.ugui"]);
            Assert.AreEqual("com.ddrive.core", (string)manifest["testables"][0]);
        }

        [Test]
        public void Apply_RoundTrip_ChangesOnlyTheEditedLines()
        {
            var original = JObject.Parse("{\n  \"dependencies\": {\n    \"a.b\": \"1.0.0\",\n    \"" + CoplayDev + "\": \"x\",\n    \"c.d\": \"2.0.0\"\n  },\n  \"testables\": [\n    \"com.ddrive.core\"\n  ]\n}");
            var before = Lines(original);
            var plan = BuildPlan(ScanManifest(original), McpChoice.RemoveRemovable);
            ApplyToManifest(original, plan);
            var after = Lines(original);

            var removed = before.Except(after).ToList();
            var added = after.Except(before).ToList();
            Assert.AreEqual(1, removed.Count);
            StringAssert.Contains(CoplayDev, removed[0]);
            Assert.AreEqual(1, added.Count);
            StringAssert.Contains(Isuzu, added[0]);

            // それ以外の行は 1 行も変わらない(順序も保たれる)。追加の行は末尾に入るので、除いて比べる。
            CollectionAssert.AreEqual(
                before.Where(l => !l.Contains(CoplayDev)).ToList(),
                after.Where(l => !l.Contains(Isuzu)).ToList());
        }

        private static List<string> Lines(JObject manifest)
            => JsonConvert.SerializeObject(manifest, Formatting.Indented).Split('\n').Select(l => l.TrimEnd('\r', ',')).ToList();

        [Test]
        public void Apply_TakesOnlyAJObject_SoItCannotTouchMcpJson()
        {
            var parameters = typeof(McpPackageSupport).GetMethod(nameof(ApplyToManifest)).GetParameters();
            Assert.AreEqual(typeof(JObject), parameters[0].ParameterType);
            Assert.AreEqual(typeof(McpPlan), parameters[1].ParameterType);
        }

        // ── 確認文面 ──

        [Test]
        public void ConfirmText_NoOthers_IsNull()
        {
            Assert.IsNull(BuildConfirmText(ScanManifest(Manifest())));
            Assert.IsNull(BuildConfirmText(ScanManifest(Manifest(Entry(Isuzu, IsuzuAt("v4.4.2"))))));
        }

        [Test]
        public void ConfirmText_MentionsEveryOtherId_AndMcpJsonNote()
        {
            var text = BuildConfirmText(ScanManifest(Manifest(Entry(CoplayDev, "x"), Entry(Unknown, "1"))));
            StringAssert.Contains(CoplayDev, text);
            StringAssert.Contains(Unknown, text);
            StringAssert.Contains("固定ポート", text);
            StringAssert.Contains(".mcp.json", text);
            StringAssert.Contains("外せないもの", text);
        }

        [Test]
        public void ConfirmText_OnlyUnknown_SaysNotRemovedByDDrive()
        {
            var text = BuildConfirmText(ScanManifest(Manifest(Entry(Unknown, "1"))));
            StringAssert.Contains(Unknown, text);
            StringAssert.Contains("外しません", text);
        }

        // ── 登録スクリプト ──

        [Test]
        public void RegisterStartInfo_PassesProjectPath_AndNoShell()
        {
            var info = McpInstallActions.BuildRegisterStartInfo("C:/p/register-mcp.ps1", "C:/proj");
            Assert.AreEqual("pwsh", info.FileName);
            Assert.IsFalse(info.UseShellExecute);
            var args = info.ArgumentList.ToList();
            Assert.AreEqual("-ProjectPath", args[args.Count - 2]);
            Assert.AreEqual("C:/proj", args[args.Count - 1]);
            CollectionAssert.Contains(args, "C:/p/register-mcp.ps1");
        }

        [Test]
        public void ManualCommand_ContainsScriptAndProject()
        {
            var cmd = BuildManualRegisterCommand("C:/proj");
            StringAssert.Contains("register-mcp.ps1", cmd);
            StringAssert.Contains("-ProjectPath", cmd);
            StringAssert.Contains("C:/proj", cmd);
        }

        [Test]
        public void TailLines_KeepsLastLines()
        {
            Assert.AreEqual("a\nb", McpInstallActions.TailLines("a\nb\n", 5));
            Assert.AreEqual("…\nc\nd", McpInstallActions.TailLines("a\nb\nc\nd\n", 2));
        }

        // ── Validator ──

        private static List<ValidationResult> Inspect(JObject manifest) => ProjectSetupValidator.InspectMcpPackages(manifest).ToList();

        [Test]
        public void Validator_NoMcp_NothingEmitted()
        {
            Assert.AreEqual(0, Inspect(Manifest()).Count);
        }

        [Test]
        public void Validator_IsuzuRecommended_NothingEmitted()
        {
            Assert.AreEqual(0, Inspect(Manifest(Entry(Isuzu, IsuzuAt("v4.4.2")))).Count);
        }

        [Test]
        public void Validator_Multiple_EmitsInfoListingIds()
        {
            var results = Inspect(Manifest(Entry(Isuzu, IsuzuAt("v4.4.2")), Entry(CoplayDev, "x")));
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ProjectSetupValidator.CodeMcpMultiple, results[0].Code);
            Assert.AreEqual(ValidationSeverity.Info, results[0].Severity);
            StringAssert.Contains(Isuzu, results[0].Message);
            StringAssert.Contains(CoplayDev, results[0].Message);
        }

        [Test]
        public void Validator_TwoOthersWithoutIsuzu_IsMultiple()
        {
            var results = Inspect(Manifest(Entry(CoplayDev, "x"), Entry(Unknown, "1")));
            Assert.AreEqual(ProjectSetupValidator.CodeMcpMultiple, results.Single().Code);
        }

        [Test]
        public void Validator_SingleOtherWithoutIsuzu_NothingEmitted()
        {
            Assert.AreEqual(0, Inspect(Manifest(Entry(CoplayDev, "x"))).Count);
        }

        [Test]
        public void Validator_Outdated_EmitsInfoWithBothVersions()
        {
            var results = Inspect(Manifest(Entry(Isuzu, IsuzuAt("v4.2.0"))));
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ProjectSetupValidator.CodeMcpIsuzuOutdated, results[0].Code);
            Assert.AreEqual(ValidationSeverity.Info, results[0].Severity);
            StringAssert.Contains("v4.2.0", results[0].Message);
            StringAssert.Contains(RecommendedIsuzuRef, results[0].Message);
        }

        [TestCase("main")]
        [TestCase("a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2")]
        [TestCase(null)]
        public void Validator_NonTagRef_IsNotOutdated(string reference)
        {
            Assert.AreEqual(0, Inspect(Manifest(Entry(Isuzu, IsuzuAt(reference)))).Count);
        }

        [Test]
        public void Validator_NewerThanRecommended_IsNotOutdated()
        {
            Assert.AreEqual(0, Inspect(Manifest(Entry(Isuzu, IsuzuAt("v4.9.0")))).Count);
        }

        [Test]
        public void Validator_Validate_UsesInjectedManifestReader()
        {
            var dummy = ScriptableObject.CreateInstance<SeData>();
            ProjectSetupValidator.McpManifestReaderOverride = () => Manifest(Entry(Isuzu, IsuzuAt("v4.2.0")), Entry(CoplayDev, "x"));
            try
            {
                var codes = new ProjectSetupValidator().Validate(dummy, new ValidationContext(new List<AssetDataBase> { dummy }))
                    .Select(r => r.Code).ToList();
                CollectionAssert.Contains(codes, ProjectSetupValidator.CodeMcpMultiple);
                CollectionAssert.Contains(codes, ProjectSetupValidator.CodeMcpIsuzuOutdated);
            }
            finally
            {
                ProjectSetupValidator.McpManifestReaderOverride = null;
                Object.DestroyImmediate(dummy);
            }
        }
    }
}
