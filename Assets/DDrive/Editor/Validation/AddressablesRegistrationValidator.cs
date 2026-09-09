using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using UnityEditor;

namespace DDrive.Editor.Validation
{
    // [02_core_framework.md] §11 共通検査「Addressable 未登録」(2026-09-09)。
    // 全 Data について「カタログにある」「Addressables に同じ address で登録されている」を Error にし、
    // FixAction で登録する(FR-1.5 / FR-11.1: 作成 → カタログ → Addressables を一つの導線にする)。
    public sealed class AddressablesRegistrationValidator : IUniversalValidator
    {
        private static ValidationContext _noSettingsReported;

        // テスト用 GameData(…/Tests/…)も対象にする(通常の Run All では除外)。
        public bool IncludeTestFolders { get; set; }

        public AssetType Target => AssetType.None;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data == null || data.Id == 0)
            {
                yield break; // ID 未発行は作成パイプライン / 別 Validator の責務
            }

            var path = AssetDatabase.GetAssetPath(data);
            if (string.IsNullOrEmpty(path) || (!IncludeTestFolders && path.Contains("/Tests/")))
            {
                yield break; // メモリ上のみ / テスト用フォルダは実行時ロード対象外
            }

            var address = AddressablesSync.FindCatalogAddress(data.Id, out _, IncludeTestFolders);
            if (address == null)
            {
                var type = ResolveType(data);
                var captured = data;
                yield return ValidationResult.Error(
                    $"カタログ未登録: '{data.name}'(ID 0x{data.Id:X})がどのカタログにもありません。実行時に解決できず Placeholder になります。",
                    () => AssetCreationService.RegisterExisting(captured, type));
                yield break;
            }

            if (!AddressablesSync.IsAvailable)
            {
                if (_noSettingsReported != ctx)
                {
                    _noSettingsReported = ctx;
                    yield return ValidationResult.Error("Addressables の設定(AddressableAssetSettings)がありません。Window > Asset Management > Addressables > Groups で作成してください。全 Data が実行時にロードできません。");
                }

                yield break;
            }

            var entry = AddressablesSync.FindEntry(data);
            if (entry == null)
            {
                var captured = data;
                yield return ValidationResult.Error(
                    $"Addressables 未登録: '{data.name}' がグループに入っていません(カタログの Address '{address}')。実行時にロードできません。",
                    () => Fix(captured, address));
            }
            else if (entry.address != address)
            {
                var captured = data;
                yield return ValidationResult.Error(
                    $"Address 不一致: '{data.name}' のカタログ '{address}' と Addressables '{entry.address}' が違います。実行時にロードできません。",
                    () => Fix(captured, address));
            }
        }

        private static void Fix(AssetDataBase data, string address)
        {
            AddressablesSync.EnsureEntry(data, address);
            AssetDatabase.SaveAssets();
        }

        private static AssetType ResolveType(AssetDataBase data)
        {
            var attr = data.GetType().GetCustomAttribute<AssetIdDefinitionAttribute>();
            return attr?.Type ?? AssetType.None;
        }
    }
}
