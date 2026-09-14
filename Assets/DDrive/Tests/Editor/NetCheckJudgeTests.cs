using DDrive.Runtime.Net;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] 6-7 — NetCheckJudge(ログ集計 → PASS/FAIL の純粋ロジック)の EditMode テスト。
    // Unity API に依存しないため NetLaunchArgsTests と同じ方針で EditMode(高速)で検証する。
    public class NetCheckJudgeTests
    {
        private static NetCheckCounters Healthy()
        {
            return new NetCheckCounters
            {
                ConnectedAtEnd = true,
                IsOffRole = false,
                ExceptionOrErrorCount = 0,
                RequireSignalActivity = true,
                SignalFireCount = 1,
                SignalRecvCount = 4,
                ForgedCancelSentCount = 5,
                ForgedCancelDiscardedCount = 5,
                RequireLateJoinRestore = false,
                LateJoinRestoreObserved = false,
                PlaceholderObserved = false,
                TrackFiredOverGraceCount = 0,
                TrackSkippedWithinGraceCount = 0,
                DisconnectedObserved = false,
                ActiveAndVfxZeroedAfterDisconnect = false,
                ContentHashApplicable = true,
                ContentHashStatus = "OK",
            };
        }

        [Test]
        public void Evaluate_AllHealthy_Passes()
        {
            var result = NetCheckJudge.Evaluate(Healthy());
            Assert.IsTrue(result.Pass, result.Reason);
        }

        [Test]
        public void Evaluate_OffRole_Passes_EvenWithoutNetActivity()
        {
            var c = new NetCheckCounters { IsOffRole = true, ExceptionOrErrorCount = 0 };
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsTrue(result.Pass);
        }

        [Test]
        public void Evaluate_OffRole_StillFailsOnException()
        {
            var c = new NetCheckCounters { IsOffRole = true, ExceptionOrErrorCount = 1 };
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsFalse(result.Pass);
            StringAssert.Contains("exception_or_error_count", result.Reason);
        }

        [Test]
        public void Evaluate_ExceptionOrError_Fails_RegardlessOfOtherFields()
        {
            var c = Healthy();
            c.ExceptionOrErrorCount = 2;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsFalse(result.Pass);
            StringAssert.Contains("exception_or_error_count=2", result.Reason);
        }

        [Test]
        public void Evaluate_NotConnected_Fails()
        {
            var c = Healthy();
            c.ConnectedAtEnd = false;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsFalse(result.Pass);
            Assert.AreEqual("not_connected", result.Reason);
        }

        [Test]
        public void Evaluate_PlaceholderObserved_Fails()
        {
            var c = Healthy();
            c.PlaceholderObserved = true;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsFalse(result.Pass);
            Assert.AreEqual("placeholder_observed", result.Reason);
        }

        [Test]
        public void Evaluate_RequireSignalActivity_ButNoRecv_Fails()
        {
            var c = Healthy();
            c.SignalRecvCount = 0;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsFalse(result.Pass);
            Assert.AreEqual("no_signal_recv_observed", result.Reason);
        }

        [Test]
        public void Evaluate_SignalActivityNotRequired_SkipsCheck_EvenWithZeroRecv()
        {
            var c = Healthy();
            c.RequireSignalActivity = false;
            c.SignalRecvCount = 0;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsTrue(result.Pass, result.Reason);
        }

        [Test]
        public void Evaluate_ForgedCancelSentButNotAllDiscarded_Fails()
        {
            var c = Healthy();
            c.ForgedCancelSentCount = 5;
            c.ForgedCancelDiscardedCount = 4;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsFalse(result.Pass);
            StringAssert.Contains("forged_cancel_mismatch", result.Reason);
        }

        [Test]
        public void Evaluate_NoForgedCancelSent_SkipsCheck_EvenIfDiscardedCountIsPositive()
        {
            // Host は自分では偽造 Cancel を送らないが、Client 発の中継を自分の台帳としても
            // 受信して破棄することがある(ForgedCancelSentCount=0)。この場合は判定対象外。
            var c = Healthy();
            c.ForgedCancelSentCount = 0;
            c.ForgedCancelDiscardedCount = 3;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsTrue(result.Pass, result.Reason);
        }

        [Test]
        public void Evaluate_LateJoinRequired_ButNotRestored_Fails()
        {
            var c = Healthy();
            c.RequireLateJoinRestore = true;
            c.LateJoinRestoreObserved = false;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsFalse(result.Pass);
            Assert.AreEqual("late_join_not_restored", result.Reason);
        }

        [Test]
        public void Evaluate_LateJoinRequired_AndRestored_Passes()
        {
            var c = Healthy();
            c.RequireLateJoinRestore = true;
            c.LateJoinRestoreObserved = true;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsTrue(result.Pass, result.Reason);
        }

        [Test]
        public void Evaluate_TrackFiredOverGrace_Fails()
        {
            var c = Healthy();
            c.TrackFiredOverGraceCount = 1;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsFalse(result.Pass);
            StringAssert.Contains("track_fired_over_grace", result.Reason);
        }

        [Test]
        public void Evaluate_TrackSkippedWithinGrace_Fails()
        {
            var c = Healthy();
            c.TrackSkippedWithinGraceCount = 1;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsFalse(result.Pass);
            StringAssert.Contains("track_skipped_within_grace", result.Reason);
        }

        [Test]
        public void Evaluate_DisconnectedButVfxNotCleared_Fails()
        {
            var c = Healthy();
            c.DisconnectedObserved = true;
            c.ActiveAndVfxZeroedAfterDisconnect = false;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsFalse(result.Pass);
            Assert.AreEqual("vfx_or_active_not_cleared_after_disconnect", result.Reason);
        }

        [Test]
        public void Evaluate_DisconnectedAndVfxCleared_Passes()
        {
            var c = Healthy();
            c.DisconnectedObserved = true;
            c.ActiveAndVfxZeroedAfterDisconnect = true;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsTrue(result.Pass, result.Reason);
        }

        [Test]
        public void Evaluate_NotDisconnected_SkipsVfxCheck()
        {
            var c = Healthy();
            c.DisconnectedObserved = false;
            c.ActiveAndVfxZeroedAfterDisconnect = false;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsTrue(result.Pass, result.Reason);
        }

        [Test]
        public void Evaluate_ContentHashApplicable_ButNotOk_Fails()
        {
            var c = Healthy();
            c.ContentHashApplicable = true;
            c.ContentHashStatus = "不一致: MaterialCatalog: entries local=3 remote=2";
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsFalse(result.Pass);
            StringAssert.Contains("content_hash_not_ok", result.Reason);
        }

        [Test]
        public void Evaluate_ContentHashNotApplicable_SkipsCheck()
        {
            var c = Healthy();
            c.ContentHashApplicable = false;
            c.ContentHashStatus = "検証中...";
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsTrue(result.Pass, result.Reason);
        }

        [Test]
        public void Evaluate_FirstFailureWins_ExceptionCheckedBeforeConnection()
        {
            // 複数条件が同時に FAIL でも、判定の優先順位(Exception/Error が最優先)が安定していることを確認する。
            var c = Healthy();
            c.ExceptionOrErrorCount = 1;
            c.ConnectedAtEnd = false;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsFalse(result.Pass);
            StringAssert.Contains("exception_or_error_count", result.Reason);
        }
    }
}
