using DDrive.Editor.Tuning;
using DDrive.Runtime.Tuning;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // M-2a(2026-09-27。[11_tasks.md] M-2 チケット、[09_editor_tools.md] §「Tuning ウィンドウ」) —
    // TuningEditorWindow のカテゴリ分類(TuningCategoryGrouper)と Enum 行の Popup⇔ValueString 変換
    // (TuningEnumFieldLogic)を GUI を介さず検証する。
    public class TuningCategoryGrouperTests
    {
        private static TuningEntry Entry(string key) => new() { Key = key, Type = TuningValueType.Float };

        [Test]
        public void Group_SplitsByPrefix_AndCountsMatch()
        {
            var entries = new[]
            {
                Entry("Player/MoveSpeedInitial"),
                Entry("Player/MoveSpeedMax"),
                Entry("Interact/Range"),
                Entry("Match/DurationSec"),
            };

            var categories = TuningCategoryGrouper.Group(entries);

            Assert.AreEqual(3, categories.Count);

            var player = Find(categories, "Player");
            Assert.AreEqual(2, player.Count);

            var interact = Find(categories, "Interact");
            Assert.AreEqual(1, interact.Count);

            var match = Find(categories, "Match");
            Assert.AreEqual(1, match.Count);
        }

        [Test]
        public void Group_KeyWithoutSlash_GoesToUncategorized()
        {
            var entries = new[] { Entry("NoPrefixKey"), Entry("Player/MoveSpeedMax") };

            var categories = TuningCategoryGrouper.Group(entries);

            var uncategorized = Find(categories, TuningCategoryGrouper.Uncategorized);
            Assert.AreEqual(1, uncategorized.Count);
        }

        [Test]
        public void Group_Uncategorized_IsSortedLast()
        {
            var entries = new[] { Entry("NoPrefixKey"), Entry("Zzz/Key"), Entry("Aaa/Key") };

            var categories = TuningCategoryGrouper.Group(entries);

            Assert.AreEqual("Aaa", categories[0].Name);
            Assert.AreEqual("Zzz", categories[1].Name);
            Assert.AreEqual(TuningCategoryGrouper.Uncategorized, categories[2].Name);
        }

        [Test]
        public void Group_EmptyEntries_ReturnsEmpty()
        {
            var categories = TuningCategoryGrouper.Group(System.Array.Empty<TuningEntry>());
            Assert.AreEqual(0, categories.Count);
        }

        [Test]
        public void DisplayName_ReturnsPartAfterSlash()
        {
            Assert.AreEqual("MoveSpeedMax", TuningCategoryGrouper.DisplayName("Player/MoveSpeedMax"));
            Assert.AreEqual("NoPrefixKey", TuningCategoryGrouper.DisplayName("NoPrefixKey"));
        }

        [Test]
        public void ResolveSelectedIndex_ReturnsMatchingIndex()
        {
            var options = new[] { "Easy", "Normal", "Hard" };
            Assert.AreEqual(1, TuningEnumFieldLogic.ResolveSelectedIndex(options, "Normal"));
        }

        [Test]
        public void ResolveSelectedIndex_UnknownValue_ReturnsZero()
        {
            var options = new[] { "Easy", "Normal", "Hard" };
            Assert.AreEqual(0, TuningEnumFieldLogic.ResolveSelectedIndex(options, "Unknown"));
        }

        [Test]
        public void ResolveValueAt_WritesExpectedOption()
        {
            var options = new[] { "Melee", "Ranged" };
            Assert.AreEqual("Ranged", TuningEnumFieldLogic.ResolveValueAt(options, 1));
        }

        [Test]
        public void ResolveValueAt_OutOfRange_ReturnsEmpty()
        {
            var options = new[] { "Melee", "Ranged" };
            Assert.AreEqual(string.Empty, TuningEnumFieldLogic.ResolveValueAt(options, 5));
        }

        private static TuningCategoryGrouper.Category Find(System.Collections.Generic.IReadOnlyList<TuningCategoryGrouper.Category> categories, string name)
        {
            foreach (var category in categories)
            {
                if (category.Name == name)
                {
                    return category;
                }
            }

            Assert.Fail($"category '{name}' not found");
            return default;
        }
    }
}
