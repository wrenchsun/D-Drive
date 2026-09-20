using System.Collections.Generic;

namespace DDrive.Editor.Migration
{
    // [42_distribution.md] §4.3 / §6 P-7(2026-09-20) — マイグレーション実行 1 回分の文脈。
    // ドライラン中かどうか(`IDataMigration`/`IProjectMigration` の実装が重い処理を避けるための目印。
    // Runner 自体はドライラン中に `Migrate` を一切呼ばないため、値の書き換えを防ぐための必須情報ではない。
    // 実装側で「ログだけ出して実際には触らない」等の追加の分岐をしたい場合のために公開する)と、
    // 適用内容の人が読めるログを持つ。
    public sealed class MigrationContext
    {
        public bool DryRun { get; }

        public IReadOnlyList<string> Log => _log;

        private readonly List<string> _log = new();

        public MigrationContext(bool dryRun)
        {
            DryRun = dryRun;
        }

        public void Note(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                _log.Add(message);
            }
        }
    }
}
