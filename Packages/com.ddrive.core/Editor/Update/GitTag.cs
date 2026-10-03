using System;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 P-15(2026-10-03、レビュー PC-R-02) — リモートのタグ 1 件。
    // **元のタグ名を保持する**(`v1.5.0-rc.1` を `v1.5.0` に丸めて存在しないタグを manifest の `#ref` に書かないため。
    // `v01.2.0` のような表記・`v` の無い `1.5.0` も、そのまま `#ref` に使う)。Unity API 非依存の純粋なデータ + 比較。
    public readonly struct GitTag
    {
        // 元のタグ名(`refs/tags/` の後ろ)。manifest の `#ref` / `Client.Add` にはこれを使う。
        public readonly string Name;

        // X.Y.Z(`-` 以降のプレリリース部は含まない)。
        public readonly Version Version;

        // `-` の後ろ(`rc.1` 等)。正式版は空。
        public readonly string Prerelease;

        public bool IsPrerelease => !string.IsNullOrEmpty(Prerelease);

        public GitTag(string name, Version version, string prerelease)
        {
            Name = name;
            Version = version;
            Prerelease = prerelease ?? string.Empty;
        }

        public override string ToString() => Name;

        // "v1.5.0" / "1.5.0" / "v1.5.0-rc.1" を解析する。`requireVPrefix` なら `v`(`V`)で始まるものだけ。
        // 版として読めないもの(`v1.5`・`vX`・`v1.5.0-`(空のプレリリース)・`1.5.0+meta`)は false。
        public static bool TryParse(string tagName, bool requireVPrefix, out GitTag tag)
        {
            tag = default;
            if (string.IsNullOrEmpty(tagName))
            {
                return false;
            }

            var hasV = tagName[0] == 'v' || tagName[0] == 'V';
            if (requireVPrefix && !hasV)
            {
                return false;
            }

            var body = hasV ? tagName.Substring(1) : tagName;
            var dash = body.IndexOf('-');
            var core = dash >= 0 ? body.Substring(0, dash) : body;
            var pre = dash >= 0 ? body.Substring(dash + 1) : string.Empty;
            if (dash >= 0 && !IsValidPrerelease(pre))
            {
                return false;
            }

            if (!Version.TryParse(core, out var version))
            {
                return false;
            }

            tag = new GitTag(tagName, version, pre);
            return true;
        }

        // 現在の `#ref`(manifest の値)がタグ表記なら解析する(コミットハッシュ・ブランチ名は false)。
        public static bool TryParseRef(string reference, out GitTag tag) => TryParse(reference, false, out tag);

        // SemVer の優先順位: X.Y.Z → 正式版 > プレリリース → プレリリース同士は区間ごと(数字は数値比較・数字 < 英字・短い方が小さい)。
        // タグ名が違っても版が同じなら 0(`v1.2.0` と `v01.2.0`)。
        public static int Compare(GitTag a, GitTag b)
        {
            var core = a.Version.CompareTo(b.Version);
            if (core != 0)
            {
                return core;
            }

            if (a.IsPrerelease != b.IsPrerelease)
            {
                return a.IsPrerelease ? -1 : 1;
            }

            return ComparePrerelease(a.Prerelease, b.Prerelease);
        }

        private static bool IsValidPrerelease(string pre)
        {
            if (string.IsNullOrEmpty(pre))
            {
                return false;
            }

            for (var i = 0; i < pre.Length; i++)
            {
                var c = pre[i];
                var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '.' || c == '-';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        private static int ComparePrerelease(string a, string b)
        {
            var pa = a.Split('.');
            var pb = b.Split('.');
            var n = Math.Min(pa.Length, pb.Length);
            for (var i = 0; i < n; i++)
            {
                var na = IsNumeric(pa[i]);
                var nb = IsNumeric(pb[i]);
                int cmp;
                if (na && nb)
                {
                    cmp = CompareNumeric(pa[i], pb[i]);
                }
                else if (na != nb)
                {
                    cmp = na ? -1 : 1; // 数字 < 英字
                }
                else
                {
                    cmp = string.CompareOrdinal(pa[i], pb[i]);
                }

                if (cmp != 0)
                {
                    return cmp < 0 ? -1 : 1;
                }
            }

            return pa.Length.CompareTo(pb.Length);
        }

        private static bool IsNumeric(string s)
        {
            if (s.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        // 桁数が多くても溢れない数値比較(先頭の 0 を除いて桁数 → 辞書順)。
        private static int CompareNumeric(string a, string b)
        {
            a = a.TrimStart('0');
            b = b.TrimStart('0');
            if (a.Length != b.Length)
            {
                return a.Length.CompareTo(b.Length);
            }

            return string.CompareOrdinal(a, b);
        }
    }
}
