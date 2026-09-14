using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using UnityEngine;

namespace DDrive.Runtime.CameraShake
{
    // [16_camera_haptics.md] Part A / C-4。ValueDef 共通検査(Curve未設定・Duration<=0 等)は
    // ValueDefValidator([17] §6)に委譲済みのため、ここでは Shake 固有の検査のみ行う。
    public sealed class CameraShakeDataValidator : IValidator
    {
        // 酔いリスクの目安となる固定閾値([13] BudgetProfile は未実装のため、暫定の固定値を使う。
        // 要判断: BudgetProfile 導入時にプロファイル参照へ差し替える)。
        private const float PosAmplitudeWarnThreshold = 2f;
        private const float RotAmplitudeWarnThreshold = 45f;

        public AssetType Target => AssetType.Shake;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not CameraShakeData shake)
            {
                yield break;
            }

            if (shake.PosAmplitude == Vector3.zero && shake.RotAmplitude == Vector3.zero)
            {
                yield return ValidationResult.Warning("PosAmplitude / RotAmplitude が両方ゼロです(揺れません)");
            }

            if (shake.PosAmplitude.magnitude > PosAmplitudeWarnThreshold)
            {
                yield return ValidationResult.Warning($"PosAmplitude が大きすぎる可能性があります(酔いのリスク。目安 {PosAmplitudeWarnThreshold}m)");
            }

            if (shake.RotAmplitude.magnitude > RotAmplitudeWarnThreshold)
            {
                yield return ValidationResult.Warning($"RotAmplitude が大きすぎる可能性があります(酔いのリスク。目安 {RotAmplitudeWarnThreshold}deg)");
            }

            if (shake.MaxStack <= 0)
            {
                yield return ValidationResult.Warning("MaxStack が 0 以下です(Shake() が常に無視されます)");
            }

            if (shake.TraumaWeight <= 0f)
            {
                yield return ValidationResult.Warning("TraumaWeight が 0 以下です(合成に寄与しません)");
            }
        }
    }
}
