using System;
using DDrive.Editor.Codegen;
using DDrive.Editor.Inspector;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.AssetBrowser
{
    // 「新規作成 = 意味情報の入力だけ」を実現する作成パイプライン(FR-1.5)。
    // ファイル名生成 → アセット作成 → GUID 由来の安定 ID 発行 → カタログ登録 → Addressables 登録 までを 1 回で行う
    // (2026-09-09: 実行時ローダーは Addressables のみを引くため、カタログの Address と同じ address で
    // AddressablesSync がグループへ登録する。カタログ自体もラベル付きで登録し、起動時にラベルから集められる)。
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

            // Canvas/ControlSkin は Ui.Open / ApplyLayerDefaults が同期解決(ResolveOrPlaceholder/TryResolveSync)
            // でしか引かないため、LazyLoad(既定)のままだと「初回参照時にロード」が起きず常に Placeholder になる。
            // 2026-09-12: 実際にこの理由で配線済みの CanvasData が動かない不具合を確認したため、既定を Preload にする。
            // 2026-09-14(5-1): Presentation も PresentationManager.Play が同じく同期解決のみで引くため、
            // 同じ理由で追加(剣攻撃デモ PRES_Demo_SkillSlash が Placeholder になる不具合で発見)。
            // 2026-09-14(5-2/5-2b): CameraFxManager.Shake / HapticsManager.Play も ResolveOrPlaceholder の
            // みで同期解決するため、同じ理由で Shake / Haptics を追加(要判断: [16_camera_haptics.md] 実装メモ参照)。
            if (assetType == AssetType.Canvas || assetType == AssetType.ControlSkin || assetType == AssetType.Presentation
                || assetType == AssetType.Shake || assetType == AssetType.Haptics)
            {
                var flags = asset.Flags;
                flags.Load = LoadMode.Preload;
                asset.Flags = flags;
            }

            configure?.Invoke(asset);

            // 6-3(2026-09-15): 新規作成は v1・作成者・作成日時で記録する。CreateAsset 直後のアセットは dirty に
            // ならず保存フック(VersionStampProcessor.OnWillSaveAssets)では 0→1 にならないため、ここで付ける。
            DDrive.Editor.Versioning.VersionStampProcessor.StampNew(asset);

            AssetDatabase.CreateAsset(asset, path);
            AssetSearch.Invalidate(); // 同じフレームで続けて検索する呼び出し元(Maya インポート等)が作りたてを見落とさないように([09] §9)

            var guid = AssetDatabase.AssetPathToGUID(path);
            asset.Id = AssetIdGenerator.StableHashFromGuid(guid);
            EditorUtility.SetDirty(asset);
            // Id は他の AssetDatabase 操作(カタログ作成等)より前にディスクへ確定させる(上記の再インポート対策)。
            AssetDatabase.SaveAssetIfDirty(asset);

            // Address は必ず「実際に作られたファイル名」から取る。同名衝突時に
            // GenerateUniqueAssetPath が "〜 1.asset" 等へリネームするため、
            // 元の fileName を使うと既存アセットの Address と重複してカタログが壊れる。
            var finalAddress = System.IO.Path.GetFileNameWithoutExtension(path);
            var catalog = RegisterToCatalog(asset, assetType, finalAddress, gameDataRoot);
            AddressablesSync.EnsureEntry(asset, finalAddress);
            AddressablesSync.EnsureCatalogEntry(catalog);

            AssetDatabase.SaveAssets();

            // 初期アイコン: 元アセット(Prefab / Texture / Sprite)が configure で入っているか、描画で表現できる種別(Material)なら
            // 自動で作る([09] §8.1、2026-09-11)。GUI の外(delayCall)で行う(Camera.Render / AssetPreview の都合)。
            if (asset.Icon == null && AssetIconService.CanCreateDefaultIcon(asset))
            {
                var created = asset;
                EditorApplication.delayCall += () =>
                {
                    if (created != null && created.Icon == null)
                    {
                        // 作成自体が Undo 対象でないので、ここで Undo を積まない(積むと Ctrl+Z が
                        // 「アイコン割り当て」だけを取り消してアセットが残る。2026-09-11 レビュー対応)。
                        // [11_tasks.md] 6-3: 作成直後の機械的なアイコン自動割り当てで Version が
                        // 1→2 に上がってしまわないよう抑止する(この Save は次のフレームに走るため、
                        // Create 本体を包むより内側でここだけ包む)。
                        using (VersionStampSuppression.Scope())
                        {
                            AssetIconService.TryCreateDefaultIcon(created, recordUndo: false);
                        }
                    }
                };
            }

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
            AssetType.Anchor or AssetType.AnchorGroup => "AnchorCatalog",
            AssetType.ControlSkin => "UiCatalog",
            _ => "MiscCatalog",
        };

        // 既存アセットのカタログ登録漏れを直す(Validation の FixAction 用)。address はファイル名(拡張子なし)。
        // [11_tasks.md] 6-3: カタログ・Addressables 同期の一種なので、機械的な登録直しで Version を上げない。
        public static AssetCatalog RegisterExisting(AssetDataBase asset, AssetType assetType, string gameDataRoot = DefaultGameDataRoot)
        {
            var path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path) || asset.Id == 0)
            {
                return null;
            }

            using (VersionStampSuppression.Scope())
            {
                var address = System.IO.Path.GetFileNameWithoutExtension(path);
                var catalog = RegisterToCatalog(asset, assetType, address, gameDataRoot);
                AddressablesSync.EnsureEntry(asset, address);
                AddressablesSync.EnsureCatalogEntry(catalog);
                AssetDatabase.SaveAssets();
                return catalog;
            }
        }

        private static AssetCatalog RegisterToCatalog(AssetDataBase asset, AssetType assetType, string address, string gameDataRoot)
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
            return catalog;
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
