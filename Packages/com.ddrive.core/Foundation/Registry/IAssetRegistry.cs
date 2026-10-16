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
        T ResolveOrPlaceholder<T>(ulong id) where T : AssetDataBase;
        IReadOnlyList<CatalogEntry> Entries(AssetType type);

        // [1014_extension_platform.md] §2.1(X-1、2026-10-16 追加) — (AssetType, KindKey) で引く。kindKey が空なら Entries(type) と同じ。
        // 既存の実装(テストの Fake 等)を壊さないよう既定実装を持つ(AssetRegistry は index で override する)。
        IReadOnlyList<CatalogEntry> Entries(AssetType type, string kindKey)
        {
            var all = Entries(type);
            if (string.IsNullOrEmpty(kindKey))
            {
                return all;
            }

            var filtered = new List<CatalogEntry>();
            for (var i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].KindKey, kindKey, StringComparison.Ordinal))
                {
                    filtered.Add(all[i]);
                }
            }

            return filtered;
        }

        // [14_networking.md] §9/§10(6-6) — ネット受信の入口で「破棄 + ログ」を行うための、Placeholder
        // フォールバック(OnPlaceholderUsed 発火・警告ログ)を一切トリガーしない純粋な存在確認。
        // 「未登録 AssetId」だけでなく「登録されているが種別が一致しない(=範囲外)」も検出できるよう
        // type も受け取る(id の値域自体は種別を問わず共有されるため、id だけでは範囲外送信を検出できない)。
        bool IsRegistered(ulong id, AssetType type);

        // [11_tasks.md] 5-7 — シーン単位の Preload リスト実行用。登録済み ID を Address に解決して
        // IAssetLoader.PreloadAsync にまとめて渡す(参照カウント +1。対になる ReleaseIds を呼ぶまで保持される)。
        // 未登録 ID は例外にせず警告(1 ID につき 1 回、OnPlaceholderUsed とは独立)してスキップする。
        UniTask PreloadIdsAsync(IReadOnlyList<ulong> ids, IProgress<float> progress);

        // PreloadIdsAsync で確保した参照を返す(シーンアンロード時等)。未登録だった ID は無視する。
        void ReleaseIds(IReadOnlyList<ulong> ids);
    }
}
