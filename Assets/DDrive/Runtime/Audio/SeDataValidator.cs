using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;

namespace DDrive.Runtime.Audio
{
    // [03_audio.md] §7。Anchor.BoneName がプレビューモデルに無いかの検査は
    // プレビュー実行時の関心事(1-7 AudioEditor 側)であり、静的な Validator の対象外とする。
    public sealed class SeDataValidator : IValidator
    {
        public AssetType Target => AssetType.Se;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not SeData se)
            {
                yield break;
            }

            if (se.Clips == null || se.Clips.Length == 0 || Array.Exists(se.Clips, c => c == null))
            {
                yield return ValidationResult.Error("Clip が未設定(または Missing)です");
            }

            if (se.Mixer == null)
            {
                yield return ValidationResult.Warning("Mixer が未割当です");
            }

            var needsPath = se.Anchor.Space is AnchorSpace.BoneName or AnchorSpace.NamedObject;
            if (se.Spatial == SpatialMode.Anchor && !se.AnchorId.IsValid && needsPath && string.IsNullOrEmpty(se.Anchor.Path))
            {
                yield return ValidationResult.Error("Spatial=Anchor ですが Anchor の Path が未設定です");
            }

            if (se.AnchorId.IsValid && DDrive.Runtime.Anchoring.AnchorDataValidator.IsEmbeddedAnchorNonDefault(se.Anchor))
            {
                yield return ValidationResult.Warning("AnchorId が設定されているため、埋め込みの Anchor は無視されます");
            }

            if (se.MaxDistance <= se.MinDistance)
            {
                yield return ValidationResult.Error("MaxDistance が MinDistance 以下です");
            }

            if (se.DopplerEnabled && !se.Loop)
            {
                yield return ValidationResult.Warning("DopplerEnabled ですが Loop=false です(ワンショットでは知覚されにくい)");
            }

            if (se.MaxConcurrent <= 0)
            {
                yield return ValidationResult.Error("MaxConcurrent が 0 以下です");
            }

            if (se.Volume <= 0f)
            {
                yield return ValidationResult.Warning("Volume が 0 です(鳴りません)");
            }

            if (se.Sources != null && se.Sources.Length > 0 && se.Clips != null && se.Sources.Length != se.Clips.Length)
            {
                yield return ValidationResult.Warning("Sources と Clips の要素数が一致していません(トリミング未適用の可能性)");
            }

            if (se.StartOffsetSec > 0f && se.Clips != null && se.Clips.Length > 0 && se.Clips[0] != null &&
                se.StartOffsetSec >= se.Clips[0].length)
            {
                yield return ValidationResult.Warning("StartOffsetSec がクリップ長以上のため無音になります");
            }
        }
    }
}
