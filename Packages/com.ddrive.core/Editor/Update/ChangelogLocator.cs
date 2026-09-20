using System.IO;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 手順 1/§6 P-8(2026-09-20) — 更新ウィンドウが読む `CHANGELOG.md` の場所を
    // 解決する。P-5 の時点では CHANGELOG.md はパッケージには同梱されておらずリポジトリ直下のままなので
    // (§2.2 末尾)、`PackageInfo.resolvedPath`(例: "<repo>/Packages/com.ddrive.core")の 2 階層上
    // (開発リポジトリのルート、または git URL 参照〔?path=Packages/com.ddrive.core〕で解決された
    // クローン全体のルート)を先に探し、無ければパッケージ直下(P-9 で同梱された場合に備える)を見る。
    //
    // ファイル I/O 判定を差し替えられるようにして(既定は実ファイルシステム)、EditMode テストが
    // 一時フォルダで検証できるようにする。
    public static class ChangelogLocator
    {
        public const string ChangelogFileName = "CHANGELOG.md";

        public delegate bool FileExists(string path);

        public static string ResolvePath(string packageResolvedPath, FileExists fileExists = null)
        {
            fileExists ??= File.Exists;

            if (string.IsNullOrEmpty(packageResolvedPath))
            {
                return null;
            }

            var devRepoRoot = AncestorPath(packageResolvedPath, levels: 2);
            if (devRepoRoot != null)
            {
                var devRepoCandidate = CombineForward(devRepoRoot, ChangelogFileName);
                if (fileExists(devRepoCandidate))
                {
                    return devRepoCandidate;
                }
            }

            var packageCandidate = CombineForward(packageResolvedPath, ChangelogFileName);
            return fileExists(packageCandidate) ? packageCandidate : null;
        }

        private static string AncestorPath(string path, int levels)
        {
            // Path.GetDirectoryName はプラットフォーム依存の区切り文字を扱うが、テストからは
            // "/" 区切りの仮想パスも渡したいため、まず正規化してから使う。
            var current = path.Replace('\\', '/').TrimEnd('/');
            for (var i = 0; i < levels; i++)
            {
                var slash = current.LastIndexOf('/');
                if (slash < 0)
                {
                    return null;
                }

                current = current.Substring(0, slash);
            }

            return current;
        }

        private static string CombineForward(string dir, string fileName) => dir.TrimEnd('/') + "/" + fileName;
    }
}
