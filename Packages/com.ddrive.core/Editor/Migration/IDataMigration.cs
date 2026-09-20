using DDrive.Foundation.Data;

namespace DDrive.Editor.Migration
{
    // [42_distribution.md] §4.3 / §6 P-7(2026-09-20) — マイグレーションの実装契約(Data 単位)。
    //
    // `IValidator`(Foundation/Validation)と同じく、実装するだけで `DDriveMigrationRunner` が
    // `TypeCache` で自動発見する(登録リスト無し)。実装のルール:
    //   - `Migrate` は値の変換だけを行う。`data.SchemaVersion` の書き込みは Runner がまとめて行うため
    //     ここでは書かない(複数の Migration が同じ Data に連鎖適用されるときに Runner 側で一度だけ
    //     `ToSchema` を書く設計のため)。
    //   - 旧フィールドは消さない([42_distribution.md] §5.1・§4.3「書き方の制約」)。次の MAJOR まで
    //     `[HideInInspector] [Obsolete]` で残す。
    //   - `AppliesTo` は「このマイグレーションが対象とする Data か」を判定する(型で絞る、特定の
    //     フィールドが未設定かどうかで絞る 等)。`Migrate` を呼ぶ前に Runner が
    //     `data.SchemaVersion < ToSchema && AppliesTo(data)` を満たすものだけを選ぶ。
    public interface IDataMigration
    {
        // CHANGELOG・移行ガイド([docs/migrations/vN.md])で参照する安定した識別子。
        // 一度公開したら変えない(DDriveProjectSettings.AppliedMigrationIds に記録されるため)。
        string Id { get; }

        // 対象にする Data の現在の SchemaVersion(この値未満は対象外)。
        int FromSchema { get; }

        // 適用後に Data へ書き込む SchemaVersion(通常は DDriveSchema.Current)。
        int ToSchema { get; }

        bool AppliesTo(AssetDataBase data);

        void Migrate(AssetDataBase data, MigrationContext context);
    }
}
