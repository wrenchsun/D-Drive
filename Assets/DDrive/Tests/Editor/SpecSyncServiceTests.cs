using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Spec;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Tuning;
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
                AssetDatabase.SaveAssets();
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
        public void BuildChoicesTsv_ContainsHeaderAndKnownValues()
        {
            var tsv = SpecSyncService.BuildChoicesTsv();

            StringAssert.StartsWith("種別\t状態\t型", tsv);
            StringAssert.Contains("Se", tsv);
            StringAssert.Contains("仮", tsv);
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
            AssetDatabase.SaveAssets();

            var tsv = SpecSyncService.BuildExistingAssetsTsv();

            StringAssert.Contains($"Se\tPlayer\t{identifier}\t剣の斬撃音", tsv);
            StringAssert.Contains("よしだ", tsv);
        }
    }
}
