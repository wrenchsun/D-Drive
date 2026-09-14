using System;
using System.IO;

namespace DDrive.Editor.Manual
{
    // [09_editor_tools.md] §6.1 — メインツールバー「マニュアル」ボタン(2026-09-14)。
    // URL の組み立てだけを行う純粋関数。副作用(Application.OpenURL / ファイル存在チェック)は
    // ManualLauncher が持つ(この型は EditMode テストで検証する。ManualUrlBuilderTests)。
    //
    // Web(デプロイ①、人向け SPA)側の契約:
    //   <HumanAppUrl>?page=manual&p=<ページ名(拡張子なし)>
    //   HumanAppUrl に既にクエリがあれば "&" で連結する。
    public static class ManualUrlBuilder
    {
        // 共通の小さな純粋関数: baseUrl に "key=value[&key=value...]" 形式のクエリ片を追加する。
        // 既にクエリ(?)があれば "&" で連結し、無ければ "?" で始める。末尾スラッシュはトリムしない
        // (呼び出し側の humanAppUrl がそのまま使われる。2026-09-14: SpecWebParser.BuildSpecLink も
        // これに揃えた。docs/32_spec_web.md §6)。
        public static string AppendQuery(string baseUrl, string query)
        {
            var separator = baseUrl.IndexOf('?') >= 0 ? "&" : "?";
            return baseUrl + separator + query;
        }

        // settings.HumanAppUrl が空なら Web は使えない(呼び出し側でローカルにフォールバックする)。
        public static string BuildWebUrl(string humanAppUrl, string pageName)
        {
            if (string.IsNullOrEmpty(humanAppUrl) || string.IsNullOrEmpty(pageName))
            {
                return null;
            }

            return AppendQuery(humanAppUrl, "page=manual&p=" + Uri.EscapeDataString(pageName));
        }

        // ローカルの docs/DesignerManual/<pageName>.html を file:// URI にする。
        // folderAbsolutePath は絶対パス(呼び出し側が Application.dataPath の親から組み立てる)。
        public static string BuildLocalFileUrl(string folderAbsolutePath, string pageName)
        {
            var filePath = Path.Combine(folderAbsolutePath, pageName + ".html");
            var fullPath = Path.GetFullPath(filePath);
            return new Uri(fullPath).AbsoluteUri;
        }

        // Web を優先すべきか(既定: Web 優先。HumanAppUrl 未設定時は常にローカル)。
        public static bool ResolveUseWeb(bool preferWeb, string humanAppUrl)
        {
            return preferWeb && !string.IsNullOrEmpty(humanAppUrl);
        }
    }
}
