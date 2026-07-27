using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Foundation.Registry
{
    // Addressables のエントリポイント。ID とアドレスのみを持つ軽量構造(01_architecture.md §5)。
    // 種別ごとの実体(AudioCatalog 等)はこのクラスのインスタンスとして GameData/Catalogs 配下に作る。
    [CreateAssetMenu(menuName = "D-Drive/Asset Catalog", fileName = "NewCatalog")]
    public sealed class AssetCatalog : ScriptableObject
    {
        [SerializeField] private List<CatalogEntry> entries = new();

        public IReadOnlyList<CatalogEntry> Entries => entries;

        public void SetEntries(List<CatalogEntry> newEntries) => entries = newEntries ?? new List<CatalogEntry>();

        // 追記型運用([10_workflow.md] §4)。同一 ID は上書きし、ID 昇順を維持することで
        // ブランチ間マージ時のコンフリクトを最小化する。
        public void AddOrUpdate(CatalogEntry entry)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Id == entry.Id)
                {
                    entries[i] = entry;
                    return;
                }
            }

            entries.Add(entry);
            entries.Sort((a, b) => a.Id.CompareTo(b.Id));
        }
    }
}
