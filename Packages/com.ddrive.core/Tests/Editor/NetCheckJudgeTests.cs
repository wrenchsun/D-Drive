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

        // ── 2026-09-15 修正(6-7 判定バグ、run-netcheck.cmd 初回実行の "disconnect" シナリオで発覚) ──
        // "disconnect" シナリオは Host が先に(正常終了で)いなくなり、Client は再接続しない設計のまま
        // 自分の自動テスト時間を使い切って終了する。つまり Client の ConnectedAtEnd=false は「切断を
        // 正しく検知して後片付けできたこと」の結果であり、それ自体を "not_connected" として即 FAIL にしては
        // ならない。DisconnectedObserved(自分が切断を検知した)が true のときは、後段の実質的な
        // チェック(⑤ vfx_or_active_not_cleared_after_disconnect)に判定を委ねる。

        [Test]
        public void Evaluate_NotConnected_ButSelfDisconnectObserved_AndCleanedUp_Passes()
        {
            // "disconnect" シナリオの Client 側で期待される成功パターン。
            var c = Healthy();
            c.ConnectedAtEnd = false;
            c.DisconnectedObserved = true;
            c.ActiveAndVfxZeroedAfterDisconnect = true;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsTrue(result.Pass, result.Reason);
        }

        [Test]
        public void Evaluate_NotConnected_ButSelfDisconnectObserved_NotCleanedUp_FailsOnVfxCheck_NotNotConnected()
        {
            // 切断は検知したが後片付け(演出 0)が機能していない場合は、"not_connected" ではなく
            // 実質的な理由(vfx_or_active_not_cleared_after_disconnect)で FAIL する。
            var c = Healthy();
            c.ConnectedAtEnd = false;
            c.DisconnectedObserved = true;
            c.ActiveAndVfxZeroedAfterDisconnect = false;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsFalse(result.Pass);
            Assert.AreEqual("vfx_or_active_not_cleared_after_disconnect", result.Reason);
        }

        [Test]
        public void Evaluate_NotConnected_AndDisconnectNeverObserved_StillFailsAsNotConnected()
        {
            // 回帰確認: 切断イベントに気づかずに接続だけが切れたケース(pair0/pair200/latejoin のように
            // 接続を保ち続けるはずのシナリオで、静かに切断された場合)は、従来どおり "not_connected"。
            var c = Healthy();
            c.ConnectedAtEnd = false;
            c.DisconnectedObserved = false;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsFalse(result.Pass);
            Assert.AreEqual("not_connected", result.Reason);
        }

        [Test]
        public void Evaluate_HostObservingOtherClientDisconnect_DoesNotRequireOwnVfxZeroed()
        {
            // Host は自分の周期デモを止めないため ActiveAndVfxZeroedAfterDisconnect は false のままだが、
            // NetCheckRunner 側で Host の場合は DisconnectedObserved を立てない(役割で絞り込む、
            // NetCheckRunner.OnBridgeDisconnected 参照)。ここでは NetCheckJudge 単体として、
            // DisconnectedObserved=false のまま ConnectedAtEnd=true(Host は継続して接続済み)なら
            // 無関係に PASS することを確認する。
            var c = Healthy();
            c.ConnectedAtEnd = true;
            c.DisconnectedObserved = false;
            c.ActiveAndVfxZeroedAfterDisconnect = false;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsTrue(result.Pass, result.Reason);
        }

        // ── [14_networking.md] §16(N-3、2026-09-22) — Host 1 + Client 3 対応 ──

        [Test]
        public void Evaluate_ExpectedClientCountZero_SkipsCheck_EvenIfNoneObserved()
        {
            // 未指定(Client 役・-ddrive-expect-clients 未指定)は従来どおり判定をスキップする。
            var c = Healthy();
            c.ExpectedClientCount = 0;
            c.MaxConnectedClientsObserved = 0;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsTrue(result.Pass, result.Reason);
        }

        [Test]
        public void Evaluate_ExpectedClientCount_NotReached_Fails()
        {
            var c = Healthy();
            c.ExpectedClientCount = 3;
            c.MaxConnectedClientsObserved = 2;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsFalse(result.Pass);
            StringAssert.Contains("expected_clients_not_reached", result.Reason);
        }

        [Test]
        public void Evaluate_ExpectedClientCount_Reached_Passes()
        {
            var c = Healthy();
            c.ExpectedClientCount = 3;
            c.MaxConnectedClientsObserved = 3;
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsTrue(result.Pass, result.Reason);
        }

        [Test]
        public void Evaluate_ExpectedClientCount_ObservedPeakKeptEvenIfOneLeftLater()
        {
            // quad_leave のように途中で 1 人抜けても、一度でも期待人数に到達した実績
            // (MaxConnectedClientsObserved の「最大値」)があれば満たす。
            var c = Healthy();
            c.ExpectedClientCount = 3;
            c.MaxConnectedClientsObserved = 3; // 一度 3 人揃った後、2 人に減っても最大値は 3 のまま
            var result = NetCheckJudge.Evaluate(c);
            Assert.IsTrue(result.Pass, result.Reason);
        }
    }
}
