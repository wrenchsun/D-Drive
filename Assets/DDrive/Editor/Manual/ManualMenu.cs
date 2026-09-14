using DDrive.Editor.Menu;
using UnityEditor;

namespace DDrive.Editor.Manual
{
    // [09_editor_tools.md] §6.1 — メインツールバーのボタンと同じ処理をメニューからも呼べるようにする。
    // メニューパスは DDriveMenu 経由(CLAUDE.md §0-6)。既存の「Asset Browser」「Presentation Editor」と
    // 同じく Root 直下の単発アクションのため、専用の定数は追加していない。
    public static class ManualMenu
    {
        [MenuItem(DDriveMenu.Root + "マニュアルを開く")]
        private static void OpenManual() => ManualLauncher.OpenTop();
    }
}
