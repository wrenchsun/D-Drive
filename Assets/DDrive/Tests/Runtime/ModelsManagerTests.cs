using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    public class ModelsManagerTests
    {
        private PoolService _pool;
        private AssetRegistry _registry;
        private ModelsManager _manager;
        private GameObject _prefab;
        private GameObject _bodyChild;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _registry = new AssetRegistry(new FakeAssetLoader());
            _manager = new ModelsManager(_pool, _registry);

            _prefab = new GameObject("ModelTestPrefab");
            _bodyChild = new GameObject("Body");
            _bodyChild.transform.SetParent(_prefab.transform);
            _bodyChild.AddComponent<MeshRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_prefab);
        }

        private ModelData CreateModelData(ulong id)
        {
            var data = ScriptableObject.CreateInstance<ModelData>();
            data.Id = id;
            data.Prefab = _prefab;
            return data;
        }

        [Test]
        public void SpawnData_PositionsAndReturnsValidHandle()
        {
            var data = CreateModelData(1);
            var pos = new Vector3(1f, 2f, 3f);
            var handle = _manager.SpawnData(data, pos, Quaternion.identity);

            Assert.AreNotEqual(Handle<ModelMarker>.Invalid, handle);
            Assert.AreEqual(pos, _manager.GetGameObject(handle).transform.position);
        }

        [Test]
        public void SpawnData_NullPrefab_ReturnsInvalidHandle()
        {
            var data = CreateModelData(1);
            data.Prefab = null;

            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            Assert.AreEqual(Handle<ModelMarker>.Invalid, handle);
        }

        [Test]
        public void Despawn_ReturnsToPoolAndInvalidatesHandle()
        {
            var data = CreateModelData(1);
            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            _manager.Despawn(handle);

            Assert.IsNull(_manager.GetGameObject(handle));
        }

        [Test]
        public void SetLayer_AppliesRecursively()
        {
            var data = CreateModelData(1);
            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            _manager.SetLayer(handle, 7);

            var root = _manager.GetGameObject(handle);
            Assert.AreEqual(7, root.layer);
            Assert.AreEqual(7, root.transform.GetChild(0).gameObject.layer);
        }

        [Test]
        public void SetMaterial_ResolvesRendererByPath_StoresIdOnSlot()
        {
            var data = CreateModelData(1);
            data.Slots = new[]
            {
                new MaterialSlot { RendererPath = "Body", SlotIndex = 0 },
            };
            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);

            var materialId = new AssetId<MaterialMarker>(123UL, AssetType.Material);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*MaterialData.*"));
            _manager.SetMaterial(handle, 0, materialId);

            Assert.AreEqual(123UL, data.Slots[0].Material.Value);
        }

        [Test]
        public void SetMaterial_OutOfRangeSlot_IsNoOp()
        {
            var data = CreateModelData(1);
            data.Slots = new MaterialSlot[0];
            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);

            Assert.DoesNotThrow(() => _manager.SetMaterial(handle, 0, AssetId<MaterialMarker>.Invalid));
        }

        [Test]
        public void StopAll_DespawnsEveryActiveInstance()
        {
            var data = CreateModelData(1);
            var h1 = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var h2 = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);

            _manager.StopAll(StopReason.Manual);

            Assert.IsNull(_manager.GetGameObject(h1));
            Assert.IsNull(_manager.GetGameObject(h2));
        }
    }
}
