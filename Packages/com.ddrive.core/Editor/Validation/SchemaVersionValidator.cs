using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;

namespace DDrive.Editor.Validation
{
    // [42_distribution.md] §4.3/§4.6/§6 P-7(2026-09-20) — 「SchemaVersion が古い」ことを知らせる検査。
    // §5.8 の 2 段階ルールに従い Warning とする(更新直後に Error を増やさない。持ち込み先の CI が
    // 突然落ちるのを避ける)。実際に値を直すのは Tools > D-Drive > Update > マイグレーション(適用)
    // (DDriveMigrationRunner)で、この Validator 自体は何も書き換えない(FixAction は付けない。
    // 適用対象のマイグレーションが無ければ何も起きないため、誤って「直る」ことを期待させないため)。
    public sealed class SchemaVersionValidator : IUniversalValidator
    {
        public AssetType Target => AssetType.None;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data.SchemaVersion < DDriveSchema.Current)
            {
                yield return ValidationResult.Warning(
                    $"データのスキーマ版が古い形式です(SchemaVersion={data.SchemaVersion} < {DDriveSchema.Current})。" +
                    "Tools > D-Drive > Update > マイグレーション(ドライラン/適用) で確認・更新してください。",
                    code: "DD-SCHEMA-OUTDATED");
            }
        }
    }
}
