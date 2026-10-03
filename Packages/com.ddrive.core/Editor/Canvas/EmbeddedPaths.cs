using System;

namespace DDrive.Editor.CanvasTool
{
    // [07_canvas_prefab.md] A-4 追記(2026-10-03、レビュー PC-R-18) — Canvas の埋め込みのパス変換(Editor 側の複製)。
    // Runtime の EmbeddedCanvasPaths(internal。DDrive.Runtime の公開 API に汎用の文字列ユーティリティを残さないため)と
    // **同じ規則**。Editor アセンブリからは Runtime の internal が見えない(InternalsVisibleTo を置かない方針)ので複製している。
    // 両者が一致することは EmbeddedCanvasPathsTests(Combine / TryToChildPath の表をリフレクションで両方に当てる)で固定している。
    // 規則を変えるときは必ず両方を直す。
    internal static class EmbeddedPaths
    {
        // "a" + "b" → "a/b"。どちらかが空ならもう片方をそのまま返す。
        public static string Combine(string prefix, string path)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return path ?? string.Empty;
            }

            if (string.IsNullOrEmpty(path))
            {
                return prefix;
            }

            return prefix + "/" + path;
        }

        // parentPath(親ルート基準)が rootPath(埋め込みルート)の配下か。配下なら childPath(子ルート基準。
        // 埋め込みルート自身は空文字)を返す。rootPath が空・配下でない場合は false(childPath は空文字)。
        public static bool TryToChildPath(string rootPath, string parentPath, out string childPath)
        {
            childPath = string.Empty;
            if (string.IsNullOrEmpty(rootPath) || parentPath == null)
            {
                return false;
            }

            var rootLength = rootPath.Length;
            if (parentPath.Length < rootLength || string.CompareOrdinal(parentPath, 0, rootPath, 0, rootLength) != 0)
            {
                return false;
            }

            if (parentPath.Length == rootLength)
            {
                return true; // 埋め込みルート自身
            }

            if (parentPath[rootLength] != '/')
            {
                return false; // "Option2/..." が "Option" の配下に誤判定されないように
            }

            childPath = parentPath.Substring(rootLength + 1);
            return true;
        }
    }
}
