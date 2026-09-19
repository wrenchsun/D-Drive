namespace DDrive.Foundation.Validation
{
    // 種別を問わず全 AssetDataBase に対して実行される Validator(ValueDef の共通検査等)。
    // Target プロパティは無視される(ValidatorRegistry はこれを持つ Validator を全アセットに適用する)。
    public interface IUniversalValidator : IValidator
    {
    }
}
