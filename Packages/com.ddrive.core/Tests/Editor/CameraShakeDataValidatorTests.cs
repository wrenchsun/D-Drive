using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Foundation.Values;
using DDrive.Runtime.CameraShake;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [16_camera_haptics.md] Part A / C-4。
    public class CameraShakeDataValidatorTests
    {
        // P5 レビュー第 1 弾 整理-4(2026-09-14): ValidShake() の ScriptableObject.CreateInstance が
        // DestroyImmediate されずリークしていた。TearDown でまとめて破棄する。
        private readonly List<CameraShakeData> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var data in _created)
            {
                if (data != null)
                {
                    Object.DestroyImmediate(data);
                }
            }

            _created.Clear();
        }

        private CameraShakeData ValidShake()
        {
            var data = ScriptableObject.CreateInstance<CameraShakeData>();
            _created.Add(data);
            data.PosAmplitude = new Vector3(0.1f, 0.1f, 0f);
            data.RotAmplitude = Vector3.zero;
            data.MaxStack = 3;
            data.TraumaWeight = 1f;
            return data;
        }

        private static List<ValidationResult> Validate(CameraShakeData data)
            => new CameraShakeDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })).ToList();

        [Test]
        public void ValidShakeData_HasNoErrorsOrWarnings()
        {
            var results = Validate(ValidShake());
            Assert.IsEmpty(results);
        }

        [Test]
        public void BothAmplitudesZero_IsWarning()
        {
            var data = ValidShake();
            data.PosAmplitude = Vector3.zero;
            data.RotAmplitude = Vector3.zero;

            var results = Validate(data);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("揺れません")));
        }

        [Test]
        public void ExcessivePosAmplitude_IsWarning()
        {
            var data = ValidShake();
            data.PosAmplitude = new Vector3(10f, 0f, 0f);

            var results = Validate(data);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("PosAmplitude")));
        }

        [Test]
        public void ExcessiveRotAmplitude_IsWarning()
        {
            var data = ValidShake();
            data.RotAmplitude = new Vector3(90f, 0f, 0f);

            var results = Validate(data);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("RotAmplitude")));
        }

        [Test]
        public void MaxStackZero_IsWarning()
        {
            var data = ValidShake();
            data.MaxStack = 0;

            var results = Validate(data);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("MaxStack")));
        }

        [Test]
        public void TraumaWeightZero_IsWarning()
        {
            var data = ValidShake();
            data.TraumaWeight = 0f;

            var results = Validate(data);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("TraumaWeight")));
        }

        // GB-R-01(2026-10-06): Constant は ValueDefValidator が Time を検査しない。CameraFxManager は Mode に関係なく
        // Envelope.Duration を寿命として読むので、尺 0 の固定値の揺れは種別の Validator が Warning で知らせる。
        [Test]
        public void ConstantEnvelope_WithZeroDuration_IsWarning_AndNotError()
        {
            var data = ValidShake();
            data.Envelope = ValueDef.Constant01(1f);

            var results = Validate(data);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Code == "DD-SHAKE-ENVELOPE-ZERO-DURATION"));
            Assert.IsFalse(results.Exists(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void ConstantEnvelope_WithDuration_HasNoWarning()
        {
            var data = ValidShake();
            var envelope = ValueDef.Constant01(1f);
            envelope.Time = TimeDef.Duration(0.4f);
            data.Envelope = envelope;

            Assert.IsEmpty(Validate(data));
        }

        [Test]
        public void ParametricEnvelope_WithZeroDurationMode_DoesNotDuplicateTheValueDefError()
        {
            var data = ValidShake();
            var envelope = data.Envelope;
            envelope.Mode = ValueMode.Parametric;
            envelope.Time = TimeDef.Duration(0f);
            data.Envelope = envelope;

            Assert.IsFalse(Validate(data).Exists(r => r.Code == "DD-SHAKE-ENVELOPE-ZERO-DURATION"), "Time を使うモードは ValueDefValidator の Error が担当する");
            Assert.IsTrue(new ValueDefValidator()
                .Validate(data, new ValidationContext(new List<AssetDataBase> { data }))
                .Any(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void SpeedModeWithZeroValue_IsWarning()
        {
            var data = ValidShake();
            var envelope = data.Envelope;
            envelope.Time = new TimeDef { Mode = TimeMode.Speed, Value = 0f, SpeedScale = 1f };
            data.Envelope = envelope;

            Assert.IsTrue(Validate(data).Exists(r => r.Code == "DD-SHAKE-ENVELOPE-ZERO-DURATION"));
        }
    }
}
