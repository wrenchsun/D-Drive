using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Validation;

namespace DDrive.Runtime.Cutscene
{
    // [26_timeline.md] §4.1/§4.2/§4.7 — 6-10a の範囲で判定できる基本チェックのみ。fps 検査 6 種・
    // Humanoid/Avatar 不整合・Presentation⇄Cutscene 循環参照・Cosmetic+Simulated 参照等は 6-10d の
    // CutsceneDataValidator 拡張で追加する([11_tasks.md] 6-10d)。
    public sealed class CutsceneDataValidator : IValidator
    {
        public AssetType Target => AssetType.Cutscene;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not CutsceneData cutscene)
            {
                yield break;
            }

            if (cutscene.Timeline == null)
            {
                yield return ValidationResult.Warning("Timeline(TimelineAsset)が未設定です。Cutscene.Play() は即完了します");
            }

            if (cutscene.FrameRate <= 0f)
            {
                yield return ValidationResult.Warning($"FrameRate({cutscene.FrameRate})が 0 以下です");
            }

            if (cutscene.SourceFrameRange.Start != 0 && cutscene.SourceFrameRange.End != 0 && cutscene.SourceFrameRange.End < cutscene.SourceFrameRange.Start)
            {
                yield return ValidationResult.Error($"SourceFrameRange.End({cutscene.SourceFrameRange.End}) が Start({cutscene.SourceFrameRange.Start}) より小さいです");
            }

            if (cutscene.Origin == CutsceneOrigin.AnchorPoint && string.IsNullOrEmpty(cutscene.OriginAnchorName))
            {
                yield return ValidationResult.Error("Origin=AnchorPoint ですが OriginAnchorName が空です");
            }

            if (cutscene.Skip == CutsceneSkip.ToMarker && string.IsNullOrEmpty(cutscene.SkipToMarkerKey))
            {
                yield return ValidationResult.Warning("Skip=ToMarker ですが SkipToMarkerKey が空です(6-10b の D-Drive Signal マーカー導入までは Immediate と同じ挙動になります)");
            }

            var bindings = cutscene.Bindings;
            if (bindings != null)
            {
                var seenNames = new HashSet<string>();
                for (var i = 0; i < bindings.Length; i++)
                {
                    var binding = bindings[i];

                    if (string.IsNullOrEmpty(binding.TrackName))
                    {
                        yield return ValidationResult.Error($"Bindings[{i}] の TrackName が空です");
                    }
                    else if (!seenNames.Add(binding.TrackName))
                    {
                        yield return ValidationResult.Warning($"Bindings[{i}] の TrackName '{binding.TrackName}' が重複しています");
                    }

                    if (binding.Target == CutsceneBindTarget.SpawnModel && !binding.Model.IsValid)
                    {
                        yield return ValidationResult.Error($"Bindings[{i}]({binding.TrackName}) は Target=SpawnModel ですが Model が未設定です");
                    }

                    if ((binding.Target == CutsceneBindTarget.SceneObjectByName || binding.Target == CutsceneBindTarget.AnchorPoint)
                        && string.IsNullOrEmpty(binding.SceneObjectName))
                    {
                        yield return ValidationResult.Error($"Bindings[{i}]({binding.TrackName}) は Target={binding.Target} ですが SceneObjectName が空です");
                    }
                }
            }

            // [14_networking.md] §5 / [26] §4.7 — Presentation と同じ方針(PredictLocal は Cosmetic 限定、
            // Simulated には Cutscene 固有の意味付けが無い)。
            if (cutscene.PredictLocal && cutscene.Flags.Net != NetMode.Cosmetic)
            {
                yield return ValidationResult.Info("PredictLocal=true ですが Flags.Net が Cosmetic ではないため無効です(常にローカル再生のみ行われます)");
            }

            if (cutscene.Flags.Net == NetMode.Simulated)
            {
                yield return ValidationResult.Info("Cutscene の Flags.Net=Simulated は未対応です(Local または Cosmetic を使ってください)");
            }
        }
    }
}
