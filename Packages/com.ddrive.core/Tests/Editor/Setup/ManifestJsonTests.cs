using System.IO;
using DDrive.Editor.Setup;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Setup
{
    // [42_distribution.md] §3.6/§6 P-6(2026-09-20) — manifest.json の JSON 操作(dependencies /
    // scopedRegistries / testables)を、実プロジェクトの Packages/manifest.json に触らず
    // 一時ファイル/インメモリの JObject だけで検証する
    // (このチケットの指示「manifest の testables 編集は一時ファイルで検証する」)。
    public class ManifestJsonTests
    {
        private string _tempFile;

        [TearDown]
        public void TearDown()
        {
            if (_tempFile != null && File.Exists(_tempFile))
            {
                File.Delete(_tempFile);
            }

            _tempFile = null;
        }

        private static JObject SampleManifest() => JObject.Parse(@"{
            ""dependencies"": {
                ""com.unity.addressables"": ""2.3.1""
            },
            ""testables"": [
                ""com.ddrive.core""
            ]
        }");

        [Test]
        public void HasDependency_Present_ReturnsTrue()
        {
            var manifest = SampleManifest();
            Assert.IsTrue(ManifestJson.HasDependency(manifest, "com.unity.addressables"));
        }

        [Test]
        public void HasDependency_Missing_ReturnsFalse()
        {
            var manifest = SampleManifest();
            Assert.IsFalse(ManifestJson.HasDependency(manifest, "com.cysharp.unitask"));
        }

        [Test]
        public void SetDependency_AddsNewEntry_WithoutTouchingExisting()
        {
            var manifest = SampleManifest();
            ManifestJson.SetDependency(manifest, "com.cysharp.unitask", "https://example.com/unitask#2.5.11");

            Assert.IsTrue(ManifestJson.HasDependency(manifest, "com.cysharp.unitask"));
            Assert.AreEqual("https://example.com/unitask#2.5.11", ManifestJson.GetDependencyValue(manifest, "com.cysharp.unitask"));
            Assert.IsTrue(ManifestJson.HasDependency(manifest, "com.unity.addressables")); // 既存は残る
        }

        [Test]
        public void HasScopedRegistry_NoRegistries_ReturnsFalse()
        {
            var manifest = SampleManifest();
            Assert.IsFalse(ManifestJson.HasScopedRegistry(manifest, "https://unitynuget-registry.openupm.com", "org.nuget"));
        }

        [Test]
        public void AddScopedRegistry_ThenHasScopedRegistry_ReturnsTrue()
        {
            var manifest = SampleManifest();
            ManifestJson.AddScopedRegistry(manifest, "Unity NuGet", "https://unitynuget-registry.openupm.com", "org.nuget");

            Assert.IsTrue(ManifestJson.HasScopedRegistry(manifest, "https://unitynuget-registry.openupm.com", "org.nuget"));
        }

        [Test]
        public void AddScopedRegistry_CalledTwice_DoesNotDuplicateScopeOrEntry()
        {
            var manifest = SampleManifest();
            ManifestJson.AddScopedRegistry(manifest, "Unity NuGet", "https://unitynuget-registry.openupm.com", "org.nuget");
            ManifestJson.AddScopedRegistry(manifest, "Unity NuGet", "https://unitynuget-registry.openupm.com", "org.nuget");

            var registries = (JArray)manifest[ManifestJson.ScopedRegistriesKey];
            Assert.AreEqual(1, registries.Count);
            var scopes = (JArray)registries[0]["scopes"];
            Assert.AreEqual(1, scopes.Count);
        }

        [Test]
        public void AddScopedRegistry_SameUrlDifferentScope_AddsScopeToExistingEntry()
        {
            var manifest = SampleManifest();
            ManifestJson.AddScopedRegistry(manifest, "Unity NuGet", "https://unitynuget-registry.openupm.com", "org.nuget");
            ManifestJson.AddScopedRegistry(manifest, "Unity NuGet", "https://unitynuget-registry.openupm.com", "com.other");

            var registries = (JArray)manifest[ManifestJson.ScopedRegistriesKey];
            Assert.AreEqual(1, registries.Count);
            var scopes = (JArray)registries[0]["scopes"];
            Assert.AreEqual(2, scopes.Count);
        }

        [Test]
        public void HasTestable_Present_ReturnsTrue()
        {
            var manifest = SampleManifest();
            Assert.IsTrue(ManifestJson.HasTestable(manifest, "com.ddrive.core"));
        }

        [Test]
        public void SetTestable_EnableTwice_IsIdempotent()
        {
            var manifest = JObject.Parse(@"{ ""dependencies"": {} }");
            ManifestJson.SetTestable(manifest, "com.ddrive.core", true);
            ManifestJson.SetTestable(manifest, "com.ddrive.core", true);

            var testables = (JArray)manifest[ManifestJson.TestablesKey];
            Assert.AreEqual(1, testables.Count);
        }

        [Test]
        public void SetTestable_DisableExisting_Removes()
        {
            var manifest = SampleManifest();
            ManifestJson.SetTestable(manifest, "com.ddrive.core", false);

            Assert.IsFalse(ManifestJson.HasTestable(manifest, "com.ddrive.core"));
        }

        [Test]
        public void SetTestable_DisableWhenAbsent_NoOp()
        {
            var manifest = JObject.Parse(@"{ ""dependencies"": {} }");
            ManifestJson.SetTestable(manifest, "com.ddrive.core", false);

            Assert.IsFalse(ManifestJson.HasTestable(manifest, "com.ddrive.core"));
            Assert.IsNull(manifest[ManifestJson.TestablesKey]);
        }

        [Test]
        public void LoadSave_RoundTrip_PreservesContent()
        {
            _tempFile = Path.GetTempFileName();
            var manifest = SampleManifest();
            ManifestJson.Save(_tempFile, manifest);

            var loaded = ManifestJson.Load(_tempFile);

            Assert.IsTrue(ManifestJson.HasDependency(loaded, "com.unity.addressables"));
            Assert.IsTrue(ManifestJson.HasTestable(loaded, "com.ddrive.core"));
        }

        [Test]
        public void Load_MissingFile_ReturnsNull()
        {
            var result = ManifestJson.Load(Path.Combine(Path.GetTempPath(), "ddrive-manifest-json-tests-missing.json"));
            Assert.IsNull(result);
        }
    }
}
