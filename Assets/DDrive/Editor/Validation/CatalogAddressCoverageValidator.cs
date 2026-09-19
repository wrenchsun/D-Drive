using System;
using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using UnityEditor;

namespace DDrive.Editor.Validation
{
    // [29_network_device_test.md] §18(2026-09-18) — AddressablesRegistrationValidator の検出漏れの修正。
    //
    // AddressablesRegistrationValidator は「渡された Data(.asset)自身が、カタログの Address と同じ address で
    // Addressables に登録されているか」を Data 単位で見る。これは ValidatorRegistry.RunAll が
    // 「プロジェクト内に実在する Data アセット」を列挙して 1 件ずつ Validate() に渡す作りなので、
    // 対象の Data ファイルが実在する限りは有効に働く(実際、§18 の事故は当時の Run All 実行で検出できていた)。
    //
    // しかし §18 の実際の経緯は「VFX_Player_Slash を一旦削除 → Slash2 で置き換え → git で Slash の削除だけを
    // discard して復元」というもので、**Data(.asset)はファイルとして復元されても、別ファイルである
    // Addressables のグループ登録(Assets/AddressableAssetsData/AssetGroups/*.asset)は git 操作に追従しない**。
    // 今回はたまたま Data ファイルが復元されていたため上記の per-Data チェックで捕まったが、もし Data
    // ファイルの復元自体が漏れていたら(=プロジェクトにその .asset が存在しなければ)、RunAll の列挙に
    // 一度も乗らず Validate() が呼ばれないため、per-Data のチェックは何も報告できない。
    //
    // この Validator は逆方向(カタログ起点)で「カタログの Address が Addressables のどのエントリにも
    // 存在しない」ことを独立に確認する。対象の Data が(ValidationContext.AllAssets に)見つかれば
    // FixAction で登録し直せるが、見つからない場合は「Data 自体が無い」ことをメッセージで明示するだけに留める
    // (存在しないものを自動生成はしない)。
    //
    // ContentHashCatalogCoverageValidator(カタログ自身が Addressables に登録されているか)と同じ実装パターン
    // (IUniversalValidator + ValidationContext ごとに 1 回だけプロジェクト全体を走査するガード)を使う。
    public sealed class CatalogAddressCoverageValidator : IUniversalValidator
    {
        private static ValidationContext _lastRunContext;

        // テスト用 GameData(…/Tests/…)も対象にする(通常の Run All では除外。他の Validator と同じ規約)。
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
                yield break; // 設定なし自体は AddressablesRegistrationValidator が別途 Error を出す(重複報告しない)。
            }

            foreach (var catalog in AddressablesSync.FindCatalogs(IncludeTestFolders))
            {
                if (catalog == null)
                {
                    continue;
                }

                var entries = catalog.Entries;
                for (var i = 0; i < entries.Count; i++)
                {
                    var address = entries[i].Address;
                    if (string.IsNullOrEmpty(address) || AddressablesSync.FindEntryByAddress(address) != null)
                    {
                        continue;
                    }

                    var id = entries[i].Id;
                    var target = FindAssetById(ctx, id);
                    var reason = target != null
                        ? $"Data '{target.name}' は存在しますが Addressables への登録が無いか古いため"
                        : "該当する Data アセットも見つからないため(削除されたか、復元漏れの可能性があります)";
                    Action fixAction = target != null ? () => Fix(target, address) : null;

                    yield return ValidationResult.Error(
                        $"カタログ Address 未実在: '{catalog.name}' の Address '{address}'(ID 0x{id:X})が Addressables のどのエントリにも見つかりません。"
                        + $"{reason}、実行時にロードできません(git で Data(.asset)を復元しても、別ファイルである Addressables のグループ登録は自動で追従しません)。",
                        fixAction);
                }
            }
        }

        private static AssetDataBase FindAssetById(ValidationContext ctx, ulong id)
        {
            var all = ctx.AllAssets;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].Id == id)
                {
                    return all[i];
                }
            }

            return null;
        }

        private static void Fix(AssetDataBase data, string address)
        {
            AddressablesSync.EnsureEntry(data, address);
            // [44_review_2026-09-19.md] P1-1: Addressables 同期の FixAction は「一括処理」なので版数を進めない。
            DDriveAssetSave.SaveAllSuppressed();
        }
    }
}
