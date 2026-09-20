using DDrive.Editor.Update;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // [42_distribution.md] §4.2 手順 1/§6 P-8(2026-09-20) — CHANGELOG.md の版ごとの節への分解と、
    // 「前回版(排他) → 現在版(含む)」の範囲抽出の EditMode テスト。実 CHANGELOG.md には触れず、
    // Keep a Changelog 形式を模した固定文字列だけを使う。
    public class ChangelogRangeReaderTests
    {
        private const string SampleChangelog =
            "# Changelog\n" +
            "\n" +
            "## [Unreleased]\n" +
            "\n" +
            "### 追加\n" +
            "- 未リリースの変更\n" +
            "\n" +
            "## [1.2.0] - 2026-09-21\n" +
            "\n" +
            "### 互換性\n" +
            "\n" +
            "- 追加のみ\n" +
            "\n" +
            "### 追加\n" +
            "\n" +
            "- 機能 B\n" +
            "\n" +
            "## [1.1.0] - 2026-09-20\n" +
            "\n" +
            "### 互換性\n" +
            "\n" +
            "- 破壊あり(docs/migrations/v1.1.0.md 参照)\n" +
            "\n" +
            "### 変更\n" +
            "\n" +
            "- 機能 A\n" +
            "\n" +
            "## [1.0.0] - 2026-09-19\n" +
            "\n" +
            "### 互換性\n" +
            "\n" +
            "- 破壊なし(初回リリース)\n";

        [Test]
        public void ParseSections_SplitsByVersionHeading_InDocumentOrder()
        {
            var sections = ChangelogRangeReader.ParseSections(SampleChangelog);

            Assert.AreEqual(4, sections.Count);
            Assert.AreEqual("Unreleased", sections[0].Version);
            Assert.AreEqual("1.2.0", sections[1].Version);
            Assert.AreEqual("1.1.0", sections[2].Version);
            Assert.AreEqual("1.0.0", sections[3].Version);
            StringAssert.Contains("機能 B", sections[1].Body);
        }

        [Test]
        public void ParseSections_EmptyOrNull_ReturnsEmptyList()
        {
            Assert.IsEmpty(ChangelogRangeReader.ParseSections(null));
            Assert.IsEmpty(ChangelogRangeReader.ParseSections(string.Empty));
        }

        [Test]
        public void ExtractRange_FromOlderVersion_ToCurrentVersion_ReturnsInBetweenSectionsOnly()
        {
            var sections = ChangelogRangeReader.ParseSections(SampleChangelog);

            var range = ChangelogRangeReader.ExtractRange(sections, fromVersionExclusive: "1.0.0", toVersionInclusive: "1.2.0");

            Assert.AreEqual(2, range.Count, "1.0.0(排他) より新しく 1.2.0(含む) 以下の節だけ(Unreleased は除く)");
            Assert.AreEqual("1.2.0", range[0].Version);
            Assert.AreEqual("1.1.0", range[1].Version);
        }

        [Test]
        public void ExtractRange_FromEmpty_TreatsAsNeverApplied_IncludesFromBeginning()
        {
            var sections = ChangelogRangeReader.ParseSections(SampleChangelog);

            var range = ChangelogRangeReader.ExtractRange(sections, fromVersionExclusive: null, toVersionInclusive: "1.1.0");

            Assert.AreEqual(2, range.Count);
            Assert.AreEqual("1.1.0", range[0].Version);
            Assert.AreEqual("1.0.0", range[1].Version);
        }

        [Test]
        public void ExtractRange_ExcludesUnreleased_EvenWhenNewerThanCurrent()
        {
            var sections = ChangelogRangeReader.ParseSections(SampleChangelog);

            var range = ChangelogRangeReader.ExtractRange(sections, fromVersionExclusive: "1.1.0", toVersionInclusive: "1.2.0");

            Assert.IsFalse(range.Exists(s => s.Version == "Unreleased"));
        }

        [Test]
        public void ExtractRange_SameFromAndTo_ReturnsEmpty()
        {
            var sections = ChangelogRangeReader.ParseSections(SampleChangelog);

            var range = ChangelogRangeReader.ExtractRange(sections, fromVersionExclusive: "1.2.0", toVersionInclusive: "1.2.0");

            Assert.IsEmpty(range);
        }
    }
}
