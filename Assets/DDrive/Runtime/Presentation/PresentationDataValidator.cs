using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
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

            // [14_networking.md] §10(5-8/5-9) — PredictLocal は Flags.Net=Cosmetic のときのみ意味を持つ。
            if (presentation.PredictLocal && presentation.Flags.Net != NetMode.Cosmetic)
            {
                yield return ValidationResult.Info("PredictLocal=true ですが Flags.Net が Cosmetic ではないため無効です(常にローカル再生のみ行われます)");
            }

            // Presentation 自体に Simulated の意味付けは無い(§5 参照。ネットは Local/Cosmetic の 2 値運用)。
            if (presentation.Flags.Net == NetMode.Simulated)
            {
                yield return ValidationResult.Info("Presentation の Flags.Net=Simulated は未対応です(Cosmetic として Host 権威の生成は行われません。Local または Cosmetic を使ってください)");
            }
        }

        // internal ではなく public: PresentationEditorWindow(5-4)が「Asset 未設定」の Kind をトラック追加時の
        // 初期値判定や D&D の可否判定に再利用する(同じ判定をエディタ側に複製しない)。
        public static bool RequiresAsset(TrackKind kind) => kind switch
        {
            TrackKind.Marker => false,
            TrackKind.Signal => false,
            TrackKind.HitStop => false,
            _ => true,
        };
    }
}
