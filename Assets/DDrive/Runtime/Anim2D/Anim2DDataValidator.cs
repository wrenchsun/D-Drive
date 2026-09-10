using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;

namespace DDrive.Runtime.Anim2D
{
    // [05_model_animation.md] C-6 — Anim2DData の静的検査(3-12)。
    // 「BlendTree に x,y パラメータ無し(FixAction=追加)」と「スライス済みスプライトの参照切れ」は Controller / Importer が要るため
    // Editor 側(Anim2DEditor、3-13)で行う。Clip 長・イベントの検査は AnimDataValidator が同じ Data に対して走る(Target=Anim)。
    public sealed class Anim2DDataValidator : IValidator
    {
        public AssetType Target => AssetType.Anim2D;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not Anim2DData anim)
            {
                yield break;
            }

            if (anim.Clip == null)
            {
                yield return ValidationResult.Error("Clip が未生成(または Missing)です。Anim2DEditor で生成してください");
            }

            if (anim.FrameRate <= 0f)
            {
                yield return ValidationResult.Error("FrameRate が 0 以下です");
            }

            if (anim.HasDirections)
            {
                var required = anim.RequiredDirectionClips;
                var count = anim.DirectionClips != null ? anim.DirectionClips.Length : 0;
                if (count < required)
                {
                    yield return ValidationResult.Error($"Directions={anim.Directions} ですが DirectionClips が {count} 本です({required} 本必要)");
                }
                else
                {
                    for (var i = 0; i < count; i++)
                    {
                        if (anim.DirectionClips[i] == null)
                        {
                            yield return ValidationResult.Error($"DirectionClips[{i}] が未設定(または Missing)です");
                        }
                    }
                }

                if (string.IsNullOrEmpty(anim.ParamXName) || string.IsNullOrEmpty(anim.ParamYName))
                {
                    yield return ValidationResult.Error("方向付きなのに BlendTree のパラメータ名(ParamXName / ParamYName)が空です");
                }
            }
            else if (anim.DirectionClips != null && anim.DirectionClips.Length > 0)
            {
                yield return ValidationResult.Warning("Directions=None ですが DirectionClips が設定されています(使われません)");
            }
        }
    }
}
