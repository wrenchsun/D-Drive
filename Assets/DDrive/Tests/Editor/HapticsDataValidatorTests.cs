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
        private static HapticsData ValidHaptic()
        {
            // クラスのフィールド初期化子(既定 0.2s)をそのまま使う。
            return ScriptableObject.CreateInstance<HapticsData>();
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
