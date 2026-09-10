using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Foundation.Values;

namespace DDrive.Runtime.Ui
{
    // [15_ui_interaction.md] B-7 — UiTweenData の静的検査(チケット 4-8)。
    public sealed class UiTweenDataValidator : IValidator
    {
        public AssetType Target => AssetType.UiTween;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not UiTweenData tween)
            {
                yield break;
            }

            if (tween.Tracks == null || tween.Tracks.Length == 0)
            {
                yield return ValidationResult.Warning("Tracks が空です(再生しても何も起こりません)");
                yield break;
            }

            var maxEnd = 0f;
            foreach (var track in tween.Tracks)
            {
                if (track.Motion.Duration <= 0f && track.Motion.Loop != LoopMode.Once)
                {
                    yield return ValidationResult.Warning("Motion.Duration が 0 以下のまま Loop/PingPong に設定されています(進行しない無限ループ)");
                }

                if (track.Property == TweenProperty.PathMove && !track.Path.IsValid)
                {
                    yield return ValidationResult.Error("Property=PathMove ですが Path の制御点が 2 点未満です");
                }

                if (track.Property == TweenProperty.FillAmount)
                {
                    yield return ValidationResult.Info("Property=FillAmount は Image を持つ対象でのみ反映されます");
                }

                if (track.Property == TweenProperty.Color)
                {
                    yield return ValidationResult.Info("Property=Color は Graphic(Image/Text 等)を持つ対象でのみ反映されます");
                }

                var end = UiTweenData.TrackEnd(track);
                if (end > maxEnd)
                {
                    maxEnd = end;
                }
            }

            if (tween.TotalDuration > 0f && tween.TotalDuration < maxEnd)
            {
                yield return ValidationResult.Warning("TotalDuration が最長 Track の終了時刻より短く設定されています");
            }
        }
    }
}
