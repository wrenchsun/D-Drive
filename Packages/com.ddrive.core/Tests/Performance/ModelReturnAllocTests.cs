using DDrive.Foundation.Data;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Model;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;

namespace DDrive.Tests.Performance
{
    // FC-2 / FC-12 — 返却時の通知とブレンドシェイプ復元は生成時のキャッシュ(配列)を for で回すだけで、
    // 復元・通知の処理自体は割り当てを増やさない。ただし Spawn 側は ModelInstance(class)と台帳のクロージャを
    // 1 つずつ作る既知の割り当てがある([12_review.md] §3、VfxAllocTests と同じ扱い)ため、ここでは厳密な 0 alloc を
    // 強制せず、Spawn → 重みを書く → Despawn の往復を Performance レポートに記録するだけにする。
    public class ModelReturnAllocTests
    {
        private sealed class NoopListener : MonoBehaviour, IModelInstanceListener
        {
            public void OnModelSpawned(in ModelInstanceContext context) { }
            public void OnModelReturning(in ModelInstanceContext context) { }
        }

        [Test, Performance]
        public void SpawnDespawn_WithBlendShapeRestoreAndListener_RecordsGc()
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.AddBlendShapeFrame("FC_A", 100f, new Vector3[3], null, null);
            mesh.AddBlendShapeFrame("Smile", 100f, new Vector3[3], null, null);
            var prefab = new GameObject("ModelReturnAllocPrefab");
            var smr = new GameObject("Body").AddComponent<SkinnedMeshRenderer>();
            smr.transform.SetParent(prefab.transform);
            smr.sharedMesh = mesh;
            prefab.AddComponent<NoopListener>();

            var pool = new PoolService();
            var manager = new ModelsManager(pool, new AssetRegistry(new NullAssetLoader()));
            var data = ScriptableObject.CreateInstance<ModelData>();
            data.Id = 1;
            data.Prefab = prefab;
            data.Flags.Pool = PoolPolicy.Pooled(0, 4);

            try
            {
                AllocProbe.RecordGc("Model.SpawnDespawn.BlendShapeRestore", () =>
                {
                    var h = manager.SpawnData(data, Vector3.zero, Quaternion.identity);
                    manager.GetGameObject(h).GetComponentInChildren<SkinnedMeshRenderer>().SetBlendShapeWeight(0, 50f);
                    manager.Despawn(h);
                });
            }
            finally
            {
                pool.Clear(PoolScope.Global);
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(data);
            }
        }
    }
}
