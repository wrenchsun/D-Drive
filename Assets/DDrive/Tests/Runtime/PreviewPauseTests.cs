using DDrive.Foundation.Manager;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // AnimEditor の ⏸(プレビュー一時停止)が SE / VFX を Flags に関係なく止める経路(SetPausedAll)。2026-09-10。
    public class PreviewPauseTests
    {
        private PoolService _pool;
        private GameObject _vfxPrefab;
        private GameObject _seTemplate;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _vfxPrefab = new GameObject("PausePrefab");
            _vfxPrefab.AddComponent<ParticleSystem>();
            _seTemplate = new GameObject("SeTemplate");
            _seTemplate.AddComponent<AudioSource>();
            _seTemplate.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_vfxPrefab);
            Object.DestroyImmediate(_seTemplate);
        }

        [Test]
        public void Vfx_SetPausedAll_PausesAndResumesEveryInstance()
        {
            var manager = new VfxManager(_pool, new AssetRegistry(new FakeAssetLoader()));
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.Id = 1;
            data.Prefab = _vfxPrefab;
            data.LifeMode = VfxLifeMode.Loop;
            var h = manager.SpawnData(data);
            var ps = manager.GetGameObject(h).GetComponent<ParticleSystem>();
            Assert.IsFalse(ps.isPaused);

            manager.SetPausedAll(true);
            Assert.IsTrue(ps.isPaused, "Flags.Pause の設定に関係なく止まる");

            manager.SetPausedAll(false);
            Assert.IsFalse(ps.isPaused);
            manager.StopAll(StopReason.Manual);
        }

        [Test]
        public void Audio_SetPausedAll_DoesNotThrow_WithoutClip()
        {
            var manager = new AudioManager(_pool, new AssetRegistry(new FakeAssetLoader()), _seTemplate);
            Assert.DoesNotThrow(() => manager.SetPausedAll(true));
            Assert.DoesNotThrow(() => manager.SetPausedAll(false));
        }
    }
}
