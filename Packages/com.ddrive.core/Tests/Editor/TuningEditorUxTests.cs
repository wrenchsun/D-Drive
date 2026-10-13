using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DDrive.Editor.Tuning;
using DDrive.Foundation.Data;
using DDrive.Runtime.Tuning;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace DDrive.Tests.Editor
{
    // docs/1010 UX-15: 新 TuningEditor(Editor/Tuning)。Ux の型は internal なので、型の中身はリフレクションで見る。
    // 実運用規模のサンプル(TuningSample。キー 51・カテゴリ 9・型混在・Tables 3〔25 行のテーブルあり〕・わざと矛盾 2)を一時フォルダに作って確かめる(実データは触らない)。
    public class TuningEditorUxTests
    {
        private const string TmpFolder = "Assets/__TuningEditorUxTestTmp";
        private const string KeysOutput = "Temp/DDriveTuningEditorUxTests/Tuning.g.cs";
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        private static Assembly EditorAssembly => typeof(DDrive.Editor.Menu.DDriveMenu).Assembly;
        private static Type WindowType => EditorAssembly.GetType("DDrive.Editor.Tuning.TuningEditor", true);
        private static Type SourceType => EditorAssembly.GetType("DDrive.Editor.Tuning.TuningTuneSource", true);

        private TuningTable _table;
        private EditorWindow _window;

        [OneTimeSetUp]
        public void BuildSampleOnce()
        {
            var sample = EditorAssembly.GetType("DDrive.Editor.Tuning.TuningSample", true);
            var result = sample.GetMethod("Build", Stat).Invoke(null, new object[] { TmpFolder });
            _table = (TuningTable)result.GetType().GetField("Table").GetValue(result);
        }

        [OneTimeTearDown]
        public void DeleteSample()
        {
            UxTestWindows.Release();
            UxTestWindows.DeleteFolder(TmpFolder);
        }

        [SetUp]
        public void SetUp() => UxTestWindows.CloseAll();

        [TearDown]
        public void TearDown()
        {
            EditorUtility.ClearDirty(_table);
            UxTestWindows.CloseAll();
            _window = null;
            EditorUtility.ClearDirty(_table);
            if (File.Exists(KeysOutput))
            {
                File.Delete(KeysOutput);
            }
        }

        // ── 部品 ──

        private static TuningEntry Entry(string key, TuningValueType type = TuningValueType.Float) => new TuningEntry { Key = key, Type = type };

        private int IndexOf(string key) => Array.FindIndex(_table.Entries, e => e.Key == key);

        private object NewSource(TuningTable table) => Activator.CreateInstance(SourceType, new object[] { table });

        private static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, Inst).Invoke(o, args);

        private static object Prop(object o, string name) => o.GetType().GetProperty(name, Inst).GetValue(o);

        private void OpenWindow(TuningTable target)
        {
            RequiresGraphicsGuard.SkipIfNoGraphicsDevice();
            _window = (EditorWindow)ScriptableObject.CreateInstance(WindowType);
            WindowType.GetField("_target", Inst).SetValue(_window, target);
            _window.Show();
            if (_window.rootVisualElement.childCount == 0)
            {
                WindowType.GetMethod("CreateGUI", Inst).Invoke(_window, null);
            }
        }

        private object Field(string name) => WindowType.GetField(name, Inst).GetValue(_window);

        private VisualElement Root => _window.rootVisualElement;

        private VisualElement Knobs => (VisualElement)Field("_knobs");

        private static void Click(Button b)
        {
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = b;
                b.SendEvent(e);
            }
        }

        private static int Shown(VisualElement knobs) => (int)knobs.GetType().GetProperty("ShownCount").GetValue(knobs);

        private static int Modified(VisualElement knobs) => (int)knobs.GetType().GetProperty("ModifiedCount").GetValue(knobs);

        // 名前(キーの "/" 以降)で行を探す。
        private static VisualElement KnobRow(VisualElement knobs, string name)
        {
            foreach (var k in knobs.Query(className: "ux-knob").ToList())
            {
                var l = k.Q<Label>(className: "ux-knob__name");
                if (l != null && l.text == name)
                {
                    return k;
                }
            }

            Assert.Fail("行 " + name + " がありません");
            return null;
        }

        // ── サンプルの規模 ──

        [Test]
        public void Sample_IsRealisticScale_AndMixedTypes()
        {
            Assert.GreaterOrEqual(_table.Entries.Length, 46, "キー 46 以上");
            var cats = TuningCategoryGrouper.Group(_table.Entries);
            Assert.GreaterOrEqual(cats.Count, 6, "カテゴリ 6 以上");
            foreach (TuningValueType type in Enum.GetValues(typeof(TuningValueType)))
            {
                Assert.IsTrue(_table.Entries.Any(e => e.Type == type), "型 " + type + " が混在");
            }

            Assert.IsTrue(_table.Entries.Any(e => (e.Type == TuningValueType.Float || e.Type == TuningValueType.Int) && e.Min == e.Max), "範囲なしの数値");
            Assert.IsTrue(_table.Entries.Any(e => e.Type == TuningValueType.Float && e.Min != e.Max), "範囲ありの数値");
            Assert.AreEqual(3, _table.Tables.Length);
            Assert.IsTrue(_table.Tables.Any(t => t.Rows.Length > 20), "20 行を超えるテーブル(ListView)");
        }

        // ── つまみの供給元 ──

        [Test]
        public void Source_OrdersByCategoryThenKey_AndMapsBackToEntries()
        {
            var src = NewSource(_table);
            var count = (int)Prop(src, "Count");
            Assert.AreEqual(_table.Entries.Length, count);
            var names = (IReadOnlyList<string>)Prop(src, "CategoryNames");
            Assert.AreEqual(TuningCategoryGrouper.Group(_table.Entries).Select(c => c.Name).ToList(), names.ToList(), "分類 = 名前順・(未分類)は最後");
            Assert.AreEqual(TuningCategoryGrouper.Uncategorized, names[names.Count - 1]);

            var seen = new HashSet<int>();
            var lastCat = -1;
            string lastKey = null;
            for (var i = 0; i < count; i++)
            {
                var entryIndex = (int)Call(src, "ArrayIndex", i);
                Assert.IsTrue(seen.Add(entryIndex), "ArrayIndex は全単射");
                var cat = (int)Call(src, "InferCategory", i);
                Assert.GreaterOrEqual(cat, lastCat, "カテゴリ順");
                if (cat == lastCat)
                {
                    Assert.Less(string.CompareOrdinal(lastKey, _table.Entries[entryIndex].Key), 0, "同じカテゴリではキー順");
                }

                lastCat = cat;
                lastKey = _table.Entries[entryIndex].Key;
                Assert.AreEqual(TuningCategoryGrouper.DisplayName(lastKey), (string)Call(src, "Label", i), "表示名 = キーの / 以降");
                Assert.AreEqual(lastKey, (string)Call(src, "TargetName", i), "反映先 = キー");
            }

            Assert.IsFalse((bool)Prop(src, "CategoryEditable"), "分類は名前で決まる(チップで付け替えない)");
            Assert.IsFalse((bool)Prop(src, "CanAddRemove"), "追加・削除は無い(仕様書が正本)");
        }

        [Test]
        public void Source_SetValueAndValue_RoundTrip_ForEveryType()
        {
            var table = ScriptableObject.CreateInstance<TuningTable>();
            table.Entries = new[]
            {
                new TuningEntry { Key = "A/F", Type = TuningValueType.Float, ValueFloat = 1f },
                new TuningEntry { Key = "A/I", Type = TuningValueType.Int, ValueInt = 2 },
                new TuningEntry { Key = "A/B", Type = TuningValueType.Bool, ValueBool = false },
                new TuningEntry { Key = "A/S", Type = TuningValueType.String, ValueString = "x" },
                new TuningEntry { Key = "A/E", Type = TuningValueType.Enum, ValueString = "p", EnumOptions = new[] { "p", "q" } },
            };
            try
            {
                var src = NewSource(table);
                int At(string key) => Array.FindIndex(Enumerable.Range(0, 5).ToArray(), i => (string)Call(src, "TargetName", i) == key);
                var f = At("A/F");
                var i1 = At("A/I");
                var b = At("A/B");
                var s = At("A/S");
                var e = At("A/E");
                Call(src, "SetValue", f, ParamValue.Of(2.5f));
                Call(src, "SetValue", i1, ParamValue.Of(7));
                Call(src, "SetValue", b, ParamValue.Of(true));
                Call(src, "SetValue", s, ParamValue.Of("hello"));
                Call(src, "SetValue", e, ParamValue.Of("q"));
                Assert.AreEqual(2.5f, table.Entries[0].ValueFloat);
                Assert.AreEqual(7, table.Entries[1].ValueInt);
                Assert.IsTrue(table.Entries[2].ValueBool);
                Assert.AreEqual("hello", table.Entries[3].ValueString);
                Assert.AreEqual("q", table.Entries[4].ValueString);
                Assert.AreEqual(2.5f, ((ParamValue)Call(src, "Value", f)).FloatValue);
                Assert.AreEqual(7, ((ParamValue)Call(src, "Value", i1)).IntValue);
                Assert.IsTrue(((ParamValue)Call(src, "Value", b)).BoolValue);
                Assert.AreEqual("hello", ((ParamValue)Call(src, "Value", s)).StringValue);
                Assert.AreEqual("q", ((ParamValue)Call(src, "Value", e)).StringValue);
                Assert.AreEqual("Float", Call(src, "KindOf", f).ToString());
                Assert.AreEqual("Int", Call(src, "KindOf", i1).ToString());
                Assert.AreEqual("Bool", Call(src, "KindOf", b).ToString());
                Assert.AreEqual("String", Call(src, "KindOf", s).ToString());
                Assert.AreEqual("Enum", Call(src, "KindOf", e).ToString());
                CollectionAssert.AreEqual(new[] { "p", "q" }, (string[])Call(src, "EnumOptions", e));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void Source_RangeSlider_OrNumberField_ByMinMax()
        {
            var src = NewSource(_table);
            var ranged = ArrayIndexOf(src, "Player/MoveSpeedMax");
            var plain = ArrayIndexOf(src, "Player/Gravity");
            var args = new object[] { ranged, 0f, 0f };
            Assert.IsTrue((bool)Call(src, "TryRange", args));
            Assert.AreEqual(0f, (float)args[1]);
            Assert.AreEqual(20f, (float)args[2]);
            Assert.IsFalse((bool)Call(src, "UseNumberField", ranged), "範囲ありはスライダー");
            Assert.IsTrue((bool)Call(src, "UseNumberField", plain), "範囲なし(Min == Max)は数値欄");
            Assert.IsFalse((bool)Call(src, "TryRange", new object[] { plain, 0f, 0f }));
            Assert.AreEqual("m/s", (string)Call(src, "UnitOf", ranged));
            StringAssert.Contains("最高速度", (string)Call(src, "Summary", ranged));
            StringAssert.Contains("Player", (string)Call(src, "SearchText", ranged), "検索はカテゴリにも当たる");
        }

        private int ArrayIndexOf(object src, string key)
        {
            var n = (int)Prop(src, "Count");
            for (var i = 0; i < n; i++)
            {
                if ((string)Call(src, "TargetName", i) == key)
                {
                    return i;
                }
            }

            Assert.Fail(key + " がありません");
            return -1;
        }

        [Test]
        public void Source_Signature_IgnoresValues_ButSeesKeysAndRanges()
        {
            var table = ScriptableObject.CreateInstance<TuningTable>();
            table.Entries = new[] { new TuningEntry { Key = "A/F", Type = TuningValueType.Float, ValueFloat = 1f, Min = 0, Max = 10 } };
            try
            {
                var src = NewSource(table);
                var s0 = (string)Call(src, "Signature");
                table.Entries[0].ValueFloat = 5f;
                Assert.AreEqual(s0, (string)Call(src, "Signature"), "値だけの変更では作り直さない");
                table.Entries[0].Max = 20f;
                Assert.AreNotEqual(s0, (string)Call(src, "Signature"), "範囲が変わったら作り直す");
                var s1 = (string)Call(src, "Signature");
                table.Entries = new[] { table.Entries[0], new TuningEntry { Key = "B/G", Type = TuningValueType.Bool } };
                Assert.AreNotEqual(s1, (string)Call(src, "Signature"), "キーが増えたら作り直す");
                Assert.AreEqual(2, (int)Prop(src, "Count"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(table);
            }
        }

        // ── 検証 ──

        [Test]
        public void Check_Sample_HasOneErrorAndOneWarning()
        {
            var issues = TuningCheck.Run(_table);
            Assert.AreEqual(1, TuningCheck.Count(issues, TuningCheck.Level.Error), string.Join(" / ", issues.Select(i => i.Message)));
            Assert.AreEqual(1, TuningCheck.Count(issues, TuningCheck.Level.Warning));
            Assert.IsTrue(issues.Any(i => i.Key == "Fan/DecayPerMin" && i.Level == TuningCheck.Level.Error), "範囲の外 = エラー");
            Assert.IsTrue(issues.Any(i => i.Key == "Camera/Preset" && i.Level == TuningCheck.Level.Warning), "選択肢に無い値 = 警告");
        }

        [Test]
        public void Check_FindsDuplicatesEmptyKeysAndTableCells()
        {
            var table = ScriptableObject.CreateInstance<TuningTable>();
            table.Entries = new[]
            {
                new TuningEntry { Key = "A/X", Type = TuningValueType.Int, ValueInt = 3, Min = 0, Max = 2 },
                new TuningEntry { Key = "A/X", Type = TuningValueType.Bool },
                new TuningEntry { Key = string.Empty, Type = TuningValueType.Bool },
                new TuningEntry { Key = "A/E", Type = TuningValueType.Enum, ValueString = "z" },
                new TuningEntry { Key = "A/N", Type = TuningValueType.Float, ValueFloat = float.NaN },
            };
            table.Tables = new[]
            {
                new TuningTableEntry
                {
                    Key = "T",
                    Columns = new[] { new TuningTableColumn { Key = "C", Type = TuningValueType.Float, Min = 0, Max = 1 }, new TuningTableColumn { Key = "K", Type = TuningValueType.Enum, EnumOptions = new[] { "a" } } },
                    Rows = new[]
                    {
                        new TuningTableRow { RowId = "r1", Cells = new[] { new TuningCellValue { ColumnKey = "C", F = 2f }, new TuningCellValue { ColumnKey = "K", S = "b" } } },
                        new TuningTableRow { RowId = "r1", Cells = new[] { new TuningCellValue { ColumnKey = "C", F = 0.5f }, new TuningCellValue { ColumnKey = "K", S = "a" } } },
                    },
                },
            };
            try
            {
                var issues = TuningCheck.Run(table);
                var text = string.Join("\n", issues.Select(i => i.Level + ":" + i.Message));
                StringAssert.Contains("が重複しています", text);
                StringAssert.Contains("番目のキーが空です", text);
                StringAssert.Contains("の外です", text);
                StringAssert.Contains("選択肢がありません", text);
                StringAssert.Contains("数値ではありません", text);
                StringAssert.Contains("行 ID 'r1' が重複", text);
                StringAssert.Contains("T / r1 / C", text, "テーブルのセルの範囲外");
                StringAssert.Contains("T / r1 / K", text, "テーブルのセルの選択肢");
                Assert.AreEqual(3, TuningCheck.Count(issues, TuningCheck.Level.Error), text);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void Check_CleanTable_HasNoIssues()
        {
            var table = ScriptableObject.CreateInstance<TuningTable>();
            table.Entries = new[] { new TuningEntry { Key = "A/F", Type = TuningValueType.Float, ValueFloat = 1f, Min = 0, Max = 2 } };
            try
            {
                Assert.IsEmpty(TuningCheck.Run(table));
                Assert.IsEmpty(TuningCheck.Run(null));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(table);
            }
        }

        // ── 登録 ──

        [Test]
        public void Menu_NewUnderEditors_OldUnderArchive()
        {
            string PathOf(Type type, string method) =>
                ((MenuItem)type.GetMethod(method, BindingFlags.Static | BindingFlags.Public).GetCustomAttribute(typeof(MenuItem))).menuItem;

            Assert.AreEqual(DDrive.Editor.Menu.DDriveMenu.Editors + "Tuning（調整値）", PathOf(WindowType, "OpenFromMenu"));
            Assert.AreEqual(DDrive.Editor.Menu.DDriveMenu.Archive + "Tuning（調整値）", PathOf(typeof(TuningEditorWindow), "OpenFromMenu"));
        }

        [Test]
        public void Window_SerializesTargetTabQueryLock_ForRecompile()
        {
            foreach (var name in new[] { "_target", "_tab", "_query", "_lock" })
            {
                var f = WindowType.GetField(name, Inst);
                Assert.IsNotNull(f, name);
                Assert.IsNotNull(f.GetCustomAttribute<SerializeField>(), name + " は再コンパイルに耐える([SerializeField])");
            }
        }

        // ── 画面 ──

        [Test]
        [Category("RequiresGraphics")]
        public void Window_Builds_HeroChipsCards_AndFooter_WithoutPlayback()
        {
            OpenWindow(_table);
            var root = Root;
            Assert.IsNotNull(root.Q(className: "ux-hero"));
            Assert.AreEqual($"{_table.Entries.Length} キー", ((Label)Field("_chipKeys")).text);
            Assert.AreEqual("9 カテゴリ", ((Label)Field("_chipCats")).text);
            Assert.AreEqual("Tables 3", ((Label)Field("_chipTables")).text);
            StringAssert.Contains("エラー 1", ((Label)Field("_chipCheck")).text);
            Assert.AreEqual("変更なし", ((Label)Field("_chipMod")).text);
            CollectionAssert.Contains(root.Query<Label>(className: "ux-card__title").ToList().ConvertAll(l => l.text), "調整値");
            CollectionAssert.Contains(root.Query<Label>(className: "ux-card__title").ToList().ConvertAll(l => l.text), "検証");
            Assert.IsNotNull(root.Q(className: "ux-bar"), "固定バー");
            var buttons = root.Query<Button>().ToList().ConvertAll(b => b.text);
            CollectionAssert.Contains(buttons, "保存");
            CollectionAssert.Contains(buttons, "キー定数を再生成");
            CollectionAssert.Contains(buttons, "仕様書と同期");
            Assert.IsFalse(buttons.Exists(t => t.Contains("▶") || t.Contains("■") || t == "リピート"), "Tuning に再生ボタンは無い");
            Assert.AreEqual(0, root.Query<Slider>(className: "ux-bar__speed").ToList().Count, "速度スライダーも無い");
            Assert.IsTrue(((VisualElement)Field("_notice")).ClassListContains("ux-hidden"), "Edit Mode では Play 中の再読込の通知は出ない");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Window_CategoryChips_FilterWithCounts_AndListViewAbove20()
        {
            OpenWindow(_table);
            var knobs = Knobs;
            var chips = knobs.Query<Button>(className: "ux-filter").ToList();
            var texts = chips.ConvertAll(b => b.text);
            CollectionAssert.Contains(texts, $"すべて {_table.Entries.Length}");
            foreach (var c in TuningCategoryGrouper.Group(_table.Entries))
            {
                CollectionAssert.Contains(texts, c.Name + " " + c.Count);
            }

            Assert.AreEqual(1, knobs.Query<ListView>(className: "ux-itemlist__lv").ToList().Count, "20 行を超えたら ListView");
            Click(chips.Find(b => b.text == "Interact 5"));
            Assert.AreEqual(0, knobs.Query<ListView>(className: "ux-itemlist__lv").ToList().Count, "5 行ならそのまま並べる");
            Assert.AreEqual(5, Shown(knobs));
            Assert.AreEqual(5, knobs.Query(className: "ux-knob").Where(k => !k.ClassListContains("ux-hidden")).ToList().Count);
            WindowType.GetMethod("RefreshChips", Inst).Invoke(_window, null);
            StringAssert.Contains("キー中 5 件を表示中", ((Label)Field("_hit")).text);
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Window_OneRowChange_WritesData_AndUndoRestores()
        {
            OpenWindow(_table);
            var knobs = Knobs;
            Click(knobs.Query<Button>(className: "ux-filter").ToList().Find(b => b.text.StartsWith("Player ")));
            var row = KnobRow(knobs, "MoveSpeedMax");
            var slider = row.Q<Slider>();
            Assert.IsNotNull(slider, "範囲ありの Float はスライダー");
            Assert.AreEqual(0f, slider.lowValue, 1e-4f);
            Assert.AreEqual(20f, slider.highValue, 1e-4f, "Min / Max がスライダーの範囲");
            Assert.AreEqual("m/s", row.Q<Label>(className: "ux-knob__unit").text, "単位");
            StringAssert.Contains("最高速度", row.Q<Label>(className: "ux-knob__summary").text, "説明の 1 行");

            var i = IndexOf("Player/MoveSpeedMax");
            var before = _table.Entries[i].ValueFloat;
            Undo.IncrementCurrentGroup();
            slider.value = 12.5f;
            Assert.AreEqual(12.5f, _table.Entries[i].ValueFloat, 1e-4f, "1 行の変更が TuningTable に入る");
            Assert.IsTrue(row.ClassListContains("ux-knob--mod"), "変更の色バー");
            Assert.AreEqual(1, Modified(knobs));
            StringAssert.Contains("変更 1 件", ((Label)Field("_chipMod")).text);
            Assert.AreEqual("保存 ●", ((Button)Field("_btnSave")).text, "未保存の印");
            Undo.PerformUndo();
            Assert.AreEqual(before, _table.Entries[i].ValueFloat, 1e-4f, "Undo で戻る");

            // ↺ で開いたときの値に戻る。
            Undo.IncrementCurrentGroup();
            slider.value = 3f;
            Click(row.Q<Button>(className: "ux-knob__reset"));
            Assert.AreEqual(before, _table.Entries[i].ValueFloat, 1e-4f, "↺ で開いたときの値");
            Assert.IsFalse(row.ClassListContains("ux-knob--mod"));
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Window_BoolStringEnumRows_AreEditable()
        {
            OpenWindow(_table);
            var knobs = Knobs;
            Click(knobs.Query<Button>(className: "ux-filter").ToList().Find(b => b.text.StartsWith("Player ")));

            var toggle = KnobRow(knobs, "DashEnabled").Q<Toggle>();
            var iDash = IndexOf("Player/DashEnabled");
            Assert.IsNotNull(toggle, "Bool はトグル");
            Undo.IncrementCurrentGroup();
            toggle.value = false;
            Assert.IsFalse(_table.Entries[iDash].ValueBool);
            Undo.PerformUndo();
            Assert.IsTrue(_table.Entries[iDash].ValueBool);

            var text = KnobRow(knobs, "Nickname").Q<TextField>();
            var iNick = IndexOf("Player/Nickname");
            Assert.IsNotNull(text, "String は文字欄");
            Undo.IncrementCurrentGroup();
            text.value = "Alice";
            Assert.AreEqual("Alice", _table.Entries[iNick].ValueString);
            Undo.PerformUndo();
            Assert.AreEqual("Player", _table.Entries[iNick].ValueString);

            var drop = KnobRow(knobs, "ControlScheme").Q<DropdownField>();
            var iCtl = IndexOf("Player/ControlScheme");
            Assert.IsNotNull(drop, "Enum はドロップダウン");
            CollectionAssert.AreEqual(new[] { "Classic", "Relative", "Camera" }, drop.choices);
            Assert.AreEqual("Relative", drop.value);
            Undo.IncrementCurrentGroup();
            drop.value = "Camera";
            Assert.AreEqual("Camera", _table.Entries[iCtl].ValueString);
            Undo.PerformUndo();
            Assert.AreEqual("Relative", _table.Entries[iCtl].ValueString);
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Window_NumberWithoutRange_IsNumberField_NotSlider()
        {
            OpenWindow(_table);
            var knobs = Knobs;
            Click(knobs.Query<Button>(className: "ux-filter").ToList().Find(b => b.text.StartsWith("Player ")));
            var row = KnobRow(knobs, "Gravity");
            Assert.IsNull(row.Q<Slider>(), "範囲なしの Float にスライダーは出さない");
            var field = row.Q<FloatField>();
            Assert.IsNotNull(field);
            var i = IndexOf("Player/Gravity");
            Undo.IncrementCurrentGroup();
            field.value = -1000f;
            Assert.AreEqual(-1000f, _table.Entries[i].ValueFloat, "範囲の外の値も入る(旧と同じ数値欄)");
            Undo.PerformUndo();
            Assert.AreEqual(-9.81f, _table.Entries[i].ValueFloat, 1e-4f);
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Window_Detail_EditsDescriptionOfTheRightEntry_ThroughPermutation()
        {
            OpenWindow(_table);
            var knobs = Knobs;
            Click(knobs.Query<Button>(className: "ux-filter").ToList().Find(b => b.text.StartsWith("Fan ")));
            var row = KnobRow(knobs, "MaxFans");
            Click(row.Query<Button>(className: "ux-btn--icon").ToList().Find(b => b.text == "⚙"));
            var details = row.Q(className: "ux-details");
            Assert.IsNotNull(details);
            Assert.IsNull(details.Query<Button>().ToList().Find(b => b.text == "このつまみを削除"), "削除は無い(キーは仕様書が正本)");
            var i = IndexOf("Fan/MaxFans");
            var so = new SerializedObject(_table);
            Assert.AreEqual(_table.Entries[i].Description, so.FindProperty("Entries").GetArrayElementAtIndex(i).FindPropertyRelative("Description").stringValue);
            var src = NewSource(_table);
            Assert.AreEqual(i, (int)Call(src, "ArrayIndex", ArrayIndexOf(src, "Fan/MaxFans")), "⚙ の欄は並べ替え後の番号ではなく Entries の添字の要素を指す");
            so.Dispose();
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Window_Search_MatchesKeyDescriptionCategory_AndTables()
        {
            OpenWindow(_table);
            var knobs = Knobs;
            var field = Root.Q<TextField>(className: "ux-searchbox__field");
            Assert.IsNotNull(field);

            field.value = "Fan";
            Assert.AreEqual(6, Shown(knobs), "カテゴリ名 Fan の 6 キー");
            field.value = "スタミナ";
            Assert.AreEqual(2, Shown(knobs), "説明の語");
            field.value = "MoveSpeed";
            Assert.AreEqual(2, Shown(knobs), "キー名の語");
            field.value = "EnemyParams";
            Assert.AreEqual(0, Shown(knobs));
            StringAssert.Contains("Tables 1 件", ((Label)Field("_hit")).text, "Tables にも当たることを案内する");
            field.value = string.Empty;
            Assert.AreEqual(_table.Entries.Length, Shown(knobs));
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Window_TablesTab_OneCardPerTable_ListViewForBigTable_AndCellEdit()
        {
            OpenWindow(_table);
            var tabs = Root.Query<Button>(className: "ux-seg__btn").ToList();
            Assert.AreEqual(2, tabs.Count);
            Click(tabs[1]);
            var host = (VisualElement)Field("_tablesHost");
            Assert.IsFalse(host.ClassListContains("ux-hidden"));
            Assert.IsTrue(((VisualElement)Field("_valuesHost")).ClassListContains("ux-hidden"));
            var titles = host.Query<Label>(className: "ux-card__title").ToList().ConvertAll(l => l.text);
            CollectionAssert.AreEqual(new[] { "EnemyParams", "DropRates", "LevelCurve" }, titles);
            Assert.AreEqual(1, host.Query<ListView>(className: "ux-tt-lv").ToList().Count, "25 行のテーブルだけ ListView");

            // EnemyParams: 6 行 + 見出し。セルの編集(Atk)は TuningTable に入り、Undo で戻る。
            var enemy = host.Query(className: "ux-tt-tablecard").ToList()[0];
            var rows = enemy.Query(className: "ux-tt-row").ToList();
            Assert.AreEqual(7, rows.Count, "見出し 1 + 行 6");
            var slimeRow = rows[1];
            Assert.AreEqual("Slime", slimeRow.Q<Label>(className: "ux-tt-rowid").text);
            var floats = slimeRow.Query<FloatField>().ToList();
            Assert.AreEqual(2, floats.Count, "Atk と Speed");
            Undo.IncrementCurrentGroup();
            floats[0].value = 77f;
            Assert.AreEqual(77f, _table.Tables[0].Rows[0].Cells[1].F, 1e-4f);
            Assert.IsTrue(floats[0].ClassListContains("ux-tt-cell--mod"), "変更のしるし");
            StringAssert.Contains("変更 1 件", ((Label)Field("_chipMod")).text, "セルの変更も数える");
            Undo.PerformUndo();
            Assert.AreEqual(4f, _table.Tables[0].Rows[0].Cells[1].F, 1e-4f);

            // Undo で Tables の中身は作り直される(古い部品は外れるので取り直す)。範囲(Atk 0〜500)の外は範囲に収める。Bool / Enum の部品もある。
            enemy = host.Query(className: "ux-tt-tablecard").ToList()[0];
            slimeRow = enemy.Query(className: "ux-tt-row").ToList()[1];
            floats = slimeRow.Query<FloatField>().ToList();
            Undo.IncrementCurrentGroup();
            floats[0].value = 9999f;
            Assert.AreEqual(500f, _table.Tables[0].Rows[0].Cells[1].F, 1e-4f, "範囲に収める");
            Undo.PerformUndo();
            Assert.AreEqual(1, slimeRow.Query<Toggle>().ToList().Count);
            Assert.AreEqual(1, slimeRow.Query<DropdownField>().ToList().Count);
            Assert.AreEqual(1, slimeRow.Query<IntegerField>().ToList().Count);
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Window_TablesSearch_ByRowId_ShowsOnlyThatRow()
        {
            OpenWindow(_table);
            Click(Root.Query<Button>(className: "ux-seg__btn").ToList()[1]);
            Root.Q<TextField>(className: "ux-searchbox__field").value = "Troll";
            var host = (VisualElement)Field("_tablesHost");
            var cards = host.Query(className: "ux-tt-tablecard").ToList();
            Assert.AreEqual(1, cards.Count, "行 ID が当たったテーブルだけ");
            var ids = cards[0].Query<Label>(className: "ux-tt-rowid").ToList().ConvertAll(l => l.text);
            CollectionAssert.AreEqual(new[] { "行 ID", "Troll" }, ids);
            Root.Q<TextField>(className: "ux-searchbox__field").value = "NoSuchTable";
            Assert.AreEqual(0, host.Query(className: "ux-tt-tablecard").ToList().Count);
            StringAssert.Contains("一致する Tables がありません", host.Query<Label>().ToList().ConvertAll(l => l.text).Last());
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Window_Validation_ListsIssues_AndClickJumpsToKey()
        {
            OpenWindow(_table);
            var issues = ((VisualElement)Field("_issueBody")).Query(className: "ux-issue").ToList();
            Assert.AreEqual(2, issues.Count);
            Assert.IsTrue(issues.Exists(i => i.ClassListContains("ux-issue--err")));
            Assert.IsTrue(issues.Exists(i => i.ClassListContains("ux-issue--warn")));
            var err = issues.Find(i => i.ClassListContains("ux-issue--err"));
            using (var e = ClickEvent.GetPooled())
            {
                e.target = err;
                err.SendEvent(e);
            }

            Assert.AreEqual("Fan/DecayPerMin", (string)WindowType.GetField("_query", Inst).GetValue(_window), "行のクリックでそのキーを検索に入れる");
            Assert.AreEqual(1, Shown(Knobs));
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Window_Save_ClearsDirtyMark_AndSetTargetSwitches()
        {
            OpenWindow(_table);
            var i = IndexOf("Audio/MasterVolume");
            Undo.IncrementCurrentGroup();
            Click(Knobs.Query<Button>(className: "ux-filter").ToList().Find(b => b.text.StartsWith("Audio ")));
            var row = KnobRow(Knobs, "MasterVolume");
            row.Q<Slider>().value = 0.3f;
            Assert.AreEqual("保存 ●", ((Button)Field("_btnSave")).text);
            WindowType.GetMethod("SaveTarget", Inst).Invoke(_window, null);
            WindowType.GetMethod("SyncFooter", Inst).Invoke(_window, null);
            Assert.AreEqual("保存", ((Button)Field("_btnSave")).text, "保存で印が消える");
            Assert.AreEqual(0.3f, _table.Entries[i].ValueFloat, 1e-4f);
            _table.Entries[i].ValueFloat = 0.8f; // サンプルの値に戻す(保存しない)
            EditorUtility.ClearDirty(_table);

            var other = ScriptableObject.CreateInstance<TuningTable>();
            other.name = "OtherTable";
            other.Entries = new[] { Entry("Z/One"), Entry("Z/Two") };
            try
            {
                WindowType.GetMethod("SetTarget", Inst).Invoke(_window, new object[] { other, true });
                Assert.AreEqual("OtherTable", ((Label)Field("_title")).text);
                Assert.AreEqual("2 キー", ((Label)Field("_chipKeys")).text);
                WindowType.GetMethod("SetTarget", Inst).Invoke(_window, new object[] { null, true });
                Assert.IsNotNull(Root.Q(className: "ux-empty"), "対象なしは空状態");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(other);
            }
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Window_RegenerateKeys_WritesConstantsForTheTable()
        {
            OpenWindow(_table);
            var dir = Path.GetDirectoryName(KeysOutput);
            Directory.CreateDirectory(dir);
            WindowType.GetMethod("RegenerateKeys", Inst).Invoke(_window, new object[] { KeysOutput });
            Assert.IsTrue(File.Exists(KeysOutput));
            var text = File.ReadAllText(KeysOutput);
            StringAssert.Contains("public const string PlayerMoveSpeedMax = \"Player/MoveSpeedMax\";", text);
        }

        [Test]
        [Category("RequiresGraphics")]
        public void OldWindow_StillOpens_WithOldTitle()
        {
            RequiresGraphicsGuard.SkipIfNoGraphicsDevice();
            TuningEditorWindow.Open(_table);
            var w = EditorWindow.GetWindow<TuningEditorWindow>();
            _window = w;
            StringAssert.Contains("(旧)", w.titleContent.text);
        }
    }
}
