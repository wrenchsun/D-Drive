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

        // ── コンポーネント経路(2026-09-11 レビュー対応) ──

        private static Anim2DFacing MakeFacing()
        {
            var go = new GameObject("FacingTest") { hideFlags = HideFlags.HideAndDontSave };
            var facing = go.AddComponent<Anim2DFacing>();
            facing.Smoothing = 0f; // 即時反映(EditMode では Awake が走らないので初期値に頼らない)
            return facing;
        }

        [Test]
        public void SetWorldDirection_WithoutCamera_KeepsCurrentDirectionValid()
        {
            var facing = MakeFacing();
            try
            {
                facing.CameraRelative = true; // Camera.main が無くてもワールド基準で成立する
                facing.SetWorldDirection(new Vector3(0f, 0f, 1f));
                facing.Tick(1f / 60f);

                Assert.AreEqual(1f, facing.CurrentDirection.magnitude, 1e-3f, "正規化された向きが入る");
            }
            finally
            {
                Object.DestroyImmediate(facing.gameObject);
            }
        }

        [Test]
        public void SetWorldDirection_Zero_KeepsPreviousDirection()
        {
            var facing = MakeFacing();
            try
            {
                facing.CameraRelative = false;
                facing.SetWorldDirection(new Vector3(1f, 0f, 0f));
                facing.Tick(1f / 60f);
                var before = facing.CurrentDirection;

                facing.SetWorldDirection(Vector3.zero);
                facing.Tick(1f / 60f);

                Assert.AreEqual(before.x, facing.CurrentDirection.x, 1e-4f, "0 ベクトルでは向きを変えない");
                Assert.AreEqual(before.y, facing.CurrentDirection.y, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(facing.gameObject);
            }
        }

        // Animator を持たない(= パラメータが引けない)対象でも Tick が例外を投げない(警告 + no-op で続ける)。
        [Test]
        public void Tick_WithAnimatorWithoutController_DoesNotThrow()
        {
            var facing = MakeFacing();
            try
            {
                facing.Target = facing.gameObject.AddComponent<Animator>();
                facing.CameraRelative = false;
                facing.SetWorldDirection(new Vector3(0f, 0f, 1f));
                Assert.DoesNotThrow(() => facing.Tick(1f / 60f));
                Assert.DoesNotThrow(() => facing.Tick(1f / 60f));
            }
            finally
            {
                Object.DestroyImmediate(facing.gameObject);
            }
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
