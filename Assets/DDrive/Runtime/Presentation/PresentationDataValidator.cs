using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;

namespace DDrive.Runtime.Presentation
{
    // [08_presentation.md] §6。
    public sealed class PresentationDataValidator : IValidator
    {
        public AssetType Target => AssetType.Presentation;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not PresentationData presentation)
            {
                yield break;
            }

            var tracks = presentation.Tracks;
            if (tracks == null)
            {
                yield break;
            }

            for (var i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];

                if (RequiresAsset(track.Kind) && !track.Asset.IsAssigned)
                {
                    yield return ValidationResult.Error($"トラック {i}({track.Kind}) の Asset が未設定です");
                }

                if (track.Trigger == TrackTrigger.OnSignal && string.IsNullOrEmpty(track.SignalKey))
                {
                    yield return ValidationResult.Error($"トラック {i}({track.Kind}) は Trigger=OnSignal ですが SignalKey が空です");
                }

                if ((track.Kind == TrackKind.Marker || track.Kind == TrackKind.Signal) && string.IsNullOrEmpty(track.SignalKey))
                {
                    yield return ValidationResult.Error($"トラック {i}({track.Kind}) の名前(SignalKey)が空です");
                }

                if (track.Trigger == TrackTrigger.AtTime && presentation.TotalDuration > 0f && track.Time > presentation.TotalDuration)
                {
                    yield return ValidationResult.Warning($"トラック {i}({track.Kind}) の Time({track.Time:0.###}s) が TotalDuration({presentation.TotalDuration:0.###}s) を超過しています");
                }

                // 循環参照(自身を含む): 現行の TrackKind には「他 Presentation を再生する」種別が無いため、
                // Asset が Presentation 種別を指している(将来の拡張)場合のみ検出する。
                if (track.Asset.Type == AssetType.Presentation && track.Asset.Id == presentation.Id && presentation.Id != 0)
                {
                    yield return ValidationResult.Error($"トラック {i} が自身({presentation.DisplayName})を参照する循環になっています");
                }
            }

            if (!presentation.Interruptible && PresentationTiming.EffectiveDuration(presentation) > 10f)
            {
                yield return ValidationResult.Warning("Interruptible=false ですが尺が 10 秒を超えています(中断できない長尺演出)");
            }
        }

        private static bool RequiresAsset(TrackKind kind) => kind switch
        {
            TrackKind.Marker => false,
            TrackKind.Signal => false,
            TrackKind.HitStop => false,
            _ => true,
        };
    }
}
