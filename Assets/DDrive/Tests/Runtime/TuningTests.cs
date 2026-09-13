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
    }
}
