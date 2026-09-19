using DDrive.Editor.Spec;
using DDrive.Foundation.Identity;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    public class SpecSheetParserTests
    {
        private const string AssetHeader = "種別,カテゴリ,識別子,表示名,状態,担当,仕様,備考\n";

        [Test]
        public void ParseAssetSheet_ValidRow_IsParsed()
        {
            var csv = AssetHeader + "Se,Player/Attack,Slash,剣の斬撃音,仮,よしだ,https://example/spec,3段階で音程を変える\n";

            var result = SpecSheetParser.ParseAssetSheet(csv);

            Assert.AreEqual(1, result.Rows.Count);
            Assert.AreEqual(0, result.Issues.Count);
            var row = result.Rows[0];
            Assert.AreEqual(AssetType.Se, row.Type);
            Assert.AreEqual("Player/Attack", row.Category);
            Assert.AreEqual("Slash", row.Identifier);
            Assert.AreEqual("剣の斬撃音", row.DisplayName);
            Assert.AreEqual("仮", row.Status);
            Assert.AreEqual("よしだ", row.Assignee);
            Assert.AreEqual("https://example/spec", row.SpecLink);
            Assert.AreEqual("3段階で音程を変える", row.Note);
            Assert.AreEqual("Se::Slash", row.Key);
        }

        [Test]
        public void ParseAssetSheet_CommentAndBlankRows_AreSkipped()
        {
            var csv = AssetHeader
                + "# 以下メモ\n"
                + ",,,,,,,\n"
                + "Se,Player,Slash,斬撃,仮,,,\n";

            var result = SpecSheetParser.ParseAssetSheet(csv);

            Assert.AreEqual(1, result.Rows.Count);
        }

        [Test]
        public void ParseAssetSheet_UnknownType_IsReportedAsIssue()
        {
            var csv = AssetHeader + "Foo,Player,Slash,斬撃,仮,,,\n";

            var result = SpecSheetParser.ParseAssetSheet(csv);

            Assert.AreEqual(0, result.Rows.Count);
            Assert.AreEqual(1, result.Issues.Count);
            StringAssert.Contains("種別", result.Issues[0].Message);
        }

        [Test]
        public void ParseAssetSheet_InvalidIdentifier_IsReportedAsIssue()
        {
            var csv = AssetHeader + "Se,Player,sword_slash,斬撃,仮,,,\n";

            var result = SpecSheetParser.ParseAssetSheet(csv);

            Assert.AreEqual(0, result.Rows.Count);
            Assert.AreEqual(1, result.Issues.Count);
            StringAssert.Contains("識別子", result.Issues[0].Message);
        }

        [Test]
        public void ParseAssetSheet_DuplicateTypeAndIdentifier_IsReportedAsIssue()
        {
            var csv = AssetHeader
                + "Se,Player,Slash,斬撃A,仮,,,\n"
                + "Se,Enemy,Slash,斬撃B,仮,,,\n";

            var result = SpecSheetParser.ParseAssetSheet(csv);

            Assert.AreEqual(1, result.Rows.Count, "先勝ちの1行だけが有効になる");
            Assert.AreEqual(1, result.Issues.Count);
            StringAssert.Contains("重複", result.Issues[0].Message);
        }

        [Test]
        public void ParseTuningSheet_ValidRow_IsParsed()
        {
            var csv = "キー,値,型,最小,最大,単位,説明\n"
                + "Influence/FanBase,1.0,float,0,10,,ファン1人あたりの影響力\n";

            var result = SpecSheetParser.ParseTuningSheet(csv);

            Assert.AreEqual(1, result.Rows.Count);
            var row = result.Rows[0];
            Assert.AreEqual("Influence/FanBase", row.Key);
            Assert.AreEqual("1.0", row.RawValue);
            Assert.AreEqual("float", row.RawType);
            Assert.AreEqual("0", row.RawMin);
            Assert.AreEqual("10", row.RawMax);
        }

        [Test]
        public void ParseTuningSheet_DuplicateKey_IsReportedAsIssue()
        {
            var csv = "キー,値,型,最小,最大,単位,説明\n"
                + "Combat/HitStopSec,0.05,float,,,,\n"
                + "Combat/HitStopSec,0.1,float,,,,\n";

            var result = SpecSheetParser.ParseTuningSheet(csv);

            Assert.AreEqual(1, result.Rows.Count);
            Assert.AreEqual(1, result.Issues.Count);
        }
    }
}
