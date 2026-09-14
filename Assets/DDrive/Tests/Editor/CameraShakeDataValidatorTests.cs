using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Camera;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [16_camera_haptics.md] Part A / C-4。
    public class CameraShakeDataValidatorTests
    {
        private static CameraShakeData ValidShake()
        {
            var data = ScriptableObject.CreateInstance<CameraShakeData>();
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
    }
}
