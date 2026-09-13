using System.Collections.Generic;
using DDrive.Foundation.Validation;

namespace DDrive.Runtime.Ui
{
    // ButtonSkin / SliderSkin 共通の当たり判定検査(2026-09-14)。各 Validator から呼ぶ。
    public static class ControlSkinHitAreaValidation
    {
        private static readonly ControlState[] States =
        {
            ControlState.Normal, ControlState.Hover, ControlState.Pressed,
            ControlState.Selected, ControlState.Disabled, ControlState.Locked,
        };

        public static IEnumerable<ValidationResult> Validate(ControlSkinData skin)
        {
            if (skin == null || skin.AlphaHitThreshold <= 0f)
            {
                yield break;
            }

            foreach (var state in States)
            {
                var sprite = skin.Get(state).OverrideSprite;
                if (sprite != null && sprite.texture != null && !sprite.texture.isReadable)
                {
                    yield return ValidationResult.Warning($"AlphaHitThreshold が有効ですが、{state} の Override Sprite '{sprite.name}' の画像が Read/Write 無効です(透明部分の判定が効かず、全面が押せます)");
                }
            }
        }
    }
}
