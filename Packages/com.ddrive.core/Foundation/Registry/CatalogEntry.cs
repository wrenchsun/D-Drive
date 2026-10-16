using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;

namespace DDrive.Foundation.Registry
{
    [Serializable]
    public struct CatalogEntry
    {
        public ulong Id;
        public AssetType Type;
        public string Address;
        public AssetFlags Flags;

        // [1014_extension_platform.md] §2.1(X-1、2026-10-16 追加): Type が AssetType.Extension のときの種別キー。
        // 組み込み種別は空(既存データは空のまま読める)。末尾に追加(フィールド順を変えない)。
        public string KindKey;
    }
}
