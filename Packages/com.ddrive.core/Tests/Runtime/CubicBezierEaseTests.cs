using DDrive.Foundation.Easing;
using NUnit.Framework;

namespace DDrive.Tests.Runtime
{
    public class CubicBezierEaseTests
    {
        private const float Epsilon = 1e-3f;

        [Test]
        public void Evaluate_BoundaryValues_AreFixed()
        {
            Assert.AreEqual(0f, CubicBezierEase.Evaluate(0.42f, 0f, 0.58f, 1f, 0f), Epsilon);
            Assert.AreEqual(1f, CubicBezierEase.Evaluate(0.42f, 0f, 0.58f, 1f, 1f), Epsilon);
        }

        [Test]
        public void Evaluate_SymmetricControlPoints_PassesThroughMidpoint()
        {
            // (a,b),(1-a,1-b) と点対称な制御点は必ず (0.5,0.5) を通る。
            var value = CubicBezierEase.Evaluate(0.42f, 0f, 0.58f, 1f, 0.5f);
            Assert.AreEqual(0.5f, value, Epsilon);
        }

        [Test]
        public void Evaluate_EvenlySpacedColinearControlPoints_IsIdentity()
        {
            // P0=(0,0) P1=(1/3,1/3) P2=(2/3,2/3) P3=(1,1) は直線 y=x に退化する。
            foreach (var t in new[] { 0.1f, 0.25f, 0.4f, 0.6f, 0.75f, 0.9f })
            {
                var value = CubicBezierEase.Evaluate(1f / 3f, 1f / 3f, 2f / 3f, 2f / 3f, t);
                Assert.AreEqual(t, value, Epsilon, $"t={t}");
            }
        }
    }
}
