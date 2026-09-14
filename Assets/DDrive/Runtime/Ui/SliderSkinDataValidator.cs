using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Haptics;

namespace DDrive.Runtime.Ui
{
    // [18_ui_controls.md] B-7 — SliderSkinData の静的検査(4-18)。
    public sealed class SliderSkinDataValidator : IValidator
    {
        public AssetType Target => AssetType.ControlSkin;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not SliderSkinData skin)
            {
                yield break;
            }

            if (skin.NotchSeMinIntervalSec < 0f)
            {
                yield return ValidationResult.Error("NotchSeMinIntervalSec が負数です");
            }

            if (AllTintAlphaZero(skin))
            {
                yield return ValidationResult.Warning("全状態の Tint.a が 0 です(何も描画されません)");
            }

            // [31_phase5_decisions.md] A2(2026-09-15): NotchHapticId/LimitHapticId は ulong のまま運用する
            // 代わりに、0(未設定)以外の値で対応する HapticsData が見つからない場合は Warning にする。
            if (skin.NotchHapticId != 0 && !HapticExists(ctx, skin.NotchHapticId))
            {
                yield return ValidationResult.Warning($"NotchHapticId(0x{skin.NotchHapticId:X}) の Haptics アセットが見つかりません");
            }

            if (skin.LimitHapticId != 0 && !HapticExists(ctx, skin.LimitHapticId))
            {
                yield return ValidationResult.Warning($"LimitHapticId(0x{skin.LimitHapticId:X}) の Haptics アセットが見つかりません");
            }

            foreach (var result in ControlSkinHitAreaValidation.Validate(skin))
            {
                yield return result;
            }

            foreach (var result in ControlSkinVisualValidation.Validate(skin))
            {
                yield return result;
            }
        }

        private static bool AllTintAlphaZero(SliderSkinData skin)
            => skin.Normal.Tint.a <= 0f && skin.Hover.Tint.a <= 0f && skin.Pressed.Tint.a <= 0f
               && skin.Selected.Tint.a <= 0f && skin.Disabled.Tint.a <= 0f && skin.Locked.Tint.a <= 0f;

        private static bool HapticExists(ValidationContext ctx, ulong id)
        {
            var all = ctx.AllAssets;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] is HapticsData h && h.Id == id)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
