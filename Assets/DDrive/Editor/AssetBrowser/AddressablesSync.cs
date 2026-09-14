using System.Collections.Generic;
using DDrive.Editor.Menu;
using DDrive.Foundation.Data;
using DDrive.Foundation.Registry;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace DDrive.Editor.AssetBrowser
{
    // [02_core_framework.md] §5 / [09] §1(2026-09-09) — カタログの Address と Addressables のエントリを一致させる。
    // 実行時ローダー(AddressablesAssetLoader)は Addressables しか引かないため、「カタログにある = Addressables に
    // 同じ address で登録済み」を作成時(AssetCreationService)・同期メニュー・Validation(FixAction)の 3 箇所で保証する。
    //
    // - Data アセット: グループ "DDrive_GameData" に address = カタログの Address(ファイル名)で登録
    // - カタログ: グループ "DDrive_Catalogs" にラベル "DDriveCatalog" 付きで登録(起動オブジェクトがラベルから集める)
    // - 既に別グループにあるエントリは移動しない(グループ運用は人が決めてよい)。address だけ揃える
    public static class AddressablesSync
    {
        public const string GroupName = "DDrive_GameData";
        public const string CatalogGroupName = "DDrive_Catalogs";
        public const string CatalogLabel = DDrive.Runtime.Loop.DDriveRuntimeBootstrap.DefaultCatalogLabel;

        private static bool _warnedNoSettings;

        public static bool IsAvailable => AddressableAssetSettingsDefaultObject.SettingsExists;

        public static AddressableAssetEntry FindEntry(Object asset)
        {
            if (!IsAvailable || asset == null)
            {
                return null;
            }

            var path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? null : AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path));
        }

        // Data アセットをカタログと同じ address で登録する。設定が無ければ警告(1 回)して null。
        public static AddressableAssetEntry EnsureEntry(AssetDataBase asset, string address) => EnsureEntry(asset, address, GroupName, null);

        public static AddressableAssetEntry EnsureCatalogEntry(AssetCatalog catalog)
            => catalog != null ? EnsureEntry(catalog, catalog.name, CatalogGroupName, CatalogLabel) : null;

        public static bool RemoveEntry(Object asset)
        {
            if (!IsAvailable || asset == null)
            {
                return false;
            }

            var path = AssetDatabase.GetAssetPath(asset);
            return !string.IsNullOrEmpty(path) && AddressableAssetSettingsDefaultObject.Settings.RemoveAssetEntry(AssetDatabase.AssetPathToGUID(path), false);
        }

        // フォルダ配下(テスト用の一時 GameData 等)のエントリをまとめて外す。
        // 2026-09-14: AssetSearch(キャッシュ付き)経由だと、フォルダ内で新規作成したばかりのアセット
        // (カタログ等)がまだキャッシュに反映されておらず取り漏らす可能性がある(理論上。ImportWatcher が
        // 同期的に無効化するはずだが、テストの後始末だけに使う経路なのでコストを気にせず確実性を優先する)。
        // ここだけ AssetDatabase.FindAssets を直接呼び、キャッシュの有無に依存しないようにする。
        public static int RemoveEntriesUnder(string folder)
        {
            if (!IsAvailable || string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                return 0;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var removed = 0;
            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
            {
                if (settings.RemoveAssetEntry(guid, false))
                {
                    removed++;
                }
            }

            return removed;
        }

        // ID → カタログの Address(プロジェクト内の全カタログから)。無ければ null。
        public static string FindCatalogAddress(ulong id, out AssetCatalog owner, bool includeTestFolders = false)
        {
            foreach (var catalog in FindCatalogs(includeTestFolders))
            {
                var entries = catalog.Entries;
                for (var i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Id == id)
                    {
                        owner = catalog;
                        return entries[i].Address;
                    }
                }
            }

            owner = null;
            return null;
        }

        public static List<AssetCatalog> FindCatalogs(bool includeTestFolders)
        {
            var result = new List<AssetCatalog>();
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(AssetCatalog)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!includeTestFolders && path.Contains("/Tests/"))
                {
                    continue;
                }

                var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>(path);
                if (catalog != null)
                {
                    result.Add(catalog);
                }
            }

            return result;
        }

        [MenuItem(DDriveMenu.Generate + "Addressables 登録を同期(カタログ → グループ)")]
        public static void SyncAllMenuItem() => SyncAll(log: true);

        // 全カタログと全 Data アセットについて Addressables 登録を揃える。戻り値: (直した Data 数, 登録したカタログ数, カタログ未登録の Data 数)。
        public static (int fixedAssets, int catalogs, int missingCatalog) SyncAll(bool log)
        {
            if (!IsAvailable)
            {
                WarnNoSettings();
                return (0, 0, 0);
            }

            var byId = new Dictionary<ulong, string>();
            var catalogs = 0;
            foreach (var catalog in FindCatalogs(includeTestFolders: false))
            {
                EnsureCatalogEntry(catalog);
                catalogs++;
                var entries = catalog.Entries;
                for (var i = 0; i < entries.Count; i++)
                {
                    byId[entries[i].Id] = entries[i].Address;
                }
            }

            var fixedAssets = 0;
            var missing = 0;
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(AssetDataBase)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Tests/"))
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(path);
                if (asset == null || asset.Id == 0)
                {
                    continue;
                }

                if (!byId.TryGetValue(asset.Id, out var address))
                {
                    missing++;
                    continue;
                }

                var entry = FindEntry(asset);
                if (entry == null || entry.address != address)
                {
                    EnsureEntry(asset, address);
                    fixedAssets++;
                }
            }

            AssetDatabase.SaveAssets();
            if (log)
            {
                Debug.Log($"[DDrive] Addressables 同期: Data {fixedAssets} 件を登録/修正、カタログ {catalogs} 件をラベル '{CatalogLabel}' で登録。カタログ未登録の Data {missing} 件(Validation > Run All で FixAction を実行してください)。");
            }

            return (fixedAssets, catalogs, missing);
        }

        private static AddressableAssetEntry EnsureEntry(Object asset, string address, string groupName, string label)
        {
            if (!IsAvailable)
            {
                WarnNoSettings();
                return null;
            }

            if (asset == null || string.IsNullOrEmpty(address))
            {
                return null;
            }

            var path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning($"[DDrive] '{asset.name}' はアセットとして保存されていないため Addressables に登録できません。");
                return null;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var guid = AssetDatabase.AssetPathToGUID(path);
            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
            {
                var group = settings.FindGroup(groupName);
                if (group == null)
                {
                    group = settings.CreateGroup(groupName, false, false, false, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
                }

                entry = settings.CreateOrMoveEntry(guid, group, false, false);
            }

            if (entry.address != address)
            {
                entry.SetAddress(address, false);
            }

            if (!string.IsNullOrEmpty(label))
            {
                if (!settings.GetLabels().Contains(label))
                {
                    settings.AddLabel(label, false);
                }

                entry.SetLabel(label, true, true, false);
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true, false);
            return entry;
        }

        private static void WarnNoSettings()
        {
            if (_warnedNoSettings)
            {
                return;
            }

            _warnedNoSettings = true;
            Debug.LogWarning("[DDrive] Addressables の設定がありません(Window > Asset Management > Addressables > Groups で作成)。作成した Data は実行時にロードできません。");
        }
    }
}
