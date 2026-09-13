using System.IO;
using DDrive.Editor.Inspectors;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEditor;
using AssetSearch = DDrive.Editor.AssetSearch;

namespace DDrive.Editor.Dependencies
{
    // [11_tasks.md] 5-6 — (AssetType, ulong)から実際の Data アセット(.asset)を引く。
    // AssetIdLookup.GetAllDefinitions()(1-8 で導入済み)と同じ発見ロジックを再利用する
    // (依存ツリー UI・使用箇所検索の「参照先の表示名」解決・未使用一覧に共通で使う)。
    public static class DependencyAssetResolver
    {
        public readonly struct Found
        {
            public readonly AssetDataBase Asset;
            public readonly string Path;

            public Found(AssetDataBase asset, string path)
            {
                Asset = asset;
                Path = path;
            }

            public bool IsValid => Asset != null;
        }

        public static Found Find(AssetType type, ulong id)
        {
            if (id == 0)
            {
                return default;
            }

            foreach (var (dataType, assetType) in AssetIdLookup.GetAllDefinitions())
            {
                if (assetType != type || dataType.Namespace?.Contains("Tests") == true)
                {
                    continue;
                }

                foreach (var guid in AssetSearch.FindAssets("t:" + dataType.Name))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var asset = AssetDatabase.LoadAssetAtPath(path, dataType) as AssetDataBase;
                    if (asset != null && asset.GetType() == dataType && asset.Id == id)
                    {
                        return new Found(asset, path);
                    }
                }
            }

            return default;
        }

        public static string DisplayNameOrFileName(AssetDataBase asset, string path)
        {
            if (asset != null && !string.IsNullOrEmpty(asset.DisplayName))
            {
                return asset.DisplayName;
            }

            return string.IsNullOrEmpty(path) ? "?" : Path.GetFileNameWithoutExtension(path);
        }
    }
}
