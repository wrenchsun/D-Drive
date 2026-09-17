using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace DDrive.Editor.Manual
{
    // [09_editor_tools.md] §6.1(2026-09-14) — メインツールバーの再生ボタン付近に「マニュアル」ボタンを置く。
    // Unity 6.3 の公式 API UnityEditor.Toolbars.[MainToolbarElement] を使う(旧 Toolbar への
    // リフレクション/内部 API 差し込みは行わない)。実シグネチャは isuzu-unity MCP の
    // reflect_find_type / execute_code で UnityEditor.CoreModule / EditorToolbarModule から確認済み:
    //   - MainToolbarButton(MainToolbarContent content, Action action)
    //   - MainToolbarDropdown(MainToolbarContent content, Action<Rect> openDropdown)
    //   - MainToolbarDockPosition { Left, Right, Middle }
    // 再生ボタン(UnityEditor.Toolbars.PlayModeButtons.Create)は path="Play Mode Controls"、
    // dockPosition=Middle、dockIndex=0 の唯一の Middle 要素だったため、ここでは
    // dockIndex=1 にして「再生ボタンの右隣」に置く(SubToolbarZone/PlayModeButtons と同じく
    // 1 メソッドが複数の MainToolbarElement を返す形を踏襲。新しい差し込み方式は増やしていない)。
    public static class ManualToolbarButtons
    {
        [MainToolbarElement("D-Drive/Manual", defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 1)]
        public static IEnumerable<MainToolbarElement> CreateManualElements()
        {
            yield return CreateOpenButton();
            yield return CreatePagesDropdown();
        }

        private static MainToolbarElement CreateOpenButton()
        {
            var icon = EditorGUIUtility.IconContent("_Help").image as Texture2D;
            // アイコンだけだと何のボタンか分かりにくいので文字も出す(MainToolbarContent(string, Texture2D, string) は実在確認済み)。
            var content = new MainToolbarContent("マニュアル", icon, "デザイナーマニュアルをブラウザで開く");
            return new MainToolbarButton(content, ManualLauncher.OpenTop);
        }

        private static MainToolbarElement CreatePagesDropdown()
        {
            var icon = EditorGUIUtility.IconContent("icon dropdown").image as Texture2D;
            var content = new MainToolbarContent(icon, "マニュアルのページ一覧");
            return new MainToolbarDropdown(content, OpenPagesMenu);
        }

        private static void OpenPagesMenu(Rect rect)
        {
            var menu = new GenericMenu();
            var projectRoot = ManualPages.GetProjectRoot();
            var pages = ManualPages.DiscoverPages(projectRoot);

            if (pages.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("(ページが見つかりません)"));
            }

            foreach (var page in pages)
            {
                var fileName = page.FileName;
                menu.AddItem(new GUIContent(page.DisplayName), false, () => ManualLauncher.OpenPage(fileName));
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Web 版を優先"), ManualPrefs.PreferWeb, () => ManualPrefs.PreferWeb = !ManualPrefs.PreferWeb);
            menu.AddItem(new GUIContent("ローカルのマニュアルを開く"), false, ManualLauncher.OpenLocalTop);

            menu.AddSeparator(string.Empty);
            AddProgrammerManualItems(menu, projectRoot);

            menu.DropDown(rect);
        }

        // プログラマーマニュアル(2026-09-17 追加)はデザイナーマニュアルと別のサブメニューにぶら下げる
        // (既存のデザイナーマニュアル側の項目は変更しない)。「Web 版を優先」トグル(ManualPrefs.PreferWeb)は
        // デザイナー/プログラマーで共有する単一の EditorPrefs のため、専用の項目は置かず
        // 上のデザイナー側の項目がそのまま両方に効く(ManualLauncher.OpenProgrammerPage)。
        private static void AddProgrammerManualItems(GenericMenu menu, string projectRoot)
        {
            const string SubMenuPrefix = "プログラマーマニュアル/";

            menu.AddItem(new GUIContent(SubMenuPrefix + "トップを開く"), false, ManualLauncher.OpenProgrammerTop);

            var pages = ManualPages.DiscoverPages(projectRoot, ManualKind.Programmer);
            if (pages.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent(SubMenuPrefix + "(ページが見つかりません)"));
                return;
            }

            foreach (var page in pages)
            {
                var fileName = page.FileName;
                menu.AddItem(new GUIContent(SubMenuPrefix + page.DisplayName), false, () => ManualLauncher.OpenProgrammerPage(fileName));
            }
        }
    }
}
