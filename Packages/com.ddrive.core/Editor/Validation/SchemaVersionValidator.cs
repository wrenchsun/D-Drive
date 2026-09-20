using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;

namespace DDrive.Editor.Validation
{
    // [42_distribution.md] §4.3/§4.6/§6 P-7(2026-09-20) — 「SchemaVersion が古い」ことを知らせる検査。
    // §5.8 の 2 段階ルールに従い Warning とする(更新直後に Error を増やさない。持ち込み先の CI が
    // 突然落ちるのを避ける)。
    // [47_review_p_tickets_2026-09-20.md] P1-5(2026-09-20 修正) — 以前は「マイグレーション(適用)」が
    // 対象の IDataMigration を 1 つも持たないと何もせず、Warning が解消不能なまま残っていた
    // (`IDataMigration` の実装が 0 件の間、この Warning は永久に消えない状態だった)。
    // `DDriveMigrationRunner.Apply` が「適用すべき IDataMigration が無くても SchemaVersion < Current の
    // Data を Current まで引き上げる」スキーマ版の刻印段を持つようになったため、この Validator が案内する
    // 操作(Tools > D-Drive > Update > マイグレーション(適用)、または更新ウィンドウの「更新を適用」)を
    // 実行すれば必ず解消する。この Validator 自体は何も書き換えない(FixAction は付けない。§5.8)。
    public sealed class SchemaVersionValidator : IUniversalValidator
    {
        public AssetType Target => AssetType.None;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            // [47_review_p_tickets_2026-09-20.md] P2-5(2026-09-20) — ValidatorRegistry.RunAll は
            // Data が 0 件のプロジェクトでも IUniversalValidator を data=null で 1 回呼ぶようになった。
            // このバリデータは特定の Data を対象にする(プロジェクト全体の一括検査ではない)ため、
            // data が無ければ何も報告しない。
            if (data == null)
            {
                yield break;
            }

            if (data.SchemaVersion < DDriveSchema.Current)
            {
                yield return ValidationResult.Warning(
                    $"データのスキーマ版が古い形式です(SchemaVersion={data.SchemaVersion} < {DDriveSchema.Current})。" +
                    "Tools > D-Drive > Update > マイグレーション(適用) を実行すると解消します。",
                    code: "DD-SCHEMA-OUTDATED");
            }
        }
    }
}
