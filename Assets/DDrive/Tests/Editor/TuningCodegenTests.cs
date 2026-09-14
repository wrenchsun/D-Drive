using System.IO;
using DDrive.Editor.Codegen;
using DDrive.Runtime.Tuning;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    public class TuningCodegenTests
    {
        // Assets 配下だとインポート・削除の後始末が増えるため、Unity が管理しない Temp/ 配下に書き出す。
        private const string OutputPath = "Temp/DDriveTuningCodegenTests/Tuning.g.cs";
        private const string TableRoot = "Assets/DDrive/Tests/Editor/TempTuningCodegen";
        private const string TablePath = TableRoot + "/TestTuningTable.asset";

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(OutputPath))
            {
                File.Delete(OutputPath);
            }

            if (AssetDatabase.IsValidFolder(TableRoot))
            {
                AssetDatabase.DeleteAsset(TableRoot);
                AssetDatabase.SaveAssets();
            }
        }

        [Test]
        public void Regenerate_WritesConstantsForEachKey()
        {
            AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "TempTuningCodegen");
            var table = ScriptableObject.CreateInstance<TuningTable>();
            table.Entries = new[]
            {
                new TuningEntry { Key = "Influence/FanBase", Type = TuningValueType.Float, ValueFloat = 1f },
                new TuningEntry { Key = "Combat/HitStopSec", Type = TuningValueType.Float, ValueFloat = 0.05f },
            };
            AssetDatabase.CreateAsset(table, TablePath);

            var result = TuningCodegen.Regenerate(table, OutputPath);

            Assert.AreEqual(2, result.TotalCount);
            Assert.IsTrue(File.Exists(OutputPath));
            var content = File.ReadAllText(OutputPath);
            StringAssert.Contains("namespace DDrive.Generated", content);
            StringAssert.Contains("public const string InfluenceFanBase = \"Influence/FanBase\";", content);
            StringAssert.Contains("public const string CombatHitStopSec = \"Combat/HitStopSec\";", content);
        }

        [Test]
        public void Regenerate_NullTable_WritesEmptyClassWithoutThrowing()
        {
            TuningCodegen.Result result = null;
            Assert.DoesNotThrow(() => result = TuningCodegen.Regenerate(table: null, outputPath: OutputPath));

            Assert.AreEqual(0, result.TotalCount);
            StringAssert.Contains("public static class TUNING", File.ReadAllText(OutputPath));
            StringAssert.Contains("public static class TUNING_TABLE", File.ReadAllText(OutputPath));
            StringAssert.Contains("public static class TUNING_COLUMN", File.ReadAllText(OutputPath));
        }

        // W-10(2026-09-14) 追加: テーブルキー・列キーの定数生成([32_spec_web.md] §5.1)。
        [Test]
        public void Regenerate_WithTables_WritesTableAndColumnConstants()
        {
            AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "TempTuningCodegen");
            var table = ScriptableObject.CreateInstance<TuningTable>();
            table.Tables = new[]
            {
                new TuningTableEntry
                {
                    Key = "Enemy/Params",
                    Columns = new[]
                    {
                        new TuningTableColumn { Key = "Hp", Type = TuningValueType.Int },
                        new TuningTableColumn { Key = "Speed", Type = TuningValueType.Float },
                    },
                    Rows = System.Array.Empty<TuningTableRow>(),
                },
            };
            AssetDatabase.CreateAsset(table, TablePath);

            var result = TuningCodegen.Regenerate(table, OutputPath);

            Assert.AreEqual(1, result.TableCount);
            Assert.AreEqual(2, result.ColumnCount);
            var content = File.ReadAllText(OutputPath);
            StringAssert.Contains("public const string EnemyParams = \"Enemy/Params\";", content);
            StringAssert.Contains("public const string EnemyParamsHp = \"Hp\";", content);
            StringAssert.Contains("public const string EnemyParamsSpeed = \"Speed\";", content);
        }
    }
}
