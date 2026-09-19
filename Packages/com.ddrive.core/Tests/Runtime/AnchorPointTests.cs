using DDrive.Foundation.Data;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // [04_vfx.md] §2.5 — シーン配置型アンカー(AnchorPoint)の適用検証。
    public class AnchorPointTests
    {
        private PoolService _pool;
        private VfxManager _manager;
        private GameObject _prefab;
        private GameObject _rig;
        private AnchorPoint _point;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _manager = new VfxManager(_pool, new AssetRegistry(new FakeAssetLoader()));

            _prefab = new GameObject("AnchorTestVfxPrefab");
            var ps = _prefab.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            _rig = new GameObject("AnchorRig");
            _rig.AddComponent<AnchorRig>();
            var pointGo = new GameObject("Anchor_Test");
            pointGo.transform.SetParent(_rig.transform);
            pointGo.transform.position = new Vector3(5f, 1f, 2f);
            _point = pointGo.AddComponent<AnchorPoint>();
        }

        [TearDown]
        public void TearDown()
        {
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_prefab);
            Object.DestroyImmediate(_rig);
        }

        private VfxData CreateAnchoredVfx()
        {
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.Id = 1;
            data.Prefab = _prefab;
            data.LifeMode = VfxLifeMode.Loop;
            data.Anchor = new AnchorDef
            {
                Space = AnchorSpace.NamedObject,
                Path = "Anchor_Test",
                LocalScale = Vector3.one,
            };
            return data;
        }

        [Test]
        public void Spawn_AtAnchorPoint_AppliesSpawnOffset()
        {
            _point.SpawnOffset = new Vector3(0f, 2f, 0f);

            var handle = _manager.SpawnData(CreateAnchoredVfx(), contextRoot: _rig.transform);
            var go = _manager.GetGameObject(handle);

            var expected = _point.transform.TransformPoint(new Vector3(0f, 2f, 0f));
            Assert.Less(Vector3.Distance(expected, go.transform.position), 1e-4f);
        }

        [Test]
        public void Spawn_WithPositionJitter_StaysWithinRadius()
        {
            _point.PositionJitterRadius = 0.5f;

            for (var i = 0; i < 8; i++)
            {
                var handle = _manager.SpawnData(CreateAnchoredVfx(), contextRoot: _rig.transform);
                var go = _manager.GetGameObject(handle);
                var distance = Vector3.Distance(_point.transform.position, go.transform.position);
                Assert.LessOrEqual(distance, 0.5f + 1e-4f);
                _manager.Kill(handle);
            }
        }

        [Test]
        public void Spawn_WithScaleRange_AppliesMultiplierWithinRange()
        {
            _point.ScaleRange = new Vector2(2f, 3f);

            var handle = _manager.SpawnData(CreateAnchoredVfx(), contextRoot: _rig.transform);
            var go = _manager.GetGameObject(handle);

            Assert.GreaterOrEqual(go.transform.localScale.x, 2f - 1e-4f);
            Assert.LessOrEqual(go.transform.localScale.x, 3f + 1e-4f);
        }

        [Test]
        public void Tick_FollowingAnchor_KeepsSampledJitterOffset()
        {
            _point.PositionJitterRadius = 0.5f;
            _point.SpawnOffset = new Vector3(1f, 0f, 0f);

            var handle = _manager.SpawnData(CreateAnchoredVfx(), contextRoot: _rig.transform);
            var go = _manager.GetGameObject(handle);
            var posAfterSpawn = go.transform.position;

            // 追従更新でランダム分が消えて位置が変わってはいけない(サンプルは Spawn 時の1回だけ)。
            _manager.Tick(0.016f);
            _manager.Tick(0.016f);

            Assert.Less(Vector3.Distance(posAfterSpawn, go.transform.position), 1e-4f);
        }

        [Test]
        public void Spawn_AnchorWithoutAnchorPoint_BehavesAsBefore()
        {
            var plainChild = new GameObject("PlainBone");
            plainChild.transform.SetParent(_rig.transform);
            plainChild.transform.position = new Vector3(-3f, 0f, 0f);

            var data = CreateAnchoredVfx();
            data.Anchor = new AnchorDef
            {
                Space = AnchorSpace.NamedObject,
                Path = "PlainBone",
                LocalOffset = new Vector3(0f, 1f, 0f),
                LocalScale = Vector3.one,
            };

            var handle = _manager.SpawnData(data, contextRoot: _rig.transform);
            var go = _manager.GetGameObject(handle);

            var expected = plainChild.transform.TransformPoint(new Vector3(0f, 1f, 0f));
            Assert.Less(Vector3.Distance(expected, go.transform.position), 1e-4f);
        }

        [Test]
        public void SampleScaleMultiplier_UninitializedZeroRange_ReturnsOne()
        {
            _point.ScaleRange = Vector2.zero;
            Assert.AreEqual(1f, _point.SampleScaleMultiplier());
        }

        [Test]
        public void AnchorRig_GetPoints_FindsAllChildren()
        {
            var second = new GameObject("Anchor_Second");
            second.transform.SetParent(_rig.transform);
            second.AddComponent<AnchorPoint>();

            var points = _rig.GetComponent<AnchorRig>().GetPoints();
            Assert.AreEqual(2, points.Length);
        }
    }
}
