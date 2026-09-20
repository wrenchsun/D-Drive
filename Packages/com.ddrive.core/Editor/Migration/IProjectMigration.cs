namespace DDrive.Editor.Migration
{
    // [42_distribution.md] §4.3 / §6 P-7(2026-09-20) — Data(AssetDataBase)を持たない SO
    // (AssetCatalog・TuningTable・UiLayerSettings 等)への一括処理用マイグレーション。
    //
    // `IDataMigration` と違い対象ごとの `SchemaVersion` が無いため、二重適用の防止は
    // `DDriveProjectSettings.AppliedMigrationIds` に `Id` が記録済みかどうかで判定する
    // (`DDriveMigrationRunner.Plan` が判定し、適用後に `MarkMigrationApplied` で記録する)。
    public interface IProjectMigration
    {
        // CHANGELOG・移行ガイドで参照する安定した識別子。一度公開したら変えない。
        string Id { get; }

        void Migrate(MigrationContext context);
    }
}
