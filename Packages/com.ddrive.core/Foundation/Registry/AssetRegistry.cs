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

        // P5 レビュー対応(2026-09-14): IAssetRegistry の PreloadIdsAsync のコメント
        // 「未登録 ID は警告(1 ID につき 1 回、OnPlaceholderUsed とは独立)」を守るため、
        // Preload 経路(_preloadWarnedIds)と Placeholder 経路(_placeholderWarnedIds)の
        // 警告済み集合を分離する(以前は 1 つの HashSet を共有し、どちらかで先に警告した ID は
        // もう一方の経路で二度と警告されなかった)。
        private readonly HashSet<ulong> _preloadWarnedIds = new();
        private readonly HashSet<ulong> _placeholderWarnedIds = new();

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
                // 二重登録(エディタ再初期化等)でも _byType が重複しないよう、既知 ID はスキップして上書きのみ。
                var alreadyKnown = _index.ContainsKey(entry.Id);
                _index[entry.Id] = entry;

                if (!alreadyKnown)
                {
                    if (!_byType.TryGetValue(entry.Type, out var list))
                    {
                        list = new List<CatalogEntry>();
                        _byType[entry.Type] = list;
                    }

                    list.Add(entry);
                }

                if (entry.Flags.Load == LoadMode.Preload)
                {
                    preloadTasks.Add(PreloadEntryAsync(entry));
                }
            }

            await UniTask.WhenAll(preloadTasks);
        }

        public async UniTask<T> ResolveAsync<T>(ulong id) where T : AssetDataBase
        {
            // キャッシュ済みでも型不一致なら placeholder に落とす(FR-1.4: null を返さない)。
            if (_loaded.TryGetValue(id, out var cached))
            {
                if (cached is T typedCached)
                {
                    return typedCached;
                }

                NotifyPlaceholderUsed(id, _index.TryGetValue(id, out var knownEntry) ? knownEntry.Type : AssetType.None);
                return PlaceholderProvider.Get<T>();
            }

            if (!_index.TryGetValue(id, out var entry))
            {
                NotifyPlaceholderUsed(id, AssetType.None);
                return PlaceholderProvider.Get<T>();
            }

            var data = await _loader.LoadAsync<T>(entry.Address, CancellationToken.None);

            // ロード失敗(null)はキャッシュしない。null を永続キャッシュすると以後ずっと
            // placeholder 保証が破られるため、失敗時はその都度 placeholder を返し再試行可能に保つ。
            if (data == null)
            {
                NotifyPlaceholderUsed(id, entry.Type);
                return PlaceholderProvider.Get<T>();
            }

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

        // 同期 Play 経路(AudioManager 等)向け。await できない呼び出し元でも
        // Registry の警告一元化(1 ID につき 1 回)に乗った状態で Placeholder に落とせる。
        public T ResolveOrPlaceholder<T>(ulong id) where T : AssetDataBase
        {
            if (_loaded.TryGetValue(id, out var cached) && cached is T typed)
            {
                return typed;
            }

            var type = _index.TryGetValue(id, out var entry) ? entry.Type : AssetType.None;
            NotifyPlaceholderUsed(id, type);
            return PlaceholderProvider.Get<T>();
        }

        public IReadOnlyList<CatalogEntry> Entries(AssetType type)
            => _byType.TryGetValue(type, out var list) ? list : Array.Empty<CatalogEntry>();

        // [14_networking.md] §9/§10(6-6) — ResolveOrPlaceholder/NotifyPlaceholderUsed を経由しない
        // (副作用なしの)存在確認。ネット受信ハンドラが「未登録 AssetId・範囲外(種別不一致)」を Placeholder
        // 経由の再生に落とす前に検出し、破棄 + ログできるようにする。
        public bool IsRegistered(ulong id, AssetType type)
            => _index.TryGetValue(id, out var entry) && entry.Type == type;

        // [11_tasks.md] 5-7 — ScenePreloadList(Editor が依存グラフから集計)を実行するランタイム側の入口。
        // 既存の CatalogEntry.Address 解決 + IAssetLoader.PreloadAsync(参照カウント式)をそのまま再利用する
        // (新しいロード経路を増やさない)。
        public UniTask PreloadIdsAsync(IReadOnlyList<ulong> ids, IProgress<float> progress)
        {
            if (ids == null || ids.Count == 0)
            {
                progress?.Report(1f);
                return UniTask.CompletedTask;
            }

            var addresses = new List<string>(ids.Count);
            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                if (_index.TryGetValue(id, out var entry))
                {
                    addresses.Add(entry.Address);
                }
                else if (_preloadWarnedIds.Add(id))
                {
                    // 例外にしない(CLAUDE.md §0-4): 未登録 ID はスキップし、ロード画面は残りだけで完了させる。
                    Debug.LogWarning($"[DDrive] ScenePreload: Unregistered AssetId 0x{id:X} was skipped.");
                }
            }

            return _loader.PreloadAsync(addresses, progress);
        }

        public void ReleaseIds(IReadOnlyList<ulong> ids)
        {
            if (ids == null)
            {
                return;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                if (_index.TryGetValue(ids[i], out var entry))
                {
                    _loader.Release(entry.Address);
                }
            }
        }

        private async UniTask PreloadEntryAsync(CatalogEntry entry)
        {
            var data = await _loader.LoadAsync<AssetDataBase>(entry.Address, CancellationToken.None);
            _loaded[entry.Id] = data;
        }

        private void NotifyPlaceholderUsed(ulong id, AssetType type)
        {
            OnPlaceholderUsed?.Invoke(id, type);

            if (_placeholderWarnedIds.Add(id))
            {
                Debug.LogWarning($"[DDrive] Unregistered AssetId 0x{id:X} resolved to Placeholder.");
            }
        }
    }
}
