using System.Collections.Generic;
using DDrive.Foundation.Data;

namespace DDrive.Foundation.Validation
{
    public sealed class ValidationContext
    {
        public IReadOnlyList<AssetDataBase> AllAssets { get; }

        public ValidationContext(IReadOnlyList<AssetDataBase> allAssets)
        {
            AllAssets = allAssets;
        }
    }
}
