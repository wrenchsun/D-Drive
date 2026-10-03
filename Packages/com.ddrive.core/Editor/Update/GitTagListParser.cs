using System;
using System.Collections.Generic;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2/§6 P-14(2026-09-20) — `git ls-remote --tags <repoUrl>` の標準出力を
    // 解析する純関数。実プロセス起動は `IGitTagLister`(`GitCliTagLister`)側の責務にし、ここは文字列 →
    // バージョンの一覧に変換するだけ(Unity API 非依存、EditMode テストから直接検証できる)。
    //
    // `git ls-remote --tags` の出力例:
    //   a1b2c3...\trefs/tags/v1.0.0
    //   d4e5f6...\trefs/tags/v1.0.0^{}      ← 注釈付きタグの「剥がした」参照(peeled)。重複なので除外する
    //   0000000...\trefs/tags/not-a-version ← SemVer として解釈できないタグは無視する
    //
    // 戻り値は `System.Version`(=SemVer.TryParse の out 型)の一覧。プロジェクトの `SemVer` は比較用の
    // 静的関数の集まりで実体を持つ値型ではないため、既存の `SemVer.TryParse` がそのまま使う `System.Version`
    // を「解析結果の 1 件」として扱う(タグ名は常に "vX.Y.Z" 運用〔README〕なので、"v" + version.ToString()
    // で元のタグ表記に戻せる)。
    public static class GitTagListParser
    {
        // 降順(新しい順)に並べて返す。
        public static List<Version> Parse(string lsRemoteOutput) => ParseCore(lsRemoteOutput, false);

        // [42_distribution.md] §4.2 P-15(2026-10-03) — 「vX.Y.Z」形式のタグだけを降順で返す(D-Drive 以外の
        // パッケージ用。UniTask の "2.5.11" のような v 無しのタグは版上げに使うと `#ref` の表記が合わなくなる
        // ため「タグ運用ではない」として除外する)。
        public static List<Version> ParseVersionTags(string lsRemoteOutput) => ParseCore(lsRemoteOutput, true);

        // [42_distribution.md] §4.2 P-15(2026-10-03、レビュー PC-R-02) — 元のタグ名を保持した一覧(降順 = SemVer の優先順位。
        // 正式版はプレリリースより新しい)。manifest の `#ref` / `Client.Add` にはここの `Name` をそのまま使う
        // (`v1.5.0-rc.1` を `v1.5.0` に丸めない)。`requireVPrefix` の意味は `ParseVersionTags` と同じ。
        public static List<GitTag> ParseTags(string lsRemoteOutput, bool requireVPrefix)
        {
            var result = new List<GitTag>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tagName in EnumerateTagNames(lsRemoteOutput))
            {
                if (GitTag.TryParse(tagName, requireVPrefix, out var tag) && seen.Add(tag.Name))
                {
                    result.Add(tag);
                }
            }

            result.Sort((a, b) =>
            {
                var cmp = GitTag.Compare(b, a); // 降順
                return cmp != 0 ? cmp : string.CompareOrdinal(a.Name, b.Name);
            });
            return result;
        }

        private static List<Version> ParseCore(string lsRemoteOutput, bool requireVPrefix)
        {
            var result = new List<Version>();
            foreach (var tagName in EnumerateTagNames(lsRemoteOutput))
            {
                var hasV = tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase);
                if (requireVPrefix && !hasV)
                {
                    continue;
                }

                var body = hasV ? tagName.Substring(1) : tagName;
                if (SemVer.TryParse(body, out var version))
                {
                    result.Add(version);
                }
            }

            result.Sort((a, b) => b.CompareTo(a)); // 降順
            return result;
        }

        // `refs/tags/<名前>` の名前だけを取り出す(peeled 行・タグ以外の行は除く)。
        private static IEnumerable<string> EnumerateTagNames(string lsRemoteOutput)
        {
            if (string.IsNullOrEmpty(lsRemoteOutput))
            {
                yield break;
            }

            const string tagsPrefix = "refs/tags/";
            const string peeledSuffix = "^{}";

            var lines = lsRemoteOutput.Split('\n');
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r').Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var tabIndex = line.IndexOf('\t');
                if (tabIndex < 0)
                {
                    // 一部の git 実装/転送はタブではなく空白区切りで返すことがあるため、フォールバックで対応する。
                    tabIndex = line.IndexOf(' ');
                    if (tabIndex < 0)
                    {
                        continue;
                    }
                }

                var refName = line.Substring(tabIndex + 1).Trim();
                if (!refName.StartsWith(tagsPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var tagName = refName.Substring(tagsPrefix.Length);
                if (tagName.EndsWith(peeledSuffix, StringComparison.Ordinal))
                {
                    // peeled 行(注釈付きタグの実体コミット)。対応する非 peeled 行が別途あるので除外する。
                    continue;
                }

                yield return tagName;
            }
        }
    }
}
