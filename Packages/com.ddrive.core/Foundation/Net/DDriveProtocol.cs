namespace DDrive.Foundation.Net
{
    // [42_distribution.md] §5.6 / §6 P-8(2026-09-20) — ネットメッセージ形式(ワイヤフォーマット)の版。
    // `CatalogContentHashMsg.ProtocolVersion` が Host/Client 間の照合に使う。`DDriveVersion.Value`
    // (パッケージの SemVer)とは別物: パッケージの MINOR/PATCH が上がってもメッセージ形式そのものが
    // 変わらなければ ProtocolVersion は不変(互換)。上げるのは [42_distribution.md] §5.6 の
    // ネットメッセージ互換を破る MAJOR 変更(struct の改名・フィールド削除・型変更・直列化方式変更)の
    // ときだけ(§5.12 の破壊的変更手続きに従う)。
    public static class DDriveProtocol
    {
        public const int Current = 1;
    }
}
