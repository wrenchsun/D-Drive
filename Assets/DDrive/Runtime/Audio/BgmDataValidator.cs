using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;

namespace DDrive.Runtime.Audio
{
    public sealed class BgmDataValidator : IValidator
    {
        public AssetType Target => AssetType.Bgm;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not BgmData bgm)
            {
                yield break;
            }

            if (bgm.LoopBody == null)
            {
                yield return ValidationResult.Error("LoopBody(Clip) が未設定(または Missing)です");
            }

            if (bgm.Mixer == null)
            {
                yield return ValidationResult.Warning("Mixer が未割当です");
            }

            if (bgm.LoopEndSec <= bgm.LoopStartSec)
            {
                yield return ValidationResult.Error("LoopEndSec が LoopStartSec 以下です");
            }

            if (bgm.Volume <= 0f)
            {
                yield return ValidationResult.Warning("Volume が 0 です(鳴りません)");
            }
        }
    }
}
