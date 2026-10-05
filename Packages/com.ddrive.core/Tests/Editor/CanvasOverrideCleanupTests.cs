using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.CanvasTool;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // 2026-10-06(U-29a): 埋め込みの登録時に、親に入っているその子の配下の ElementFx の行を整理する(既定のままの行は確認なしで
    // 取り除き、設定のある行は 1 回確認。1 つの Undo グループ)の純ロジック。メモリ上の CanvasData だけを使う(アセットを作らない)。
    public class CanvasOverrideCleanupTests
    {
        private CanvasData _parent;
        private CanvasData _child;

        [SetUp]
        public void SetUp()
        {
            _parent = ScriptableObject.CreateInstance<CanvasData>();
            _parent.Id = 9101;
            _parent.name = "CleanupParent";
            _child = ScriptableObject.CreateInstance<CanvasData>();
            _child.Id = 9102;
            _child.name = "CleanupChild";
        }

        [TearDown]
        public void TearDown()
        {
            CanvasEmbeddedEditing.ConfirmOverrideCleanupForTests = null;
            Undo.ClearUndo(_parent);
            Object.DestroyImmediate(_parent);
            Object.DestroyImmediate(_child);
        }

        private static ElementFx Row(string path) => new ElementFx { ElementPath = path };

        private static ElementFx Custom(string path)
        {
            var fx = Row(path);
            fx.IdlePreset = new UiPresetRef { Preset = UiPreset.Pulse };
            return fx;
        }

        private static AssetId<UiTweenMarker> TweenId(ulong v) => new AssetId<UiTweenMarker>(v, AssetType.UiTween);

        private static AssetId<SeMarker> SeId(ulong v) => new AssetId<SeMarker>(v, AssetType.Se);

        // ── 「既定のまま」の判定(実コードの ElementFx の欄を全部見る) ──

        [Test]
        public void IsDefaultFx_EmptyRow_IsDefault_EvenWithPath()
        {
            Assert.IsTrue(CanvasEmbeddedEditing.IsDefaultFx(Row("Inner/Deep")));
            Assert.IsTrue(CanvasEmbeddedEditing.IsDefaultFx(default(ElementFx)));
        }

        private static IEnumerable<TestCaseData> NonDefaultMutations()
        {
            yield return new TestCaseData((System.Func<ElementFx, ElementFx>)(f => { f.Appear = TweenId(1); return f; })).SetName("Appear id");
            yield return new TestCaseData((System.Func<ElementFx, ElementFx>)(f => { f.Idle = TweenId(1); return f; })).SetName("Idle id");
            yield return new TestCaseData((System.Func<ElementFx, ElementFx>)(f => { f.Disappear = TweenId(1); return f; })).SetName("Disappear id");
            yield return new TestCaseData((System.Func<ElementFx, ElementFx>)(f => { f.AppearDelay = 0.1f; return f; })).SetName("AppearDelay");
            yield return new TestCaseData((System.Func<ElementFx, ElementFx>)(f => { f.AppearSe = SeId(1); return f; })).SetName("AppearSe");
            yield return new TestCaseData((System.Func<ElementFx, ElementFx>)(f => { f.DisappearSe = SeId(1); return f; })).SetName("DisappearSe");
            foreach (var phase in new[] { "Appear", "Idle", "Disappear" })
            {
                var p = phase;
                yield return new TestCaseData(Mutate(p, r => { r.Preset = UiPreset.FadeIn; return r; })).SetName(p + "Preset.Preset");
                yield return new TestCaseData(Mutate(p, r => { r.Duration = 0.5f; return r; })).SetName(p + "Preset.Duration");
                yield return new TestCaseData(Mutate(p, r => { r.Distance = 10f; return r; })).SetName(p + "Preset.Distance");
                yield return new TestCaseData(Mutate(p, r => { r.Se = SeId(2); return r; })).SetName(p + "Preset.Se");
                yield return new TestCaseData(Mutate(p, r => { r.EaseOverride = EaseDef.Named(Ease.OutQuad); return r; })).SetName(p + "Preset.EaseOverride.Named");
                yield return new TestCaseData(Mutate(p, r => { r.EaseOverride.Kind = ParametricKind.CustomBezier; return r; })).SetName(p + "Preset.EaseOverride.Kind");
                yield return new TestCaseData(Mutate(p, r => { r.EaseOverride.BezierP1 = new Vector2(0.1f, 0f); return r; })).SetName(p + "Preset.EaseOverride.P1");
                yield return new TestCaseData(Mutate(p, r => { r.EaseOverride.BezierP2 = new Vector2(0f, 0.1f); return r; })).SetName(p + "Preset.EaseOverride.P2");
            }
        }

        private static System.Func<ElementFx, ElementFx> Mutate(string phase, System.Func<UiPresetRef, UiPresetRef> change) => f =>
        {
            switch (phase)
            {
                case "Appear": f.AppearPreset = change(f.AppearPreset); break;
                case "Idle": f.IdlePreset = change(f.IdlePreset); break;
                default: f.DisappearPreset = change(f.DisappearPreset); break;
            }

            return f;
        };

        [TestCaseSource(nameof(NonDefaultMutations))]
        public void IsDefaultFx_AnyFieldSet_IsNotDefault(System.Func<ElementFx, ElementFx> mutate)
        {
            Assert.IsFalse(CanvasEmbeddedEditing.IsDefaultFx(mutate(Row("Inner/Deep"))));
        }

        // ElementFx / UiPresetRef / EaseDef に欄が増えたら、IsDefaultFx の判定に入れ忘れないよう、ここが落ちて気づかせる。
        [Test]
        public void IsDefaultFx_CoversEveryField_FieldCountsAreFixed()
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Assert.AreEqual(10, typeof(ElementFx).GetFields(flags).Length, "ElementFx に欄が増えた: IsDefaultFx と NonDefaultMutations に足す");
            Assert.AreEqual(5, typeof(UiPresetRef).GetFields(flags).Length, "UiPresetRef に欄が増えた: IsDefaultPreset と NonDefaultMutations に足す");
            Assert.AreEqual(4, typeof(EaseDef).GetFields(flags).Length, "EaseDef に欄が増えた: IsDefaultPreset と NonDefaultMutations に足す");
        }

        // ── 配下の行の抽出 ──

        [Test]
        public void PlanOverrides_SplitsDefaultAndCustom_ExcludesRootItselfAndSiblingsWithSamePrefix()
        {
            _parent.ElementEffects = new[]
            {
                Row("Title"),                  // 0: 親の要素
                Row("Inner"),                  // 1: ルート自身(対象外)
                Row("Inner/Deep"),             // 2: 配下・既定
                Custom("Inner/BtnV"),          // 3: 配下・設定あり
                Row("Inner2/X"),               // 4: 別の要素("Inner" の配下ではない)
                Row("Inner/A/B"),              // 5: 配下の深い所・既定
            };

            var plan = CanvasEmbeddedEditing.PlanOverrides(_parent, "Inner");

            CollectionAssert.AreEqual(new[] { 2, 5 }, plan.DefaultRows);
            CollectionAssert.AreEqual(new[] { 3 }, plan.CustomRows);
            Assert.AreEqual(3, plan.Total);
        }

        [Test]
        public void PlanOverrides_CountsWires_ButNeverRemovesThem()
        {
            _parent.Buttons = new[] { new ButtonWire { ButtonPath = "Inner/Btn" }, new ButtonWire { ButtonPath = "Other" }, new ButtonWire { ButtonPath = "Inner" } };
            _parent.Sliders = new[] { new SliderWire { ElementPath = "Inner/Slider" } };
            _parent.ElementEffects = new[] { Row("Inner/Deep") };

            var plan = CanvasEmbeddedEditing.PlanOverrides(_parent, "Inner");
            Assert.AreEqual(1, plan.ButtonWires, "ルート自身を指す配線は数えない");
            Assert.AreEqual(1, plan.SliderWires);

            CanvasEmbeddedEditing.ApplyCleanup(_parent, plan, CanvasEmbeddedEditing.OverrideChoice.Remove);
            Assert.AreEqual(3, _parent.Buttons.Length);
            Assert.AreEqual(1, _parent.Sliders.Length);
        }

        [Test]
        public void WithoutRows_KeepsOrder_AndHandlesNull()
        {
            var rows = new[] { Row("A"), Row("B"), Row("C"), Row("D") };
            var next = CanvasEmbeddedEditing.WithoutRows(rows, new[] { 3, 1 });
            CollectionAssert.AreEqual(new[] { "A", "C" }, new[] { next[0].ElementPath, next[1].ElementPath });
            Assert.AreEqual(0, CanvasEmbeddedEditing.WithoutRows(null, new[] { 0 }).Length);
        }

        // ── 登録 + 整理(確認ダイアログの 3 択・Undo) ──

        private void SeedRows()
        {
            _parent.ElementEffects = new[] { Row("Title"), Row("Inner/Deep"), Custom("Inner/BtnV"), Row("Inner/BtnW") };
        }

        private static List<string> Paths(CanvasData d)
        {
            var list = new List<string>();
            foreach (var fx in d.ElementEffects)
            {
                list.Add(fx.ElementPath);
            }

            return list;
        }

        [Test]
        public void RegisterWithCleanup_OnlyDefaultRows_RemovedWithoutAsking()
        {
            _parent.ElementEffects = new[] { Row("Title"), Row("Inner/Deep"), Row("Inner/BtnV") };
            var asked = 0;
            CanvasEmbeddedEditing.ConfirmOverrideCleanupForTests = (t, m) => { asked++; return CanvasEmbeddedEditing.OverrideChoice.Cancel; };

            var ok = CanvasEmbeddedEditing.RegisterWithCleanup(_parent, "Inner", _child, out var cleanup);

            Assert.IsTrue(ok);
            Assert.AreEqual(0, asked, "設定のある行が無ければ確認しない");
            CollectionAssert.AreEqual(new[] { "Title" }, Paths(_parent));
            Assert.AreEqual(2, cleanup.RemovedDefault);
            Assert.AreEqual(1, _parent.EmbeddedCanvases.Length);
        }

        [Test]
        public void RegisterWithCleanup_Remove_RemovesCustomRowsToo_AndAsksOnce()
        {
            SeedRows();
            var asked = 0;
            string message = null;
            CanvasEmbeddedEditing.ConfirmOverrideCleanupForTests = (t, m) => { asked++; message = m; return CanvasEmbeddedEditing.OverrideChoice.Remove; };

            var ok = CanvasEmbeddedEditing.RegisterWithCleanup(_parent, "Inner", _child, out var cleanup);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, asked);
            StringAssert.Contains("Inner/BtnV", message, "設定のある行のパスを列挙する");
            StringAssert.DoesNotContain("Inner/Deep", message, "既定のままの行は列挙しない(件数だけ)");
            CollectionAssert.AreEqual(new[] { "Title" }, Paths(_parent));
            Assert.AreEqual(2, cleanup.RemovedDefault);
            Assert.AreEqual(1, cleanup.RemovedCustom);
        }

        [Test]
        public void RegisterWithCleanup_Keep_RemovesOnlyDefaultRows()
        {
            SeedRows();
            CanvasEmbeddedEditing.ConfirmOverrideCleanupForTests = (t, m) => CanvasEmbeddedEditing.OverrideChoice.Keep;

            var ok = CanvasEmbeddedEditing.RegisterWithCleanup(_parent, "Inner", _child, out var cleanup);

            Assert.IsTrue(ok);
            CollectionAssert.AreEqual(new[] { "Title", "Inner/BtnV" }, Paths(_parent));
            Assert.AreEqual(1, cleanup.KeptCustom);
            Assert.AreEqual(1, _parent.EmbeddedCanvases.Length);
        }

        [Test]
        public void RegisterWithCleanup_Cancel_ChangesNothing_AndDoesNotRegister()
        {
            SeedRows();
            CanvasEmbeddedEditing.ConfirmOverrideCleanupForTests = (t, m) => CanvasEmbeddedEditing.OverrideChoice.Cancel;

            var ok = CanvasEmbeddedEditing.RegisterWithCleanup(_parent, "Inner", _child, out var cleanup);

            Assert.IsFalse(ok);
            Assert.IsTrue(cleanup.Cancelled);
            Assert.AreEqual(4, _parent.ElementEffects.Length, "既定のままの行も残る(何も変更しない)");
            Assert.AreEqual(0, _parent.EmbeddedCanvases?.Length ?? 0);
        }

        [Test]
        public void RegisterWithCleanup_ManyCustomRows_ListsFirstFiveAndCount()
        {
            var rows = new List<ElementFx>();
            for (var i = 0; i < 8; i++)
            {
                rows.Add(Custom("Inner/B" + i));
            }

            _parent.ElementEffects = rows.ToArray();
            string message = null;
            CanvasEmbeddedEditing.ConfirmOverrideCleanupForTests = (t, m) => { message = m; return CanvasEmbeddedEditing.OverrideChoice.Cancel; };

            CanvasEmbeddedEditing.RegisterWithCleanup(_parent, "Inner", _child, out _);

            StringAssert.Contains("Inner/B4", message);
            StringAssert.DoesNotContain("Inner/B5", message);
            StringAssert.Contains("ほか 3 件", message);
            StringAssert.Contains("8 件", message);
        }

        [Test]
        public void RegisterWithCleanup_IsOneUndoGroup()
        {
            SeedRows();
            CanvasEmbeddedEditing.ConfirmOverrideCleanupForTests = (t, m) => CanvasEmbeddedEditing.OverrideChoice.Remove;
            Undo.IncrementCurrentGroup();

            Assert.IsTrue(CanvasEmbeddedEditing.RegisterWithCleanup(_parent, "Inner", _child, out _));
            Undo.FlushUndoRecordObjects();
            Assert.AreEqual(1, _parent.EmbeddedCanvases.Length);
            Assert.AreEqual(1, _parent.ElementEffects.Length);

            Undo.PerformUndo();

            Assert.AreEqual(0, _parent.EmbeddedCanvases?.Length ?? 0, "Ctrl+Z 1 回で登録前に戻る");
            Assert.AreEqual(4, _parent.ElementEffects.Length, "取り除いた行も 1 回で戻る");
        }

        // ── 登録済みの整理 / 欄の変更 ──

        [Test]
        public void CleanUpOverrides_RegisteredEmbed_SameRules()
        {
            SeedRows();
            CanvasEmbeddedEditing.Register(_parent, "Inner", _child);
            CanvasEmbeddedEditing.ConfirmOverrideCleanupForTests = (t, m) => CanvasEmbeddedEditing.OverrideChoice.Keep;

            var cleanup = CanvasEmbeddedEditing.CleanUpOverrides(_parent, "Inner", _child);

            CollectionAssert.AreEqual(new[] { "Title", "Inner/BtnV" }, Paths(_parent));
            Assert.AreEqual(2, cleanup.RemovedDefault);
            Assert.AreEqual(1, cleanup.KeptCustom);
        }

        [Test]
        public void CleanUpOverrides_NothingToClean_ReturnsDefault_AndNeverAsks()
        {
            _parent.ElementEffects = new[] { Row("Title") };
            CanvasEmbeddedEditing.ConfirmOverrideCleanupForTests = (t, m) => { Assert.Fail("確認は出ないはず"); return CanvasEmbeddedEditing.OverrideChoice.Cancel; };

            var cleanup = CanvasEmbeddedEditing.CleanUpOverrides(_parent, "Inner", _child);

            Assert.AreEqual(0, cleanup.Removed);
            Assert.AreEqual(string.Empty, cleanup.Describe());
        }

        [Test]
        public void ChangeEmbedWithCleanup_NewRootPath_CleansNewSubtree_Cancel_KeepsOldValues()
        {
            _parent.ElementEffects = new[] { Row("Other/Deep"), Custom("Other/Btn") };
            CanvasEmbeddedEditing.AddEmpty(_parent);
            CanvasEmbeddedEditing.Register(_parent, "Inner", _child);
            Assert.AreEqual(2, _parent.EmbeddedCanvases.Length);

            // 空の行(0 番)に Other / child を設定: 設定ありの行があるので確認 → キャンセル
            CanvasEmbeddedEditing.ConfirmOverrideCleanupForTests = (t, m) => CanvasEmbeddedEditing.OverrideChoice.Cancel;
            Assert.IsFalse(CanvasEmbeddedEditing.ChangeEmbedWithCleanup(_parent, 0, "Other", _child, out var cancelled));
            Assert.IsTrue(cancelled.Cancelled);
            Assert.AreEqual(string.Empty, _parent.EmbeddedCanvases[0].RootPath ?? string.Empty, "キャンセルしたら変更しない");
            Assert.AreEqual(2, _parent.ElementEffects.Length);

            // 今度は「取り除く」
            CanvasEmbeddedEditing.ConfirmOverrideCleanupForTests = (t, m) => CanvasEmbeddedEditing.OverrideChoice.Remove;
            Assert.IsTrue(CanvasEmbeddedEditing.ChangeEmbedWithCleanup(_parent, 0, "Other", _child, out var done));
            Assert.AreEqual("Other", _parent.EmbeddedCanvases[0].RootPath);
            Assert.AreEqual(_child.Id, _parent.EmbeddedCanvases[0].Canvas.Value);
            Assert.AreEqual(0, _parent.ElementEffects.Length);
            Assert.AreEqual(2, done.Removed);
        }

        [Test]
        public void ChangeEmbedWithCleanup_ClearingChild_DoesNotTouchRows()
        {
            _parent.ElementEffects = new[] { Row("Inner/Deep") };
            CanvasEmbeddedEditing.Register(_parent, "Inner", _child);
            // Register は行を触らない(RegisterWithCleanup でない)ので 1 行残っている
            Assert.IsTrue(CanvasEmbeddedEditing.ChangeEmbedWithCleanup(_parent, 0, "Inner", null, out var cleanup));
            Assert.IsFalse(_parent.EmbeddedCanvases[0].Canvas.IsValid);
            Assert.AreEqual(1, _parent.ElementEffects.Length);
            Assert.AreEqual(0, cleanup.Removed);
        }

        // RootPath / 子の変更は、その行の他の設定(StartInactive)を保つ(2026-10-06、埋め込みの有効 / 無効)。
        [Test]
        public void ChangeEmbedWithCleanup_KeepsStartInactive()
        {
            CanvasEmbeddedEditing.Register(_parent, "Inner", _child);
            var embed = _parent.EmbeddedCanvases[0];
            embed.StartInactive = true;
            _parent.EmbeddedCanvases[0] = embed;

            Assert.IsTrue(CanvasEmbeddedEditing.ChangeEmbedWithCleanup(_parent, 0, "Other", _child, out _));

            Assert.AreEqual("Other", _parent.EmbeddedCanvases[0].RootPath);
            Assert.IsTrue(_parent.EmbeddedCanvases[0].StartInactive);
        }

        [Test]
        public void Describe_MentionsWires_WhenPresent()
        {
            var result = new CanvasEmbeddedEditing.CleanupResult(false, 2, 0, 0, 1, 3);
            var text = result.Describe();
            StringAssert.Contains("2 件取り除きました", text);
            StringAssert.Contains("Buttons 1 件", text);
            StringAssert.Contains("Sliders 3 件", text);
        }
    }
}
