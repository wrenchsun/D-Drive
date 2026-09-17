using System.Linq;
using DDrive.Editor.Spec;
using DDrive.Foundation.Identity;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [32_spec_web.md] §3.1/§3.2/§5.1 W-9 — Web API(GAS)の JSON 応答のパース。
    // ネットワークに出ず、JSON 文字列を直接注入して検証する(5-13 の要件を継承)。
    public class SpecWebParserTests
    {
        [Test]
        public void ParseAssets_ValidItem_ReturnsRowWithSpecLink()
        {
            // 2026-09-17([41] P1-7): 発注スキーマ(O-1)の新フィールド名・新 3 値の状態で書く
            // (`contractor` / `referenceMd` / 3 値の status。旧 `assignee` / `note` / 旧 4 値ではない)。
            const string json = "{\"ok\":true,\"status\":200,\"items\":[" +
                "{\"id\":\"Se::Slash\",\"assetType\":\"Se\",\"category\":\"Player\",\"identifier\":\"Slash\"," +
                "\"displayName\":\"斬撃音\",\"status\":\"納品済\",\"orderer\":\"やまぐち\",\"contractor\":\"よしだ\"," +
                "\"referenceMd\":\"備考\",\"archived\":false}" +
                "]}";

            var result = SpecWebParser.ParseAssets(json, "https://example.com/spec");

            Assert.AreEqual(1, result.Rows.Count);
            var row = result.Rows[0];
            Assert.AreEqual(AssetType.Se, row.Type);
            Assert.AreEqual("Slash", row.Identifier);
            Assert.AreEqual("Player", row.Category);
            Assert.AreEqual("斬撃音", row.DisplayName);
            Assert.AreEqual("納品済", row.Status);
            Assert.AreEqual("よしだ", row.Assignee);
            Assert.AreEqual("備考", row.Note);
            // 2026-09-14: PR #50(O-13)の Web 側ディープリンク(`?page=order&id=...`)に合わせた形式
            // (旧 `#/assets/<id>` ハッシュ形式は Web の SPA が location.hash に依存しないため機能しなかった)。
            Assert.AreEqual("https://example.com/spec?page=order&id=Se%3A%3ASlash", row.SpecLink);
        }

        [Test]
        public void ParseAssets_LegacyKeysOnly_FallsBackToAssigneeAndNote()
        {
            // GAS 側の物理移行(migrateLegacyOrdersToNewSchema)をまだ実行していないデータが
            // 残っていても読めること(Migration.js の specWebNormalizeLegacyOrderItem_ と同じ規則)。
            const string json = "{\"ok\":true,\"items\":[" +
                "{\"id\":\"Se::Slash\",\"assetType\":\"Se\",\"identifier\":\"Slash\"," +
                "\"displayName\":\"斬撃音\",\"assignee\":\"よしだ\",\"note\":\"旧備考\"}" +
                "]}";

            var result = SpecWebParser.ParseAssets(json);

            Assert.AreEqual("よしだ", result.Rows[0].Assignee);
            Assert.AreEqual("旧備考", result.Rows[0].Note);
        }

        [Test]
        public void ParseAssets_NewKeysEmptyWithLegacyValues_PrefersLegacyValues()
        {
            // 新キーが空文字で存在する(正規化を経ていないデータ)ときは旧キーを見る。
            const string json = "{\"ok\":true,\"items\":[" +
                "{\"id\":\"Se::Slash\",\"assetType\":\"Se\",\"identifier\":\"Slash\",\"displayName\":\"斬撃音\"," +
                "\"contractor\":\"\",\"referenceMd\":\"\",\"assignee\":\"よしだ\",\"note\":\"旧備考\"}" +
                "]}";

            var result = SpecWebParser.ParseAssets(json);

            Assert.AreEqual("よしだ", result.Rows[0].Assignee);
            Assert.AreEqual("旧備考", result.Rows[0].Note);
        }

        [Test]
        public void ParseAssets_NewKeysWin_WhenBothPresent()
        {
            const string json = "{\"ok\":true,\"items\":[" +
                "{\"id\":\"Se::Slash\",\"assetType\":\"Se\",\"identifier\":\"Slash\",\"displayName\":\"斬撃音\"," +
                "\"contractor\":\"あたらしい\",\"referenceMd\":\"新備考\",\"assignee\":\"ふるい\",\"note\":\"旧備考\"}" +
                "]}";

            var result = SpecWebParser.ParseAssets(json);

            Assert.AreEqual("あたらしい", result.Rows[0].Assignee);
            Assert.AreEqual("新備考", result.Rows[0].Note);
        }

        [Test]
        public void ParseAssets_NoContractorOrReference_RowFieldsAreEmpty()
        {
            // 受注者・リファレンス未入力の発注(新スキーマ)。行としては空で読み、
            // 「空で既存値を上書きしない」のは SpecDiffService / SpecSyncService 側の責務
            // (SpecDiffServiceTests の P1-7 回帰テスト参照)。
            const string json = "{\"ok\":true,\"items\":[" +
                "{\"id\":\"Se::Slash\",\"assetType\":\"Se\",\"identifier\":\"Slash\",\"displayName\":\"斬撃音\"," +
                "\"status\":\"発注済\",\"contractor\":\"\",\"referenceMd\":\"\"}" +
                "]}";

            var result = SpecWebParser.ParseAssets(json);

            Assert.AreEqual(string.Empty, result.Rows[0].Assignee);
            Assert.AreEqual(string.Empty, result.Rows[0].Note);
        }

        [Test]
        public void ParseAssets_NoHumanAppUrl_SpecLinkIsEmpty()
        {
            const string json = "{\"ok\":true,\"items\":[{\"id\":\"Se::Slash\",\"assetType\":\"Se\",\"identifier\":\"Slash\",\"displayName\":\"斬撃音\"}]}";

            var result = SpecWebParser.ParseAssets(json);

            Assert.AreEqual(string.Empty, result.Rows[0].SpecLink);
        }

        [Test]
        public void ParseAssets_HumanAppUrlHasExistingQuery_AppendsWithAmpersand()
        {
            // ManualUrlBuilder.BuildWebUrl と同じ挙動(AppendQuery 共有): 既にクエリがあれば "&" で連結する。
            const string json = "{\"ok\":true,\"items\":[{\"id\":\"Se::Slash\",\"assetType\":\"Se\",\"identifier\":\"Slash\",\"displayName\":\"斬撃音\"}]}";

            var result = SpecWebParser.ParseAssets(json, "https://example.com/spec?foo=1");

            Assert.AreEqual("https://example.com/spec?foo=1&page=order&id=Se%3A%3ASlash", result.Rows[0].SpecLink);
        }

        [Test]
        public void ParseAssets_IdIsEscaped()
        {
            // Uri.EscapeDataString で "::" 等が正しくエスケープされることを確認する
            // (OrderLinkLogic.buildOrderUrl の encodeURIComponent と同じ値になる想定。テストの %3A 参照)。
            const string json = "{\"ok\":true,\"items\":[{\"id\":\"Se::Slash Sound\",\"assetType\":\"Se\",\"identifier\":\"Slash\",\"displayName\":\"斬撃音\"}]}";

            var result = SpecWebParser.ParseAssets(json, "https://example.com/spec");

            Assert.AreEqual("https://example.com/spec?page=order&id=Se%3A%3ASlash%20Sound", result.Rows[0].SpecLink);
        }

        [Test]
        public void ParseAssets_ArchivedItem_IsSkipped()
        {
            const string json = "{\"ok\":true,\"items\":[{\"id\":\"Se::Slash\",\"assetType\":\"Se\",\"identifier\":\"Slash\",\"displayName\":\"斬撃音\",\"archived\":true}]}";

            var result = SpecWebParser.ParseAssets(json);

            Assert.AreEqual(0, result.Rows.Count);
        }

        [Test]
        public void ParseAssets_UnknownAssetType_IsIssue()
        {
            const string json = "{\"ok\":true,\"items\":[{\"id\":\"x\",\"assetType\":\"NoSuchType\",\"identifier\":\"Slash\",\"displayName\":\"x\"}]}";

            var result = SpecWebParser.ParseAssets(json);

            Assert.AreEqual(0, result.Rows.Count);
            Assert.IsTrue(result.Issues.Any(i => i.Message.Contains("NoSuchType")));
        }

        [Test]
        public void ParseAssets_DuplicateKey_IsIssue()
        {
            const string json = "{\"ok\":true,\"items\":[" +
                "{\"id\":\"a\",\"assetType\":\"Se\",\"identifier\":\"Slash\",\"displayName\":\"a\"}," +
                "{\"id\":\"b\",\"assetType\":\"Se\",\"identifier\":\"Slash\",\"displayName\":\"b\"}" +
                "]}";

            var result = SpecWebParser.ParseAssets(json);

            Assert.AreEqual(1, result.Rows.Count);
            Assert.IsTrue(result.Issues.Any(i => i.Message.Contains("重複")));
        }

        [Test]
        public void ParseAssets_ErrorEnvelope_ReturnsNoRowsWithIssue()
        {
            const string json = "{\"ok\":false,\"status\":401,\"error\":\"トークンが無効です\"}";

            var result = SpecWebParser.ParseAssets(json);

            Assert.AreEqual(0, result.Rows.Count);
            Assert.IsTrue(result.Issues.Any(i => i.Message.Contains("401")));
        }

        [Test]
        public void ParseAssets_InvalidJson_DoesNotThrow()
        {
            SpecParseResult<SpecAssetRow> result = null;
            Assert.DoesNotThrow(() => result = SpecWebParser.ParseAssets("not json"));
            Assert.AreEqual(0, result.Rows.Count);
            Assert.IsTrue(result.Issues.Count > 0);
        }

        [Test]
        public void ParseTuningScalars_FloatEntry_ReturnsRow()
        {
            const string json = "{\"ok\":true,\"items\":{\"Combat/HitStopSec\":{" +
                "\"kind\":\"scalar\",\"valueType\":\"float\",\"value\":0.05,\"enumOptions\":[]," +
                "\"min\":0,\"max\":0.3,\"unit\":\"秒\",\"description\":\"ヒットストップ\"}}}";

            var result = SpecWebParser.ParseTuningScalars(json);

            Assert.AreEqual(1, result.Rows.Count);
            var row = result.Rows[0];
            Assert.AreEqual("Combat/HitStopSec", row.Key);
            Assert.AreEqual("float", row.RawType);
            Assert.AreEqual("0.05", row.RawValue);
            Assert.AreEqual("0", row.RawMin);
            Assert.AreEqual("0.3", row.RawMax);
            Assert.AreEqual("秒", row.Unit);
        }

        [Test]
        public void ParseTuningScalars_EnumEntry_ReturnsEnumOptions()
        {
            const string json = "{\"ok\":true,\"items\":{\"Difficulty/Level\":{" +
                "\"kind\":\"scalar\",\"valueType\":\"enum\",\"value\":\"Normal\"," +
                "\"enumOptions\":[\"Easy\",\"Normal\",\"Hard\"]}}}";

            var result = SpecWebParser.ParseTuningScalars(json);

            var row = result.Rows.Single();
            Assert.AreEqual("enum", row.RawType);
            Assert.AreEqual("Normal", row.RawValue);
            CollectionAssert.AreEqual(new[] { "Easy", "Normal", "Hard" }, row.RawEnumOptions);
        }

        [Test]
        public void ParseTuningScalars_TableKindEntry_IsSkipped()
        {
            const string json = "{\"ok\":true,\"items\":{\"Enemy/Params\":{\"kind\":\"table\",\"columns\":[],\"rows\":[]}}}";

            var result = SpecWebParser.ParseTuningScalars(json);

            Assert.AreEqual(0, result.Rows.Count);
        }

        [Test]
        public void ParseTuningTables_ValidEntry_ReturnsRawJObject()
        {
            const string json = "{\"ok\":true,\"items\":{\"Enemy/Params\":{" +
                "\"kind\":\"table\"," +
                "\"columns\":[{\"key\":\"Hp\",\"valueType\":\"int\",\"min\":1,\"max\":9999,\"unit\":\"\",\"enumOptions\":[]}]," +
                "\"rows\":[{\"rowId\":\"Slime\",\"cells\":{\"Hp\":10},\"comments\":[]}]," +
                "\"locked\":false}}}";

            var result = SpecWebParser.ParseTuningTables(json);

            Assert.AreEqual(1, result.Rows.Count);
            var row = result.Rows[0];
            Assert.AreEqual("Enemy/Params", row.Key);
            Assert.IsNotNull(row.Raw);
            Assert.AreEqual("table", (string)row.Raw["kind"]);
            Assert.AreEqual(1, ((Newtonsoft.Json.Linq.JArray)row.Raw["columns"]).Count);
        }
    }
}
