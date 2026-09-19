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
        public void Despawn_InvalidatesHandle()
        {
            var data = CreateModelData(1);
            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            _manager.Despawn(handle);

            Assert.IsNull(_manager.GetGameObject(handle));
        }

        // Codex レビュー 2026-09-10: Kind == None(既定)は「プールしない」を意味する。
        // Despawn で GameObject が実際に破棄され、再 Spawn は別インスタンスになることを検証する。
        // PlayMode の Object.Destroy は次フレームまで実体が残るため UnityTest でフレームをまたぐ。
        [UnityTest]
        public System.Collections.IEnumerator Despawn_NonePolicy_DestroysGameObject_AndReuseGivesDifferentGameObject()
        {
            var data = CreateModelData(1); // Flags.Pool は既定(None)

            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var root = _manager.GetGameObject(handle);
            _manager.Despawn(handle);

            yield return null;

            Assert.IsTrue(root == null, "None ポリシーの Instance は Despawn で実際に破棄されるはず。");

            var handle2 = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var root2 = _manager.GetGameObject(handle2);
            Assert.IsNotNull(root2);
            Assert.AreNotSame(root, root2);
        }

        [Test]
        public void Despawn_PooledPolicy_ReturnsToPool_AndReuseGivesSameGameObject()
        {
            var data = CreateModelData(1);
            data.Flags.Pool = DDrive.Foundation.Data.PoolPolicy.Pooled(0, 8);

            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var root = _manager.GetGameObject(handle);
            _manager.Despawn(handle);

            Assert.IsNull(_manager.GetGameObject(handle));

            var handle2 = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            Assert.AreSame(root, _manager.GetGameObject(handle2));
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
        public void SetMaterial_ResolvesRendererByPath_StoresIdOnInstance()
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

            // 共有 Data は書き換えず、Instance 側の現在値として持つ(2026-09-09 レビュー P1-4)。
            Assert.AreEqual(0UL, data.Slots[0].Material.Value);
            Assert.IsTrue(_manager.TryGetMaterial(handle, 0, out var current));
            Assert.AreEqual(123UL, current.Value);
        }

        [Test]
        public void SetMaterial_OutOfRangeSlot_IsNoOp()
        {
            var data = CreateModelData(1);
            data.Slots = new MaterialSlot[0];
            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);

            Assert.DoesNotThrow(() => _manager.SetMaterial(handle, 0, AssetId<MaterialMarker>.Invalid));
        }

        // U-1(2026-09-17): 既定 0 をそのまま renderingLayerMask に書いていたため、URP の描画フィルタに
        // 1 つも一致せずモデルが描かれなかった(プレビューが透明)。VfxData と同じく 0 = Prefab の設定を保つ。
        [Test]
        public void LightLayerMask_DefaultIsOne()
        {
            var data = CreateModelData(1);
            Assert.AreEqual(1u, data.LightLayerMask);
        }

        [Test]
        public void LightLayerMask_Zero_KeepsPrefabRendererSetting()
        {
            _bodyChild.GetComponent<MeshRenderer>().renderingLayerMask = 4u;
            var data = CreateModelData(1);
            data.LightLayerMask = ModelData.LightLayerKeepPrefab;

            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);

            Assert.AreEqual(4u, _manager.GetGameObject(handle).GetComponentInChildren<MeshRenderer>(true).renderingLayerMask);
        }

        [Test]
        public void LightLayerMask_NonZero_OverridesRenderer()
        {
            _bodyChild.GetComponent<MeshRenderer>().renderingLayerMask = 4u;
            var data = CreateModelData(1);
            data.LightLayerMask = 2u;

            var handle = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);

            Assert.AreEqual(2u, _manager.GetGameObject(handle).GetComponentInChildren<MeshRenderer>(true).renderingLayerMask);
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

        // 2026-09-17 レビュー対応(P1-1 恒久策): Pool の上限超過で強制回収(evict)された Instance は
        // ModelsManager の台帳からも消える(= 古い Handle が無効になる)。これが無いと、回収後も古い
        // Handle 経由で「次の借り手」の GameObject を操作・Despawn できてしまう。
        [Test]
        public void SpawnData_AtPoolLimit_EvictedInstance_IsRemovedFromLedger()
        {
            var data = CreateModelData(1);
            data.Flags.Pool = DDrive.Foundation.Data.PoolPolicy.Pooled(0, 1);

            var first = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var second = _manager.SpawnData(data, Vector3.one, Quaternion.identity);

            Assert.IsFalse(_manager.IsValid(first), "強制回収された Handle は無効になる");
            Assert.IsTrue(_manager.IsValid(second));
        }

        // 回収済み Handle の Despawn が「次の借り手」を巻き込まないこと(P1-1 の最小修正側)。
        [Test]
        public void Despawn_OfEvictedHandle_DoesNotAffectNewInstance()
        {
            var data = CreateModelData(1);
            data.Flags.Pool = DDrive.Foundation.Data.PoolPolicy.Pooled(0, 1);

            var first = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var second = _manager.SpawnData(data, Vector3.one, Quaternion.identity);
            var secondRoot = _manager.GetGameObject(second);

            _manager.Despawn(first);

            Assert.IsTrue(_manager.IsValid(second), "回収済み Handle の Despawn で新しい Instance が消えてはいけない");
            Assert.IsTrue(secondRoot.activeSelf, "回収済み Handle の Despawn で新しい GameObject が非アクティブ化されてはいけない");
            Assert.AreEqual(0, _pool.FreeCount(_prefab), "貸出中の GameObject が Free に積み直されてはいけない");
        }
    }
}
