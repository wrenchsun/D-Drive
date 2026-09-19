using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Foundation.Values;
using DDrive.Runtime.Haptics;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [16_camera_haptics.md] Part B / C-4。
    public class HapticsDataValidatorTests
    {
        // P5 レビュー第 1 弾 整理-4(2026-09-14): ValidHaptic() の ScriptableObject.CreateInstance が
        // DestroyImmediate されずリークしていた。TearDown でまとめて破棄する。
        private readonly List<HapticsData> _created = new();

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

        private HapticsData ValidHaptic()
        {
            // クラスのフィールド初期化子(既定 0.2s)をそのまま使う。
            var data = ScriptableObject.CreateInstance<HapticsData>();
            _created.Add(data);
            return data;
        }

        private static List<ValidationResult> Validate(HapticsData data)
            => new HapticsDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })).ToList();

        [Test]
        public void ValidHapticData_HasNoErrorsOrWarnings()
        {
            var results = Validate(ValidHaptic());
            Assert.IsEmpty(results);
        }

        [Test]
        public void LongDuration_IsWarning()
        {
            var data = ValidHaptic();
            var longTime = new TimeDef { Mode = TimeMode.Duration, Value = 3f, SpeedScale = 1f };
            var low = data.LowFreq;
            low.Time = longTime;
            data.LowFreq = low;

            var results = Validate(data);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("長すぎる振動")));
        }

        [Test]
        public void ExtensionsPresent_IsInfo()
        {
            var data = ValidHaptic();
            data.Extensions = new[] { new HapticExt { PlatformKey = "DualSense" } };

            var results = Validate(data);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Info && r.Message.Contains("未実装")));
        }

        [Test]
        public void ExtensionsWithEmptyPlatformKey_IsInfo()
        {
            var data = ValidHaptic();
            data.Extensions = new[] { new HapticExt { PlatformKey = "" } };

            var results = Validate(data);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Info && r.Message.Contains("PlatformKey")));
        }
    }
}
