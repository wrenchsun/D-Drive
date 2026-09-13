using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;

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

            foreach (var result in ControlSkinHitAreaValidation.Validate(skin))
            {
                yield return result;
            }
        }

        private static bool AllTintAlphaZero(SliderSkinData skin)
            => skin.Normal.Tint.a <= 0f && skin.Hover.Tint.a <= 0f && skin.Pressed.Tint.a <= 0f
               && skin.Selected.Tint.a <= 0f && skin.Disabled.Tint.a <= 0f && skin.Locked.Tint.a <= 0f;
    }
}
