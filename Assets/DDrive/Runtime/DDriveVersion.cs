namespace DDrive.Runtime
{
    // [42_distribution.md] §4.1 / §5.11-9(P-3、2026-09-20) — パッケージ版のランタイムからの読み出し口。
    //
    // 正は package.json の "version"(P-5 でパッケージ化するまでは存在しない)。P-5 以降は
    // `UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DDriveVersion).Assembly).version`
    // と本定数が一致することを PackageVersionConsistencyTests が固定する。CHANGELOG.md の最新見出しとも
    // (プレリリースサフィックスを除いた MAJOR.MINOR.PATCH で)一致させる。
    //
    // 値は「まだ 1.0.0 を正式発効していない(package.json 不在・パッケージ化前)」ことを示す
    // "-dev" サフィックス付きで開始する([42] §6 P-3 のチケット指示どおり)。P-5 でパッケージ化し
    // 1.0.0 を発効するタイミングでサフィックスを外す。
    public static class DDriveVersion
    {
        public const string Value = "1.0.0-dev";
    }
}
