using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 手順 1/§6 P-8(2026-09-20) — `CHANGELOG.md`(Keep a Changelog 形式、
    // `## [X.Y.Z] - date` 見出し)を版ごとの節に分解し、「前回版(排他) → 現在版(含む)」の範囲を
    // 切り出す純関数。Unity API に依存しないので EditMode テストから直接検証できる。
    public static class ChangelogRangeReader
    {
        public sealed class VersionSection
        {
            public readonly string Version;
            public readonly string HeaderLine;
            public readonly string Body;

            public VersionSection(string version, string headerLine, string body)
            {
                Version = version;
                HeaderLine = headerLine;
                Body = body;
            }
        }

        private static readonly Regex HeaderPattern = new(@"^##\s*\[(?<version>[^\]]+)\]", RegexOptions.Compiled);

        public static List<VersionSection> ParseSections(string changelogText)
        {
            var sections = new List<VersionSection>();
            if (string.IsNullOrEmpty(changelogText))
            {
                return sections;
            }

            var lines = changelogText.Replace("\r\n", "\n").Split('\n');
            string currentVersion = null;
            string currentHeader = null;
            var body = new StringBuilder();

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var match = HeaderPattern.Match(line);
                if (match.Success)
                {
                    if (currentVersion != null)
                    {
                        sections.Add(new VersionSection(currentVersion, currentHeader, body.ToString()));
                    }

                    currentVersion = match.Groups["version"].Value;
                    currentHeader = line;
                    body.Clear();
                    continue;
                }

                if (currentVersion != null)
                {
                    body.Append(line).Append('\n');
                }
            }

            if (currentVersion != null)
            {
                sections.Add(new VersionSection(currentVersion, currentHeader, body.ToString()));
            }

            return sections;
        }

        // fromVersionExclusive: 前回適用した版(null/空/パース不能 = 「未適用」→ 先頭〔最も古い節〕まで含める)。
        // toVersionInclusive: 現在の版(パース不能な見出し、例えば "Unreleased" は範囲に含めない。
        // 消費側が実際に導入できるのはリリース済みの版までのため)。
        // 戻り値は `sections` と同じ順序(CHANGELOG.md の慣習どおり新しい版が先)。
        public static List<VersionSection> ExtractRange(IReadOnlyList<VersionSection> sections, string fromVersionExclusive, string toVersionInclusive)
        {
            var result = new List<VersionSection>();
            if (sections == null)
            {
                return result;
            }

            var toParsed = SemVer.TryParse(toVersionInclusive, out var to);
            var fromParsed = SemVer.TryParse(fromVersionExclusive, out var from);

            foreach (var section in sections)
            {
                if (!SemVer.TryParse(section.Version, out var v))
                {
                    continue; // "Unreleased" 等、SemVer で解釈できない見出しは範囲に含めない。
                }

                if (toParsed && v.CompareTo(to) > 0)
                {
                    continue; // 現在の版より新しい(まだ導入していない)節は対象外。
                }

                if (fromParsed && v.CompareTo(from) <= 0)
                {
                    continue; // 前回適用済みの版以下は対象外。
                }

                result.Add(section);
            }

            return result;
        }
    }
}
