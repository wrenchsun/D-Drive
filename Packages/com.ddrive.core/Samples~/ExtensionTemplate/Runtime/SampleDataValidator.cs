using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Extension;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;

namespace SampleExtension
{
    // 検証(docs/1014 §2.1)。IKindValidator = この KindKey の Data にだけ実行される。AssetBrowser の Validation と
    // 窓のインライン検証(ValidationResult.WithField で欄に結びつく)に出る。
    // Code は自分の接頭辞(SMP-)を使う。D-Drive の "DD-" は使わない。
    public sealed class SampleDataValidator : IKindValidator
    {
        public AssetType Target => AssetType.Extension;

        public string TargetKindKey => SampleKind.Key;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not SampleData sample)
            {
                yield break;
            }

            if (!sample.Loop && sample.Duration <= 0f)
            {
                yield return ValidationResult.Error("続く秒数が 0 以下です。Loop を ON にするか、秒数を入れてください。", null, "SMP-DURATION")
                    .WithField(nameof(SampleData.Duration));
            }

            if (sample.Intensity < 0f)
            {
                yield return ValidationResult.Warning("強さが負の値です。", null, "SMP-INTENSITY")
                    .WithField(nameof(SampleData.Intensity));
            }
        }
    }
}
