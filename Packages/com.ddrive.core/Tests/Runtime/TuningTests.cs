using DDrive.Runtime.Tuning;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    // [27_spec_sheet.md] §3.2 / [11_tasks.md] 5-13 — TuningTable/Tuning ファサード(Options.cs と同じ設計)。
    public class TuningTests
    {
        [TearDown]
        public void TearDown()
        {
            Tuning.Bind(null);
        }

        private static TuningTable CreateTable(params TuningEntry[] entries)
        {
            var table = ScriptableObject.CreateInstance<TuningTable>();
            table.Entries = entries;
            return table;
        }

        [Test]
        public void GetFloat_RegisteredKey_ReturnsStoredValue()
        {
            var table = CreateTable(new TuningEntry { Key = "Combat/HitStopSec", Type = TuningValueType.Float, ValueFloat = 0.05f });
            Tuning.Bind(table);

            Assert.AreEqual(0.05f, Tuning.GetFloat("Combat/HitStopSec"), 0.0001f);
        }

        [Test]
        public void GetInt_RegisteredKey_ReturnsStoredValue()
        {
            var table = CreateTable(new TuningEntry { Key = "Combat/ComboMax", Type = TuningValueType.Int, ValueInt = 3 });
            Tuning.Bind(table);

            Assert.AreEqual(3, Tuning.GetInt("Combat/ComboMax"));
        }

        [Test]
        public void GetBool_RegisteredKey_ReturnsStoredValue()
        {
            var table = CreateTable(new TuningEntry { Key = "Debug/Enabled", Type = TuningValueType.Bool, ValueBool = true });
            Tuning.Bind(table);

            Assert.IsTrue(Tuning.GetBool("Debug/Enabled"));
        }

        [Test]
        public void GetString_RegisteredKey_ReturnsStoredValue()
        {
            var table = CreateTable(new TuningEntry { Key = "Debug/Label", Type = TuningValueType.String, ValueString = "テスト" });
            Tuning.Bind(table);

            Assert.AreEqual("テスト", Tuning.GetString("Debug/Label"));
        }

        [Test]
        public void GetFloat_UnknownKey_ReturnsDefaultAndWarnsOnce()
        {
            Tuning.Bind(CreateTable());

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Unknown/Key.*"));
            Assert.AreEqual(1.5f, Tuning.GetFloat("Unknown/Key", 1.5f));

            // 2回目は警告を出さない(スパム防止)。
            Assert.AreEqual(1.5f, Tuning.GetFloat("Unknown/Key", 1.5f));
        }

        [Test]
        public void GetFloat_NotBound_ReturnsDefault_NoException()
        {
            Assert.IsFalse(Tuning.IsBound);
            Assert.DoesNotThrow(() => Tuning.GetFloat("Anything", 9f));
        }

        // ── W-10(2026-09-14) 追加: Enum・テーブル型 ──

        [Test]
        public void GetEnum_RegisteredKey_ReturnsStoredValue()
        {
            var table = CreateTable(new TuningEntry
            {
                Key = "Difficulty/Level", Type = TuningValueType.Enum, ValueString = "Normal",
                EnumOptions = new[] { "Easy", "Normal", "Hard" },
            });
            Tuning.Bind(table);

            Assert.AreEqual("Normal", Tuning.GetEnum("Difficulty/Level"));
        }

        [Test]
        public void GetEnum_UnknownKey_ReturnsDefaultAndWarnsOnce()
        {
            Tuning.Bind(CreateTable());

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Unknown/Enum.*"));
            Assert.AreEqual("Fallback", Tuning.GetEnum("Unknown/Enum", "Fallback"));
            Assert.AreEqual("Fallback", Tuning.GetEnum("Unknown/Enum", "Fallback"));
        }

        private static TuningTable CreateTableWithSingleColumnTable(string tableKey, string rowId, TuningTableColumn column, TuningCellValue cell)
        {
            var table = ScriptableObject.CreateInstance<TuningTable>();
            table.Tables = new[]
            {
                new TuningTableEntry
                {
                    Key = tableKey,
                    Columns = new[] { column },
                    Rows = new[] { new TuningTableRow { RowId = rowId, Cells = new[] { cell } } },
                },
            };
            return table;
        }

        [Test]
        public void GetTableFloat_RegisteredCell_ReturnsStoredValue()
        {
            var table = CreateTableWithSingleColumnTable(
                "Enemy/Params", "Slime",
                new TuningTableColumn { Key = "Speed", Type = TuningValueType.Float, Min = 0, Max = 20 },
                new TuningCellValue { ColumnKey = "Speed", F = 1.2f });
            Tuning.Bind(table);

            Assert.AreEqual(1.2f, Tuning.GetTableFloat("Enemy/Params", "Slime", "Speed"), 0.0001f);
        }

        [Test]
        public void GetTableInt_RegisteredCell_ReturnsStoredValue()
        {
            var table = CreateTableWithSingleColumnTable(
                "Enemy/Params", "Slime",
                new TuningTableColumn { Key = "Hp", Type = TuningValueType.Int, Min = 1, Max = 9999 },
                new TuningCellValue { ColumnKey = "Hp", I = 10 });
            Tuning.Bind(table);

            Assert.AreEqual(10, Tuning.GetTableInt("Enemy/Params", "Slime", "Hp"));
        }

        [Test]
        public void GetTableBool_RegisteredCell_ReturnsStoredValue()
        {
            var table = CreateTableWithSingleColumnTable(
                "Enemy/Params", "Slime",
                new TuningTableColumn { Key = "IsBoss", Type = TuningValueType.Bool },
                new TuningCellValue { ColumnKey = "IsBoss", B = true });
            Tuning.Bind(table);

            Assert.IsTrue(Tuning.GetTableBool("Enemy/Params", "Slime", "IsBoss"));
        }

        [Test]
        public void GetTableString_RegisteredEnumCell_ReturnsStoredValue()
        {
            var table = CreateTableWithSingleColumnTable(
                "Enemy/Params", "Slime",
                new TuningTableColumn { Key = "Type", Type = TuningValueType.Enum, EnumOptions = new[] { "Melee", "Ranged" } },
                new TuningCellValue { ColumnKey = "Type", S = "Melee" });
            Tuning.Bind(table);

            Assert.AreEqual("Melee", Tuning.GetTableString("Enemy/Params", "Slime", "Type"));
        }

        [Test]
        public void GetTableFloat_UnknownTable_ReturnsDefaultAndWarnsOnce()
        {
            Tuning.Bind(CreateTable());

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Unknown/Table.*"));
            Assert.AreEqual(9f, Tuning.GetTableFloat("Unknown/Table", "Row", "Col", 9f));
            Assert.AreEqual(9f, Tuning.GetTableFloat("Unknown/Table", "Row", "Col", 9f));
        }

        [Test]
        public void GetTableFloat_UnknownRow_ReturnsDefault()
        {
            var table = CreateTableWithSingleColumnTable(
                "Enemy/Params", "Slime",
                new TuningTableColumn { Key = "Speed", Type = TuningValueType.Float },
                new TuningCellValue { ColumnKey = "Speed", F = 1.2f });
            Tuning.Bind(table);

            Assert.AreEqual(5f, Tuning.GetTableFloat("Enemy/Params", "NoSuchRow", "Speed", 5f));
        }

        [Test]
        public void GetTableFloat_UnknownColumn_ReturnsDefault()
        {
            var table = CreateTableWithSingleColumnTable(
                "Enemy/Params", "Slime",
                new TuningTableColumn { Key = "Speed", Type = TuningValueType.Float },
                new TuningCellValue { ColumnKey = "Speed", F = 1.2f });
            Tuning.Bind(table);

            Assert.AreEqual(5f, Tuning.GetTableFloat("Enemy/Params", "Slime", "NoSuchColumn", 5f));
        }

        [Test]
        public void GetTableFloat_NotBound_ReturnsDefault_NoException()
        {
            Assert.IsFalse(Tuning.IsBound);
            Assert.DoesNotThrow(() => Tuning.GetTableFloat("Any", "Any", "Any", 3f));
        }
    }
}
