using System.Collections;
using System.Collections.Generic;
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
            Assert.IsNotNull(bootstrap.Groups);
            Assert.IsNotNull(bootstrap.Dispatcher);

            Assert.IsTrue(DDrive.Runtime.Audio.Audio.IsBound);
            Assert.IsTrue(Vfx.IsBound);
            Assert.IsTrue(Anim.IsBound);
            Assert.IsTrue(Models.IsBound);
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
