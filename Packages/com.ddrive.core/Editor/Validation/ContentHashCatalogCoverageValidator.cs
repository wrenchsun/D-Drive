using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using UnityEditor;

namespace DDrive.Editor.Validation
{
    // [14_networking.md] §10(6-5) — 「ContentHash 生成対象外のカタログ」→ Error(CI)。
    //
    // ContentHash(CatalogContentHasher)は CatalogEntry の構造的フィールド(Id/Type/Address/NetMode)のみを
    // 対象にし、種別ごとの登録リストを持たない(新しい AssetType を追加しても生成器側の対応は不要。
    // [14] §7 実装メモ参照)。そのため「種別が生成器に未登録」というドキュメント記載の失敗モードは、
    // この実装では構造的に発生しない。残る現実的なリスクは「カタログ自体がプロジェクトに存在するのに、
    // 実行時に一度も Registry へ登録されない(=誰にも読み込まれず、ContentHash にも反映されない)」ケース
    // であり、これは DDriveRuntimeBootstrap が起動時にカタログを集める唯一の経路(Addressables ラベル
    // 'DDriveCatalog')に registered かどうかで判定できる(AddressablesRegistrationValidator の
    // 「Data がカタログ/Addressables に登録されているか」と同じ考え方を、Data ではなくカタログ自身に適用する)。
    //
    // AssetCatalog は AssetDataBase を継承しないため ValidatorRegistry.RunAll(AssetDataBase だけを列挙)には
    // 自然には乗らない。IUniversalValidator として登録しつつ、渡された data は無視し、ValidationContext
    // (RunAll 1 回につき新しいインスタンス)ごとに 1 回だけ全カタログを走査する
    // (AddressablesRegistrationValidator._noSettingsReported と同じガード手法)。
    public sealed class ContentHashCatalogCoverageValidator : IUniversalValidator
    {
        private static ValidationContext _lastRunContext;

        // テスト用 GameData(…/Tests/…)も対象にする(通常の Run All では除外。AddressablesRegistrationValidator と同じ規約)。
        public bool IncludeTestFolders { get; set; }

        public AssetType Target => AssetType.None;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (_lastRunContext == ctx)
            {
                yield break;
            }

            _lastRunContext = ctx;

            if (!AddressablesSync.IsAvailable)
            {
                yield break; // Addressables 未設定は AddressablesRegistrationValidator が別途 Error を出す(重複報告しない)。
            }

            foreach (var catalog in AddressablesSync.FindCatalogs(IncludeTestFolders))
            {
                if (catalog == null)
                {
                    continue;
                }

                var entry = AddressablesSync.FindEntry(catalog);
                if (entry == null)
                {
                    var captured = catalog;
                    yield return ValidationResult.Error(
                        $"ContentHash 生成対象外: カタログ '{catalog.name}' が Addressables に未登録です。起動時に Registry へ登録されず、ContentHash(接続時照合)にも反映されません。",
                        () => Fix(captured));
                    continue;
                }

                if (!entry.labels.Contains(AddressablesSync.CatalogLabel))
                {
                    var captured = catalog;
                    yield return ValidationResult.Error(
                        $"ContentHash 生成対象外: カタログ '{catalog.name}' に '{AddressablesSync.CatalogLabel}' ラベルが付いていません。起動オブジェクト(DDriveRuntimeBootstrap)がラベル経由で集められないため、実行時に登録されず ContentHash にも反映されません。",
                        () => Fix(captured));
                }
            }
        }

        private static void Fix(AssetCatalog catalog)
        {
            AddressablesSync.EnsureCatalogEntry(catalog);
            // [44_review_2026-09-19.md] P1-1: Addressables 同期の FixAction は「一括処理」なので版数を進めない。
            DDriveAssetSave.SaveAllSuppressed();
        }
    }
}
