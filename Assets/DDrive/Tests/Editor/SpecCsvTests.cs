using DDrive.Editor.Spec;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    public class SpecCsvTests
    {
        [Test]
        public void Parse_SimpleRows_SplitsByCommaAndNewline()
        {
            var rows = SpecCsv.Parse("a,b,c\n1,2,3\n");

            Assert.AreEqual(2, rows.Count);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, rows[0]);
            CollectionAssert.AreEqual(new[] { "1", "2", "3" }, rows[1]);
        }

        [Test]
        public void Parse_QuotedCellWithCommaAndNewline_KeepsCellIntact()
        {
            var rows = SpecCsv.Parse("種別,備考\nSe,\"3 段階で音程を変える, 3段目だけ強め\n(次の行)\"\n");

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("3 段階で音程を変える, 3段目だけ強め\n(次の行)", rows[1][1]);
        }

        [Test]
        public void Parse_EscapedDoubleQuote_Unescapes()
        {
            var rows = SpecCsv.Parse("note\n\"she said \"\"hi\"\"\"\n");

            Assert.AreEqual("she said \"hi\"", rows[1][0]);
        }

        [Test]
        public void Parse_NoTrailingNewline_StillReturnsLastRow()
        {
            var rows = SpecCsv.Parse("a,b\n1,2");

            Assert.AreEqual(2, rows.Count);
            CollectionAssert.AreEqual(new[] { "1", "2" }, rows[1]);
        }

        [Test]
        public void IsIgnoredRow_CommentAndBlankRows_AreIgnored()
        {
            Assert.IsTrue(SpecCsv.IsIgnoredRow(new[] { "# メモ", "", "" }));
            Assert.IsTrue(SpecCsv.IsIgnoredRow(new[] { "", "", "" }));
            Assert.IsFalse(SpecCsv.IsIgnoredRow(new[] { "Se", "Player", "Slash" }));
        }

        [Test]
        public void BuildGvizCsvUrl_ExtractsIdFromFullEditUrl()
        {
            var url = SpecCsv.BuildGvizCsvUrl("https://docs.google.com/spreadsheets/d/ABC123/edit#gid=0", "アセット");

            StringAssert.Contains("/d/ABC123/gviz/tq", url);
            StringAssert.Contains("tqx=out:csv", url);
        }

        [Test]
        public void BuildGvizCsvUrl_PlainId_BuildsSameShapeUrl()
        {
            var url = SpecCsv.BuildGvizCsvUrl("ABC123", "アセット");

            StringAssert.Contains("/d/ABC123/gviz/tq", url);
        }
    }
}
