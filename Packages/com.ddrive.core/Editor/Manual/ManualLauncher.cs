using System.IO;
using DDrive.Editor.Extension;
using DDrive.Editor.Spec;
using UnityEngine;

namespace DDrive.Editor.Manual
{
    // [09_editor_tools.md] §6.1 — マニュアルを開く実処理(副作用側)。URL の組み立ては
    // ManualUrlBuilder(純粋関数)、優先設定は ManualPrefs、ページ一覧は ManualPages に分離している。
    //
    // 開く先の決定(契約。デザイナー/プログラマーの両マニュアルで共通):
    //   1. ManualPrefs.PreferWeb(既定 OFF、2026-09-27 変更)かつ DDriveSpecSettings.HumanAppUrl が空でなければ Web(デプロイ①)を開く
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
            if (TryOpenExternalPage(page, null))
            {
                return;
            }

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

        // 2026-10-12(UX-1) — 新デザインの専用エディターのカード「?」から、ページ内の節(#anchor)まで開く。
        // anchor が空なら OpenPage(page) と同じ。URL の末尾に #anchor を付けるだけ(ページ側に id が無ければ先頭が開く)。
        public static void OpenPage(string pageName, string anchor)
        {
            if (string.IsNullOrEmpty(anchor))
            {
                OpenPage(pageName);
                return;
            }

            var page = string.IsNullOrEmpty(pageName) ? ManualPages.TopPageName : pageName;
            if (TryOpenExternalPage(page, anchor))
            {
                return;
            }

            var settings = DDriveSpecSettings.Load();
            var humanAppUrl = settings != null ? settings.HumanAppUrl : null;

            if (ManualUrlBuilder.ResolveUseWeb(ManualPrefs.PreferWeb, humanAppUrl))
            {
                var webUrl = ManualUrlBuilder.BuildWebUrl(humanAppUrl, page);
                if (!string.IsNullOrEmpty(webUrl))
                {
                    Application.OpenURL(webUrl + "#" + anchor);
                    return;
                }
            }

            var projectRoot = ManualPages.GetProjectRoot();
            var folder = ManualPages.GetManualFolder(projectRoot, ManualKind.Designer);
            var filePath = Path.Combine(folder, page + ".html");
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[D-Drive] マニュアル「{page}」が見つかりません: {filePath}");
                return;
            }

            Application.OpenURL(ManualUrlBuilder.BuildLocalFileUrl(folder, page) + "#" + anchor);
        }

        // [1014] §2.4(X-4、2026-10-16) — 組み込みのページ(docs/DesignerManual/<page>.html)に無い名前は、IManualPageProvider が宣言した
        // 外部ページから探して開く(組み込みが先。外部の key が組み込みと同じならそのページは組み込みが開く)。
        // 戻り値 true = 外部ページとして処理した(ファイルが無いときも警告して true。組み込みのページへは流さない)。
        // path はプロジェクト基準(相対はプロジェクトのルートから)。URL は file:// で、anchor があれば #anchor を付ける。
        public static bool TryOpenExternalPage(string page, string anchor)
        {
            if (!TryResolveExternalPage(page, out var full))
            {
                return false;
            }

            // X-R-16: 開く = OS の関連付けで実行されるので、文書の拡張子だけを許す(.exe / .bat などは開かない)。
            if (!IsDocumentExtension(full))
            {
                Debug.LogWarning($"[D-Drive] 外部マニュアル「{page}」は文書(.html / .htm / .md / .pdf / .txt)ではないため開きません: {full}");
                return true;
            }

            if (!File.Exists(full))
            {
                Debug.LogWarning($"[D-Drive] 外部マニュアル「{page}」が見つかりません: {full}");
                return true;
            }

            var url = new System.Uri(full).AbsoluteUri;
            Application.OpenURL(string.IsNullOrEmpty(anchor) ? url : url + "#" + anchor);
            return true;
        }

        // 外部マニュアルとして開いてよい拡張子(大小無視)。
        public static bool IsDocumentExtension(string path)
        {
            var ext = Path.GetExtension(path ?? string.Empty);
            return string.Equals(ext, ".html", System.StringComparison.OrdinalIgnoreCase)
                   || string.Equals(ext, ".htm", System.StringComparison.OrdinalIgnoreCase)
                   || string.Equals(ext, ".md", System.StringComparison.OrdinalIgnoreCase)
                   || string.Equals(ext, ".pdf", System.StringComparison.OrdinalIgnoreCase)
                   || string.Equals(ext, ".txt", System.StringComparison.OrdinalIgnoreCase);
        }

        // 外部ページの解決だけ(開かない)。page が IManualPageProvider の宣言にあり、組み込みのページが無ければ true と絶対パス(ファイルが無くても返す)。
        public static bool TryResolveExternalPage(string page, out string fullPath)
        {
            fullPath = null;
            if (!ExtensionProviders.TryGetManualPage(page, out var path, out _))
            {
                return false;
            }

            var builtIn = Path.Combine(ManualPages.GetManualFolder(ManualPages.GetProjectRoot(), ManualKind.Designer), page + ".html");
            if (File.Exists(builtIn))
            {
                return false;
            }

            fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(ManualPages.GetProjectRoot(), path));
            return true;
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

            var url = ManualUrlBuilder.BuildLocalFileUrl(folder, page);
            // 2026-09-27 — 持ち込み先で「開ける人と開けない人」が出たとき、どのファイルを何の URL で開こうと
            // したかを Console で確認できるようにする(Application.OpenURL は成否を返さないため、失敗は
            // OS 側の .html の関連付け・既定ブラウザの問題として切り分ける。RevealManualFolder が代替手段)。
            Debug.Log($"[D-Drive] マニュアルを開きます: {filePath}\n{url}");
            Application.OpenURL(url);
        }

        // 2026-09-27 — ブラウザで開けない環境向けの代替手段。マニュアルのフォルダ(トップページ)を
        // エクスプローラー/Finder で表示する(ユーザーが HTML をブラウザへドラッグして開ける)。
        public static void RevealManualFolder(ManualKind kind = ManualKind.Designer)
        {
            var projectRoot = ManualPages.GetProjectRoot();
            var folder = ManualPages.GetManualFolder(projectRoot, kind);
            var topPath = Path.Combine(folder, ManualPages.TopPageName + ".html");
            if (!File.Exists(topPath))
            {
                Debug.LogWarning($"[D-Drive] マニュアルのフォルダが見つかりません: {folder}");
                return;
            }

            UnityEditor.EditorUtility.RevealInFinder(topPath);
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
