using System;
using System.Collections.Generic;
using DDrive.Editor.Setup;
using Newtonsoft.Json.Linq;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 P-15(2026-10-03) — manifest の git URL 依存を「対象パッケージ ID を引数に」
    // 読み書きする純関数(JObject 入出力のみ。実ファイルには触れない)。P-14 で UpdateWindow に直書きされていた
    // 「#ref の差し替え」「前の参照に戻す」を D-Drive 以外のパッケージにも使えるように切り出した。
    public static class PackageManifestOps
    {
        public readonly struct GitDependency
        {
            public readonly string Id;
            public readonly string Value;
            public readonly GitPackageUrl Url;

            public GitDependency(string id, string value, GitPackageUrl url)
            {
                Id = id;
                Value = value;
                Url = url;
            }

            // "v1.2.3" 形式の ref(タグ運用の印)か。UniTask / R3 のような "2.5.11" や コミットハッシュは false。
            public bool HasVersionTagRef => IsVersionTagRef(Url.Ref);
        }

        public static bool IsVersionTagRef(string reference)
            => !string.IsNullOrEmpty(reference)
               && reference.Length > 1
               && (reference[0] == 'v' || reference[0] == 'V')
               && SemVer.TryParse(reference.Substring(1), out _);

        // manifest.dependencies のうち git URL のもの(キーの出現順)。
        public static List<GitDependency> ListGitDependencies(JObject manifest)
        {
            var result = new List<GitDependency>();
            if (manifest?[ManifestJson.DependenciesKey] is not JObject deps)
            {
                return result;
            }

            foreach (var prop in deps.Properties())
            {
                var value = prop.Value?.Type == JTokenType.String ? (string)prop.Value : null;
                var url = GitPackageUrl.Parse(value);
                if (url.IsGitUrl)
                {
                    result.Add(new GitDependency(prop.Name, value, url));
                }
            }

            return result;
        }

        // 同じリポジトリ + 同じ ?path= の依存があれば、そのパッケージ ID を返す(無ければ null)。ref は比較しない。
        public static string FindDependencyIdByUrl(JObject manifest, GitPackageUrl url)
        {
            if (!url.IsGitUrl)
            {
                return null;
            }

            foreach (var dep in ListGitDependencies(manifest))
            {
                if (SameRepository(dep.Url, url))
                {
                    return dep.Id;
                }
            }

            return null;
        }

        public static bool SameRepository(GitPackageUrl a, GitPackageUrl b)
            => a.IsGitUrl && b.IsGitUrl
               && string.Equals(NormalizeClone(a.CloneUrl), NormalizeClone(b.CloneUrl), StringComparison.OrdinalIgnoreCase)
               && string.Equals(NormalizePath(a.Path), NormalizePath(b.Path), StringComparison.Ordinal);

        // [42] §4.2.1(2026-10-03、レビュー PC-R-07) — 同じリポジトリ(?path= が違うだけ)の別パッケージで、`#ref` が targetRef と違うもの
        // (id, 今の ref)。同じリポジトリの複数パッケージは同じタグに揃える前提なので、片方だけ上げようとしたときの案内に使う。
        public static List<KeyValuePair<string, string>> FindSiblingsAtOtherRef(JObject manifest, string packageId, string targetRef)
        {
            var result = new List<KeyValuePair<string, string>>();
            GitPackageUrl self = default;
            var found = false;
            foreach (var dep in ListGitDependencies(manifest))
            {
                if (dep.Id == packageId)
                {
                    self = dep.Url;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return result;
            }

            foreach (var dep in ListGitDependencies(manifest))
            {
                if (dep.Id == packageId
                    || !string.Equals(NormalizeClone(dep.Url.CloneUrl), NormalizeClone(self.CloneUrl), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dep.Url.Ref ?? string.Empty, targetRef ?? string.Empty, StringComparison.Ordinal))
                {
                    continue;
                }

                result.Add(new KeyValuePair<string, string>(dep.Id, dep.Url.Ref ?? string.Empty));
            }

            return result;
        }

        // `#ref` だけを差し替える(URL・?path=・git+ の形式は変えない)。git URL 依存でなければ false(何も変えない)。
        public static bool TryBumpRef(JObject manifest, string packageId, string targetRef, out string previousValue, out string newValue)
        {
            previousValue = ManifestJson.GetDependencyValue(manifest, packageId);
            newValue = null;
            var parsed = GitPackageUrl.Parse(previousValue);
            if (!parsed.IsGitUrl || string.IsNullOrEmpty(targetRef))
            {
                return false;
            }

            newValue = parsed.WithRef(targetRef);
            ManifestJson.SetDependency(manifest, packageId, newValue);
            return true;
        }

        // 退避しておいた値に戻し、置き換えた(戻す前の)値を返す(= 再度押すと元に戻せる入れ替え)。
        public static bool TryRestore(JObject manifest, string packageId, string previousValue, out string replacedValue)
        {
            replacedValue = ManifestJson.GetDependencyValue(manifest, packageId);
            if (manifest == null || string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(previousValue))
            {
                return false;
            }

            ManifestJson.SetDependency(manifest, packageId, previousValue);
            return true;
        }

        private static string NormalizeClone(string cloneUrl)
        {
            if (string.IsNullOrEmpty(cloneUrl))
            {
                return string.Empty;
            }

            var s = cloneUrl.Trim().TrimEnd('/');
            if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(0, s.Length - 4);
            }

            // https と ssh の違いは同じリポジトリとして扱わない(末尾の "/" と ".git" の揺れだけ吸収する)。
            return s;
        }

        private static string NormalizePath(string path)
            => string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').Trim('/');
    }
}
