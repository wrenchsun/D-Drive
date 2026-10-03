using System;

namespace DDrive.Editor.Settings
{
    // [42_distribution.md] §4.2 P-15(2026-10-03) — 更新ウィンドウが「管理対象」として登録した D-Drive 以外の
    // git URL パッケージ 1 件分の設定(`DDriveProjectSettings.ManagedPackages` の要素)。
    // D-Drive 自身は従来の単数フィールド(`LastAppliedVersion` / `PreviousPackageRef`)をそのまま使うため
    // ここには入らない。シリアライズ形式は追加のみ(フィールドの削除・型変更は MAJOR、[42] §5.1)。
    [Serializable]
    public sealed class ManagedPackageEntry
    {
        // UPM のパッケージ ID(package.json の name。manifest の dependencies のキーと同じ)。
        public string PackageId = string.Empty;

        // 版を上げる直前の manifest 値(元に戻す用。`PreviousPackageRef` と同じ意味)。
        public string PreviousRef = string.Empty;

        // 更新後の確認(Validation > Run All 等)を済ませたと記録した版。
        public string LastAppliedVersion = string.Empty;
    }
}
