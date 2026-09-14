using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using UnityEngine;

namespace DDrive.Runtime.Haptics
{
    // [16_camera_haptics.md] Part B / C-4。ValueDef 共通検査は ValueDefValidator([17] §6)に委譲済み。
    public sealed class HapticsDataValidator : IValidator
    {
        private const float LongDurationWarnThreshold = 2f;

        public AssetType Target => AssetType.Haptics;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not HapticsData haptic)
            {
                yield break;
            }

            var duration = Mathf.Max(haptic.LowFreq.Duration, haptic.HighFreq.Duration);
            if (duration > LongDurationWarnThreshold)
            {
                yield return ValidationResult.Warning($"振動の尺が {LongDurationWarnThreshold}秒 を超えています(長すぎる振動)");
            }

            if (haptic.Extensions != null)
            {
                foreach (var ext in haptic.Extensions)
                {
                    if (string.IsNullOrEmpty(ext.PlatformKey))
                    {
                        yield return ValidationResult.Info("Extensions に PlatformKey が空の要素があります");
                    }
                }

                if (haptic.Extensions.Length > 0)
                {
                    yield return ValidationResult.Info("Extensions(プラットフォーム別拡張)は未実装のため、基本の 2 モーターカーブのみ再生されます");
                }
            }
        }
    }
}
