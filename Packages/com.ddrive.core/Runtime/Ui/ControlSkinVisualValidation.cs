using System.Collections.Generic;
using DDrive.Foundation.Validation;
using UnityEngine;

namespace DDrive.Runtime.Ui
{
    // ButtonSkin / SliderSkin 共通のスプライトアニメ / スクロールの検査(2026-09-14)。各 Validator から呼ぶ。
    public static class ControlSkinVisualValidation
    {
        private static readonly ControlState[] States =
        {
            ControlState.Normal, ControlState.Hover, ControlState.Pressed,
            ControlState.Selected, ControlState.Disabled, ControlState.Locked,
        };

        public static IEnumerable<ValidationResult> Validate(ControlSkinData skin)
        {
            if (skin == null)
            {
                yield break;
            }

            foreach (var state in States)
            {
                var v = skin.Get(state);

                if (v.AnimFrames != null)
                {
                    foreach (var frame in v.AnimFrames)
                    {
                        if (frame == null)
                        {
                            yield return ValidationResult.Warning($"{state} の Anim Frames に空のコマがあります(そのコマの間は直前のコマのまま。1 コマ目が空なら元の画像になります)");
                            break;
                        }
                    }
                }

                if (v.ScrollSpeed == Vector2.zero)
                {
                    continue;
                }

                if (skin.ScrollMaterial == null)
                {
                    yield return ValidationResult.Warning($"{state} に Scroll Speed がありますが、Scroll Material が空です(スクロールしません)");
                }

                // 流す画像はコマがあれば 1 コマ目、無ければ Override Sprite(Image の元画像はここからは見えないので対象外)。
                var sprite = v.AnimFrames != null && v.AnimFrames.Length > 0 && v.AnimFrames[0] != null ? v.AnimFrames[0] : v.OverrideSprite;
                if (sprite != null && sprite.packed)
                {
                    yield return ValidationResult.Warning($"{state} の画像 '{sprite.name}' は Sprite Atlas に入っています(スクロールすると隣の画像が流れ込みます)");
                }
                else if (sprite != null && sprite.texture != null && sprite.texture.wrapMode != TextureWrapMode.Repeat)
                {
                    yield return ValidationResult.Info($"{state} の画像 '{sprite.name}' の Wrap Mode が Repeat ではありません(スクロールすると端の色が引き伸ばされます)");
                }
            }
        }
    }
}
