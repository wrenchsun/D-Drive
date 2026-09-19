using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Versioning;
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

            // 2026-09-12: Ui.Open(CanvasData) / ApplyLayerDefaults(ControlSkinData)は AssetRegistry の
            // 同期解決(ResolveOrPlaceholder/TryResolveSync)しか使わず、これは Flags.Load=Preload でカタログ
            // 登録時にロード済みのものしか引けない(LazyLoad の「初回参照時にロード」は非同期経路専用)。
            // 配線が正しくても Preload を忘れると常に Placeholder になり原因が分かりにくいため、ここで検出する。
            // 2026-09-17(U-20、[39_usability_fixes_2026-09-17.md]): Anim.Play/Anim2D.Play(ID 版)も
            // AnimManager.Play → ResolveOrPlaceholder<AnimData> でしか解決しないため同じ穴だった
            // (`AssetCreationService.cs` の既定 Preload 化とあわせて追加。既存アセットはここで検出・修正する)。
            // 2026-09-17: 対象種別の判定は `AssetCreationService.NeedsPreloadDefault` に集約した(このメソッドが
            // 独自に判定を持つと、種別追加時に片方だけ更新して漏れる事故が起きる。実際に Presentation/Shake/Haptics
            // が Create() 側にだけ追加されここに反映されておらず、既存アセットの検出漏れが発生していた)。
            var resolvedType = ResolveType(data);
            if (AssetCreationService.NeedsPreloadDefault(resolvedType) && data.Flags.Load != LoadMode.Preload)
            {
                var captured = data;
                yield return ValidationResult.Error(
                    $"Flags.Load が Preload ではありません: '{data.name}'({resolvedType})は Ui.Open 等の同期解決でしか引かれないため、Preload 以外だと実行時に常に Placeholder になります。",
                    () => FixPreload(captured));
            }
        }

        private static void Fix(AssetDataBase data, string address)
        {
            AddressablesSync.EnsureEntry(data, address);
            // [44_review_2026-09-19.md] P1-1: Addressables 同期の FixAction は「一括処理」なので版数を進めない。
            DDriveAssetSave.SaveAllSuppressed();
        }

        private static void FixPreload(AssetDataBase data)
        {
            Undo.RecordObject(data, "Set Flags.Load = Preload");
            var flags = data.Flags;
            flags.Load = LoadMode.Preload;
            data.Flags = flags;
            EditorUtility.SetDirty(data);
            // [44_review_2026-09-19.md] P1-1: data 自身のフィールドを直す fix なので、対象 1 個だけ保存する
            // (版数は通常どおり進む。他は AssetIdGenerator の Fix() と違い data 自体の内容修正のため)。
            DDriveAssetSave.SaveDirty(data);
        }

        private static AssetType ResolveType(AssetDataBase data)
        {
            var attr = data.GetType().GetCustomAttribute<AssetIdDefinitionAttribute>();
            return attr?.Type ?? AssetType.None;
        }
    }
}
