using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;

namespace DDrive.Foundation.Validation
{
    // 種別ごとの Validator を登録制にする。新種別追加時は実装を足すだけでよい(NFR-7)。
    public interface IValidator
    {
        AssetType Target { get; }
        IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx);
    }
}
