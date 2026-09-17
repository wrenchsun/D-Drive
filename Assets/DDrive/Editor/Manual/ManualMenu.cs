using DDrive.Editor.Menu;
using UnityEditor;

namespace DDrive.Editor.Manual
{
    // [09_editor_tools.md] §6.1 — メインツールバーのボタンと同じ処理をメニューからも呼べるようにする。
    // メニューパスは DDriveMenu 経由(CLAUDE.md §0-6)。既存の「Asset Browser」「Presentation Editor」と
    // 同じく Root 直下の単発アクションのため、専用の定数は追加していない。
    //
    // プログラマーマニュアル(2026-09-17 追加)も同じ流儀で Root 直下に置く。デザイナーマニュアルと
    // 同じ Web/ローカルの分岐(ManualLauncher.OpenProgrammerTop、ManualPrefs.PreferWeb 共有)。
    public static class ManualMenu
    {
        [MenuItem(DDriveMenu.Root + "マニュアルを開く")]
        private static void OpenManual() => ManualLauncher.OpenTop();

        [MenuItem(DDriveMenu.Root + "プログラマーマニュアルを開く")]
        private static void OpenProgrammerManual() => ManualLauncher.OpenProgrammerTop();
    }
}
