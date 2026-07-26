using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Foundation.Values;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    public class ValueDefValidatorTests
    {
        private sealed class HolderData : AssetDataBase
        {
            public ValueDef Motion;
            public ValueDef3 Scale;
            public ValueDefColor Tint;
        }

        private static List<ValidationResult> Check(ValueDef def)
        {
            var holder = ScriptableObject.CreateInstance<HolderData>();
            holder.Motion = def;

            var validator = new ValueDefValidator();
            return new List<ValidationResult>(validator.Validate(holder, new ValidationContext(new List<AssetDataBase> { holder })));
        }

        [Test]
        public void CurveMode_WithNullCurve_IsError()
        {
            var results = Check(new ValueDef { Mode = ValueMode.Curve, Curve = null });
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Curve")));
        }

        [Test]
        public void ParametricCustomBezier_WithZeroControlPoints_IsError()
        {
            var results = Check(new ValueDef
            {
                Mode = ValueMode.Parametric,
                Parametric = EaseDef.Bezier(Vector2.zero, Vector2.zero),
            });

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("制御点")));
        }

        [Test]
        public void ParametricCustomBezier_WithRealControlPoints_NoBezierError()
        {
            var results = Check(new ValueDef
            {
                Mode = ValueMode.Parametric,
                Parametric = EaseDef.Bezier(new Vector2(0.2f, 0f), new Vector2(0.8f, 1f)),
                From = 0f,
                To = 1f,
            });

            Assert.IsFalse(results.Exists(r => r.Message.Contains("制御点")));
        }

        [Test]
        public void DurationMode_ValueZero_IsError()
        {
            var results = Check(new ValueDef { Time = TimeDef.Duration(0f) });
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Duration")));
        }

        [Test]
        public void LoopWithZeroDuration_ProducesFreezeSpecificError()
        {
            var results = Check(new ValueDef { Time = TimeDef.Duration(0f), Loop = LoopMode.Loop });
            Assert.IsTrue(results.Exists(r => r.Message.Contains("フリーズ")));
        }

        [Test]
        public void FromEqualsTo_OnParametric_IsWarning()
        {
            var results = Check(new ValueDef
            {
                Mode = ValueMode.Parametric,
                Parametric = EaseDef.Named(DDrive.Foundation.Easing.Ease.Linear),
                From = 5f,
                To = 5f,
            });

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("From")));
        }

        [Test]
        public void ConstantMode_WithLoop_IsInfo()
        {
            var results = Check(new ValueDef { Mode = ValueMode.Constant, Loop = LoopMode.Loop });
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Info));
        }

        [Test]
        public void RateMode_WithLoopOnce_IsWarning()
        {
            var results = Check(new ValueDef { Time = TimeDef.Rate(1f), Loop = LoopMode.Once });
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("Rate")));
        }

        [Test]
        public void NonPositiveSpeedScale_IsError()
        {
            var results = Check(new ValueDef { Time = new TimeDef { Mode = TimeMode.Duration, Value = 1f, SpeedScale = 0f } });
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("SpeedScale")));
        }

        [Test]
        public void Validate_FindsNestedValueDefsInValueDef3AndValueDefColor()
        {
            var holder = ScriptableObject.CreateInstance<HolderData>();
            holder.Motion = ValueDef.Constant01(1f);
            holder.Scale = new ValueDef3
            {
                X = new ValueDef { Time = TimeDef.Duration(0f) },
                Y = ValueDef.Constant01(1f),
                Z = ValueDef.Constant01(1f),
            };
            holder.Tint = new ValueDefColor
            {
                Alpha = new ValueDef { Time = TimeDef.Duration(-1f) },
            };

            var validator = new ValueDefValidator();
            var results = validator.Validate(holder, new ValidationContext(new List<AssetDataBase> { holder })).ToList();

            Assert.IsTrue(results.Exists(r => r.Message.StartsWith("Scale.X")));
            Assert.IsTrue(results.Exists(r => r.Message.StartsWith("Tint.Alpha")));
        }

        [Test]
        public void ValidatorRegistry_AppliesUniversalValidator_RegardlessOfAssetType()
        {
            var registry = new ValidatorRegistry();
            registry.Register(new ValueDefValidator());

            var holder = ScriptableObject.CreateInstance<HolderData>();
            holder.Motion = new ValueDef { Time = TimeDef.Duration(0f) };

            var reports = registry.RunAll(new AssetDataBase[] { holder });

            Assert.IsTrue(reports.Count > 0);
        }
    }
}
