using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Loading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    // [11_tasks.md] 5-7 — ScenePreload 静的ファサード(Bind/RunAsync/Release)と ScenePreloadList のテスト。
    // DDriveRuntimeBootstrap は経由せず(重い上に本テストの関心はロジックのみ)、AssetRegistry を直接構築して
    // Bind する(他の静的ファサードのテストと同じ流儀。例: 04_vfx 系のテストは Vfx.Bind を直接叩く)。
    public class ScenePreloadTests
    {
        [TearDown]
        public void TearDown()
        {
            ScenePreload.Bind(null); // 他のテストへ影響しないよう毎回 Unbind する(static state)
        }

        private static ScenePreloadList BuildList(string sceneName, params PreloadEntry[] entries)
        {
            var list = ScriptableObject.CreateInstance<ScenePreloadList>();
            list.SetEntries(sceneName, new List<PreloadEntry>(entries));
            return list;
        }

        // System.Progress<T> は SynchronizationContext.Post 経由(または ThreadPool)で常に非同期に配送されるため、
        // UniTask.CompletedTask を await した直後に同じフレーム内で検証するテストには向かない
        // (実測: PlayMode テストランナーでポストされたコールバックが assert より後に実行され失敗した)。
        // 呼び出し即座に記録する同期版を使う(AssetRegistryTests.SyncProgress と同じ理由)。
        private sealed class SyncProgress : IProgress<float>
        {
            public readonly List<float> Reports = new();
            public void Report(float value) => Reports.Add(value);
        }

        [Test]
        public async Task RunAsync_Bound_DelegatesToRegistry_AndReportsCompletion()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry>
            {
                new() { Id = 10, Type = AssetType.Se, Address = "addr/se10" },
            });
            await registry.RegisterCatalogAsync(catalog);
            ScenePreload.Bind(registry);

            var list = BuildList("ZzTestScene", new PreloadEntry(AssetType.Se, 10, "Se10"));
            var progress = new SyncProgress();

            await ScenePreload.RunAsync(list, progress);

            CollectionAssert.Contains(loader.PreloadedAddresses, "addr/se10");
            Assert.Contains(1f, progress.Reports);
        }

        [Test]
        public async Task RunAsync_NullList_CompletesImmediately_WithoutTouchingRegistry()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            ScenePreload.Bind(registry);

            await ScenePreload.RunAsync(null);

            Assert.AreEqual(0, loader.PreloadedAddresses.Count);
        }

        [Test]
        public async Task RunAsync_NotBound_WarnsAndCompletes_DoesNotThrow()
        {
            var list = BuildList("ZzTestScene", new PreloadEntry(AssetType.Se, 10, "Se10"));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*"));
            await ScenePreload.RunAsync(list);
        }

        [Test]
        public async Task Release_Bound_ReleasesEachEntryAddress()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry>
            {
                new() { Id = 10, Type = AssetType.Se, Address = "addr/se10" },
            });
            await registry.RegisterCatalogAsync(catalog);
            ScenePreload.Bind(registry);

            var list = BuildList("ZzTestScene", new PreloadEntry(AssetType.Se, 10, "Se10"));
            await ScenePreload.RunAsync(list);

            ScenePreload.Release(list);

            CollectionAssert.Contains(loader.ReleasedAddresses, "addr/se10");
        }
    }
}
