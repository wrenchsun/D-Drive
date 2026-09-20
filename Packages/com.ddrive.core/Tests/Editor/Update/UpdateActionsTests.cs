using DDrive.Editor.Update;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Update
{
    // [42_distribution.md] §4.2 手順 5/§6 P-8(2026-09-20) — 「更新を適用」の 4 段の実行順・
    // 「途中で失敗したら以降を実行しない」・「全段成功したときだけ後処理(LastAppliedVersion 相当)を
    // 呼ぶ」を、実 Unity API(AssetDatabase/Addressables 等)を一切使わないフェイクの段で固定する
    // ([42_distribution.md] §6 P-8 の指示どおり、実行そのものはこのテストから呼ばない)。
    public class UpdateActionsTests
    {
        private static UpdateActions.StepOutcome Ok(string name) => new(name, true, "ok");
        private static UpdateActions.StepOutcome Fail(string name) => new(name, false, "boom");

        [Test]
        public void Apply_AllStepsSucceed_RunsAllInOrder_AndCallsMarkApplied()
        {
            var callOrder = new System.Collections.Generic.List<string>();
            var markAppliedCalled = false;

            var steps = new UpdateActions.Steps
            {
                Migrate = () => { callOrder.Add("migrate"); return Ok("migrate"); },
                RegenerateIdsAndTuning = () => { callOrder.Add("regen"); return Ok("regen"); },
                SyncAddressables = () => { callOrder.Add("addr"); return Ok("addr"); },
                RunValidation = () => { callOrder.Add("validate"); return Ok("validate"); },
                MarkApplied = () => markAppliedCalled = true,
            };

            var result = UpdateActions.Apply(steps);

            CollectionAssert.AreEqual(new[] { "migrate", "regen", "addr", "validate" }, callOrder, "手順どおりの順序で実行される");
            Assert.IsTrue(result.AllSucceeded);
            Assert.IsTrue(result.MarkAppliedCalled);
            Assert.IsTrue(markAppliedCalled);
            Assert.AreEqual(4, result.StepOutcomes.Count);
        }

        [Test]
        public void Apply_SecondStepFails_StopsBeforeRemainingSteps_AndDoesNotMarkApplied()
        {
            var callOrder = new System.Collections.Generic.List<string>();
            var markAppliedCalled = false;

            var steps = new UpdateActions.Steps
            {
                Migrate = () => { callOrder.Add("migrate"); return Ok("migrate"); },
                RegenerateIdsAndTuning = () => { callOrder.Add("regen"); return Fail("regen"); },
                SyncAddressables = () => { callOrder.Add("addr"); return Ok("addr"); },
                RunValidation = () => { callOrder.Add("validate"); return Ok("validate"); },
                MarkApplied = () => markAppliedCalled = true,
            };

            var result = UpdateActions.Apply(steps);

            CollectionAssert.AreEqual(new[] { "migrate", "regen" }, callOrder, "失敗した段より後ろは実行しない");
            Assert.IsFalse(result.AllSucceeded);
            Assert.IsFalse(result.MarkAppliedCalled);
            Assert.IsFalse(markAppliedCalled);
            Assert.AreEqual(2, result.StepOutcomes.Count);
            Assert.IsFalse(result.StepOutcomes[1].Success);
        }

        [Test]
        public void Apply_FirstStepFails_RunsNothingElse()
        {
            var callOrder = new System.Collections.Generic.List<string>();

            var steps = new UpdateActions.Steps
            {
                Migrate = () => { callOrder.Add("migrate"); return Fail("migrate"); },
                RegenerateIdsAndTuning = () => { callOrder.Add("regen"); return Ok("regen"); },
                SyncAddressables = () => { callOrder.Add("addr"); return Ok("addr"); },
                RunValidation = () => { callOrder.Add("validate"); return Ok("validate"); },
                MarkApplied = () => Assert.Fail("MarkApplied は呼ばれてはいけない"),
            };

            var result = UpdateActions.Apply(steps);

            CollectionAssert.AreEqual(new[] { "migrate" }, callOrder);
            Assert.IsFalse(result.AllSucceeded);
            Assert.AreEqual(1, result.StepOutcomes.Count);
        }

        [Test]
        public void Apply_NullSteps_ReturnsEmptyResult_WithoutThrowing()
        {
            var result = UpdateActions.Apply(null);

            Assert.IsNotNull(result);
            Assert.IsEmpty(result.StepOutcomes);
            Assert.IsFalse(result.AllSucceeded);
            Assert.IsFalse(result.MarkAppliedCalled);
        }

        [Test]
        public void Apply_NoStepsInjected_DoesNotMarkApplied()
        {
            var markAppliedCalled = false;
            var steps = new UpdateActions.Steps { MarkApplied = () => markAppliedCalled = true };

            var result = UpdateActions.Apply(steps);

            Assert.IsFalse(result.AllSucceeded, "何も実行していないので成功扱いにしない");
            Assert.IsFalse(markAppliedCalled);
        }
    }
}
