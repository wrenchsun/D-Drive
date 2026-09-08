using DDrive.Editor.Vfx;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Tests.Editor
{
    // [04_vfx.md] §5(2026-07-28 改定) — SceneView 方式 VFX プレビューの検証。
    public class SceneVfxPreviewDriverTests
    {
        private SceneVfxPreviewDriver _driver;
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _driver = new SceneVfxPreviewDriver();
            _prefab = new GameObject("SceneVfxTestPrefab");
            var ps = _prefab.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        [TearDown]
        public void TearDown()
        {
            _driver.Dispose();
            Object.DestroyImmediate(_prefab);
        }

        private VfxData CreateVfxData()
        {
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.Id = 1;
            data.Prefab = _prefab;
            data.LifeMode = VfxLifeMode.Loop;
            return data;
        }

        [Test]
        public void Play_SpawnsIntoActiveScene_WithDontSaveFlag()
        {
            var handle = _driver.Play(CreateVfxData());
            var go = _driver.Manager.GetGameObject(handle);

            Assert.IsNotNull(go);
            Assert.AreEqual(SceneManager.GetActiveScene(), go.scene, "開いているシーンへ直接スポーンされる");
            Assert.AreEqual(HideFlags.DontSave, go.hideFlags, "シーンに保存されないフラグが付く");
            Assert.IsTrue(_driver.HasActive);
        }

        [Test]
        public void Tick_AdvancesWithoutError_AndCleansUpStoppedInstances()
        {
            var handle = _driver.Play(CreateVfxData());

            _driver.Tick(0.016f);
            Assert.IsTrue(_driver.HasActive);

            _driver.Manager.Kill(handle);
            _driver.Tick(0.016f);
            Assert.IsFalse(_driver.HasActive);
        }

        [Test]
        public void Dispose_RemovesSpawnedObjectsFromScene()
        {
            var handle = _driver.Play(CreateVfxData());
            var go = _driver.Manager.GetGameObject(handle);
            Assert.IsNotNull(go);

            _driver.Dispose();

            Assert.IsTrue(go == null, "Dispose でシーンからスポーン物が破棄される");
        }

        [Test]
        public void Play_WithAttach_ResolvesAnchorUnderAttachTarget()
        {
            var rig = new GameObject("Rig");
            var anchor = new GameObject("Anchor_Test");
            anchor.transform.SetParent(rig.transform);
            anchor.transform.position = new Vector3(3f, 0f, 0f);

            var data = CreateVfxData();
            data.Anchor = new DDrive.Foundation.Data.AnchorDef
            {
                Space = DDrive.Foundation.Data.AnchorSpace.NamedObject,
                Path = "Anchor_Test",
                LocalScale = Vector3.one,
            };

            var handle = _driver.Play(data, rig.transform);
            var go = _driver.Manager.GetGameObject(handle);

            Assert.Less(Vector3.Distance(anchor.transform.position, go.transform.position), 1e-4f);

            Object.DestroyImmediate(rig);
        }
    }
}
