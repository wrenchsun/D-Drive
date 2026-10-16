using DDrive.Foundation.Validation;

namespace DDrive.Foundation.Extension
{
    // [1014_extension_platform.md] §2.1(X-1、2026-10-16) — Target が AssetType.Extension の Validator を
    // 特定の種別キーに絞る任意インターフェース。実装しない Extension の Validator は全ての Extension 種別に適用される。
    public interface IKindValidator : IValidator
    {
        string TargetKindKey { get; }
    }
}
