using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;

namespace DDrive.Tests.Performance
{
    // [11_tasks.md] 6-2 / [04_vfx.md] §3 — VfxManager の定常経路(Tick)の 0 alloc と、
    // Spawn(=Play)側の既知課題(下記コメント)を記録する。
    public class VfxAllocTests
    {
        private PoolService _pool;
        private AssetRegistry _registry;
        private VfxManager _manager;
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _registry = new AssetRegistry(new NullAssetLoader());
            _manager = new VfxManager(_pool, _registry);
            _prefab = CreateParticlePrefab();
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_prefab);
        }

        private static GameObject CreateParticlePrefab()
        {
            var go = new GameObject("VfxAllocPrefab");
            var ps = go.AddComponent<ParticleSystem>();
            // AddComponent は Play On Awake で即座に再生を始めるため、main の設定を変更する前に
            // 必ず先に停止させる(既存の VfxManagerTests と同じ手順)。
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 5f;
            main.loop = false;
            main.startLifetime = 5f;
            return go;
        }

        private VfxData CreateData(ulong id)
        {
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.Id = id;
            data.Prefab = _prefab;
            data.LifeMode = VfxLifeMode.Loop; // Tick で寿命切れにならないようにする
            data.Duration = 1000f;
            return data;
        }

        // Tick は毎フレーム必ず呼ばれる定常経路。既に再生中の Instance を回すだけなら 0 alloc であるべき
        // ([12_review.md] §3)。
        [Test, Performance]
        public void Tick_EightActiveLoops_AllocatesNothing()
        {
            var data = CreateData(1);
            for (var i = 0; i < 8; i++)
            {
                _manager.SpawnData(data);
            }

            AllocProbe.AssertZeroAlloc(
                "Vfx.Tick",
                warmup: () => _manager.Tick(0.016f),
                measured: () => _manager.Tick(0.016f));
        }

        // Spawn(=Play)は「1 アクションにつき 1 回」の経路であり、Tick と違って毎フレーム走るわけではない。
        // VfxManager.SpawnDataLocal は VfxInstance(class)を 1 個 new し、Materialize が
        // MaterialPropertyBlock も再生成する既存設計(6-2 で判明)。Instance をプールし直すのは
        // Handle の世代管理(InstanceStore<TMarker,TInstance>)を含む大きな設計変更になり、
        // 6-6(Presentation/Net)と並行作業中の他ファイルへの影響も未知数のため本チケットでは対象外とする
        // (既知課題として [12_review.md] §3 に記録)。Pool の Rent/Return 自体は本チケットで
        // 0 alloc 化済み(PoolAllocTests)。ここでは Performance レポートに記録するだけに留め、
        // CI の fail 条件にはしない。
        [Test, Performance]
        public void SpawnData_StopImmediately_RecordsAllocForKnownIssue()
        {
            var data = CreateData(2);
            data.FadeOutSec = 0f;

            AllocProbe.RecordGc("Vfx.SpawnStop", () =>
            {
                var handle = _manager.SpawnData(data);
                _manager.Stop(handle);
            });
        }
    }
}
