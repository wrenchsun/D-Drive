using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.Ux;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Tests.Editor
{
    // docs/1020 EP-1〜EP-4 — 専用エディター土台(UxEditorWindow)の軽量化。
    // 窓は Show せずに CreateGUI を呼んで組み立てる(UxFoundationTests と同じ手順)。Tick / OnPropertyChanged は private なのでリフレクションで呼ぶ。
    [TestFixture]
    public class UxEditorPerfTests
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly System.Type BaseType = typeof(UxEditorWindow<VfxData>);
        private static System.Type Hooks => typeof(UxTheme).Assembly.GetType("DDrive.Editor.Ux.UxTestHooks", true);

        private readonly List<Object> _cleanup = new List<Object>();

        // public 型だけで書いた最小の派生窓(持ち込み先の窓と同じ作り)。
        private sealed class PerfGuide : FieldGuide<VfxData>
        {
            private static readonly FieldGuideEntry[] Table =
            {
                E(nameof(VfxData.Duration), "長さ", "続く秒数。", FieldTier.Common, "長さ"),
            };

            protected override IReadOnlyList<FieldGuideEntry> SpecificEntries => Table;
            protected override IReadOnlyList<string> SpecificPurposes { get; } = new[] { "長さ" };
        }

        private class PerfWindow : UxEditorWindow<VfxData>
        {
            private static readonly PerfGuide GuideInstance = new PerfGuide();

            public int Edited;

            protected override string PrefsKey => "UxPerfTest";
            protected override FieldGuide<VfxData> Guide => GuideInstance;
            protected override string ManualPage => "perf-test";
            protected override string CardDescription(string purpose) => "テスト";
            protected override void OnPropertyEdited(string path) => Edited++;
        }

        private sealed class SlowWindow : PerfWindow
        {
            protected override int TickIntervalMs => 500;
        }

        private sealed class FastWindow : PerfWindow
        {
            protected override int TickIntervalMs => 10;
        }

        [SetUp]
        public void SetUp() => UxTestWindows.CloseAll();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _cleanup)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _cleanup.Clear();
            UxTestWindows.CloseAll();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() => UxTestWindows.Release();

        private static int Hook(string name) => (int)Hooks.GetField(name, Stat).GetValue(null);

        private PerfWindow Build<T>(out VfxData data) where T : PerfWindow
        {
            data = ScriptableObject.CreateInstance<VfxData>();
            data.name = "UxPerf";
            _cleanup.Add(data);
            var window = ScriptableObject.CreateInstance<T>();
            _cleanup.Add(window);
            BaseType.GetField("_target", Inst).SetValue(window, data);
            BaseType.GetMethod("CreateGUI", Inst).Invoke(window, null);
            BaseType.GetField("_tick", Inst).SetValue(window, 0);
            return window;
        }

        private static void Tick(PerfWindow w) => BaseType.GetMethod("Tick", Inst).Invoke(w, null);

        private static void Fire(PerfWindow w, VfxData data)
        {
            var so = (SerializedObject)BaseType.GetProperty("So", Inst).GetValue(w);
            var prop = so.FindProperty(nameof(VfxData.Duration));
            using (var evt = SerializedPropertyChangeEvent.GetPooled(prop))
            {
                BaseType.GetMethod("OnPropertyChanged", Inst).Invoke(w, new object[] { evt });
            }
        }

        // So 経由で Duration を書いて ApplyModifiedProperties する(欄の編集と同じ経路。dirty が進む)。
        private static void Write(PerfWindow w, float value)
        {
            var so = (SerializedObject)BaseType.GetProperty("So", Inst).GetValue(w);
            so.Update();
            so.FindProperty(nameof(VfxData.Duration)).floatValue = value;
            so.ApplyModifiedProperties();
        }

        private static bool AnyRowModified(PerfWindow w)
        {
            var rows = (IList)BaseType.GetField("_rows", Inst).GetValue(w);
            foreach (UxRow row in rows)
            {
                if (row.IsModified)
                {
                    return true;
                }
            }

            return false;
        }

        // 1. 変化の無い連続 Tick で 2 回目以降が飛ぶ。7 回目は飛ばない。
        [Test]
        public void Tick_SkipsRowLoop_WhenNothingChanged_ButRunsOnTheSeventh()
        {
            var w = Build<PerfWindow>(out _);
            var before = Hook("SkippedTicks");
            Tick(w); // 1 回目: 基準が無いので通す
            Assert.AreEqual(before, Hook("SkippedTicks"), "1 回目は飛ばない");
            for (var i = 2; i <= 6; i++)
            {
                Tick(w);
            }

            Assert.AreEqual(before + 5, Hook("SkippedTicks"), "2〜6 回目は飛ぶ");
            Tick(w); // 7 回目: 拍なので必ず通す
            Assert.AreEqual(before + 5, Hook("SkippedTicks"), "7 回目は飛ばない");
        }

        // 2. So 経由で書いて Apply したあとの Tick は飛ばない(変更の印が更新される)。
        [Test]
        public void Tick_DoesNotSkip_AfterDataWasWrittenAndApplied()
        {
            var w = Build<PerfWindow>(out _);
            Tick(w);
            Tick(w);
            Assert.IsFalse(AnyRowModified(w));
            var before = Hook("SkippedTicks");
            Write(w, 3f);
            Tick(w);
            Assert.AreEqual(before, Hook("SkippedTicks"), "書いたあとの Tick は飛ばない");
            Assert.IsTrue(AnyRowModified(w), "変更の印が更新される");
        }

        // 3. dirty が進んでいない通知は捨てる。進んでいれば OnPropertyEdited が呼ばれる。
        [Test]
        public void SpuriousPropertyChange_IsDropped_RealEditIsForwarded()
        {
            var w = Build<PerfWindow>(out var data);
            var before = Hook("SpuriousEdits");
            Fire(w, data);
            Assert.AreEqual(before + 1, Hook("SpuriousEdits"), "偽の通知を数える");
            Assert.AreEqual(0, w.Edited, "OnPropertyEdited は呼ばれない");

            Write(w, 2f);
            Fire(w, data);
            Assert.AreEqual(before + 1, Hook("SpuriousEdits"), "実編集は偽ではない");
            Assert.AreEqual(1, w.Edited, "OnPropertyEdited が呼ばれる");

            // 同じ通知がもう一度来ても(dirty が進んでいないので)偽。
            Fire(w, data);
            Assert.AreEqual(before + 2, Hook("SpuriousEdits"));
            Assert.AreEqual(1, w.Edited);
        }

        // 4. 0.3 秒以内の 2 回目の実編集は保留され、Flush のあとに行の状態が最新になる。OnPropertyEdited は毎回呼ばれる。
        [Test]
        public void SecondEditWithinBurstGap_IsCoalesced_AndFlushedLater()
        {
            var w = Build<PerfWindow>(out var data);
            var before = Hook("CoalescedEdits");

            Write(w, 3f);
            Fire(w, data); // 単発: すぐ反映
            Assert.AreEqual(before, Hook("CoalescedEdits"));
            Assert.IsTrue(AnyRowModified(w), "単発の編集はすぐ反映される");

            Write(w, 1f); // 既定に戻す
            Fire(w, data); // 連続: 保留
            Assert.AreEqual(before + 1, Hook("CoalescedEdits"));
            Assert.AreEqual(2, w.Edited, "OnPropertyEdited は実編集ごとに呼ぶ");
            Assert.IsTrue(AnyRowModified(w), "保留中は行の状態が古いまま");

            BaseType.GetMethod("FlushPendingEdits", Inst).Invoke(w, null);
            Assert.IsFalse(AnyRowModified(w), "保留の実行後に行の状態が最新になる");
        }

        // 5. TickIntervalMs の上書きが Every の間隔に使われる。50 未満は 50 に丸める。
        [Test]
        public void TickIntervalMs_IsOverridable_AndClampedToFifty()
        {
            var normal = Build<PerfWindow>(out _);
            var slow = Build<SlowWindow>(out _);
            var fast = Build<FastWindow>(out _);
            var prop = BaseType.GetProperty("EffectiveTickIntervalMs", Inst);
            Assert.AreEqual(150, (int)prop.GetValue(normal));
            Assert.AreEqual(500, (int)prop.GetValue(slow));
            Assert.AreEqual(50, (int)prop.GetValue(fast));
        }
    }
}
