namespace DDrive.Foundation.Data
{
    // [42_distribution.md] §4.3 / §7 A-4(P-7、2026-09-20) — Data のシリアライズ形式の「スキーマ版」。
    //
    // `AssetDataBase.Version`(保存回数)とは別物: こちらは「今の D-Drive コードが期待するデータ形式の
    // 版」を表す単調増加の整数で、値そのものに意味を持たせない(比較にしか使わない)。
    // `VersionStampProcessor` が保存の都度 `AssetDataBase.SchemaVersion` にこの値を書き込む。
    // `IDataMigration.ToSchema` が新しい形式へ引き上げるときの目標値としてもこの値を使う。
    //
    // 上げてよいタイミングは「実際にシリアライズ形式が変わり、マイグレーション(IDataMigration)が
    // 要るとき」だけ([../../docs/42_distribution.md] §5.1「SchemaVersion の意味変更: 禁止(単調増加のみ)」)。
    // 既存 .asset は(このフィールド追加時点では)0 で読まれ、「1.0.0 以前の形式」を意味する。
    public static class DDriveSchema
    {
        public const int Current = 1;
    }
}
