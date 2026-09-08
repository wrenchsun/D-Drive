using System;
using DDrive.Editor.Codegen;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.AssetBrowser
{
    // 「新規作成 = 意味情報の入力だけ」を実現する作成パイプライン(FR-1.5)。
    // ファイル名生成 → アセット作成 → GUID 由来の安定 ID 発行 → カタログ登録 までを 1 回で行う。
    // Addressables グループへの登録は未実装(カタログの Address 文字列までは発行する。
    // AddressableAssetSettings への自動組み込みは Preload 実装(5-7)前までに対応する)。
    public static class AssetCreationService
    {
        public const string DefaultGameDataRoot = "Assets/GameData";

        public static AssetDataBase Create(
            Type dataType,
            AssetType assetType,
            string displayName,
            string category,
            string identifier,
            Action<AssetDataBase> configure = null,
            string gameDataRoot = DefaultGameDataRoot)
        {
            if (!typeof(AssetDataBase).IsAssignableFrom(dataType) || dataType.IsAbstract)
            {
                Debug.LogError($"[DDrive] {dataType.Name} is not a creatable AssetDataBase type.");
                return null;
            }

            if (!AssetNamingService.IsValidIdentifier(identifier))
            {
                Debug.LogError($"[DDrive] Identifier '{identifier}' must be PascalCase alphanumeric (e.g. PlayerSlash).");
                return null;
            }

            // カテゴリはフォルダ階層にも反映する(例: Audio/SE/Player/)。[01_architecture.md] §5
            var folder = $"{gameDataRoot}/{AssetNamingService.GetTargetFolder(assetType, category)}";
            EnsureFolder(folder);

            // カタログ用フォルダも先に作る。CreateAsset の後に CreateFolder を呼ぶと、その Refresh で
            // 作りたてのアセットが再インポートされ、メモリ上の Id と dirty がディスク値(Id=0)で
            // 上書きされることがある(1 回おきに再現)。
            EnsureFolder($"{gameDataRoot}/Catalogs");

            var fileName = AssetNamingService.BuildFileName(assetType, category, identifier);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}.asset");

            var asset = (AssetDataBase)ScriptableObject.CreateInstance(dataType);
            asset.DisplayName = string.IsNullOrEmpty(displayName) ? identifier : displayName;
            asset.Category = category;
            configure?.Invoke(asset);

            AssetDatabase.CreateAsset(asset, path);

            var guid = AssetDatabase.AssetPathToGUID(path);
            asset.Id = AssetIdGenerator.StableHashFromGuid(guid);
            EditorUtility.SetDirty(asset);
            // Id は他の AssetDatabase 操作(カタログ作成等)より前にディスクへ確定させる(上記の再インポート対策)。
            AssetDatabase.SaveAssetIfDirty(asset);

            // Address は必ず「実際に作られたファイル名」から取る。同名衝突時に
            // GenerateUniqueAssetPath が "〜 1.asset" 等へリネームするため、
            // 元の fileName を使うと既存アセットの Address と重複してカタログが壊れる。
            var finalAddress = System.IO.Path.GetFileNameWithoutExtension(path);
            RegisterToCatalog(asset, assetType, finalAddress, gameDataRoot);

            AssetDatabase.SaveAssets();
            return asset;
        }

        // 種別→カタログファイルの対応([01_architecture.md] §5: AudioCatalog は SE/BGM を束ねる)。
        public static string GetCatalogName(AssetType type) => type switch
        {
            AssetType.Se or AssetType.Bgm => "AudioCatalog",
            AssetType.Vfx => "VfxCatalog",
            AssetType.Anim or AssetType.Anim2D => "AnimCatalog",
            AssetType.Material or AssetType.Texture => "MaterialCatalog",
            AssetType.Canvas => "CanvasCatalog",
            AssetType.Prefab => "PrefabCatalog",
            AssetType.Model => "ModelCatalog",
            AssetType.Presentation => "PresentationCatalog",
            AssetType.Shake or AssetType.Haptics => "CameraFxCatalog",
            AssetType.UiTween => "UiTweenCatalog",
            _ => "MiscCatalog",
        };

        private static void RegisterToCatalog(AssetDataBase asset, AssetType assetType, string address, string gameDataRoot)
        {
            var catalogFolder = $"{gameDataRoot}/Catalogs";
            EnsureFolder(catalogFolder);

            var catalogPath = $"{catalogFolder}/{GetCatalogName(assetType)}.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<AssetCatalog>();
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }

            catalog.AddOrUpdate(new CatalogEntry
            {
                Id = asset.Id,
                Type = assetType,
                Address = address,
                Flags = asset.Flags,
            });

            EditorUtility.SetDirty(catalog);
        }

        internal static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parts = folder.Split('/');
            var current = parts[0]; // "Assets"

            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
