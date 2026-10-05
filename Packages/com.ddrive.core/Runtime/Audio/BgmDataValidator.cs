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

            // GB-R-02(2026-10-06): LoopEndSec = 0(既定)は「LoopStartSec からクリップ末尾まで」の意味で、BgmManager.StartLoopBody も
            // Audio エディタもそう扱う。実行時に破綻するのは、(a) End を指定したのに Start 以下(End が無視され意図と違う範囲になる)、
            // (b) End=0 でも Start がクリップ末尾以上(実質の終端が Start 以下 = 1 サンプルのループになる)のとき。
            var effectiveEnd = bgm.LoopEndSec > 0.0 ? bgm.LoopEndSec : (bgm.LoopBody != null ? bgm.LoopBody.length : 0.0);
            if (effectiveEnd > 0.0 && effectiveEnd <= bgm.LoopStartSec)
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
