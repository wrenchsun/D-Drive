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
            var results = Check(new ValueDef { Mode = ValueMode.Parametric, From = 0f, To = 1f, Time = TimeDef.Duration(0f) });
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Duration")));
        }

        [Test]
        public void LoopWithZeroDuration_ProducesFreezeSpecificError()
        {
            var results = Check(new ValueDef { Mode = ValueMode.Parametric, From = 0f, To = 1f, Time = TimeDef.Duration(0f), Loop = LoopMode.Loop });
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
            var results = Check(new ValueDef { Mode = ValueMode.Parametric, From = 0f, To = 1f, Time = new TimeDef { Mode = TimeMode.Duration, Value = 1f, SpeedScale = 0f } });
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("SpeedScale")));
        }

        [Test]
        public void Validate_FindsNestedValueDefsInValueDef3AndValueDefColor()
        {
            var holder = ScriptableObject.CreateInstance<HolderData>();
            holder.Motion = ValueDef.Constant01(1f);
            holder.Scale = new ValueDef3
            {
                X = new ValueDef { Mode = ValueMode.Parametric, From = 0f, To = 1f, Time = TimeDef.Duration(0f) },
                Y = ValueDef.Constant01(1f),
                Z = ValueDef.Constant01(1f),
            };
            holder.Tint = new ValueDefColor
            {
                Alpha = new ValueDef { Mode = ValueMode.Parametric, From = 0f, To = 1f, Time = TimeDef.Duration(-1f) },
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
            holder.Motion = new ValueDef { Mode = ValueMode.Parametric, From = 0f, To = 1f, Time = TimeDef.Duration(0f) };

            var reports = registry.RunAll(new AssetDataBase[] { holder });

            Assert.IsTrue(reports.Count > 0);
        }

        // 2026-10-06 - Mode=Constant は Time を評価に使わないので Time の欄(0 のまま)を検査しない。
        [Test]
        public void Constant01_HasNoError()
        {
            Assert.IsFalse(Check(ValueDef.Constant01(1f)).Exists(r => r.Severity == ValidationSeverity.Error));
            Assert.IsFalse(Check(default).Exists(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void Constant_WithZeroTime_AndLoop_HasNoError()
        {
            var def = ValueDef.Constant01(1f);
            def.Loop = LoopMode.Loop;
            var results = Check(def);
            Assert.IsFalse(results.Exists(r => r.Severity == ValidationSeverity.Error));
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Info));
        }

        [Test]
        public void TimeUsingModes_WithZeroValueOrSpeedScale_StillError()
        {
            var curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            foreach (var def in new[]
            {
                new ValueDef { Mode = ValueMode.Parametric, From = 0f, To = 1f },
                new ValueDef { Mode = ValueMode.Curve, Curve = curve },
            })
            {
                var results = Check(def);
                Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Duration")), def.Mode.ToString());
                Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("SpeedScale")), def.Mode.ToString());
            }
        }

        [Test]
        public void FreshAnim2DCameraShakeAndSkin_HaveNoValueDefError()
        {
            foreach (var data in new AssetDataBase[]
            {
                ScriptableObject.CreateInstance<DDrive.Runtime.Anim2D.Anim2DData>(),
                ScriptableObject.CreateInstance<DDrive.Runtime.CameraShake.CameraShakeData>(),
                ScriptableObject.CreateInstance<DDrive.Runtime.Ui.ButtonSkinData>(),
            })
            {
                var results = new List<ValidationResult>(new ValueDefValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })));
                Assert.IsFalse(results.Exists(r => r.Severity == ValidationSeverity.Error), data.GetType().Name);
            }
        }
    }
}
