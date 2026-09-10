using System.Collections.Generic;
using DDrive.Foundation.Event;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Prefab;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    public class PrefabsManagerTests
    {
        private PoolService _pool;
        private AssetRegistry _registry;
        private FakeAssetLoader _loader;
        private PrefabsManager _manager;
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _loader = new FakeAssetLoader();
            _registry = new AssetRegistry(_loader);
            _manager = new PrefabsManager(_pool, _registry);

            _prefab = new GameObject("PrefabTestPrefab");
            var child = new GameObject("Child");
            child.transform.SetParent(_prefab.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_prefab);
        }

        private PrefabData CreatePrefabData(ulong id)
        {
            var data = ScriptableObject.CreateInstance<PrefabData>();
            data.Id = id;
            data.Prefab = _prefab;
            return data;
        }

        [Test]
        public void SpawnData_PositionsRoot_AndSetsLayerRecursively_WhenCollisionLayerSet()
        {
            var data = CreatePrefabData(1);
            data.CollisionLayer = 7;
            var pos = new Vector3(1f, 2f, 3f);

            var handle = _manager.SpawnData(data, pos, Quaternion.identity);

            Assert.AreNotEqual(Handle<PrefabMarker>.Invalid, handle);
            var root = _manager.GetGameObject(handle);
            Assert.AreEqual(pos, root.transform.position);
            Assert.AreEqual(7, root.layer);
            Assert.AreEqual(7, root.transform.GetChild(0).gameObject.layer);
        }

        [Test]
        public void SpawnData_NegativeCollisionLayer_KeepsPrefabLayer()
        {
            _prefab.layer = 3;
            var data = CreatePrefabData(1);
            data.CollisionLayer = -1;

            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var root = _manager.GetGameObject(handle);

            Assert.AreEqual(3, root.layer);
        }

        [Test]
        public void Spawn_WithParent_ParentsRoot()
        {
            var data = CreatePrefabData(1);
            RegisterAndCache(data);
            var parentGo = new GameObject("Parent");

            var handle = _manager.Spawn(new AssetId<PrefabMarker>(data.Id, AssetType.Prefab), parentGo.transform);
            var root = _manager.GetGameObject(handle);

            Assert.AreEqual(parentGo.transform, root.transform.parent);
            Object.DestroyImmediate(parentGo);
        }

        [Test]
        public void Despawn_ReturnsToPool_AndInvalidatesHandle_AndReuseGivesSameGameObject()
        {
            var data = CreatePrefabData(1);
            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var root = _manager.GetGameObject(handle);
            _manager.Despawn(handle);

            Assert.IsNull(_manager.GetGameObject(handle));
            Assert.IsFalse(_manager.IsValid(handle));

            var handle2 = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            Assert.AreSame(root, _manager.GetGameObject(handle2));
        }

        [Test]
        public void HasTag_And_GetComponent_ResolveFromInstance()
        {
            _prefab.AddComponent<BoxCollider>();
            var data = CreatePrefabData(1);
            data.GameplayTags = new[] { "Destructible" };

            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);

            Assert.IsTrue(_manager.HasTag(handle, "Destructible"));
            Assert.IsFalse(_manager.HasTag(handle, "Pickup"));
            Assert.IsNotNull(_manager.GetComponent<BoxCollider>(handle));
            Assert.IsNull(_manager.GetComponent<Rigidbody>(handle));
        }

        [Test]
        public void SpawnAndDespawn_FireOnSpawnAndOnDestroy_ViaEventBus()
        {
            var fired = new List<EventTrigger>();
            _manager.Events.OnEventFired += (_, evt) => fired.Add(evt.Trigger);

            var data = CreatePrefabData(1);
            data.Events = new[]
            {
                new AssetEvent { Trigger = EventTrigger.OnSpawn },
                new AssetEvent { Trigger = EventTrigger.OnDestroy },
            };

            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            Assert.Contains(EventTrigger.OnSpawn, fired);

            _manager.Despawn(handle);
            Assert.Contains(EventTrigger.OnDestroy, fired);
        }

        [Test]
        public void SpawnData_UnregisteredId_SpawnsPlaceholder_WithoutThrowing()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*PrefabData.*"));
            Handle<PrefabMarker> handle = default;
            Assert.DoesNotThrow(() => handle = _manager.Spawn(new AssetId<PrefabMarker>(0xDEAD, AssetType.Prefab), Vector3.zero, Quaternion.identity));

            var root = _manager.GetGameObject(handle);
            Assert.IsNotNull(root);
            Assert.AreEqual("<Placeholder:PREFAB>", root.name);

            Assert.DoesNotThrow(() => _manager.Despawn(handle));
        }

        [Test]
        public void Preload_DoesNotThrow_AndSubsequentSpawnIsActive()
        {
            var data = CreatePrefabData(1);
            data.Flags.Pool = DDrive.Foundation.Data.PoolPolicy.Pooled(2, 8);

            Assert.DoesNotThrow(() => _manager.Preload(new AssetId<PrefabMarker>(data.Id, AssetType.Prefab)));

            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var root = _manager.GetGameObject(handle);
            Assert.IsNotNull(root);
            Assert.IsTrue(root.activeSelf);
        }

        [Test]
        public void StopAll_DespawnsEveryActiveInstance()
        {
            var data = CreatePrefabData(1);
            var h1 = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var h2 = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);

            _manager.StopAll(DDrive.Foundation.Manager.StopReason.Manual);

            Assert.IsNull(_manager.GetGameObject(h1));
            Assert.IsNull(_manager.GetGameObject(h2));
        }

        // registry.ResolveOrPlaceholder は既にロード済み(_loaded にキャッシュ済み)の ID しか実データを返さない
        // ([02] AssetRegistry.ResolveOrPlaceholder)ため、Spawn(id,...) を使うテストは先にカタログ登録 +
        // 1 回 ResolveAsync してキャッシュに乗せておく。
        private void RegisterAndCache(PrefabData data)
        {
            var address = "prefab/" + data.Id;
            _loader.Assets[address] = data;

            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = data.Id, Type = AssetType.Prefab, Address = address } });
            _registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            _registry.ResolveAsync<PrefabData>(data.Id).GetAwaiter().GetResult();
        }
    }
}
