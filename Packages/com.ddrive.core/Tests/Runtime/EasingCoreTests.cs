using System;
using DDrive.Foundation.Easing;
using NUnit.Framework;

namespace DDrive.Tests.Runtime
{
    public class EasingCoreTests
    {
        private const float Epsilon = 1e-4f;

        private static readonly Ease[] AllEases = (Ease[])Enum.GetValues(typeof(Ease));

        private static readonly Ease[] InOutEases =
        {
            Ease.InOutSine, Ease.InOutQuad, Ease.InOutCubic, Ease.InOutQuart, Ease.InOutQuint,
            Ease.InOutExpo, Ease.InOutCirc, Ease.InOutBack, Ease.InOutElastic, Ease.InOutBounce,
        };

        private static readonly (Ease inEase, Ease outEase)[] MirroredFamilies =
        {
            (Ease.InSine, Ease.OutSine), (Ease.InQuad, Ease.OutQuad), (Ease.InCubic, Ease.OutCubic),
            (Ease.InQuart, Ease.OutQuart), (Ease.InQuint, Ease.OutQuint), (Ease.InExpo, Ease.OutExpo),
            (Ease.InCirc, Ease.OutCirc), (Ease.InBack, Ease.OutBack), (Ease.InElastic, Ease.OutElastic),
            (Ease.InBounce, Ease.OutBounce),
        };

        [TestCaseSource(nameof(AllEases))]
        public void Evaluate_AtZeroAndOne_HitsBoundary(Ease ease)
        {
            Assert.AreEqual(0f, EasingCore.Evaluate(ease, 0f), Epsilon, $"{ease}(0) should be 0");
            Assert.AreEqual(1f, EasingCore.Evaluate(ease, 1f), Epsilon, $"{ease}(1) should be 1");
        }

        [TestCaseSource(nameof(InOutEases))]
        public void Evaluate_InOutFamily_MidpointIsHalf(Ease ease)
        {
            Assert.AreEqual(0.5f, EasingCore.Evaluate(ease, 0.5f), Epsilon, $"{ease}(0.5) should be 0.5");
        }

        [Test]
        public void Evaluate_OutFamily_IsMirrorOfInFamily()
        {
            foreach (var (inEase, outEase) in MirroredFamilies)
            {
                foreach (var t in new[] { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f })
                {
                    var outValue = EasingCore.Evaluate(outEase, t);
                    var mirrored = 1f - EasingCore.Evaluate(inEase, 1f - t);
                    Assert.AreEqual(mirrored, outValue, Epsilon,
                        $"{outEase}({t}) should equal 1 - {inEase}({1f - t})");
                }
            }
        }

        [Test]
        public void Evaluate_Linear_IsIdentity()
        {
            Assert.AreEqual(0.5f, EasingCore.Evaluate(Ease.Linear, 0.5f), Epsilon);
            Assert.AreEqual(0.25f, EasingCore.Evaluate(Ease.Linear, 0.25f), Epsilon);
        }

        [TestCase(Ease.InQuad, 0.5f, 0.25f)]
        [TestCase(Ease.OutQuad, 0.5f, 0.75f)]
        [TestCase(Ease.InCubic, 0.5f, 0.125f)]
        [TestCase(Ease.OutCubic, 0.5f, 0.875f)]
        [TestCase(Ease.InQuart, 0.5f, 0.0625f)]
        [TestCase(Ease.OutQuart, 0.5f, 0.9375f)]
        [TestCase(Ease.InQuint, 0.5f, 0.03125f)]
        [TestCase(Ease.OutQuint, 0.5f, 0.96875f)]
        [TestCase(Ease.InExpo, 0.5f, 0.03125f)]
        [TestCase(Ease.OutExpo, 0.5f, 0.96875f)]
        [TestCase(Ease.InSine, 0.5f, 0.29289322f)]
        [TestCase(Ease.OutSine, 0.5f, 0.70710678f)]
        [TestCase(Ease.InCirc, 0.5f, 0.13397460f)]
        [TestCase(Ease.OutCirc, 0.5f, 0.86602540f)]
        [TestCase(Ease.InBack, 0.5f, -0.08769750f)]
        [TestCase(Ease.OutBack, 0.5f, 1.08769750f)]
        public void Evaluate_KnownReferenceValues(Ease ease, float t, float expected)
        {
            Assert.AreEqual(expected, EasingCore.Evaluate(ease, t), 1e-3f, $"{ease}({t})");
        }
    }
}
