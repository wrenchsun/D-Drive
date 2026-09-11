using DDrive.Runtime.Anim2D;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [05_model_animation.md] C-4 — Anim2DFacing の純関数(カメラ相対の向き / 指数平滑化)。2026-09-11。
    public class Anim2DFacingTests
    {
        [Test]
        public void ToScreenDirection_NoYaw_IsIdentity()
        {
            var d = Anim2DFacing.ToScreenDirection(new Vector2(0f, 1f), 0f);
            Assert.AreEqual(0f, d.x, 1e-4f);
            Assert.AreEqual(1f, d.y, 1e-4f);
        }

        [Test]
        public void ToScreenDirection_CameraYaw90_RotatesWorldForwardToScreenLeft()
        {
            // カメラが +90°(東向き)なら、ワールド +Z(北)は画面では左になる
            var d = Anim2DFacing.ToScreenDirection(new Vector2(0f, 1f), 90f);
            Assert.AreEqual(-1f, d.x, 1e-3f);
            Assert.AreEqual(0f, d.y, 1e-3f);
        }

        [Test]
        public void ToScreenDirection_Normalizes_AndFallsBackOnZero()
        {
            var d = Anim2DFacing.ToScreenDirection(new Vector2(3f, 4f), 0f);
            Assert.AreEqual(1f, d.magnitude, 1e-4f);
            Assert.AreEqual(Vector2.down, Anim2DFacing.ToScreenDirection(Vector2.zero, 45f));
        }

        [Test]
        public void Smooth_ZeroSmoothing_IsImmediate()
        {
            Assert.AreEqual(Vector2.right, Anim2DFacing.Smooth(Vector2.up, Vector2.right, 0f, 0.016f));
        }

        [Test]
        public void Smooth_ConvergesTowardDesired_AndStaysNormalized()
        {
            var current = Vector2.up;
            for (var i = 0; i < 60; i++)
            {
                current = Anim2DFacing.Smooth(current, Vector2.right, 0.05f, 1f / 60f);
                Assert.AreEqual(1f, current.magnitude, 1e-3f);
            }

            Assert.Greater(current.x, 0.99f, "1 秒で収束");
        }
    }
}
