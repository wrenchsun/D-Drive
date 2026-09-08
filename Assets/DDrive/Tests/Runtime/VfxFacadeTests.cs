using DDrive.Foundation.Handle;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // [04_vfx.md] §3 — 静的ファサード Vfx と Handle 拡張(`h.Move(...)` 等)。未 Bind 時は例外を出さず no-op。
    public class VfxFacadeTests
    {
        private PoolService _pool;
        private VfxManager _manager;
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _manager = new VfxManager(_pool, new AssetRegistry(new FakeAssetLoader()));
            _prefab = new GameObject("FacadePrefab");
            var ps = _prefab.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        [TearDown]
        public void TearDown()
        {
            Vfx.Bind(null);
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_prefab);
        }

        private VfxData CreateData()
        {
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.Id = 1;
            data.Prefab = _prefab;
            data.LifeMode = VfxLifeMode.Loop;
            return data;
        }

        [Test]
        public void Unbound_HandleOperations_AreNoOpWithoutException()
        {
            Vfx.Bind(null);
            var handle = Handle<VfxMarker>.Invalid;

            Assert.IsFalse(Vfx.IsBound);
            Assert.DoesNotThrow(() =>
            {
                handle.Move(Vector3.one);
                handle.Attach(null);
                handle.Detach();
                handle.SetSpeed(2f);
                handle.SetParam("X", 1f);
                handle.SetParam("X", Color.red);
                handle.Stop();
                handle.Kill();
            });
            Assert.IsFalse(handle.IsPlaying());
        }

        [Test]
        public void Bound_HandleExtensions_DelegateToManager()
        {
            Vfx.Bind(_manager);
            var handle = _manager.SpawnData(CreateData());
            Assert.IsTrue(handle.IsPlaying());

            handle.Move(new Vector3(4f, 5f, 6f));
            Assert.AreEqual(new Vector3(4f, 5f, 6f), _manager.GetGameObject(handle).transform.position);

            handle.Kill();
            Assert.IsFalse(handle.IsPlaying());
        }
    }
}
