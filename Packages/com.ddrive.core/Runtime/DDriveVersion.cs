namespace DDrive.Runtime
{
    // [42_distribution.md] §4.1 / §5.11-9(P-3、2026-09-20) — パッケージ版のランタイムからの読み出し口。
    //
    // 正は package.json の "version"(P-5 でパッケージ化するまでは存在しない)。P-5 以降は
    // `UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DDriveVersion).Assembly).version`
    // と本定数が一致することを PackageVersionConsistencyTests が固定する。CHANGELOG.md の最新見出しとも
    // (プレリリースサフィックスを除いた MAJOR.MINOR.PATCH で)一致させる。
    //
    // 2026-09-20(P-5、[42_distribution.md] §4.1/§6): パッケージ化して 1.0.0 を発効したため
    // "-dev" サフィックスを外した。package.json の "version" と一致することを
    // PackageVersionConsistencyTests(Tests/Editor/Compat)が固定する。
    public static class DDriveVersion
    {
        public const string Value = "1.1.0";
    }
}
