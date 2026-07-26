using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;

namespace DDrive.Foundation.Registry
{
    public interface IAssetRegistry
    {
        event Action<ulong, AssetType> OnPlaceholderUsed;

        UniTask RegisterCatalogAsync(AssetCatalog catalog);
        UniTask<T> ResolveAsync<T>(ulong id) where T : AssetDataBase;
        bool TryResolveSync<T>(ulong id, out T data) where T : AssetDataBase;
        IReadOnlyList<CatalogEntry> Entries(AssetType type);
    }
}
