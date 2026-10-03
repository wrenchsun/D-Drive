using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;

namespace ExternalPackage.Fake
{
    // 外部アセンブリの IValidator(public な引数なしコンストラクタ)。DDrive.Tests* ではない名前のアセンブリに置くため、
    // CI.DiscoverValidators() に発見される。
    // 持ち込み先で testables を ON にしたときも発見される = 実プロジェクトの「Validation > Run All」に載るため、
    // ExternalDummyData 以外(= 実データ全部)に対しては何も報告しない(Target=None なので AssetIdDefinition を持たない Data だけが対象)。
    public sealed class ExternalDummyValidator : IValidator
    {
        public const string Message = "ExternalContract: 外部アセンブリの IValidator が実行された(契約テスト用ダミー)";

        public AssetType Target => AssetType.None;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not ExternalDummyData)
            {
                yield break;
            }

            yield return ValidationResult.Info(Message);
        }
    }
}
