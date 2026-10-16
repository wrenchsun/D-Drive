using DDrive.Foundation.Manager;

namespace DDrive.Foundation.Extension
{
    // [1014_extension_platform.md] §2.1(X-1、2026-10-16) — IAssetManager.Type が AssetType.Extension の Manager が、
    // どの種別キーを担当するかを返す任意インターフェース(既存 Manager は実装しない)。
    public interface IExtensionAssetManager : IAssetManager
    {
        string KindKey { get; }
    }
}
