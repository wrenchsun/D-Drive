using System.IO;
using DDrive.Editor.Spec;
using UnityEngine;

namespace DDrive.Editor.Manual
{
    // [09_editor_tools.md] §6.1 — マニュアルを開く実処理(副作用側)。URL の組み立ては
    // ManualUrlBuilder(純粋関数)、優先設定は ManualPrefs、ページ一覧は ManualPages に分離している。
    //
    // 開く先の決定(契約。デザイナー/プログラマーの両マニュアルで共通):
    //   1. ManualPrefs.PreferWeb かつ DDriveSpecSettings.HumanAppUrl が空でなければ Web(デプロイ①)を開く
    //      (<HumanAppUrl>?page=manual&p=<page>、プログラマーマニュアルは &kind=programmer を追加。
    //      ManualUrlBuilder.BuildWebUrl)
    //   2. それ以外はローカルの docs/DesignerManual|ProgrammerManual/<page>.html を file:// で開く
    //      (ファイルが無ければ Debug.LogWarning のみ、例外で止めない。CLAUDE.md §0-4)
    //
    // 2026-09-17 追記: プログラマーマニュアルは当初(新設時点)「SpecWeb に配信されていないため
    // 常にローカル」だったが、Tools/SpecWeb 側にプログラマーマニュアルの配信(manualGet の
    // kind 引数)を追加したため、デザイナーマニュアルと同じ Web/ローカルの分岐に揃えた
    // (ManualPrefs.PreferWeb はデザイナー/プログラマーで共有する単一のトグル。
    // docs/32_spec_web.md「実装メモ(マニュアル配信)」参照)。
    public static class ManualLauncher
    {
        public static void OpenTop() => OpenPage(ManualPages.TopPageName);

        public static void OpenPage(string pageName)
        {
            var page = string.IsNullOrEmpty(pageName) ? ManualPages.TopPageName : pageName;
            var settings = DDriveSpecSettings.Load();
            var humanAppUrl = settings != null ? settings.HumanAppUrl : null;

            if (ManualUrlBuilder.ResolveUseWeb(ManualPrefs.PreferWeb, humanAppUrl))
            {
                var webUrl = ManualUrlBuilder.BuildWebUrl(humanAppUrl, page);
                if (!string.IsNullOrEmpty(webUrl))
                {
                    Application.OpenURL(webUrl);
                    return;
                }
            }

            OpenLocal(page, ManualKind.Designer);
        }

        // ドロップダウン末尾の「ローカルのマニュアルを開く」から呼ぶ。優先設定に関わらず常にローカル。
        public static void OpenLocalTop() => OpenLocal(ManualPages.TopPageName, ManualKind.Designer);

        public static void OpenLocal(string pageName, ManualKind kind = ManualKind.Designer)
        {
            var page = string.IsNullOrEmpty(pageName) ? ManualPages.TopPageName : pageName;
            var projectRoot = ManualPages.GetProjectRoot();
            var folder = ManualPages.GetManualFolder(projectRoot, kind);
            var filePath = Path.Combine(folder, page + ".html");

            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[D-Drive] マニュアル「{page}」が見つかりません: {filePath}");
                return;
            }

            Application.OpenURL(ManualUrlBuilder.BuildLocalFileUrl(folder, page));
        }

        // プログラマーマニュアルのトップ(Readme)を開く。
        public static void OpenProgrammerTop() => OpenProgrammerPage(ManualPages.TopPageName);

        // プログラマーマニュアルの指定ページを開く。デザイナーマニュアルの OpenPage と同じ
        // Web/ローカルの分岐(ManualPrefs.PreferWeb + DDriveSpecSettings.HumanAppUrl)を使う。
        public static void OpenProgrammerPage(string pageName)
        {
            var page = string.IsNullOrEmpty(pageName) ? ManualPages.TopPageName : pageName;
            var settings = DDriveSpecSettings.Load();
            var humanAppUrl = settings != null ? settings.HumanAppUrl : null;

            if (ManualUrlBuilder.ResolveUseWeb(ManualPrefs.PreferWeb, humanAppUrl))
            {
                var webUrl = ManualUrlBuilder.BuildWebUrl(humanAppUrl, page, ManualKind.Programmer);
                if (!string.IsNullOrEmpty(webUrl))
                {
                    Application.OpenURL(webUrl);
                    return;
                }
            }

            OpenLocal(page, ManualKind.Programmer);
        }
    }
}
