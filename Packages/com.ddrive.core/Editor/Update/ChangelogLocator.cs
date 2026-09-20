using System.IO;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 手順 1/§6 P-8(2026-09-20) — 更新ウィンドウが読む `CHANGELOG.md` の場所を
    // 解決する。
    //
    // [47_review_p_tickets_2026-09-20.md] P2-4(2026-09-20 修正) — P-9 でパッケージ直下に CHANGELOG.md が
    // 同梱されるようになったため、探索順を変更した:
    //   - 既定(持ち込み先): パッケージ直下(`resolvedPath/CHANGELOG.md`)を先に見る。これが正本
    //     (P-9 でパッケージに同梱済み)。持ち込み先が D-Drive を「埋め込み」配置([42] §4.5 の緊急回避)に
    //     している場合、`resolvedPath` の 2 階層上は「持ち込み先自身のリポジトリ直下」になるため、
    //     そちらを先に見ると持ち込み先自身の CHANGELOG.md を D-Drive のものと誤認する
    //     (実測: 3 行目のケース、[47] P2-4)。
    //   - 開発リポジトリ(`preferDevRepoRoot = true`、`DDriveProjectSettings.IsDevelopmentRepo`): 従来どおり
    //     2 階層上(このリポジトリ自身のルート)を先に見る(開発中はリポジトリ直下の CHANGELOG.md を
    //     編集するため、そちらが正本)。
    //
    // ファイル I/O 判定を差し替えられるようにして(既定は実ファイルシステム)、EditMode テストが
    // 一時フォルダで検証できるようにする。
    public static class ChangelogLocator
    {
        public const string ChangelogFileName = "CHANGELOG.md";

        public delegate bool FileExists(string path);

        public static string ResolvePath(string packageResolvedPath, bool preferDevRepoRoot, FileExists fileExists = null)
        {
            fileExists ??= File.Exists;

            if (string.IsNullOrEmpty(packageResolvedPath))
            {
                return null;
            }

            var packageCandidate = CombineForward(packageResolvedPath, ChangelogFileName);
            var devRepoRoot = AncestorPath(packageResolvedPath, levels: 2);
            var devRepoCandidate = devRepoRoot != null ? CombineForward(devRepoRoot, ChangelogFileName) : null;

            if (preferDevRepoRoot && devRepoCandidate != null && fileExists(devRepoCandidate))
            {
                return devRepoCandidate;
            }

            if (fileExists(packageCandidate))
            {
                return packageCandidate;
            }

            return devRepoCandidate != null && fileExists(devRepoCandidate) ? devRepoCandidate : null;
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
