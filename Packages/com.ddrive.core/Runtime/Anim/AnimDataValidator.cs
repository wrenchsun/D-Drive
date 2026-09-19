using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;

namespace DDrive.Runtime.Anim
{
    // [05_model_animation.md] B-6。「StateName が Controller に存在しない」「BlendShape 名が対象モデルに無い」は
    // 対象(Controller / モデル)が要るためプレビュー実行時の関心事(AnimEditor 側)とし、静的な Validator の対象外とする。
    public sealed class AnimDataValidator : IValidator
    {
        public AssetType Target => AssetType.Anim;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not AnimData anim)
            {
                yield break;
            }

            if (anim.Clip == null)
            {
                yield return ValidationResult.Error("Clip が未設定(または Missing)です");
            }

            if (string.IsNullOrEmpty(anim.ResolvedStateName))
            {
                yield return ValidationResult.Error("StateName が空で Clip も無いため、再生するステートが決まりません");
            }

            if (anim.Events != null)
            {
                var length = anim.LengthSec;
                var frames = length * anim.FrameRate;
                foreach (var evt in anim.Events)
                {
                    if (evt.Trigger == EventTrigger.Frame && anim.Clip != null && evt.Time > frames + 0.001f)
                    {
                        yield return ValidationResult.Error($"Frame イベント({evt.Time:0})が Clip 長({frames:0} フレーム)を超えています");
                    }

                    if (evt.Trigger == EventTrigger.Time && anim.Clip != null && evt.Time > length + 0.001f)
                    {
                        yield return ValidationResult.Error($"Time イベント({evt.Time:0.##}s)が Clip 長({length:0.##}s)を超えています");
                    }

                    if (evt.Trigger == EventTrigger.OnLoop && !anim.Loop)
                    {
                        yield return ValidationResult.Warning("Loop=false ですが OnLoop イベントがあります(発火しません)");
                    }
                }
            }

            if (anim.BlendShapes != null)
            {
                foreach (var track in anim.BlendShapes)
                {
                    if (string.IsNullOrEmpty(track.ShapeName))
                    {
                        yield return ValidationResult.Warning("BlendShape の ShapeName が空の行があります");
                    }
                }
            }

            if (anim.DefaultCrossFade > 2f)
            {
                yield return ValidationResult.Warning("DefaultCrossFade が 2 秒を超えています");
            }
        }
    }
}
