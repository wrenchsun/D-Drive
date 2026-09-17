using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DDrive.Editor.Manual
{
    // [09_editor_tools.md] §6.1(2026-09-17 追記) — 2 つのマニュアル(デザイナー/プログラマー)を扱う。
    // デザイナーマニュアルは既定(ManualKind.Designer)のままなので既存呼び出しは変更不要。
    public enum ManualKind
    {
        Designer,
        Programmer,
    }

    // [09_editor_tools.md] §6.1 — docs/DesignerManual|ProgrammerManual/*.html のページ一覧。
    // 表示名はハードコードせず各 HTML の <title> から取る(実ファイルとずれないようにするため。
    // ManualPagesTests が実フォルダと DiscoverPages の結果を照合する)。
    public static class ManualPages
    {
        public const string TopPageName = "Readme";

        private const string DesignerFolderRelativePath = "docs/DesignerManual";
        private const string DesignerTitleSuffix = " | D-Drive デザイナーマニュアル";
        private const string ProgrammerFolderRelativePath = "docs/ProgrammerManual";
        private const string ProgrammerTitleSuffix = " | D-Drive プログラマーマニュアル";

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

        // マニュアル種別ごとのフォルダ相対パス(docs/DesignerManual または docs/ProgrammerManual)。
        public static string GetFolderRelativePath(ManualKind kind)
            => kind == ManualKind.Programmer ? ProgrammerFolderRelativePath : DesignerFolderRelativePath;

        // マニュアル種別ごとの <title> 接尾辞。
        public static string GetTitleSuffix(ManualKind kind)
            => kind == ManualKind.Programmer ? ProgrammerTitleSuffix : DesignerTitleSuffix;

        public static string GetManualFolder(string projectRoot, ManualKind kind = ManualKind.Designer)
            => Path.Combine(projectRoot, GetFolderRelativePath(kind).Replace('/', Path.DirectorySeparatorChar));

        // トップ(Readme)を除くページ一覧。フォルダが無ければ空配列(警告ログのみ、例外で止めない)。
        public static Page[] DiscoverPages(string projectRoot, ManualKind kind = ManualKind.Designer)
        {
            var folder = GetManualFolder(projectRoot, kind);
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
                pages[i] = new Page(fileNames[i], ResolveDisplayName(folder, fileNames[i], kind));
            }
            return pages;
        }

        // <title> から表示名を取る。読めない/無ければファイル名にフォールバックする。
        public static string ResolveDisplayName(string folder, string fileName, ManualKind kind = ManualKind.Designer)
        {
            var path = Path.Combine(folder, fileName + ".html");
            try
            {
                var html = File.ReadAllText(path);
                var title = ExtractTitle(html);
                if (!string.IsNullOrEmpty(title))
                {
                    return StripManualSuffix(title, kind);
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

        public static string StripManualSuffix(string title, ManualKind kind = ManualKind.Designer)
        {
            var suffix = GetTitleSuffix(kind);
            if (title != null && title.EndsWith(suffix, StringComparison.Ordinal))
            {
                return title.Substring(0, title.Length - suffix.Length);
            }
            return title;
        }
    }
}
