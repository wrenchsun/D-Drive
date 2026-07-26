using System.Collections;
using DDrive.Foundation.Pool;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    public class PoolServiceTests
    {
        private sealed class Poolable : MonoBehaviour, IPoolable
        {
            public int ReturnCount;
            public void OnReturn() => ReturnCount++;
        }

        [UnityTest]
        public IEnumerator Rent_ReusesReturnedInstance()
        {
            var prefab = new GameObject("Prefab");
            var pool = new PoolService();

            var a = pool.Rent(prefab);
            pool.Return(a);
            var b = pool.Rent(prefab);

            Assert.AreSame(a.GameObject, b.GameObject);

            pool.Clear(PoolScope.Global);
            yield return null;
            Object.DestroyImmediate(prefab);
        }

        [UnityTest]
        public IEnumerator Return_CallsIPoolableOnReturn()
        {
            var prefab = new GameObject("Prefab");
            prefab.AddComponent<Poolable>();
            var pool = new PoolService();

            var rented = pool.Rent(prefab);
            var poolable = rented.GameObject.GetComponent<Poolable>();
            pool.Return(rented);

            Assert.AreEqual(1, poolable.ReturnCount);

            pool.Clear(PoolScope.Global);
            yield return null;
            Object.DestroyImmediate(prefab);
        }

        [UnityTest]
        public IEnumerator Rent_AtLimit_ReclaimsOnlyLowestPriorityActive()
        {
            var prefab = new GameObject("Prefab");
            var pool = new PoolService();
            pool.SetLimit(prefab, 2);

            var low = pool.Rent(prefab);
            low.Priority = 0;
            var high = pool.Rent(prefab);
            high.Priority = 10;

            var lowGo = low.GameObject;
            var highGo = high.GameObject;

            var third = pool.Rent(prefab);
            third.Priority = 3;

            Assert.AreSame(lowGo, third.GameObject);
            Assert.IsTrue(highGo.activeSelf);

            pool.Clear(PoolScope.Global);
            yield return null;
            Object.DestroyImmediate(prefab);
        }

        [UnityTest]
        public IEnumerator Prewarm_CreatesInstancesReadyToRentActivated()
        {
            var prefab = new GameObject("Prefab");
            var pool = new PoolService();

            pool.Prewarm(prefab, 3);
            var rented = pool.Rent(prefab);

            Assert.IsTrue(rented.GameObject.activeSelf);

            pool.Clear(PoolScope.Global);
            yield return null;
            Object.DestroyImmediate(prefab);
        }

        [UnityTest]
        public IEnumerator Clear_Scene_KeepsPersistentPools()
        {
            var prefab = new GameObject("Prefab");
            var pool = new PoolService();
            pool.SetLimit(prefab, 10, persistent: true);

            var rented = pool.Rent(prefab);
            pool.Clear(PoolScope.Scene);
            yield return null;

            Assert.IsNotNull(rented.GameObject);

            pool.Clear(PoolScope.Global);
            yield return null;
            Object.DestroyImmediate(prefab);
        }
    }
}
