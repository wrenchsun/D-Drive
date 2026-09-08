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

        // ── 2026-09-08 改定([19_vfx_usability_review.md]): Anchor 姿勢の再適用 / LightLayer / 破棄済み Root ──

        [Test]
        public void NewVfxData_HasSafeDefaults()
        {
            var data = ScriptableObject.CreateInstance<VfxData>();

            Assert.AreEqual(Vector3.one, data.Anchor.LocalScale, "Anchor は WorldDefault(スケール 1)で初期化される");
            Assert.AreEqual(1u, data.LightLayerMask, "LightLayerMask の既定は Default(1)");
        }

        [Test]
        public void ReapplyAnchor_MovesLiveInstance_WhenDataAnchorChanges()
        {
            var rig = new GameObject("Rig");
            var bone = new GameObject("Bone");
            bone.transform.SetParent(rig.transform);
            bone.transform.position = new Vector3(10f, 0f, 0f);

            var data = CreateVfxData(1);
            data.Anchor = new AnchorDef { Space = AnchorSpace.NamedObject, Path = "Bone", LocalScale = Vector3.one };
            var handle = _manager.SpawnData(data, contextRoot: rig.transform);
            var root = _manager.GetGameObject(handle).transform;
            Assert.Less(Vector3.Distance(new Vector3(10f, 0f, 0f), root.position), 1e-4f);

            data.Anchor.LocalOffset = new Vector3(0f, 2f, 0f);
            _manager.ReapplyAnchor(handle);
            Assert.Less(Vector3.Distance(new Vector3(10f, 2f, 0f), root.position), 1e-4f, "ReapplyAnchor で即時反映");

            // 以後の Tick(追従)でも新しいオフセットが維持される。
            _manager.Tick(0.016f);
            Assert.Less(Vector3.Distance(new Vector3(10f, 2f, 0f), root.position), 1e-4f);

            Object.DestroyImmediate(rig);
        }

        [Test]
        public void FollowRotation_AppliesLocalEulerRelativeToTarget()
        {
            var rig = new GameObject("Rig");
            rig.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            var data = CreateVfxData(1);
            data.Anchor = new AnchorDef
            {
                Space = AnchorSpace.ContextTarget,
                FollowRotation = true,
                LocalEuler = new Vector3(0f, 45f, 0f),
                LocalScale = Vector3.one,
            };
            var handle = _manager.SpawnData(data, contextRoot: rig.transform);
            var root = _manager.GetGameObject(handle).transform;

            Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, 135f, 0f), root.rotation), 1e-3f, "Spawn 時");

            rig.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            _manager.Tick(0.016f);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, 225f, 0f), root.rotation), 1e-3f, "追従時も LocalEuler を保つ");

            Object.DestroyImmediate(rig);
        }

        [Test]
        public void LightLayerMask_Zero_KeepsPrefabRendererSetting()
        {
            var renderer = _prefab.GetComponent<ParticleSystemRenderer>();
            renderer.renderingLayerMask = 4u;

            var data = CreateVfxData(1);
            data.LightLayerMask = VfxData.LightLayerKeepPrefab;
            var handle = _manager.SpawnData(data);

            Assert.AreEqual(4u, _manager.GetGameObject(handle).GetComponent<Renderer>().renderingLayerMask);
        }

        [Test]
        public void LightLayerMask_NonZero_OverridesRenderer()
        {
            var data = CreateVfxData(1);
            data.LightLayerMask = 2u;
            var handle = _manager.SpawnData(data);

            Assert.AreEqual(2u, _manager.GetGameObject(handle).GetComponent<Renderer>().renderingLayerMask);
        }

        [Test]
        public void Tick_DestroyedRoot_IsRemovedWithoutException()
        {
            var rig = new GameObject("Rig");
            var data = CreateVfxData(1);
            data.Anchor = new AnchorDef { Space = AnchorSpace.ContextTarget, LocalScale = Vector3.one };
            var handle = _manager.SpawnData(data, contextRoot: rig.transform);

            Object.DestroyImmediate(_manager.GetGameObject(handle));

            Assert.DoesNotThrow(() => _manager.Tick(0.016f));
            Assert.IsFalse(_manager.IsPlaying(handle));
            Assert.AreEqual(0, _manager.ActiveCount);

            Object.DestroyImmediate(rig);
        }

        [Test]
        public void TryGetAnchorTarget_ReturnsResolvedTransform()
        {
            var rig = new GameObject("Rig");
            var data = CreateVfxData(1);
            data.Anchor = new AnchorDef { Space = AnchorSpace.ContextTarget, LocalScale = Vector3.one };
            var handle = _manager.SpawnData(data, contextRoot: rig.transform);

            Assert.IsTrue(_manager.TryGetAnchorTarget(handle, out var target));
            Assert.AreEqual(rig.transform, target);

            var worldHandle = _manager.SpawnData(CreateVfxData(2));
            Assert.IsFalse(_manager.TryGetAnchorTarget(worldHandle, out _));

            Object.DestroyImmediate(rig);
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
