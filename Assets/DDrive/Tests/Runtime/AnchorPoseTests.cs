using DDrive.Foundation.Data;
using DDrive.Runtime.Anchoring;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // [04_vfx.md] §2 — Anchor 姿勢の式(Manager の Spawn/Tick/ReapplyAnchor と VfxEditor の SceneView ハンドルが共有)。
    public class AnchorPoseTests
    {
        private GameObject _target;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("AnchorTarget");
            _target.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
        }

        [Test]
        public void WorldPosition_WithoutTarget_UsesOffsetAsWorld()
        {
            var anchor = new AnchorDef { LocalOffset = new Vector3(5f, 0f, -1f) };

            Assert.AreEqual(new Vector3(5f, 0f, -1f), AnchorPose.WorldPosition(anchor, null, Vector3.zero));
        }

        [Test]
        public void WorldPosition_WithTarget_TransformsLocalOffsetPlusExtra()
        {
            var anchor = new AnchorDef { LocalOffset = new Vector3(0f, 0f, 1f) };
            var extra = new Vector3(0f, 1f, 0f);

            var expected = _target.transform.TransformPoint(new Vector3(0f, 1f, 1f));
            Assert.Less(Vector3.Distance(expected, AnchorPose.WorldPosition(anchor, _target.transform, extra)), 1e-5f);
        }

        [Test]
        public void WorldRotation_FollowRotation_AppliesLocalEulerRelativeToTarget()
        {
            var anchor = new AnchorDef { FollowRotation = true, LocalEuler = new Vector3(0f, 45f, 0f) };

            var rot = AnchorPose.WorldRotation(anchor, _target.transform, Quaternion.identity);

            Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, 135f, 0f), rot), 1e-3f);
        }

        [Test]
        public void WorldRotation_NotFollowing_IgnoresTargetRotation()
        {
            var anchor = new AnchorDef { FollowRotation = false, LocalEuler = new Vector3(0f, 45f, 0f) };

            var rot = AnchorPose.WorldRotation(anchor, _target.transform, Quaternion.identity);

            Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, 45f, 0f), rot), 1e-3f);
        }

        [Test]
        public void WorldScale_ZeroLocalScale_IsTreatedAsOne()
        {
            var anchor = new AnchorDef { LocalScale = Vector3.zero };

            Assert.AreEqual(Vector3.one * 2f, AnchorPose.WorldScale(anchor, 2f));
        }

        [Test]
        public void LocalOffsetFromWorld_RoundTripsWorldPosition()
        {
            var anchor = new AnchorDef { LocalOffset = new Vector3(0.3f, -0.2f, 1.5f) };
            var extra = new Vector3(0.1f, 0.1f, 0.1f);

            var world = AnchorPose.WorldPosition(anchor, _target.transform, extra);
            var local = AnchorPose.LocalOffsetFromWorld(_target.transform, world, extra);

            Assert.Less(Vector3.Distance(anchor.LocalOffset, local), 1e-5f);
        }

        [Test]
        public void LocalEulerFromWorld_RoundTripsWorldRotation_WhenFollowing()
        {
            var anchor = new AnchorDef { FollowRotation = true, LocalEuler = new Vector3(10f, 20f, 30f) };

            var world = AnchorPose.WorldRotation(anchor, _target.transform, Quaternion.identity);
            var euler = AnchorPose.LocalEulerFromWorld(anchor, _target.transform, world);

            Assert.Less(Quaternion.Angle(Quaternion.Euler(anchor.LocalEuler), Quaternion.Euler(euler)), 1e-3f);
        }
    }
}
