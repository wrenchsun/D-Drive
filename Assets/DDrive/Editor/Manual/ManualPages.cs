using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DDrive.Editor.Manual
{
    // [09_editor_tools.md] §6.1 — docs/DesignerManual/*.html のページ一覧。
    // 表示名はハードコードせず各 HTML の <title> から取る(実ファイルとずれないようにするため。
    // ManualPagesTests が実フォルダと DiscoverPages の結果を照合する)。
    public static class ManualPages
    {
        public const string TopPageName = "Readme";
        private const string FolderRelativePath = "docs/DesignerManual";
        private const string ManualTitleSuffix = " | D-Drive デザイナーマニュアル";

        private static readonly Regex TitleRegex =
            new Regex("<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        public readonly struct Page
        {
            public readonly string FileName;
            public readonly string DisplayName;

            public Page(string fileName, string displayName)
            {
                FileName = fileName;
                DisplayName = displayName;
            }
        }

        // Application.dataPath の親(プロジェクトルート)。
        public static string GetProjectRoot()
        {
            var parent = Directory.GetParent(Application.dataPath);
            return parent != null ? parent.FullName : Application.dataPath;
        }

        public static string GetManualFolder(string projectRoot)
            => Path.Combine(projectRoot, FolderRelativePath.Replace('/', Path.DirectorySeparatorChar));

        // トップ(Readme)を除くページ一覧。フォルダが無ければ空配列(警告ログのみ、例外で止めない)。
        public static Page[] DiscoverPages(string projectRoot)
        {
            var folder = GetManualFolder(projectRoot);
            if (!Directory.Exists(folder))
            {
                Debug.LogWarning($"[D-Drive] マニュアルフォルダが見つかりません: {folder}");
                return Array.Empty<Page>();
            }

            var fileNames = Directory.GetFiles(folder, "*.html")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.Equals(name, TopPageName, StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var pages = new Page[fileNames.Length];
            for (var i = 0; i < fileNames.Length; i++)
            {
                pages[i] = new Page(fileNames[i], ResolveDisplayName(folder, fileNames[i]));
            }
            return pages;
        }

        // <title> から表示名を取る。読めない/無ければファイル名にフォールバックする。
        public static string ResolveDisplayName(string folder, string fileName)
        {
            var path = Path.Combine(folder, fileName + ".html");
            try
            {
                var html = File.ReadAllText(path);
                var title = ExtractTitle(html);
                if (!string.IsNullOrEmpty(title))
                {
                    return StripManualSuffix(title);
                }
            }
            catch (IOException)
            {
                // 読めない場合はファイル名にフォールバック(例外で止めない。CLAUDE.md §0-4)。
            }
            return fileName;
        }

        public static string ExtractTitle(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return null;
            }
            var match = TitleRegex.Match(html);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        public static string StripManualSuffix(string title)
        {
            if (title != null && title.EndsWith(ManualTitleSuffix, StringComparison.Ordinal))
            {
                return title.Substring(0, title.Length - ManualTitleSuffix.Length);
            }
            return title;
        }
    }
}
