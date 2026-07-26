using DDrive.Foundation.Easing;
using DDrive.Foundation.Values;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    public class ValueDefTests
    {
        private const float Epsilon = 1e-4f;

        [Test]
        public void Evaluate_Constant_IgnoresT()
        {
            var def = ValueDef.Constant01(3.5f);

            Assert.AreEqual(3.5f, def.Evaluate(0f));
            Assert.AreEqual(3.5f, def.Evaluate(0.5f));
            Assert.AreEqual(3.5f, def.Evaluate(1f));
        }

        [Test]
        public void Evaluate_Parametric_LinearMapsThroughFromTo()
        {
            var def = new ValueDef
            {
                Mode = ValueMode.Parametric,
                Parametric = EaseDef.Named(Ease.Linear),
                From = 0f,
                To = 10f,
            };

            Assert.AreEqual(5f, def.Evaluate(0.5f), Epsilon);
            Assert.AreEqual(0f, def.Evaluate(0f), Epsilon);
            Assert.AreEqual(10f, def.Evaluate(1f), Epsilon);
        }

        [Test]
        public void Evaluate_Parametric_NonLinearEaseMatchesEasingCore()
        {
            var def = new ValueDef
            {
                Mode = ValueMode.Parametric,
                Parametric = EaseDef.Named(Ease.InQuad),
                From = 0f,
                To = 1f,
            };

            Assert.AreEqual(EasingCore.Evaluate(Ease.InQuad, 0.3f), def.Evaluate(0.3f), Epsilon);
        }

        [Test]
        public void Evaluate_Curve_Normalized_UsesRawCurveValue()
        {
            var curve = AnimationCurve.Linear(0f, -1f, 1f, 1f);
            var def = new ValueDef { Mode = ValueMode.Curve, Curve = curve, Normalized = true, From = 0f, To = 100f };

            Assert.AreEqual(-1f, def.Evaluate(0f), Epsilon);
            Assert.AreEqual(1f, def.Evaluate(1f), Epsilon);
        }

        [Test]
        public void Evaluate_Curve_NotNormalized_RemapsThroughFromTo()
        {
            var curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            var def = new ValueDef { Mode = ValueMode.Curve, Curve = curve, Normalized = false, From = 10f, To = 20f };

            Assert.AreEqual(15f, def.Evaluate(0.5f), Epsilon);
        }

        [Test]
        public void Evaluate_IsPure_SameInputSameOutput()
        {
            var def = new ValueDef
            {
                Mode = ValueMode.Parametric,
                Parametric = EaseDef.Named(Ease.OutBounce),
                From = -5f,
                To = 5f,
            };

            var a = def.Evaluate(0.37f);
            var b = def.Evaluate(0.37f);

            Assert.AreEqual(a, b);
        }

        [Test]
        public void Duration_DurationMode_ReturnsValueDirectly()
        {
            var def = new ValueDef { Time = TimeDef.Duration(2f) };
            Assert.AreEqual(2f, def.Duration, Epsilon);
        }

        [Test]
        public void Duration_RateMode_ReturnsOneOverValue()
        {
            var def = new ValueDef { Time = TimeDef.Rate(4f) };
            Assert.AreEqual(0.25f, def.Duration, Epsilon);
        }

        [Test]
        public void EvaluateAt_Once_ClampsAtDurationEnd()
        {
            var def = new ValueDef
            {
                Mode = ValueMode.Parametric,
                Parametric = EaseDef.Named(Ease.Linear),
                From = 0f,
                To = 1f,
                Time = TimeDef.Duration(2f),
                Loop = LoopMode.Once,
            };

            Assert.AreEqual(0.5f, def.EvaluateAt(1f), Epsilon);
            Assert.AreEqual(1f, def.EvaluateAt(2f), Epsilon);
            Assert.AreEqual(1f, def.EvaluateAt(10f), Epsilon);
        }

        [Test]
        public void EvaluateAt_Loop_WrapsAroundDuration()
        {
            var def = new ValueDef
            {
                Mode = ValueMode.Parametric,
                Parametric = EaseDef.Named(Ease.Linear),
                From = 0f,
                To = 1f,
                Time = TimeDef.Duration(2f),
                Loop = LoopMode.Loop,
            };

            Assert.AreEqual(0.25f, def.EvaluateAt(0.5f), Epsilon);
            Assert.AreEqual(0.25f, def.EvaluateAt(2.5f), Epsilon, "second lap should wrap back to the same phase");
        }

        [Test]
        public void EvaluateAt_Loop_WithFiniteCount_StopsAtEndAfterLastCycle()
        {
            var def = new ValueDef
            {
                Mode = ValueMode.Parametric,
                Parametric = EaseDef.Named(Ease.Linear),
                From = 0f,
                To = 1f,
                Time = TimeDef.Duration(1f),
                Loop = LoopMode.Loop,
                LoopCount = 2,
            };

            Assert.AreEqual(0.5f, def.EvaluateAt(1.5f), Epsilon, "still within the 2 allowed cycles");
            Assert.AreEqual(1f, def.EvaluateAt(5f), Epsilon, "past the allowed cycles, should freeze at the end");
        }

        [Test]
        public void EvaluateAt_PingPong_BouncesBackAfterOneCycle()
        {
            var def = new ValueDef
            {
                Mode = ValueMode.Parametric,
                Parametric = EaseDef.Named(Ease.Linear),
                From = 0f,
                To = 1f,
                Time = TimeDef.Duration(1f),
                Loop = LoopMode.PingPong,
            };

            Assert.AreEqual(1f, def.EvaluateAt(1f), Epsilon);
            Assert.AreEqual(0.5f, def.EvaluateAt(1.5f), Epsilon, "should be heading back down");
            Assert.AreEqual(0f, def.EvaluateAt(2f), Epsilon);
        }
    }

    public class ValueDef3Tests
    {
        [Test]
        public void Evaluate_Uniform_AppliesXToAllAxes()
        {
            var def = new ValueDef3
            {
                Uniform = true,
                X = ValueDef.Constant01(2f),
                Y = ValueDef.Constant01(99f),
                Z = ValueDef.Constant01(99f),
            };

            Assert.AreEqual(new Vector3(2f, 2f, 2f), def.Evaluate(0.5f));
        }

        [Test]
        public void Evaluate_NonUniform_EvaluatesEachAxisIndependently()
        {
            var def = new ValueDef3
            {
                Uniform = false,
                X = ValueDef.Constant01(1f),
                Y = ValueDef.Constant01(2f),
                Z = ValueDef.Constant01(3f),
            };

            Assert.AreEqual(new Vector3(1f, 2f, 3f), def.Evaluate(0f));
        }
    }

    public class ValueDefColorTests
    {
        [Test]
        public void Evaluate_Constant_AppliesAlphaOnTop()
        {
            var def = new ValueDefColor
            {
                Mode = ValueMode.Constant,
                Constant = new Color(1f, 0f, 0f, 1f),
                Alpha = ValueDef.Constant01(0.5f),
            };

            var result = def.Evaluate(0f);

            Assert.AreEqual(1f, result.r);
            Assert.AreEqual(0.5f, result.a);
        }

        [Test]
        public void EvaluateAt_SharesAlphaTimingForGradientLookup()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

            var def = new ValueDefColor
            {
                Mode = ValueMode.Curve,
                Curve = gradient,
                Alpha = new ValueDef { Mode = ValueMode.Constant, Constant = 1f, Time = TimeDef.Duration(2f), Loop = LoopMode.Once },
            };

            var atStart = def.EvaluateAt(0f);
            var atQuarterDuration = def.EvaluateAt(0.5f); // duration=2s なので t=0.25 相当
            var atHalfDuration = def.EvaluateAt(1f); // t=0.5 相当
            var atEnd = def.EvaluateAt(2f);

            Assert.Less(atStart.grayscale, atQuarterDuration.grayscale);
            Assert.Less(atQuarterDuration.grayscale, atHalfDuration.grayscale);
            Assert.Less(atHalfDuration.grayscale, atEnd.grayscale);
        }
    }
}
