using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    public class AssetRegistryTests
    {
        private sealed class DummyData : AssetDataBase
        {
        }

        private static AssetCatalog BuildCatalog(params CatalogEntry[] entries)
        {
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry>(entries));
            return catalog;
        }

        [Test]
        public async Task ResolveAsync_ReturnsRegisteredData()
        {
            var loader = new FakeAssetLoader();
            var data = ScriptableObject.CreateInstance<DummyData>();
            loader.Assets["addr/a"] = data;

            var registry = new AssetRegistry(loader);
            var catalog = BuildCatalog(new CatalogEntry { Id = 1, Type = AssetType.Se, Address = "addr/a" });
            await registry.RegisterCatalogAsync(catalog);

            var resolved = await registry.ResolveAsync<DummyData>(1);

            Assert.AreSame(data, resolved);
            Assert.IsTrue(registry.TryResolveSync<DummyData>(1, out var synced));
            Assert.AreSame(data, synced);
        }

        [Test]
        public void TryResolveSync_BeforeLoad_ReturnsFalse()
        {
            var registry = new AssetRegistry(new FakeAssetLoader());
            Assert.IsFalse(registry.TryResolveSync<DummyData>(999, out var data));
            Assert.IsNull(data);
        }

        [Test]
        public async Task ResolveAsync_UnregisteredId_ReturnsPlaceholderAndWarnsOnce()
        {
            var registry = new AssetRegistry(new FakeAssetLoader());
            var placeholderCount = 0;
            registry.OnPlaceholderUsed += (_, _) => placeholderCount++;

            LogAssert.Expect(LogType.Warning, new Regex(".*"));
            var first = await registry.ResolveAsync<DummyData>(12345);
            var second = await registry.ResolveAsync<DummyData>(12345);
            LogAssert.NoUnexpectedReceived();

            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.AreEqual(2, placeholderCount);
        }

        [Test]
        public void ResolveOrPlaceholder_UnregisteredId_WarnsOnceAndReturnsPlaceholder()
        {
            var registry = new AssetRegistry(new FakeAssetLoader());

            LogAssert.Expect(LogType.Warning, new Regex(".*"));
            var first = registry.ResolveOrPlaceholder<DummyData>(777);
            var second = registry.ResolveOrPlaceholder<DummyData>(777);
            LogAssert.NoUnexpectedReceived();

            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
        }

        [Test]
        public async Task Entries_FiltersByType()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var catalog = BuildCatalog(
                new CatalogEntry { Id = 1, Type = AssetType.Se, Address = "a" },
                new CatalogEntry { Id = 2, Type = AssetType.Vfx, Address = "b" });

            await registry.RegisterCatalogAsync(catalog);

            var seEntries = registry.Entries(AssetType.Se);
            Assert.AreEqual(1, seEntries.Count);
            Assert.AreEqual(1UL, seEntries[0].Id);
        }
    }
}
