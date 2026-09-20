using System;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2/§6 P-14(2026-09-20) — `Packages/manifest.json` の `com.ddrive.core` の値
    // (`ManifestJson.GetDependencyValue`)を「更新チェック」が扱える形に分解する純関数。UPM の git 依存
    // 構文(`[git+]<scheme>://<host>/<repo>[?path=<subfolder>][#<ref>]`)を対象にする。
    // 例: "git+https://github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#v1.0.0"
    //     "git+ssh://git@github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#v1.0.0"
    // レジストリ配布の値(例: "1.0.0")や `file:` 参照は IsGitUrl=false になり、更新チェックの対象外として
    // 扱う(呼び出し側は「更新チェック対象外」を表示して no-op にする)。
    // Unity API に一切依存しないので EditMode テストから直接検証できる。
    public readonly struct GitPackageUrl
    {
        // manifest.json に書かれていたそのままの値。WithRef はこの文字列に対する `#` 以降の置き換えのみ行う
        // (URL・`?path=`・`git+https`/`git+ssh` の形式を一切変えないため)。
        public readonly string RawValue;

        public readonly bool IsGitUrl;

        // "git+https" / "git+ssh" / "git+http" / "" (プレフィックス無しの素の https/http)。表示用。
        public readonly string Prefix;

        // `git ls-remote` にそのまま渡せる URL(`git+` プレフィックスを剥がした後の値。クエリ・フラグメントは含まない)。
        public readonly string CloneUrl;

        // `?path=` の値。無ければ null。
        public readonly string Path;

        // `#` 以降の値(タグ名やコミットハッシュ)。無ければ null。
        public readonly string Ref;

        private GitPackageUrl(string rawValue, bool isGitUrl, string prefix, string cloneUrl, string path, string reference)
        {
            RawValue = rawValue;
            IsGitUrl = isGitUrl;
            Prefix = prefix;
            CloneUrl = cloneUrl;
            Path = path;
            Ref = reference;
        }

        public static GitPackageUrl NotGitUrl(string rawValue) => new(rawValue, false, null, null, null, null);

        public static GitPackageUrl Parse(string manifestValue)
        {
            if (string.IsNullOrWhiteSpace(manifestValue))
            {
                return NotGitUrl(manifestValue);
            }

            var value = manifestValue.Trim();

            // 1. "#ref" を切り出す(存在しなければ null)。
            var hashIndex = value.IndexOf('#');
            var beforeHash = hashIndex >= 0 ? value.Substring(0, hashIndex) : value;
            var reference = hashIndex >= 0 && hashIndex + 1 < value.Length ? value.Substring(hashIndex + 1) : null;

            // 2. "?path=..." を切り出す(他のクエリと混在していても "path" キーだけを見る)。
            var queryIndex = beforeHash.IndexOf('?');
            var baseUrl = queryIndex >= 0 ? beforeHash.Substring(0, queryIndex) : beforeHash;
            var path = queryIndex >= 0 ? ExtractPathParam(beforeHash.Substring(queryIndex + 1)) : null;

            if (!LooksLikeGitUrl(baseUrl))
            {
                return NotGitUrl(manifestValue);
            }

            string prefix;
            string cloneUrl;
            const string gitPlusMarker = "git+";
            if (baseUrl.StartsWith(gitPlusMarker, StringComparison.Ordinal))
            {
                var afterMarker = baseUrl.Substring(gitPlusMarker.Length); // 例: "https://..." / "ssh://..."
                var schemeEnd = afterMarker.IndexOf("://", StringComparison.Ordinal);
                prefix = schemeEnd >= 0 ? gitPlusMarker + afterMarker.Substring(0, schemeEnd) : gitPlusMarker;
                cloneUrl = afterMarker;
            }
            else
            {
                prefix = string.Empty;
                cloneUrl = baseUrl;
            }

            return new GitPackageUrl(manifestValue, true, prefix, cloneUrl, path, reference);
        }

        // "#ref" だけを差し替えた manifest 値を返す(git URL でなければ RawValue をそのまま返す = no-op)。
        public string WithRef(string newRef)
        {
            if (!IsGitUrl || string.IsNullOrEmpty(RawValue))
            {
                return RawValue;
            }

            var hashIndex = RawValue.IndexOf('#');
            var head = hashIndex >= 0 ? RawValue.Substring(0, hashIndex) : RawValue;
            return string.IsNullOrEmpty(newRef) ? head : head + "#" + newRef;
        }

        private static bool LooksLikeGitUrl(string baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl))
            {
                return false;
            }

            if (baseUrl.StartsWith("git+https://", StringComparison.Ordinal)
                || baseUrl.StartsWith("git+ssh://", StringComparison.Ordinal)
                || baseUrl.StartsWith("git+http://", StringComparison.Ordinal)
                || baseUrl.StartsWith("git://", StringComparison.Ordinal)
                || baseUrl.StartsWith("ssh://", StringComparison.Ordinal))
            {
                return true;
            }

            // "git+" 無しでも ".git" で終わる https/http は UPM の git 依存として扱う
            // (README の UniTask/R3 の依存記述と同じ形式。com.ddrive.core 自体は常に "git+" を付ける運用)。
            if ((baseUrl.StartsWith("https://", StringComparison.Ordinal) || baseUrl.StartsWith("http://", StringComparison.Ordinal))
                && baseUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string ExtractPathParam(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return null;
            }

            foreach (var part in query.Split('&'))
            {
                var eq = part.IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }

                var key = part.Substring(0, eq);
                if (string.Equals(key, "path", StringComparison.Ordinal))
                {
                    var v = part.Substring(eq + 1);
                    return string.IsNullOrEmpty(v) ? null : v;
                }
            }

            return null;
        }
    }
}
