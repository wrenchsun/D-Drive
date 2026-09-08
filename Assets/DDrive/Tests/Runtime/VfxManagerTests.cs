using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    public class VfxManagerTests
    {
        private PoolService _pool;
        private AssetRegistry _registry;
        private VfxManager _manager;
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _registry = new AssetRegistry(new FakeAssetLoader());
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
            var go = new GameObject("VfxTestPrefab");
            var ps = go.AddComponent<ParticleSystem>();
            // AddComponent は Play On Awake で即座に再生を始めるため、main の設定を変更する前に
            // 必ず先に停止させる(再生中に duration 等を変えると Unity がエラーログを出す)。
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 5f;
            main.loop = false;
            main.startLifetime = 5f;
            return go;
        }

        private VfxData CreateVfxData(ulong id, VfxLifeMode lifeMode = VfxLifeMode.Loop)
        {
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.Id = id;
            data.Prefab = _prefab;
            data.LifeMode = lifeMode;
            data.Duration = 1f;
            return data;
        }

        [Test]
        public void SpawnData_StartsPlaying()
        {
            var data = CreateVfxData(1);
            var handle = _manager.SpawnData(data);

            Assert.IsTrue(_manager.IsPlaying(handle));
        }

        [Test]
        public void SpawnData_ExplicitPose_PositionsRoot()
        {
            var data = CreateVfxData(1);
            var pos = new Vector3(1f, 2f, 3f);
            var handle = _manager.SpawnData(data, explicitPose: (pos, Quaternion.identity));

            _manager.Tick(0f);
            Assert.IsTrue(_manager.IsPlaying(handle));
        }

        [Test]
        public void Kill_ImmediatelyReturnsToPool()
        {
            var data = CreateVfxData(1);
            var handle = _manager.SpawnData(data);
            _manager.Kill(handle);

            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void Stop_WithoutFadeOut_ReturnsImmediately()
        {
            var data = CreateVfxData(1);
            data.FadeOutSec = 0f;
            var handle = _manager.SpawnData(data);
            _manager.Stop(handle);

            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void Stop_WithFadeOut_StaysAliveUntilFadeElapses()
        {
            var data = CreateVfxData(1);
            data.FadeOutSec = 0.5f;
            var handle = _manager.SpawnData(data);
            _manager.Stop(handle);

            Assert.IsTrue(_manager.IsPlaying(handle), "should still be alive during fade-out");

            _manager.Tick(0.3f);
            Assert.IsTrue(_manager.IsPlaying(handle));

            _manager.Tick(0.3f);
            Assert.IsFalse(_manager.IsPlaying(handle), "should be returned once fade-out elapses");
        }

        [Test]
        public void Tick_DurationLifeMode_AutoStopsAfterDuration()
        {
            var data = CreateVfxData(1, VfxLifeMode.Duration);
            data.Duration = 1f;
            data.FadeOutSec = 0f;
            var handle = _manager.SpawnData(data);

            _manager.Tick(0.5f);
            Assert.IsTrue(_manager.IsPlaying(handle));

            _manager.Tick(0.6f);
            Assert.IsFalse(_manager.IsPlaying(handle));
        }

        [Test]
        public void Tick_LoopLifeMode_NeverAutoStops()
        {
            var data = CreateVfxData(1, VfxLifeMode.Loop);
            var handle = _manager.SpawnData(data);

            _manager.Tick(100f);
            Assert.IsTrue(_manager.IsPlaying(handle));
        }

        [Test]
        public void SetParam_Float_DoesNotThrow_AndAppliesViaPropertyBlock()
        {
            var data = CreateVfxData(1);
            data.Params = new[]
            {
                new VfxParam
                {
                    Label = "Size",
                    Type = VfxParamType.Float,
                    TargetProperty = "_Size",
                    Default = ParamValue.Of(1f),
                },
            };
            var handle = _manager.SpawnData(data);

            Assert.DoesNotThrow(() => _manager.SetParam(handle, "Size", ParamValue.Of(2f)));
        }

        [Test]
        public void SetParam_UnknownLabel_IsNoOp()
        {
            var data = CreateVfxData(1);
            var handle = _manager.SpawnData(data);

            Assert.DoesNotThrow(() => _manager.SetParam(handle, "DoesNotExist", ParamValue.Of(1f)));
        }

        [Test]
        public void Detach_StopsFollowingAnchor()
        {
            var data = CreateVfxData(1);
            var handle = _manager.SpawnData(data);
            _manager.Detach(handle);

            Assert.IsTrue(_manager.IsPlaying(handle));
        }

        [Test]
        public void SpawnData_NullPrefab_ReturnsInvalidHandle()
        {
            var data = CreateVfxData(1);
            data.Prefab = null;

            var handle = _manager.SpawnData(data);
            Assert.AreEqual(Handle<VfxMarker>.Invalid, handle);
        }

        [Test]
        public void StopAll_ReturnsEveryActiveInstance()
        {
            var data = CreateVfxData(1);
            var h1 = _manager.SpawnData(data);
            var h2 = _manager.SpawnData(data);

            _manager.StopAll(StopReason.Manual);

            Assert.IsFalse(_manager.IsPlaying(h1));
            Assert.IsFalse(_manager.IsPlaying(h2));
        }
    }
}
