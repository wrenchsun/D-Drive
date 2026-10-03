using System;
using System.Collections.Generic;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 P-15 追加修正(2026-10-03、docs/53 まとめ役指摘) — git CLI に渡す引数の組み立て。
    // URL・タグ名(ref)・パスは manifest やリモートのタグ一覧など外から来る値なので、`-` で始まるとオプションとして解釈され得る
    // (例: `--upload-pack=...`)。対策は 2 つ: (1) `-` で始まる値・制御文字を含む値は実行前に弾く(IsSafeValue。弾いたら警告で
    // 続行できなくするだけで例外にはしない)。(2) 位置引数の前に `--` を置く(clone / ls-remote)。
    // 引数は 1 個ずつ ProcessStartInfo.ArgumentList に渡す(引用符の組み立てをしない)。実 git は呼ばない純粋な関数なのでテストできる。
    // public(InternalsVisibleTo 未設定のため、テスト asmdef から直接検証できるようにする)。
    public static class GitArguments
    {
        // `-` で始まらず、空でなく、制御文字(改行など)を含まない。
        public static bool IsSafeValue(string value)
        {
            if (string.IsNullOrEmpty(value) || value[0] == '-')
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // git ls-remote --tags -- <url>
        public static string[] LsRemoteTags(string repoUrl) => new[] { "ls-remote", "--tags", "--", repoUrl };

        // git clone --depth 1 --filter=blob:none --no-checkout --no-tags --branch <reference> -- <url> <dir>
        // 作業ツリーを作らない(2026-10-03、レビュー PC-R-01): チェックアウト時のフィルター(LFS の smudge 等)・シンボリックリンク・
        // 大きなファイルの取得が起きない。必要な blob は次の ShowPackageJson が 1 個だけ遅延取得する。
        public static string[] CloneNoCheckout(string cloneUrl, string reference, string workDir)
            => new[] { "clone", "--depth", "1", "--filter=blob:none", "--no-checkout", "--no-tags", "--branch", reference, "--", cloneUrl, workDir };

        // git show HEAD:<path>/package.json(HEAD は上の clone が取ったタグのコミット。ユーザー入力の rev は使わない)
        public static string[] ShowPackageJson(string packagePath)
            => new[] { "show", "HEAD:" + (string.IsNullOrEmpty(packagePath) ? string.Empty : packagePath + "/") + "package.json" };

        // `?path=` の値を検査して、リポジトリ直下からの相対パス(区切りは `/`、先頭と末尾の `/` なし)にする。
        // UPM の `?path=/sub/dir`(先頭 `/` = リポジトリ直下基準)は許す。空は null(= リポジトリ直下)。
        // 弾く(false): `..` や `.` の区間・空の区間(`a//b`)・ドライブ名やコロン・UNC・`-` で始まる値・制御文字。
        public static bool TryNormalizePackagePath(string subPath, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrEmpty(subPath))
            {
                return true;
            }

            var text = subPath.Replace('\\', '/');
            if (text.StartsWith("//", StringComparison.Ordinal))
            {
                return false; // UNC
            }

            text = text.Trim('/');
            if (text.Length == 0)
            {
                return true;
            }

            if (text.IndexOf(':') >= 0 || !IsSafeValue(text))
            {
                return false;
            }

            var segments = text.Split('/');
            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    return false;
                }
            }

            normalized = text;
            return true;
        }

        // 外から来る値のうち、安全でないものの名前を返す(全部安全なら null)。names と values は同じ長さ。
        public static string FirstUnsafe(IReadOnlyList<string> names, IReadOnlyList<string> values)
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (!IsSafeValue(values[i]))
                {
                    return names[i];
                }
            }

            return null;
        }

        public static string UnsafeWarning(string what)
            => $"{what} が `-` で始まる、または不正な文字を含むため git を実行しませんでした(安全のため)。";
    }
}
