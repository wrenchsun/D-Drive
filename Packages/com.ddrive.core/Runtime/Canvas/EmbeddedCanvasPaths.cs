using System;

namespace DDrive.Runtime.Ui
{
    // [07_canvas_prefab.md] A-2/A-3 追記(2026-10-03、Canvas の埋め込み) — 親 Prefab ルート基準のパスと、
    // 埋め込み子 Canvas のルート(EmbeddedCanvas.RootPath)基準のパスを相互に変換する純ロジック。
    // UiManager(優先順位の判定)・CanvasDataValidator・Canvas Editor(グループ表示・自動収集・選択からの対象解決)が
    // 同じ規則を共有する。Transform には依存しない(文字列だけ)ので EditMode で単体テストできる。
    public static class EmbeddedCanvasPaths
    {
        // "a" + "b" → "a/b"。どちらかが空ならもう片方をそのまま返す(新しい文字列を作らない)。
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

        // 割り当てを作らない比較: rowPath が Combine(prefix, childPath) と等しいか。
        public static bool IsJoinedPath(string rowPath, string prefix, string childPath)
        {
            rowPath ??= string.Empty;
            childPath ??= string.Empty;
            if (string.IsNullOrEmpty(prefix))
            {
                return string.Equals(rowPath, childPath, StringComparison.Ordinal);
            }

            if (childPath.Length == 0)
            {
                return string.Equals(rowPath, prefix, StringComparison.Ordinal);
            }

            var expected = prefix.Length + 1 + childPath.Length;
            return rowPath.Length == expected
                   && rowPath[prefix.Length] == '/'
                   && string.CompareOrdinal(rowPath, 0, prefix, 0, prefix.Length) == 0
                   && string.CompareOrdinal(rowPath, prefix.Length + 1, childPath, 0, childPath.Length) == 0;
        }
    }
}
