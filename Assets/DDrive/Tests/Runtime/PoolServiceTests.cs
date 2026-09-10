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
        public IEnumerator Rent_AtLimit_WithDestroyedActive_DoesNotThrowAndRentsLiveInstance()
        {
            var prefab = new GameObject("Prefab");
            var pool = new PoolService();
            pool.SetLimit(prefab, 1);

            var dead = pool.Rent(prefab);
            dead.Priority = 0;
            Object.DestroyImmediate(dead.GameObject);

            // 破棄済み GO を持つ Active エントリしか無い状態で上限に達しても、Free.Pop で例外にならず
            // 生きた Instance が返ること(レビュー指摘 1)。
            PooledObject rented = null;
            Assert.DoesNotThrow(() => rented = pool.Rent(prefab));
            Assert.IsNotNull(rented);
            Assert.IsNotNull(rented.GameObject);
            Assert.IsTrue(rented.GameObject.activeSelf);

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

        // Codex レビュー 2026-09-10: Discard は Return と違い Free に積まず GameObject を破棄する
        // (Kind == None の Prefabs/Models が「プールしない」を実現するための API)。
        [UnityTest]
        public IEnumerator Discard_RemovesFromActive_AndDestroysGameObject_WithoutPushingToFree()
        {
            var prefab = new GameObject("Prefab");
            var pool = new PoolService();

            var rented = pool.Rent(prefab);
            var go = rented.GameObject;

            pool.Discard(rented);
            yield return null;

            Assert.IsTrue(go == null, "Discard された GameObject は破棄されているはず。");

            // Free に積まれていなければ、次の Rent は新規 Instantiate になる(同じ GO は再利用されない)。
            var next = pool.Rent(prefab);
            Assert.AreNotSame(go, next.GameObject);

            pool.Clear(PoolScope.Global);
            yield return null;
            Object.DestroyImmediate(prefab);
        }

        [UnityTest]
        public IEnumerator Discard_AlreadyDiscarded_IsIdempotent()
        {
            var prefab = new GameObject("Prefab");
            var pool = new PoolService();

            var rented = pool.Rent(prefab);
            pool.Discard(rented);
            yield return null;

            Assert.DoesNotThrow(() => pool.Discard(rented));

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
