using DDrive.Foundation.Pool;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;

namespace DDrive.Tests.Performance
{
    // [11_tasks.md] 6-2 — Foundation/Pool の Rent/Return は Vfx/Se/Prefab/Model の Spawn/Despawn
    // ごとに必ず通る定常経路。6-2 で判明した既知バグ(Rent の度に "new PooledObject(...)" していた)を
    // PoolService.Rent/ForceReturn で修正し(Free に GameObject でなく PooledObject 自体を積んで
    // ラッパーも再利用する)、通常の Rent→Return サイクルが 0 alloc になったことを検証する。
    public class PoolAllocTests
    {
        private GameObject _prefab;
        private PoolService _pool;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("PoolAllocPrefab");
            _pool = new PoolService();
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_prefab);
        }

        [Test, Performance]
        public void RentReturn_SteadyCycle_AllocatesNothing()
        {
            AllocProbe.AssertZeroAlloc(
                "Pool.RentReturn",
                warmup: () =>
                {
                    var pooled = _pool.Rent(_prefab);
                    _pool.Return(pooled);
                },
                measured: () =>
                {
                    var pooled = _pool.Rent(_prefab);
                    _pool.Return(pooled);
                });
        }
    }
}
