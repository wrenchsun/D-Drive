namespace DDrive.Editor.Compat
{
    // [42_distribution.md] §5.11(P-3) — ゴールデンファイルの置き場所を 1 箇所にまとめる
    // (`CompatSnapshotMenu`(書く側)と `Tests/Editor/Compat/*Tests.cs`(比較する側)の両方から参照する)。
    public static class CompatSnapshotPaths
    {
        // 2026-09-20(P-5): Assets/DDrive → Packages/com.ddrive.core への移設に伴い更新。
        public const string Root = "Packages/com.ddrive.core/Tests/Editor/Compat/Snapshots";

        public const string PublicApiFoundation = Root + "/public-api-DDrive.Foundation.txt";
        public const string PublicApiRuntime = Root + "/public-api-DDrive.Runtime.txt";
        public const string SerializedLayout = Root + "/serialized-layout.txt";
        public const string Enums = Root + "/enums.txt";
        public const string NetMessages = Root + "/net-messages.txt";
        public const string EditorContract = Root + "/editor-contract.txt";

        // 一時フィクスチャ依存のゴールデン(メニューでは更新できず、環境変数 DDRIVE_UPDATE_COMPAT_SNAPSHOTS=1
        // でテストを再実行して更新する。CompatSnapshotMenu の doc コメント参照)。
        public const string TuningCodegen = Root + "/tuning-codegen.golden.cs";
        public const string ValidatorSeverity = Root + "/validator-severity.txt";
    }
}
