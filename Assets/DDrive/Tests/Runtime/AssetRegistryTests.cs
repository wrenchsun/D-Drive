using System;
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

        // System.Progress<T> は SynchronizationContext.Post 経由(または ThreadPool)で常に非同期に配送されるため、
        // UniTask.CompletedTask を await した直後に「同じフレーム内で」中身を検証するテストには向かない
        // (実測: PlayMode テストランナーではポストされたコールバックが assert より後に実行され、空のまま失敗した)。
        // 呼び出し即座に記録する同期版を使う。
        private sealed class SyncProgress : IProgress<float>
        {
            public readonly List<float> Reports = new();
            public void Report(float value) => Reports.Add(value);
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

        // [11_tasks.md] 5-7 — PreloadIdsAsync/ReleaseIds(ScenePreload が呼ぶ実体)。
        [Test]
        public async Task PreloadIdsAsync_ResolvesRegisteredIdsToAddresses_AndReportsProgress()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var catalog = BuildCatalog(
                new CatalogEntry { Id = 1, Type = AssetType.Se, Address = "addr/a" },
                new CatalogEntry { Id = 2, Type = AssetType.Vfx, Address = "addr/b" });
            await registry.RegisterCatalogAsync(catalog);

            var progress = new SyncProgress();

            await registry.PreloadIdsAsync(new ulong[] { 1, 2 }, progress);

            CollectionAssert.AreEquivalent(new[] { "addr/a", "addr/b" }, loader.PreloadedAddresses);
            Assert.Contains(1f, progress.Reports);
        }

        [Test]
        public async Task PreloadIdsAsync_UnregisteredId_WarnsOnceAndSkips_DoesNotThrow()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var catalog = BuildCatalog(new CatalogEntry { Id = 1, Type = AssetType.Se, Address = "addr/a" });
            await registry.RegisterCatalogAsync(catalog);

            LogAssert.Expect(LogType.Warning, new Regex(".*"));
            await registry.PreloadIdsAsync(new ulong[] { 1, 999 }, null);
            LogAssert.NoUnexpectedReceived();

            CollectionAssert.AreEquivalent(new[] { "addr/a" }, loader.PreloadedAddresses);

            // 2 回目は同じ未登録 ID でも警告が増えない(1 ID につき 1 回。ResolveAsync 系と共通の _warnedIds)。
            await registry.PreloadIdsAsync(new ulong[] { 999 }, null);
        }

        [Test]
        public async Task PreloadIdsAsync_EmptyList_ReportsCompleteImmediately()
        {
            var registry = new AssetRegistry(new FakeAssetLoader());
            var progress = new SyncProgress();

            await registry.PreloadIdsAsync(Array.Empty<ulong>(), progress);

            Assert.Contains(1f, progress.Reports);
        }

        [Test]
        public async Task ReleaseIds_RegisteredIds_ReleasesTheirAddresses()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var catalog = BuildCatalog(new CatalogEntry { Id = 1, Type = AssetType.Se, Address = "addr/a" });
            await registry.RegisterCatalogAsync(catalog);

            registry.ReleaseIds(new ulong[] { 1, 42 }); // 42 は未登録。無視されるだけで例外にならないはず

            CollectionAssert.AreEqual(new[] { "addr/a" }, loader.ReleasedAddresses);
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
