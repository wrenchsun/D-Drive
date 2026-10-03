using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using ExternalPackage.Fake;
using NUnit.Framework;
using UnityEngine;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] 外部拡張の契約(Model / Pool)。E-1(A-1)・E-13(doc17 §4 罠 1 の回避策の前提)。
    // R-6(ルートに外部の IPoolable があると ModelInstancePoolable.OnReturn も呼ばれる)は FC-2 の
    // ModelsManagerReturnNotifyTests.ExternalIPoolableOnRoot_BothOnReturnsRun が固定済み(重複させない)。
    public class ExternalContractModelTests
    {
        private readonly List<Object> _cleanup = new();
        private PoolService _pool;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            foreach (var o in _cleanup)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _cleanup.Clear();
        }

        private T Own<T>(T o) where T : Object
        {
            _cleanup.Add(o);
            return o;
        }

        // E-1(A-1): Prefab に付けた外部 MonoBehaviour は Instantiate されたまま生き、プール往復で同じインスタンスが再利用される。
        [Test]
        public void E1_ExternalComponentOnPrefab_SurvivesPoolRoundTrip_AndListenerIsNotified()
        {
            var registry = new AssetRegistry(new ExternalContractLoader());
            var models = new ModelsManager(_pool, registry);

            var prefab = Own(new GameObject("ExternalContractPrefab"));
            prefab.AddComponent<ExternalLifecycleProbe>();
            prefab.AddComponent<ExternalModelListener>();

            var data = Own(ScriptableObject.CreateInstance<ModelData>());
            data.Id = 1;
            data.Prefab = prefab;
            data.Flags.Pool = PoolPolicy.Pooled(0, 4);

            var h1 = models.SpawnData(data, Vector3.zero, Quaternion.identity);
            var root = models.GetGameObject(h1);
            var probe = root.GetComponent<ExternalLifecycleProbe>();
            var listener = root.GetComponent<ExternalModelListener>();
            Assert.IsNotNull(probe, "Prefab 上の外部コンポーネントが Instantiate されたインスタンスに残る");
            Assert.IsTrue(root.activeInHierarchy);
            Assert.AreEqual(1, probe.AwakeCount);
            Assert.GreaterOrEqual(probe.EnableCount, 1);
            var enableAfterSpawn = probe.EnableCount;
            var disableAfterSpawn = probe.DisableCount;
            Assert.AreEqual(1, listener.SpawnedCount);
            Assert.AreSame(root, listener.LastRoot);

            models.Despawn(h1);
            Assert.IsFalse(root.activeSelf, "プール返却で非アクティブになる");
            Assert.AreEqual(disableAfterSpawn + 1, probe.DisableCount, "Despawn(プール返却)で OnDisable が 1 回呼ばれる");
            Assert.AreEqual(1, listener.ReturningCount);
            Assert.AreEqual(0, probe.DestroyCount, "プールに戻すだけで Destroy されない");

            var h2 = models.SpawnData(data, Vector3.zero, Quaternion.identity);
            Assert.AreSame(root, models.GetGameObject(h2), "同じインスタンスが再利用される");
            Assert.AreSame(probe, root.GetComponent<ExternalLifecycleProbe>());
            Assert.IsTrue(root.activeInHierarchy);
            Assert.AreEqual(1, probe.AwakeCount, "Awake は初回のみ");
            Assert.AreEqual(enableAfterSpawn + 1, probe.EnableCount, "再スポーンで OnEnable が再度 1 回呼ばれる");
            Assert.AreEqual(2, listener.SpawnedCount);

            models.Despawn(h2);
        }

        // OnModelReturning の中で同じハンドルを Despawn されても無限再帰せず、二重返却にもならない(FC-R-08)。
        [Test]
        public void ListenerDespawningSameHandleInOnModelReturning_DoesNotRecurse()
        {
            var registry = new AssetRegistry(new ExternalContractLoader());
            var models = new ModelsManager(_pool, registry);

            var prefab = Own(new GameObject("ExternalContractSelfDespawnPrefab"));
            var listener = prefab.AddComponent<SelfDespawnListener>();
            var data = Own(ScriptableObject.CreateInstance<ModelData>());
            data.Id = 2;
            data.Prefab = prefab;
            data.Flags.Pool = PoolPolicy.Pooled(0, 4);

            var handle = models.SpawnData(data, Vector3.zero, Quaternion.identity);
            var root = models.GetGameObject(handle);
            var instanceListener = root.GetComponent<SelfDespawnListener>();
            instanceListener.Models = models;

            Assert.DoesNotThrow(() => models.Despawn(handle));

            Assert.AreEqual(1, instanceListener.ReturningCount, "通知は 1 回だけ");
            Assert.IsFalse(root.activeSelf, "プールへ 1 回だけ返却される");
            Assert.IsNotNull(listener);
        }

        private sealed class SelfDespawnListener : MonoBehaviour, IModelInstanceListener
        {
            public ModelsManager Models;
            public int ReturningCount;

            public void OnModelSpawned(in ModelInstanceContext context) { }

            public void OnModelReturning(in ModelInstanceContext context)
            {
                ReturningCount++;
                Models?.Despawn(context.Handle);
            }
        }

        // E-13: ModelData.Slots が空 / Material が無効 ID のスロットは Prefab の sharedMaterials を触らない。有効 ID のスロットだけ差し替わる。
        [Test]
        public void E13_EmptyOrInvalidSlots_KeepPrefabMaterials_ValidSlotIsReplaced()
        {
            var loader = new ExternalContractLoader();
            var registry = new AssetRegistry(loader);
            var materials = new MaterialManager(registry);
            var models = new ModelsManager(_pool, registry, null, materials);

            var shader = Shader.Find("Hidden/ExternalContract/ToonProps");
            Assert.IsNotNull(shader, "テスト用シェーダー(Tests/Runtime/ExternalContract/Shaders)が見つからない");

            var prefabMaterial = Own(new UnityEngine.Material(shader));
            var prefab = Own(GameObject.CreatePrimitive(PrimitiveType.Sphere));
            prefab.GetComponent<Renderer>().sharedMaterial = prefabMaterial;

            var matData = Own(ScriptableObject.CreateInstance<MaterialData>());
            matData.Id = 11;
            matData.DisplayName = "ExternalContractMat";
            matData.Shader = shader;
            matData.Common = MaterialCommon.Default;
            ExternalContractRegistry.Register(loader, registry, AssetType.Material, matData);

            UnityEngine.Material SpawnAndRead(MaterialSlot[] slots, ulong id)
            {
                var data = Own(ScriptableObject.CreateInstance<ModelData>());
                data.Id = id;
                data.Prefab = prefab;
                data.Slots = slots;
                var h = models.SpawnData(data, Vector3.zero, Quaternion.identity);
                var material = models.GetGameObject(h).GetComponent<Renderer>().sharedMaterial;
                models.Despawn(h);
                return material;
            }

            Assert.AreSame(prefabMaterial, SpawnAndRead(null, 1), "Slots が null");
            Assert.AreSame(prefabMaterial, SpawnAndRead(new MaterialSlot[0], 2), "Slots が空");
            Assert.AreSame(prefabMaterial, SpawnAndRead(new[] { new MaterialSlot { RendererPath = string.Empty, SlotIndex = 0 } }, 3), "Material が無効 ID のスロット");

            var replaced = SpawnAndRead(new[]
            {
                new MaterialSlot { RendererPath = string.Empty, SlotIndex = 0, Material = new AssetId<MaterialMarker>(11UL, AssetType.Material) },
            }, 4);
            Assert.AreNotSame(prefabMaterial, replaced, "有効 ID のスロットだけが差し替わる");
            Assert.AreSame(materials.GetData(matData), replaced);

            materials.Clear();
        }

#if UNITY_EDITOR
        // E-17: 合成 FBX(ボーン Hips / Spine / head + シェイプ FC_ / fcs_ / smile)を ModelsManager でスポーン〜返却〜再スポーンしても、
        // ボーンの GameObject・名前・ローカル姿勢・シェイプ名・スケールが変わらない。静的確認・取り込み側は Editor の ExternalContractImportTests。
        [Test]
        public void E17_ImportedFbx_SpawnedThroughModelsManager_KeepsBonesShapesAndScale_AcrossPoolRoundTrip()
        {
            const string Fixture = "Packages/com.ddrive.core/Tests/Editor/ExternalContract/Fixtures/ExternalContractRig.fbx";
            var fbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Fixture);
            Assert.IsNotNull(fbx, "合成 FBX フィクスチャを読めない: " + Fixture);
            var source = fbx.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var boneNames = new List<string>();
            foreach (var b in source.bones)
            {
                boneNames.Add(b.name);
            }

            var models = new ModelsManager(_pool, new AssetRegistry(new ExternalContractLoader()));
            var data = Own(ScriptableObject.CreateInstance<ModelData>());
            data.Id = 17;
            data.Prefab = fbx;
            data.Flags.Pool = PoolPolicy.Pooled(0, 2);

            for (var round = 0; round < 2; round++)
            {
                var h = models.SpawnData(data, new Vector3(1f, 2f, 3f), Quaternion.identity);
                var root = models.GetGameObject(h);
                var smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.IsNotNull(smr, $"round {round}");
                Assert.AreEqual(source.bones.Length, smr.bones.Length);
                for (var i = 0; i < smr.bones.Length; i++)
                {
                    Assert.AreEqual(boneNames[i], smr.bones[i].name, $"round {round}: ボーン名");
                    Assert.AreEqual(source.bones[i].localPosition, smr.bones[i].localPosition, $"round {round}: ボーンのローカル位置");
                    Assert.AreEqual(source.bones[i].localScale, smr.bones[i].localScale, $"round {round}: ボーンのスケール");
                }

                Assert.AreEqual(source.sharedMesh.blendShapeCount, smr.sharedMesh.blendShapeCount);
                for (var i = 0; i < source.sharedMesh.blendShapeCount; i++)
                {
                    Assert.AreEqual(source.sharedMesh.GetBlendShapeName(i), smr.sharedMesh.GetBlendShapeName(i), $"round {round}: シェイプ名");
                }

                Assert.AreEqual(fbx.transform.localScale, root.transform.localScale, $"round {round}: ルートのスケール(Spawn は位置・回転・親だけ)");
                models.Despawn(h);
            }
        }
#endif
    }
}
