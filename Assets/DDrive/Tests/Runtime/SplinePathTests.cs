using DDrive.Foundation.Easing;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    public class SplinePathTests
    {
        // 区間長がバラつく(1 → 2.24 → 3.01 → 2.06)緩やかな折れ線。弧長補正なしなら等速にならない。
        private static readonly Vector3[] CurvedPoints =
        {
            new(0f, 0f, 0f),
            new(1f, 0f, 0f),
            new(2f, 2f, 0f),
            new(5f, 2.2f, 0f),
            new(6f, 4f, 0f),
        };

        private static readonly Vector3[] BezierPoints =
        {
            new(0f, 0f, 0f),
            new(0f, 3f, 0f),
            new(1f, 3f, 0f),
            new(1f, 0f, 0f),
        };

        [TestCase(SplineType.CatmullRom)]
        [TestCase(SplineType.Hermite)]
        [TestCase(SplineType.BSpline)]
        public void Evaluate_NonBezier_IsConstantSpeedWithinOnePercent(SplineType type)
        {
            AssertConstantSpeed(new SplinePath(CurvedPoints, type));
        }

        [Test]
        public void Evaluate_Bezier_IsConstantSpeedWithinOnePercent()
        {
            AssertConstantSpeed(new SplinePath(BezierPoints, SplineType.Bezier));
        }

        [Test]
        public void Evaluate_AtZeroAndOne_ReturnsPathEndpoints()
        {
            var path = new SplinePath(CurvedPoints, SplineType.CatmullRom);

            Assert.AreEqual(CurvedPoints[0], path.Evaluate(0f));
            Assert.AreEqual(CurvedPoints[^1], path.Evaluate(1f));
        }

        private static void AssertConstantSpeed(SplinePath path)
        {
            // 弧長そのものは u に比例するよう厳密に構築されている。ここでチェックしているのは
            // 離散サンプル間の直線距離(弦長)が弧長の近似としてどれだけブレるかで、
            // 曲率が強い区間では弦長 ≠ 弧長になるため、ステップ数を十分に細かく取る必要がある。
            const int steps = 200;
            var positions = new Vector3[steps + 1];
            for (var i = 0; i <= steps; i++)
            {
                positions[i] = path.Evaluate((float)i / steps);
            }

            var segmentLengths = new float[steps];
            var sum = 0f;
            for (var i = 0; i < steps; i++)
            {
                segmentLengths[i] = Vector3.Distance(positions[i], positions[i + 1]);
                sum += segmentLengths[i];
            }

            var mean = sum / steps;
            Assert.Greater(mean, 0f, "Path should have non-zero length");

            foreach (var length in segmentLengths)
            {
                var deviation = Mathf.Abs(length - mean) / mean;
                Assert.LessOrEqual(deviation, 0.01f,
                    $"segment length {length} deviates from mean {mean} by more than 1%");
            }
        }
    }
}
