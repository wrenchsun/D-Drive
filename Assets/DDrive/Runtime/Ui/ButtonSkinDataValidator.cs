using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;

namespace DDrive.Runtime.Ui
{
    // [15_ui_interaction.md] A-4 — ButtonSkinData の静的検査。
    public sealed class ButtonSkinDataValidator : IValidator
    {
        public AssetType Target => AssetType.ControlSkin;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not ButtonSkinData skin)
            {
                yield break;
            }

            if (AllTintAlphaZero(skin))
            {
                yield return ValidationResult.Warning("全状態の Tint.a が 0 です(何も描画されません)");
            }

            if (!skin.ClickSe.IsValid)
            {
                yield return ValidationResult.Info("ClickSe が未設定です");
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

        private static bool AllTintAlphaZero(ButtonSkinData skin)
            => skin.Normal.Tint.a <= 0f && skin.Hover.Tint.a <= 0f && skin.Pressed.Tint.a <= 0f
               && skin.Selected.Tint.a <= 0f && skin.Disabled.Tint.a <= 0f && skin.Locked.Tint.a <= 0f;
    }
}
