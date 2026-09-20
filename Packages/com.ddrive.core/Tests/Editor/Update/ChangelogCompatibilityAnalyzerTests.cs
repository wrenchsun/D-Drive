using DDrive.Editor.Update;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // [42_distribution.md] §4.1/§6 P-8(2026-09-20) — CHANGELOG 1 版分の本文から `### 互換性` 節を
    // 取り出し、「破壊あり」を検出する純関数の EditMode テスト。
    public class ChangelogCompatibilityAnalyzerTests
    {
        [Test]
        public void HasBreakingChange_WithBreakingMarker_ReturnsTrue()
        {
            const string body = "\n### 互換性\n\n- 破壊あり(docs/migrations/v2.md 参照)\n\n### 追加\n\n- なにか\n";

            Assert.IsTrue(ChangelogCompatibilityAnalyzer.HasBreakingChange(body));
        }

        [Test]
        public void HasBreakingChange_WithoutBreakingMarker_ReturnsFalse()
        {
            const string body = "\n### 互換性\n\n- 追加のみ\n\n### 追加\n\n- なにか\n";

            Assert.IsFalse(ChangelogCompatibilityAnalyzer.HasBreakingChange(body));
        }

        [Test]
        public void HasBreakingChange_NoCompatibilityHeading_ReturnsFalse()
        {
            const string body = "\n### 追加\n\n- なにか(互換性節が無い)\n";

            Assert.IsFalse(ChangelogCompatibilityAnalyzer.HasBreakingChange(body));
        }

        [Test]
        public void HasBreakingChange_NullOrEmpty_ReturnsFalse()
        {
            Assert.IsFalse(ChangelogCompatibilityAnalyzer.HasBreakingChange(null));
            Assert.IsFalse(ChangelogCompatibilityAnalyzer.HasBreakingChange(string.Empty));
        }

        [Test]
        public void ExtractCompatibilityText_StopsAtNextHeading()
        {
            const string body = "\n### 互換性\n\n- 破壊なし\n\n### 追加\n\n- 破壊あり(この行は互換性節の外なので含まれない)\n";

            var text = ChangelogCompatibilityAnalyzer.ExtractCompatibilityText(body);

            StringAssert.Contains("破壊なし", text);
            StringAssert.DoesNotContain("この行は互換性節の外", text);
            Assert.IsFalse(ChangelogCompatibilityAnalyzer.HasBreakingChange(body), "「破壊あり」の文字列があっても互換性節の外なら検出しない");
        }

        [Test]
        public void ExtractCompatibilityText_StopsAtNextVersionHeading_WhenNoOtherH3Follows()
        {
            const string body = "\n### 互換性\n\n- 破壊なし\n";

            var text = ChangelogCompatibilityAnalyzer.ExtractCompatibilityText(body);

            StringAssert.Contains("破壊なし", text);
        }
    }
}
