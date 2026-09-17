using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Spec;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Tuning;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Editor
{
    public class SpecSyncServiceTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempSpecSyncGameData";
        private const string TuningTablePath = TestRoot + "/TestTuningTable.asset";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            }
        }

        private static TuningTable CreateTuningTable()
        {
            if (!AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "TempSpecSyncGameData");
            }

            var table = ScriptableObject.CreateInstance<TuningTable>();
            AssetDatabase.CreateAsset(table, TuningTablePath);
            return table;
        }

        [Test]
        public void ApplyTuning_ValidRows_PopulatesEntries()
        {
            var table = CreateTuningTable();
            var csv = "キー,値,型,最小,最大,単位,説明\n"
                + "Combat/HitStopSec,0.05,float,0,0.3,秒,ヒットストップの長さ\n"
                + "Combat/ComboMax,3,int,,,,\n"
                + "Debug/Enabled,true,bool,,,,\n"
                + "Debug/Label,テスト,string,,,,\n";
            var parsed = SpecSheetParser.ParseTuningSheet(csv);

            SpecSyncService.ApplyTuning(parsed, table);

            Assert.AreEqual(4, table.Entries.Length);
            var hitStop = table.Entries.Single(e => e.Key == "Combat/HitStopSec");
            Assert.AreEqual(TuningValueType.Float, hitStop.Type);
            Assert.AreEqual(0.05f, hitStop.ValueFloat, 0.0001f);
            Assert.AreEqual(0f, hitStop.Min);
            Assert.AreEqual(0.3f, hitStop.Max);

            var comboMax = table.Entries.Single(e => e.Key == "Combat/ComboMax");
            Assert.AreEqual(3, comboMax.ValueInt);

            var enabled = table.Entries.Single(e => e.Key == "Debug/Enabled");
            Assert.IsTrue(enabled.ValueBool);

            var label = table.Entries.Single(e => e.Key == "Debug/Label");
            Assert.AreEqual("テスト", label.ValueString);

            Assert.IsTrue(table.TryFindIndex("Combat/HitStopSec", out _));
        }

        [Test]
        public void ApplyTuning_UnparsableValue_SkipsRowAndWarns()
        {
            var table = CreateTuningTable();
            var csv = "キー,値,型,最小,最大,単位,説明\n"
                + "Combat/Broken,notanumber,float,,,,\n";
            var parsed = SpecSheetParser.ParseTuningSheet(csv);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Combat/Broken.*"));
            SpecSyncService.ApplyTuning(parsed, table);

            Assert.AreEqual(0, table.Entries.Length);
        }

        [Test]
        public void ApplyTuning_EnumRow_PopulatesEnumOptions()
        {
            var table = CreateTuningTable();
            var parsed = new SpecParseResult<SpecTuningRow>();
            parsed.Rows.Add(new SpecTuningRow
            {
                RowNumber = 1,
                Key = "Difficulty/Level",
                RawValue = "Normal",
                RawType = "enum",
                RawEnumOptions = new[] { "Easy", "Normal", "Hard" },
            });

            SpecSyncService.ApplyTuning(parsed, table);

            var entry = table.Entries.Single();
            Assert.AreEqual(TuningValueType.Enum, entry.Type);
            Assert.AreEqual("Normal", entry.ValueString);
            CollectionAssert.AreEqual(new[] { "Easy", "Normal", "Hard" }, entry.EnumOptions);
        }

        [Test]
        public void ApplyTuning_EnumRow_ValueNotInOptions_SkipsRowAndWarns()
        {
            var table = CreateTuningTable();
            var parsed = new SpecParseResult<SpecTuningRow>();
            parsed.Rows.Add(new SpecTuningRow
            {
                RowNumber = 1,
                Key = "Difficulty/Level",
                RawValue = "Impossible",
                RawType = "enum",
                RawEnumOptions = new[] { "Easy", "Normal", "Hard" },
            });

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Difficulty/Level.*"));
            SpecSyncService.ApplyTuning(parsed, table);

            Assert.AreEqual(0, table.Entries.Length);
        }

        [Test]
        public void ApplyTuningTable_ValidRow_PopulatesTables()
        {
            var table = CreateTuningTable();
            var raw = JObject.Parse(
                "{\"kind\":\"table\"," +
                "\"columns\":[" +
                "{\"key\":\"Hp\",\"valueType\":\"int\",\"min\":1,\"max\":9999,\"unit\":\"\",\"enumOptions\":[]}," +
                "{\"key\":\"Type\",\"valueType\":\"enum\",\"min\":null,\"max\":null,\"unit\":\"\",\"enumOptions\":[\"Melee\",\"Ranged\"]}" +
                "]," +
                "\"rows\":[" +
                "{\"rowId\":\"Slime\",\"cells\":{\"Hp\":10,\"Type\":\"Melee\"},\"comments\":[]}," +
                "{\"rowId\":\"Archer\",\"cells\":{\"Hp\":20,\"Type\":\"Ranged\"},\"comments\":[]}" +
                "]," +
                "\"locked\":false}");

            var parsed = new SpecParseResult<SpecTuningTableRow>();
            parsed.Rows.Add(new SpecTuningTableRow { RowNumber = 1, Key = "Enemy/Params", Raw = raw });

            SpecSyncService.ApplyTuningTable(parsed, table);

            Assert.AreEqual(1, table.Tables.Length);
            var entry = table.Tables[0];
            Assert.AreEqual("Enemy/Params", entry.Key);
            Assert.AreEqual(2, entry.Columns.Length);
            Assert.AreEqual(2, entry.Rows.Length);

            var slime = entry.Rows.Single(r => r.RowId == "Slime");
            var hpCell = slime.Cells.Single(c => c.ColumnKey == "Hp");
            Assert.AreEqual(10, hpCell.I);
            var typeCell = slime.Cells.Single(c => c.ColumnKey == "Type");
            Assert.AreEqual("Melee", typeCell.S);

            Assert.IsTrue(table.TryFindTableIndex("Enemy/Params", out _));
        }

        [Test]
        public void ApplyTuningTable_DoesNotAffectExistingScalarEntries()
        {
            var table = CreateTuningTable();
            table.Entries = new[] { new TuningEntry { Key = "Combat/HitStopSec", Type = TuningValueType.Float, ValueFloat = 0.1f } };

            var raw = JObject.Parse("{\"kind\":\"table\",\"columns\":[],\"rows\":[],\"locked\":false}");
            var parsed = new SpecParseResult<SpecTuningTableRow>();
            parsed.Rows.Add(new SpecTuningTableRow { RowNumber = 1, Key = "Empty/Table", Raw = raw });

            SpecSyncService.ApplyTuningTable(parsed, table);

            Assert.AreEqual(1, table.Entries.Length, "ApplyTuningTable はスカラーの Entries に触れないはず");
            Assert.AreEqual(1, table.Tables.Length);
        }

        // ── 2026-09-17(docs/41_phase6_review_2026-09-17.md P1-2)の回帰テスト ──
        // GAS は HTTP ステータスを設定できず常に 200 を返すため、トークン未設定/無効(401)・許可外(403)・
        // レート制限でも取得は「成功」する。その応答(`ok:false`)で TuningTable を上書きすると
        // Entries/Tables が全消えになり、Tuning.g.cs が空で再生成されて TUNING.Xxx を参照している
        // 全コードがコンパイルエラーになっていた。

        [Test]
        public void ApplyTuning_ErrorEnvelopeResponse_KeepsExistingEntries()
        {
            var table = CreateTuningTable();
            table.Entries = new[] { new TuningEntry { Key = "Combat/HitStopSec", Type = TuningValueType.Float, ValueFloat = 0.05f } };
            table.RebuildIndex();

            var parsed = SpecWebParser.ParseTuningScalars("{\"ok\":false,\"status\":401,\"error\":\"unauthorized\"}");
            Assert.AreEqual(0, parsed.Rows.Count, "前提: ok:false の応答は行 0 件になる");
            Assert.AreEqual(1, parsed.Issues.Count, "前提: エンベロープ段(行番号 0)の Issue が積まれる");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*スキップしました.*"));
            SpecSyncService.ApplyTuning(parsed, table);

            Assert.AreEqual(1, table.Entries.Length, "ok:false の応答で既存の Entries を消してはいけない");
            Assert.AreEqual("Combat/HitStopSec", table.Entries[0].Key);
            Assert.IsTrue(table.TryFindIndex("Combat/HitStopSec", out _));
        }

        [Test]
        public void ApplyTuningTable_ErrorEnvelopeResponse_KeepsExistingTables()
        {
            var table = CreateTuningTable();
            table.Tables = new[]
            {
                new TuningTableEntry
                {
                    Key = "Enemy/Params",
                    Columns = new[] { new TuningTableColumn { Key = "Hp", Type = TuningValueType.Int } },
                    Rows = System.Array.Empty<TuningTableRow>(),
                },
            };
            table.RebuildIndex();

            var parsed = SpecWebParser.ParseTuningTables("{\"ok\":false,\"status\":403,\"error\":\"forbidden\"}");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*スキップしました.*"));
            SpecSyncService.ApplyTuningTable(parsed, table);

            Assert.AreEqual(1, table.Tables.Length, "ok:false の応答で既存の Tables を消してはいけない");
            Assert.AreEqual("Enemy/Params", table.Tables[0].Key);
        }

        // 「取得はできたが 0 件」も、意図的な全削除と区別できないため適用しない(全消しを避ける安全側)。
        [Test]
        public void ApplyTuning_ZeroRows_KeepsExistingEntries()
        {
            var table = CreateTuningTable();
            table.Entries = new[] { new TuningEntry { Key = "Combat/HitStopSec", Type = TuningValueType.Float, ValueFloat = 0.05f } };
            table.RebuildIndex();

            var parsed = SpecWebParser.ParseTuningScalars("{\"ok\":true,\"items\":{}}");
            Assert.AreEqual(0, parsed.Rows.Count);
            Assert.AreEqual(0, parsed.Issues.Count, "前提: ok:true・items 空はエンベロープ失敗ではない");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*スキップしました.*"));
            SpecSyncService.ApplyTuning(parsed, table);

            Assert.AreEqual(1, table.Entries.Length);
        }

        [Test]
        public void IsUnusableForApply_ValidRows_ReturnsFalse()
        {
            var parsed = new SpecParseResult<SpecTuningRow>();
            parsed.Rows.Add(new SpecTuningRow { RowNumber = 1, Key = "A/B", RawValue = "1", RawType = "int" });

            Assert.IsFalse(SpecSyncService.IsUnusableForApply(parsed, out var reason));
            Assert.IsNull(reason);
        }

        // 行ごとの Issue(重複キー等)は「一部の行だけ無効」なので適用を止めない。
        [Test]
        public void IsUnusableForApply_RowLevelIssueOnly_ReturnsFalse()
        {
            var parsed = new SpecParseResult<SpecTuningRow>();
            parsed.Rows.Add(new SpecTuningRow { RowNumber = 1, Key = "A/B", RawValue = "1", RawType = "int" });
            parsed.Issues.Add(new SpecIssue(2, "行 2 は壊れています"));

            Assert.IsFalse(SpecSyncService.IsUnusableForApply(parsed, out _));
        }

        [Test]
        public void IsUnusableForApply_Null_ReturnsTrue()
        {
            Assert.IsTrue(SpecSyncService.IsUnusableForApply<SpecTuningRow>(null, out var reason));
            Assert.IsNotNull(reason);
        }

        [Test]
        public void BuildChoicesTsv_ContainsHeaderAndKnownValues()
        {
            var tsv = SpecSyncService.BuildChoicesTsv();

            StringAssert.StartsWith("種別\t状態\t型", tsv);
            StringAssert.Contains("Se", tsv);
            // 2026-09-17([41] P1-7): 状態は 3 値(発注済 / 納品済 / インポート済)。旧 4 値の「仮」は出ない。
            StringAssert.Contains("発注済", tsv);
            StringAssert.Contains("納品済", tsv);
            StringAssert.Contains("インポート済", tsv);
            StringAssert.DoesNotContain("仮", tsv);
            StringAssert.Contains("float", tsv);
        }

        [Test]
        public void BuildExistingAssetsTsv_ContainsCreatedAsset()
        {
            // 識別子は実プロジェクトの既存アセットと衝突しない専用の接頭辞にする(例: 実在する SE_Player_Slash)。
            const string identifier = "SpecSyncTsvCheck";
            var asset = AssetCreationService.Create(typeof(SeData), AssetType.Se, "剣の斬撃音", "Player", identifier, gameDataRoot: TestRoot);
            asset.Assignee = "よしだ";
            EditorUtility.SetDirty(asset);
            using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }

            var tsv = SpecSyncService.BuildExistingAssetsTsv();

            StringAssert.Contains($"Se\tPlayer\t{identifier}\t剣の斬撃音", tsv);
            StringAssert.Contains("よしだ", tsv);
        }
    }
}
