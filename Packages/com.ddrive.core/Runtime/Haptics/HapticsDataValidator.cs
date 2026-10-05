using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Foundation.Values;
using UnityEngine;

namespace DDrive.Runtime.Haptics
{
    // [16_camera_haptics.md] Part B / C-4。ValueDef 共通検査は ValueDefValidator([17] §6)に委譲済み。
    public sealed class HapticsDataValidator : IValidator
    {
        private const float LongDurationWarnThreshold = 2f;

        // [42] §5.8: 新規の検査は Warning 始まり。Code は追加のみ。
        private const string ZeroDurationCode = "DD-HAPTICS-ZERO-DURATION";

        public AssetType Target => AssetType.Haptics;

        private static bool IsReportedByValueDefValidator(ValueDef def)
            => def.Mode != ValueMode.Constant && def.Time.Mode == TimeMode.Duration && def.Time.Value <= 0f;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not HapticsData haptic)
            {
                yield break;
            }

            var duration = Mathf.Max(haptic.LowFreq.Duration, haptic.HighFreq.Duration);

            // GB-R-01(2026-10-06): HapticsManager.IsExpired は Mode に関係なく Max(Low, High の Duration) を振動の寿命として読む。
            // Constant は ValueDefValidator が Time を検査しないため、ここで知らせる。Time を使うモード + Duration 指定の
            // Value<=0 は ValueDefValidator の Error が既に出るので重ねない。
            if (duration <= 0f && !IsReportedByValueDefValidator(haptic.LowFreq) && !IsReportedByValueDefValidator(haptic.HighFreq))
            {
                yield return ValidationResult.Warning(
                    "LowFreq / HighFreq の尺(Duration)がどちらも 0 以下のため、再生してもすぐ終わり振動しません(固定値でも尺は振動の長さとして使われます)",
                    code: ZeroDurationCode);
            }
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
