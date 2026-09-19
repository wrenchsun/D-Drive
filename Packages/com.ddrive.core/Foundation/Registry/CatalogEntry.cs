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
    }
}
