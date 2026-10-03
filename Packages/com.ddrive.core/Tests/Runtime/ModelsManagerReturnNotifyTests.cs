using System.Collections.Generic;
using System.Text.RegularExpressions;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Model;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    // FC-2(返却時にブレンドシェイプの重みを戻す)/ FC-12(IModelInstanceListener)、[docs/51] §4.3・§4.13。
    public class ModelsManagerReturnNotifyTests
    {
        private const string FacialShape = "FC_Hero_Eye_R0_C0";
        private const string SculptShape = "fcs_Hero_Eye";
        private const string PlainShape = "Smile";

        private static readonly List<string> Log = new();

        private sealed class Listener : MonoBehaviour, IModelInstanceListener
        {
            public string Tag = "L";
            public bool ThrowOnSpawn;
            public bool ThrowOnReturning;
            public SkinnedMeshRenderer Smr;
            public float WeightAtReturning = -1f;
            public int SpawnedCount;
            public int ReturningCount;
            public GameObject LastRoot;

            public void OnModelSpawned(in ModelInstanceContext context)
            {
                SpawnedCount++;
                LastRoot = context.Root;
                Log.Add(Tag + ":spawned");
                if (ThrowOnSpawn)
                {
                    throw new System.InvalidOperationException("spawn listener test exception");
                }
            }

            public void OnModelReturning(in ModelInstanceContext context)
            {
                ReturningCount++;
                Log.Add(Tag + ":returning");
                if (Smr != null)
                {
                    WeightAtReturning = Smr.GetBlendShapeWeight(0);
                }

                if (ThrowOnReturning)
                {
                    throw new System.InvalidOperationException("returning listener test exception");
                }
            }
        }

        private sealed class ExternalPoolable : MonoBehaviour, IPoolable
        {
            public int ReturnCount;
            public void OnReturn() => ReturnCount++;
        }

        private PoolService _pool;
        private ModelsManager _manager;
        private GameObject _prefab;
        private SkinnedMeshRenderer _prefabSmr;
        private Mesh _mesh;

        [SetUp]
        public void SetUp()
        {
            Log.Clear();
            _pool = new PoolService();
            _manager = new ModelsManager(_pool, new AssetRegistry(new FakeAssetLoader()));

            _mesh = new Mesh();
            _mesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right };
            _mesh.triangles = new[] { 0, 1, 2 };
            var delta = new Vector3[3];
            delta[1] = Vector3.one;
            _mesh.AddBlendShapeFrame(FacialShape, 100f, delta, null, null);
            _mesh.AddBlendShapeFrame(SculptShape, 100f, delta, null, null);
            _mesh.AddBlendShapeFrame(PlainShape, 100f, delta, null, null);

            _prefab = new GameObject("FcModelPrefab");
            var body = new GameObject("Body");
            body.transform.SetParent(_prefab.transform);
            _prefabSmr = body.AddComponent<SkinnedMeshRenderer>();
            _prefabSmr.sharedMesh = _mesh;
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_prefab);
            Object.DestroyImmediate(_mesh);
        }

        private ModelData CreateData(ulong id, bool pooled = true, int max = 8)
        {
            var data = ScriptableObject.CreateInstance<ModelData>();
            data.Id = id;
            data.Prefab = _prefab;
            if (pooled)
            {
                data.Flags.Pool = PoolPolicy.Pooled(0, max);
            }

            return data;
        }

        private SkinnedMeshRenderer SmrOf(Handle<ModelMarker> h)
            => _manager.GetGameObject(h).GetComponentInChildren<SkinnedMeshRenderer>();

        private static void WriteAll(SkinnedMeshRenderer smr, float v)
        {
            for (var i = 0; i < smr.sharedMesh.blendShapeCount; i++)
            {
                smr.SetBlendShapeWeight(i, v);
            }
        }

        [Test]
        public void Despawn_RespawnRestoresDefaultWeights_IncludingFcAndFcsShapes()
        {
            var data = CreateData(1);
            var h = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var root = _manager.GetGameObject(h);
            WriteAll(SmrOf(h), 77f);

            _manager.Despawn(h);
            var h2 = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);

            Assert.AreSame(root, _manager.GetGameObject(h2));
            for (var i = 0; i < 3; i++)
            {
                Assert.AreEqual(0f, SmrOf(h2).GetBlendShapeWeight(i), "shape " + i);
            }
        }

        [Test]
        public void Despawn_RestoresPrefabInitialNonZeroWeights()
        {
            _prefabSmr.SetBlendShapeWeight(1, 40f);
            var data = CreateData(1);
            var h = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var smr = SmrOf(h);
            Assert.AreEqual(40f, smr.GetBlendShapeWeight(1));
            WriteAll(smr, 90f);

            _manager.Despawn(h);

            Assert.AreEqual(0f, smr.GetBlendShapeWeight(0));
            Assert.AreEqual(40f, smr.GetBlendShapeWeight(1));
            Assert.AreEqual(0f, smr.GetBlendShapeWeight(2));
        }

        [Test]
        public void ForceReclaim_AtPoolLimit_RestoresWeights()
        {
            var data = CreateData(1, true, 1);
            var first = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var smr = SmrOf(first);
            WriteAll(smr, 55f);

            var second = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);

            Assert.IsFalse(_manager.IsValid(first));
            Assert.AreSame(smr, SmrOf(second), "強制回収された GameObject が再利用される");
            Assert.AreEqual(0f, smr.GetBlendShapeWeight(0));
            Assert.AreEqual(0f, smr.GetBlendShapeWeight(2));
        }

        [Test]
        public void NoSkinnedMeshRenderer_DoesNotThrow()
        {
            Object.DestroyImmediate(_prefabSmr.gameObject);
            var data = CreateData(1);
            var h = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            Assert.DoesNotThrow(() => _manager.Despawn(h));
        }

        [Test]
        public void NullSharedMesh_DoesNotThrow_AndOtherRenderersStillRestored()
        {
            var extra = new GameObject("Extra");
            extra.transform.SetParent(_prefab.transform);
            extra.AddComponent<SkinnedMeshRenderer>(); // sharedMesh = null
            var data = CreateData(1);
            var h = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var smr = SmrOf(h);
            smr.SetBlendShapeWeight(0, 30f);

            Assert.DoesNotThrow(() => _manager.Despawn(h));
            Assert.AreEqual(0f, smr.GetBlendShapeWeight(0));
        }

        [Test]
        public void Listener_SpawnedIsCalledOnce_WithRoot()
        {
            _prefab.AddComponent<Listener>();
            var data = CreateData(1);

            var h = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var l = _manager.GetGameObject(h).GetComponent<Listener>();

            Assert.AreEqual(1, l.SpawnedCount);
            Assert.AreSame(_manager.GetGameObject(h), l.LastRoot);
        }

        [Test]
        public void Listener_ReturningRunsBeforeWeightReset()
        {
            _prefab.AddComponent<Listener>();
            var data = CreateData(1);
            var h = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var l = _manager.GetGameObject(h).GetComponent<Listener>();
            l.Smr = SmrOf(h);
            l.Smr.SetBlendShapeWeight(0, 66f);

            _manager.Despawn(h);

            Assert.AreEqual(1, l.ReturningCount);
            Assert.AreEqual(66f, l.WeightAtReturning, "通知の時点ではまだ重みが残っている(通知 → リセットの順)");
            Assert.AreEqual(0f, l.Smr.GetBlendShapeWeight(0), "通知の後にリセットされる");
        }

        [Test]
        public void Listener_ReturningRunsOnForceReclaim()
        {
            _prefab.AddComponent<Listener>();
            var data = CreateData(1, true, 1);
            var first = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var l = _manager.GetGameObject(first).GetComponent<Listener>();

            _manager.SpawnData(data, Vector3.zero, Quaternion.identity);

            Assert.AreEqual(1, l.ReturningCount);
        }

        [Test]
        public void Listener_ReturningRunsOnDiscardPath()
        {
            _prefab.AddComponent<Listener>();
            var data = CreateData(1, pooled: false);
            var h = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var l = _manager.GetGameObject(h).GetComponent<Listener>();

            _manager.Despawn(h);

            Assert.AreEqual(1, l.ReturningCount);
        }

        [Test]
        public void Listener_ThrowingDoesNotStopOthersOrReturn()
        {
            var bad = _prefab.AddComponent<Listener>();
            bad.Tag = "bad";
            bad.ThrowOnSpawn = true;
            bad.ThrowOnReturning = true;
            var good = _prefab.AddComponent<Listener>();
            good.Tag = "good";
            var data = CreateData(1);

            LogAssert.Expect(LogType.Exception, new Regex("spawn listener test exception"));
            var h = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var smr = SmrOf(h);
            smr.SetBlendShapeWeight(0, 12f);

            LogAssert.Expect(LogType.Exception, new Regex("returning listener test exception"));
            _manager.Despawn(h);

            Assert.IsFalse(_manager.IsValid(h));
            CollectionAssert.AreEqual(new[] { "bad:spawned", "good:spawned", "bad:returning", "good:returning" }, Log);
            Assert.AreEqual(0f, smr.GetBlendShapeWeight(0));
        }

        [Test]
        public void ExternalIPoolableOnRoot_BothOnReturnsRun()
        {
            // R-6(U-3 = a): ルートに外部の IPoolable があっても ModelInstancePoolable.OnReturn も呼ばれる。
            _prefab.AddComponent<ExternalPoolable>();
            var data = CreateData(1, true, 1);
            var first = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            var root = _manager.GetGameObject(first);
            var smr = SmrOf(first);
            smr.SetBlendShapeWeight(0, 50f);

            _manager.Despawn(first);

            Assert.AreEqual(1, root.GetComponent<ExternalPoolable>().ReturnCount);
            Assert.AreEqual(0f, smr.GetBlendShapeWeight(0));

            // 強制回収経路でも台帳が掃除される(ModelInstancePoolable.OnReturn が呼ばれる)。
            var a = _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            _manager.SpawnData(data, Vector3.zero, Quaternion.identity);
            Assert.IsFalse(_manager.IsValid(a));
        }
    }
}
