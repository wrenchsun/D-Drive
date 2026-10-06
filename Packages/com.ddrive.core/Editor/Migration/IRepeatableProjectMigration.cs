namespace DDrive.Editor.Migration
{
    // [64_review_m6_2026-10-06.md] GF-R-01(c) / [42_distribution.md] §4.3 — 「Id が記録済みでも、対象が残っていれば再度未適用として扱う」
    // プロジェクトマイグレーション用の任意の拡張。`IProjectMigration` にメンバーを足すと既存の実装(持ち込み先を含む)が壊れるため、
    // 別のインターフェースにした(追加のみ)。`DDriveMigrationRunner.Plan` は、Id が記録済みの `IProjectMigration` がこれも実装していて
    // `HasPendingWork()` が true なら計画に入れる。実装は冪等で、対象が無いときは軽く(ファイルの走査程度で)false を返すこと。
    public interface IRepeatableProjectMigration
    {
        bool HasPendingWork();
    }
}
