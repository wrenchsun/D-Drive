using DDrive.Foundation.Data;

namespace DDrive.Foundation.Validation
{
    public readonly struct ValidationReport
    {
        public readonly AssetDataBase Asset;
        public readonly ValidationResult Result;

        public ValidationReport(AssetDataBase asset, ValidationResult result)
        {
            Asset = asset;
            Result = result;
        }
    }
}
