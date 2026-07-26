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
    }
}
