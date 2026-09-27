using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Loop;
using DDrive.Runtime.Model;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    // [02_core_framework.md] §14 — 起動オブジェクト(Composition Root)の検証(2026-09-09)。
    public class RuntimeBootstrapTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
        }

        private DDriveRuntimeBootstrap Create()
        {
            _go = new GameObject("BootstrapTest");
            _go.SetActive(false);
            var bootstrap = _go.AddComponent<DDriveRuntimeBootstrap>();
            bootstrap.CatalogLabel = string.Empty; // テストでは Addressables のラベル収集をしない
            bootstrap.KeepAcrossScenes = false;
            _go.SetActive(true); // ここで Awake
            return bootstrap;
        }

        [Test]
        public void Awake_BuildsManagers_RegistersLoop_AndBindsFacades()
        {
            var bootstrap = Create();

            Assert.AreSame(bootstrap, DDriveRuntimeBootstrap.Instance);
            Assert.IsNotNull(_go.GetComponent<GameLoopDriver>(), "GameLoopDriver が同居する");
            Assert.IsNotNull(bootstrap.Registry);
            Assert.IsNotNull(bootstrap.Audio);
            Assert.IsNotNull(bootstrap.Bgm);
            Assert.IsNotNull(bootstrap.Vfx);
            Assert.IsNotNull(bootstrap.Anim);
            Assert.IsNotNull(bootstrap.Models);
            Assert.IsNotNull(bootstrap.Prefabs);
            Assert.IsNotNull(bootstrap.Groups);
            Assert.IsNotNull(bootstrap.Dispatcher);
            Assert.IsNotNull(bootstrap.PrefabDispatcher);

            Assert.IsTrue(DDrive.Runtime.Audio.Audio.IsBound);
            Assert.IsTrue(Vfx.IsBound);
            Assert.IsTrue(Anim.IsBound);
            Assert.IsTrue(Models.IsBound);
            Assert.IsTrue(DDrive.Runtime.Prefab.Prefabs.IsBound);
            Assert.IsTrue(Anchors.IsBound);

            // 未登録 ID でも例外なく Placeholder で動く(FR-1.4)。
            var animator = new GameObject("Actor").AddComponent<Animator>();
            try
            {
                LogAssert.ignoreFailingMessages = true;
                var h = Anim.Play(new AssetId<AnimMarker>(0xDEAD, AssetType.Anim), animator);
                Assert.IsTrue(Anim.IsPlaying(h));
                bootstrap.Loop.GameLoop.Tick(0.1f);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                Object.DestroyImmediate(animator.gameObject);
            }

            Object.DestroyImmediate(_go);
            _go = null;

            Assert.IsNull(DDriveRuntimeBootstrap.Instance);
            Assert.IsFalse(Anim.IsBound, "破棄でファサードが Unbind される");
            Assert.IsFalse(Vfx.IsBound);
            Assert.IsFalse(Models.IsBound);
            Assert.IsFalse(DDrive.Runtime.Prefab.Prefabs.IsBound);
            Assert.IsFalse(Anchors.IsBound);
        }

        [UnityTest]
        public IEnumerator RegisterCatalogs_MakesIdsResolvable_AndSetsReady()
        {
            var bootstrap = Create();
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = 42, Type = AssetType.Anim, Address = "anim/42" } });
            bootstrap.Catalogs = new[] { catalog };
            var readyFired = false;
            bootstrap.OnReady += () => readyFired = true;

            yield return bootstrap.RegisterCatalogsAsync().ToCoroutine();

            Assert.IsTrue(bootstrap.IsReady);
            Assert.IsTrue(readyFired);
            Assert.AreEqual(1, bootstrap.RegisteredCatalogCount);
            Assert.AreEqual(1, bootstrap.Registry.Entries(AssetType.Anim).Count);
        }

        // [14_networking.md] §20(M-3a、2026-09-27、DD-8) — Start() が RegisterCatalogsAsync().Forget() の
        // 直後に StartNetworkingIfPending() を呼んでいた旧実装だと、IsReady=true になる前(カタログ登録前)に
        // 保留中のネット開始(_pendingNetStart)が呼ばれてしまう。StartAsync() が await RegisterCatalogsAsync()
        // の後に呼ぶよう直したことを、IsReady のタイミングで検証する(private フィールドへは reflection で
        // 直接ダミーの delegate を差し込む。InternalsVisibleTo 未設定のため、UiButton.cs 等と同じ理由で
        // 実運用コードを public にはしない)。
        [UnityTest]
        public IEnumerator StartAsync_InvokesPendingNetStart_OnlyAfterCatalogsReady()
        {
            var bootstrap = Create();
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = 99, Type = AssetType.Anim, Address = "anim/99" } });
            bootstrap.Catalogs = new[] { catalog };

            var invoked = false;
            var invokedWhileNotReady = false;
            var field = typeof(DDriveRuntimeBootstrap).GetField("_pendingNetStart", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "_pendingNetStart フィールドの名前が変わっていないか確認する");
            field.SetValue(bootstrap, (System.Action)(() =>
            {
                invoked = true;
                if (!bootstrap.IsReady)
                {
                    invokedWhileNotReady = true;
                }
            }));

            yield return bootstrap.StartAsync().ToCoroutine();

            Assert.IsTrue(invoked, "保留中のネット開始が呼ばれること");
            Assert.IsFalse(invokedWhileNotReady, "RegisterCatalogsAsync 完了(IsReady=true)前に呼ばれてはいけない");
            Assert.IsTrue(bootstrap.IsReady);
        }

        [Test]
        public void SecondInstance_IsRejected()
        {
            var first = Create();
            var second = new GameObject("BootstrapTest2");
            second.SetActive(false);
            var b2 = second.AddComponent<DDriveRuntimeBootstrap>();
            b2.CatalogLabel = string.Empty;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("既に"));
            second.SetActive(true);

            Assert.AreSame(first, DDriveRuntimeBootstrap.Instance);
            Object.DestroyImmediate(second);
        }
    }
}
