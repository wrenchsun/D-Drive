using System.Collections.Generic;
using System.IO;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Codegen;
using DDrive.Foundation.Data;
using DDrive.Foundation.Extension;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Loader;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using DDrive.Tests.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] 外部拡張の契約(種別の外部定義)。E-26(X-1、[1014_extension_platform.md] §2.1)。
    // テスト用アセンブリの provider(ExternalSampleKind)は既定では発見されない(IncludeTestAssemblies=false)ので、
    // ここだけ true にして発見し直し、終わったら必ず元に戻す。
    public class ExternalContractKindProviderTests
    {
        private const string TestRoot = TestTempFolder.Root + "/TempGameDataKind";

        private bool _savedInclude;
        private string _tempOutputPath;

        [SetUp]
        public void SetUp()
        {
            _savedInclude = AssetKindRegistry.IncludeTestAssemblies;
            AssetKindRegistry.IncludeTestAssemblies = true;
            AssetKindRegistry.Refresh();
            _tempOutputPath = Path.Combine(Path.GetTempPath(), "ddrive_test_kind_ids_" + System.Guid.NewGuid().ToString("N") + ".cs");
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            }

            if (File.Exists(_tempOutputPath))
            {
                File.Delete(_tempOutputPath);
            }

            ExternalBadKinds.Enabled = false;
            AssetKindRegistry.IncludeTestAssemblies = _savedInclude;
            AssetKindRegistry.Refresh();
        }

        // ── 発見・命名・既定 Load ──

        [Test]
        public void E26_Provider_IsDiscovered_AndDuplicateKindKeyKeepsFirst()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Duplicate KindKey 'Sample'"));
            AssetKindRegistry.Refresh();

            Assert.IsTrue(AssetKindRegistry.TryGet("Sample", out var provider));
            Assert.IsInstanceOf<ExternalSampleKind>(provider, "同じ KindKey は型の完全名で先のものが勝つ");
            Assert.AreEqual(1, AssetKindRegistry.All.Count(p => p.KindKey == "Sample"));
            Assert.IsTrue(AssetKindRegistry.TryGetByDataType(typeof(ExternalSampleData), out var byType));
            Assert.AreSame(provider, byType);
        }

        [Test]
        public void E26_Provider_IsNotDiscoveredFromTestAssemblies_ByDefault()
        {
            AssetKindRegistry.IncludeTestAssemblies = false;
            AssetKindRegistry.Refresh();

            Assert.IsFalse(AssetKindRegistry.TryGet("Sample", out _), "既定ではテストアセンブリの provider を実プロジェクトの一覧に載せない");
        }

        [Test]
        public void E26_Naming_UsesProviderPrefixFolderAndCatalog()
        {
            Assert.AreEqual("SMP", AssetNamingService.GetTypePrefix(AssetType.Extension, "Sample"));
            Assert.AreEqual("Sample", AssetNamingService.GetTargetFolder(AssetType.Extension, "Sample", null));
            Assert.AreEqual("Sample/Player", AssetNamingService.GetTargetFolder(AssetType.Extension, "Sample", "Player"));
            Assert.AreEqual("SMP_Sample", AssetNamingService.BuildFileName(AssetType.Extension, "Sample", null, "Sample"));
            Assert.AreEqual("SampleCatalog", AssetCreationService.GetCatalogName(AssetType.Extension, "Sample"));
            Assert.IsTrue(AssetCreationService.NeedsPreloadDefault(AssetType.Extension, "Sample"), "provider の DefaultLoad(Preload)に従う");
        }

        [Test]
        public void E26_UnknownKindKey_FallsBackToMisc()
        {
            Assert.AreEqual("ASSET", AssetNamingService.GetTypePrefix(AssetType.Extension, "NoSuchKind"));
            Assert.AreEqual("Misc", AssetNamingService.GetTargetFolder(AssetType.Extension, "NoSuchKind", null));
            Assert.AreEqual("MiscCatalog", AssetCreationService.GetCatalogName(AssetType.Extension, "NoSuchKind"));
            Assert.IsFalse(AssetCreationService.NeedsPreloadDefault(AssetType.Extension, "NoSuchKind"));
        }

        // X-R-03: public でない provider(internal な型、internal な型の中の public 入れ子)は発見されない(ModuleRegistry / ExtensionProviders と同じ規則)。
        [Test]
        public void E26_NonPublicProviders_AreNotDiscovered()
        {
            AssetKindRegistry.Refresh();
            Assert.IsFalse(AssetKindRegistry.TryGet("ZzInternal", out _), "internal な型");
            Assert.IsFalse(AssetKindRegistry.TryGet("ZzNestedInternal", out _), "internal な型の中の public 入れ子(IsVisible = false)");
            Assert.IsTrue(AssetKindRegistry.TryGet("Sample", out _), "public な provider は発見される(テストの前提)");
        }

        // X-R-05: KindKey / DisplayName が組み込みの種別名(大小無視)と衝突する provider、DisplayName が他の provider と重なる provider は、
        // 警告 1 件で無視される(先勝ち)。MCP の scope は組み込みを先に見るので、組み込みの scope は変わらない。
        [Test]
        public void E26_NameCollisions_AreIgnoredWithWarnings_AndBuiltInScopeIsUnchanged()
        {
            ExternalBadKinds.Enabled = true;
            // 発見は型の完全名順: ExternalBadDisplayKind → ExternalBadKeyKind → ExternalSampleKindDuplicate → ZzExternalDupDisplayKind。
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ExternalBadDisplayKind.*collides with a built-in AssetType name"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ExternalBadKeyKind.*collides with a built-in AssetType name"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Duplicate KindKey 'Sample'"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ZzExternalDupDisplayKind.*DisplayName 'sample' is already used"));
            AssetKindRegistry.Refresh();

            Assert.IsFalse(AssetKindRegistry.TryGet("model", out _), "KindKey が組み込みの種別名と衝突");
            Assert.IsFalse(AssetKindRegistry.TryGet("ZzBadDisplay", out _), "DisplayName が組み込みの種別名と衝突");
            Assert.IsFalse(AssetKindRegistry.TryGet("ZzDupDisplay", out _), "DisplayName が他の provider と重複");
            Assert.IsTrue(AssetKindRegistry.TryGet("Sample", out var sample));
            Assert.IsInstanceOf<ExternalSampleKind>(sample, "先勝ち");
        }

        // X-R-10: provider が無い Extension 種別は Misc に落ちるが警告を 1 回出し、新規作成は拒否する。
        [Test]
        public void E26_MissingProvider_WarnsOnceAndFallsBackToMisc_ButCreateIsRefused()
        {
            var key = "NoSuchKind" + System.Guid.NewGuid().ToString("N");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("No IAssetKindProvider for KindKey '" + key + "'"));
            Assert.AreEqual("ASSET", AssetNamingService.GetTypePrefix(AssetType.Extension, key));
            Assert.AreEqual("Misc", AssetNamingService.GetTargetFolder(AssetType.Extension, key, null));
            Assert.AreEqual("MiscCatalog", AssetCreationService.GetCatalogName(AssetType.Extension, key), "同じ KindKey の警告は 1 回だけ");

            // provider が見えない状態(テスト用アセンブリを除外)で ExternalSampleData を作ろうとすると拒否される。
            AssetKindRegistry.IncludeTestAssemblies = false;
            AssetKindRegistry.Refresh();
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("No IAssetKindProvider for KindKey 'Sample'"));
            var asset = AssetCreationService.Create(typeof(ExternalSampleData), AssetType.Extension, "x", "", "Nokind", gameDataRoot: TestRoot);
            Assert.IsNull(asset, "Misc/ASSET_ には作らない");
        }

        // (f) 既存種別は不変(KindKey を渡しても無視される)。
        [Test]
        public void E26_BuiltInTypes_AreUnchanged()
        {
            foreach (AssetType type in System.Enum.GetValues(typeof(AssetType)))
            {
                if (type == AssetType.Extension)
                {
                    continue;
                }

                Assert.AreEqual(AssetNamingService.GetTypePrefix(type), AssetNamingService.GetTypePrefix(type, "Sample"), type.ToString());
                Assert.AreEqual(AssetNamingService.GetTargetFolder(type, "Cat"), AssetNamingService.GetTargetFolder(type, "Sample", "Cat"), type.ToString());
                Assert.AreEqual(AssetCreationService.GetCatalogName(type), AssetCreationService.GetCatalogName(type, "Sample"), type.ToString());
                Assert.AreEqual(AssetCreationService.NeedsPreloadDefault(type), AssetCreationService.NeedsPreloadDefault(type, "Sample"), type.ToString());
            }

            Assert.AreEqual("SE", AssetNamingService.GetTypePrefix(AssetType.Se));
            Assert.AreEqual("Audio/SE", AssetNamingService.GetTargetFolder(AssetType.Se));
            Assert.AreEqual("AudioCatalog", AssetCreationService.GetCatalogName(AssetType.Se));
            Assert.AreEqual("CUT", AssetNamingService.GetTypePrefix(AssetType.Cutscene));
            Assert.AreEqual("CutsceneCatalog", AssetCreationService.GetCatalogName(AssetType.Cutscene));
        }

        // ── 一覧 (a) ──

        [Test]
        public void E26_AssetBrowserFilterAndCatalogNames_ListTheKind()
        {
            var choices = AssetBrowserWindow.TypeFilterChoices();
            Assert.AreEqual("All", choices[0]);
            Assert.Contains("Sample", choices);
            Assert.IsFalse(choices.Contains(nameof(AssetType.Extension)), "Extension 自体は並べず、provider ごとに展開する");
            Assert.Contains(nameof(AssetType.Cutscene), choices);

            Assert.Contains("SampleCatalog", AssetCreationService.AllCatalogNames().ToList());
        }

        // ── 新規作成 (b) ──

        [Test]
        public void E26_Create_PutsAssetInProviderFolder_AndCatalogCarriesKindKey()
        {
            var asset = AssetCreationService.Create(
                typeof(ExternalSampleData), AssetType.Extension,
                displayName: "サンプル", category: "", identifier: "Sample",
                gameDataRoot: TestRoot);

            Assert.IsNotNull(asset);
            var path = AssetDatabase.GetAssetPath(asset);
            StringAssert.EndsWith("TempGameDataKind/Sample/SMP_Sample.asset", path);
            Assert.AreEqual(LoadMode.Preload, asset.Flags.Load, "provider の DefaultLoad が反映される");

            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>($"{TestRoot}/Catalogs/SampleCatalog.asset");
            Assert.IsNotNull(catalog);
            Assert.IsTrue(catalog.Entries.Any(e =>
                e.Id == asset.Id && e.Type == AssetType.Extension && e.KindKey == "Sample" && e.Address == "SMP_Sample"));
        }

        [Test]
        public void E26_Create_ExtensionWithoutKindKeyOnDataType_IsRefused()
        {
            // KindKey の無い Extension Data 型は作れない(SeData は Extension ではないので AssetType だけ偽って渡す)。
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no KindKey"));
            var asset = AssetCreationService.Create(typeof(DDrive.Runtime.Audio.SeData), AssetType.Extension, "x", "", "Nokey", gameDataRoot: TestRoot);
            Assert.IsNull(asset);
        }

        // ── ID 生成 (c)(e) ──

        [Test]
        public void E26_Generator_EmitsConstantClassPerKind()
        {
            var asset = AssetCreationService.Create(
                typeof(ExternalSampleData), AssetType.Extension, "サンプル", "", "Sample", gameDataRoot: TestRoot);
            Assert.IsNotNull(asset);

            var result = AssetIdGenerator.Regenerate(_tempOutputPath, includeTestAssemblies: true);
            Assert.IsTrue(result.Success);

            var text = File.ReadAllText(_tempOutputPath);
            StringAssert.Contains("public static class SMPID", text);
            StringAssert.Contains($"public static readonly AssetId<{typeof(ExternalSampleMarker).FullName}> Sample = new(0x{asset.Id:X}UL, AssetType.Extension);", text);
        }

        [Test]
        public void E26_Generator_SkipsExtensionDefinitionWithoutKindKey_WithOneError()
        {
            var noKey = new AssetIdDefinitionAttribute(AssetType.Extension, typeof(ExternalSampleMarker), "NOKEYID");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("without KindKey"));
            Assert.IsFalse(AssetIdGenerator.TryAcceptDefinition(typeof(ExternalSampleData), noKey));

            var withKey = new AssetIdDefinitionAttribute(AssetType.Extension, typeof(ExternalSampleMarker), "KEYID") { KindKey = "Sample" };
            Assert.IsTrue(AssetIdGenerator.TryAcceptDefinition(typeof(ExternalSampleData), withKey));

            var builtIn = new AssetIdDefinitionAttribute(AssetType.Se, typeof(ExternalSampleMarker), "SEX");
            Assert.IsTrue(AssetIdGenerator.TryAcceptDefinition(typeof(ExternalSampleData), builtIn), "組み込み種別は KindKey 不要");
        }

        // ── Validator (d) ──

        [Test]
        public void E26_KindValidator_RunsOnlyForItsKind()
        {
            var registry = new ValidatorRegistry();
            registry.Register(new ExternalSampleValidator());

            var sample = ScriptableObject.CreateInstance<ExternalSampleData>();
            var se = ScriptableObject.CreateInstance<DDrive.Runtime.Audio.SeData>();
            try
            {
                var reports = registry.RunAll(new List<AssetDataBase> { sample, se });
                Assert.AreEqual(1, reports.Count);
                Assert.AreSame(sample, reports[0].Asset);
            }
            finally
            {
                Object.DestroyImmediate(sample);
                Object.DestroyImmediate(se);
            }
        }

        // ── Registry index ──

        [Test]
        public void E26_Registry_IndexesByAssetTypeAndKindKey()
        {
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            try
            {
                catalog.AddOrUpdate(new CatalogEntry { Id = 1, Type = AssetType.Extension, Address = "A", KindKey = "Sample" });
                catalog.AddOrUpdate(new CatalogEntry { Id = 2, Type = AssetType.Extension, Address = "B", KindKey = "Other" });
                catalog.AddOrUpdate(new CatalogEntry { Id = 3, Type = AssetType.Se, Address = "C" });

                var registry = new AssetRegistry(new NullLoader());
                registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();

                Assert.AreEqual(2, registry.Entries(AssetType.Extension).Count);
                Assert.AreEqual(1, registry.Entries(AssetType.Extension, "Sample").Count);
                Assert.AreEqual(1UL, registry.Entries(AssetType.Extension, "Sample")[0].Id);
                Assert.AreEqual(0, registry.Entries(AssetType.Extension, "None").Count);
                Assert.AreEqual(1, registry.Entries(AssetType.Se, null).Count, "kindKey が空なら従来の Entries(type) と同じ");
                Assert.IsTrue(registry.IsRegistered(1, AssetType.Extension, "Sample"));
                Assert.IsFalse(registry.IsRegistered(2, AssetType.Extension, "Sample"));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void E26_ContentHash_IsUnchangedForEmptyKindKey_AndDiffersByKindKey()
        {
            var plain = new CatalogEntry { Id = 7, Type = AssetType.Se, Address = "SE_X" };
            var withEmpty = new CatalogEntry { Id = 7, Type = AssetType.Se, Address = "SE_X", KindKey = "" };
            var kind = new CatalogEntry { Id = 7, Type = AssetType.Se, Address = "SE_X", KindKey = "Sample" };

            Assert.AreEqual(CatalogContentHasher.HashEntry(plain), CatalogContentHasher.HashEntry(withEmpty));
            Assert.AreNotEqual(CatalogContentHasher.HashEntry(plain), CatalogContentHasher.HashEntry(kind));
        }

        private sealed class NullLoader : IAssetLoader
        {
            public Cysharp.Threading.Tasks.UniTask<T> LoadAsync<T>(string address, System.Threading.CancellationToken ct) where T : Object
                => Cysharp.Threading.Tasks.UniTask.FromResult<T>(null);

            public Cysharp.Threading.Tasks.UniTask PreloadAsync(IEnumerable<string> addresses, System.IProgress<float> progress)
                => Cysharp.Threading.Tasks.UniTask.CompletedTask;

            public void Release(string address)
            {
            }
        }
    }
}
