using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Loader;
using UnityEngine;

namespace DDrive.Foundation.Registry
{
    // ID→Data 解決の中核。内部は Dictionary<ulong, CatalogEntry> による O(1) 引き(NFR-1)。
    // 未登録 ID は例外を投げず PlaceholderProvider にフォールバックする(FR-1.4)。
    public sealed class AssetRegistry : IAssetRegistry
    {
        private readonly IAssetLoader _loader;
        private readonly Dictionary<ulong, CatalogEntry> _index = new();
        private readonly Dictionary<AssetType, List<CatalogEntry>> _byType = new();
        private readonly Dictionary<ulong, AssetDataBase> _loaded = new();
        private readonly HashSet<ulong> _warnedIds = new();

        public event Action<ulong, AssetType> OnPlaceholderUsed;

        public AssetRegistry(IAssetLoader loader)
        {
            _loader = loader;
        }

        public async UniTask RegisterCatalogAsync(AssetCatalog catalog)
        {
            var preloadTasks = new List<UniTask>();

            foreach (var entry in catalog.Entries)
            {
                _index[entry.Id] = entry;

                if (!_byType.TryGetValue(entry.Type, out var list))
                {
                    list = new List<CatalogEntry>();
                    _byType[entry.Type] = list;
                }

                list.Add(entry);

                if (entry.Flags.Load == LoadMode.Preload)
                {
                    preloadTasks.Add(PreloadEntryAsync(entry));
                }
            }

            await UniTask.WhenAll(preloadTasks);
        }

        public async UniTask<T> ResolveAsync<T>(ulong id) where T : AssetDataBase
        {
            if (_loaded.TryGetValue(id, out var cached))
            {
                return cached as T;
            }

            if (!_index.TryGetValue(id, out var entry))
            {
                NotifyPlaceholderUsed(id, AssetType.None);
                return PlaceholderProvider.Get<T>();
            }

            var data = await _loader.LoadAsync<T>(entry.Address, CancellationToken.None);
            _loaded[id] = data;
            return data;
        }

        public bool TryResolveSync<T>(ulong id, out T data) where T : AssetDataBase
        {
            if (_loaded.TryGetValue(id, out var cached) && cached is T typed)
            {
                data = typed;
                return true;
            }

            data = null;
            return false;
        }

        public IReadOnlyList<CatalogEntry> Entries(AssetType type)
            => _byType.TryGetValue(type, out var list) ? list : Array.Empty<CatalogEntry>();

        private async UniTask PreloadEntryAsync(CatalogEntry entry)
        {
            var data = await _loader.LoadAsync<AssetDataBase>(entry.Address, CancellationToken.None);
            _loaded[entry.Id] = data;
        }

        private void NotifyPlaceholderUsed(ulong id, AssetType type)
        {
            OnPlaceholderUsed?.Invoke(id, type);

            if (_warnedIds.Add(id))
            {
                Debug.LogWarning($"[DDrive] Unregistered AssetId 0x{id:X} resolved to Placeholder.");
            }
        }
    }
}
