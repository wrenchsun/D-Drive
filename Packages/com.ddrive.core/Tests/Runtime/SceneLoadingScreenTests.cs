using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Loading;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // P5 レビュー第 1 弾(2026-09-14) P1-1 回帰テスト — SceneLoadingScreen.OnDisable が
    // RunAsync 未実行でも ScenePreload.Release を呼んでいた問題([28] 5-7 節・review1_runtime.md #1)。
    // Release は RefCountedAsyncCache 経由で他インスタンスの参照カウントを減らしうるため、
    // 「開始していないなら Release しない」ことを Manager/Registry を直接構築して確認する。
    public class SceneLoadingScreenTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            ScenePreload.Bind(null); // static state を他テストへ持ち込まない
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = typeof(SceneLoadingScreen).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"private field '{fieldName}' が見つからない(リネームされた?)");
            field.SetValue(target, value);
        }

        private static ScenePreloadList BuildList(params PreloadEntry[] entries)
        {
            var list = ScriptableObject.CreateInstance<ScenePreloadList>();
            list.SetEntries("ZzSceneLoadingScreenTest", new List<PreloadEntry>(entries));
            return list;
        }

        private static async Task<AssetRegistry> BuildBoundRegistryAsync(FakeAssetLoader loader, ulong id, string address)
        {
            var registry = new AssetRegistry(loader);
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry>
            {
                new() { Id = id, Type = AssetType.Se, Address = address },
            });
            await registry.RegisterCatalogAsync(catalog);
            ScenePreload.Bind(registry);
            return registry;
        }

        [Test]
        public async Task OnDisable_WithoutRunAsyncEverStarted_DoesNotReleaseAnything()
        {
            var loader = new FakeAssetLoader();
            await BuildBoundRegistryAsync(loader, 10, "addr/se10");
            var list = BuildList(new PreloadEntry(AssetType.Se, 10, "Se10"));

            // autoStartOnEnable=false のまま、RunAsync を一度も呼ばずに有効化 → 無効化する
            // (このコンポーネントが「参照を確保したことが一度もない」状態を再現する)。
            _go = new GameObject("SceneLoadingScreen_NeverStarted");
            _go.SetActive(false);
            var screen = _go.AddComponent<SceneLoadingScreen>();
            SetPrivateField(screen, "preloadList", list);
            SetPrivateField(screen, "autoStartOnEnable", false);
            _go.SetActive(true); // OnEnable(autoStartOnEnable=false なので RunAsync は走らない)

            _go.SetActive(false); // OnDisable

            Assert.AreEqual(0, loader.ReleasedAddresses.Count,
                "RunAsync が一度も走っていないのに Release が呼ばれると、他インスタンスの参照カウントを誤って減らす");
        }

        [Test]
        public async Task OnDisable_AfterRunAsyncCompleted_ReleasesAcquiredReferences()
        {
            var loader = new FakeAssetLoader();
            await BuildBoundRegistryAsync(loader, 11, "addr/se11");
            var list = BuildList(new PreloadEntry(AssetType.Se, 11, "Se11"));

            // 回帰確認: RunAsync が実際に走ったケースでは、これまでと同じく OnDisable で Release される。
            _go = new GameObject("SceneLoadingScreen_Started");
            _go.SetActive(false);
            var screen = _go.AddComponent<SceneLoadingScreen>();
            SetPrivateField(screen, "preloadList", list);
            SetPrivateField(screen, "autoStartOnEnable", false); // ここでは手動で 1 回だけ RunAsync を呼ぶ
            _go.SetActive(true); // OnEnable(autoStartOnEnable=false なので何もしない)

            // RunAsync() は UniTaskVoid(await 不可)。FakeAssetLoader.PreloadAsync は同期完結
            // (UniTask.CompletedTask を返すだけ)なので、呼び出しの時点で同期的に完了している。
            screen.RunAsync().Forget();
            Assert.IsTrue(screen.IsDone);

            _go.SetActive(false); // OnDisable

            CollectionAssert.Contains(loader.ReleasedAddresses, "addr/se11");
        }
    }
}
