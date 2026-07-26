namespace DDrive.Foundation.Identity
{
    public interface IAssetId
    {
        ulong Value { get; }
        AssetType Type { get; }
        bool IsValid { get; }
    }
}
