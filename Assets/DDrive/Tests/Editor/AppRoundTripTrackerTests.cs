using DDrive.Runtime.Net;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] 6-6(K2 修正、2026-09-18 再修正) — v5 実機確認(docs/29_network_device_test.md §16.2)で
    // 見つかった「実機でしか出ない実時間依存のバグ」の回帰テスト。AppRoundTripTracker(Unity API 非依存)を
    // NetCheckJudgeTests と同じ方針で EditMode(高速)で検証する。
    //
    // §16.2 で見逃した理由そのもの ─ 「1 秒周期で Ping を送り続けたまま Pong が返らない」状況 ─ を明示的に
    // 再現する(a. AppRoundTripMs が経過時間とともに単調に増えること、b. 平常時は IsAppRoundTripMsStale が
    // false のままであること)。
    public class AppRoundTripTrackerTests
    {
        // 修正前の実装は `elapsedMs = now - _lastPingSentRealtime` を使っており、`_lastPingSentRealtime` は
        // Pong の有無に関係なく毎秒の Ping 送信で上書きされていた。そのため「1 秒おきの Ping 送信の
        // 直前」にサンプリングすると、常に「直前の Ping 送信からの経過時間」(高々 1000ms 弱)しか返らず、
        // 通信停止が何秒続いても値が頭打ちになっていた(実機確認 v5 の実測値 372/714/370/370/370/633/…
        // がこれに当たる、docs/29 §16.2)。このテストはまさにそのサンプリングタイミング(次の Ping 送信
        // 直前)で値を取るため、修正前の実装のままだとこのテストは赤くなる
        // (5 秒間停止しても最大で ~1000ms 程度にしかならず、Assert.Greater(..., 4000d) 等で失敗する)。
        [Test]
        public void AppRoundTripMs_GrowsMonotonically_WhileOutageContinues_WithPingSentEverySecond()
        {
            var tracker = new AppRoundTripTracker();
            var t = 0d;

            // 接続確立直後、しばらくは正常に Ping/Pong が往復する(実機確認と同じく、既に繋がっている
            // 接続が途中から止まるケースを再現する)。
            for (var i = 0; i < 3; i++)
            {
                t += 1d; // Ping 送信(1 秒おき)
                tracker.OnPingSent(t);
                t += 0.2d; // 200ms 後に Pong が返る
                tracker.OnPongReceived(200d);
            }

            Assert.IsFalse(tracker.IsStale, "正常往復中は stale ではない");

            // ここから Pong が一切返らない「通信停止」を 6 秒分再現する(Wi-Fi 切断の実機確認と同じ長さ)。
            // 1 秒おきに Ping を送り続け、「次の Ping を送る直前」の値をサンプリングする
            // (修正前バグが観測されたのと同じタイミング)。
            double? previous = null;
            for (var i = 0; i < 6; i++)
            {
                t += 1d;
                tracker.OnPingSent(t);

                // 次の Ping 送信の直前(0.999 秒後)でサンプリングする。
                var sample = tracker.GetRoundTripMs(t + 0.999d);

                Assert.IsTrue(sample.HasValue, "応答待ちの間も経過時間ベースの推定値が返る");
                if (previous.HasValue)
                {
                    Assert.Greater(sample.Value, previous.Value,
                        $"i={i}: 停止が長引くほど単調に増え続けるはず(修正前は 1 秒周期の基準点リセットで頭打ちになっていた)");
                }

                previous = sample;
            }

            // 6 秒間停止し続けた後は、少なくとも 5 秒(5000ms)近くまで伸びているはず
            // (修正前の実装は基準点が毎秒リセットされるため、どれだけ停止が続いても ~1000ms 前後にしか
            // ならなかった。docs/29 §16.2 の実測 372/714/370/... 参照)。
            Assert.Greater(previous.Value, 4500d,
                "経過時間の基準が「最後の Ping 送信時刻」のままだと頭打ちになり、ここで失敗する");
        }

        // 平常運用(Ping 周期 1 秒に対して Pong が数百 ms で毎回返ってくる)では、旧実装は「Ping 送信〜
        // Pong 到達までの間」を毎回 stale=true として報告していた(1 秒のうち約 20% が stale)。
        // 修正後は連続 N 回(既定 3 回)Pong が返らない場合だけ stale になるため、平常運用中は一度も
        // true にならないことを確認する。
        [Test]
        public void IsStale_StaysFalse_DuringNormalOperation_EvenWhileAwaitingEachPong()
        {
            var tracker = new AppRoundTripTracker();
            var t = 0d;

            for (var i = 0; i < 20; i++)
            {
                t += 1d;
                tracker.OnPingSent(t);

                // Ping 送信直後、Pong 到着前(旧実装が誤って stale=true としていたタイミング)。
                Assert.IsFalse(tracker.IsStale, $"i={i}: Pong 到着前でも、まだ 1 回も未達になっていないので stale ではない");

                t += 0.2d; // 200ms 後に Pong が返る(RTT 実測値。1 秒周期の Ping に対して十分速い)
                tracker.OnPongReceived(200d);

                Assert.IsFalse(tracker.IsStale, $"i={i}: Pong 受信直後も stale ではない");
            }
        }

        // 単発の Pong ロス(Unreliable チャンネルでは日常的に起こりうる)だけでは stale にならないこと。
        [Test]
        public void IsStale_StaysFalse_AfterSingleIsolatedMissedPong_BelowThreshold()
        {
            var tracker = new AppRoundTripTracker(staleThreshold: 3);
            var t = 0d;

            t += 1d;
            tracker.OnPingSent(t); // Ping #1
            t += 1d;
            tracker.OnPingSent(t); // Ping #2(#1 の Pong がまだ届いていない → 未達 1 回目)
            Assert.IsFalse(tracker.IsStale, "1 回の未達だけでは stale にならない(閾値 3 回未満)");

            t += 0.2d;
            tracker.OnPongReceived(1200d); // #2 に対する Pong がここで届く

            Assert.IsFalse(tracker.IsStale, "Pong を受信すれば未達カウントはリセットされる");
        }

        // 連続 N 回(既定 3 回)Pong が返らなければ stale になること。
        [Test]
        public void IsStale_BecomesTrue_AfterConsecutiveMissedPongsReachThreshold()
        {
            var tracker = new AppRoundTripTracker(staleThreshold: 3);
            var t = 0d;

            t += 1d;
            tracker.OnPingSent(t); // Ping #1(まだ未達 0 回目)
            Assert.IsFalse(tracker.IsStale);

            t += 1d;
            tracker.OnPingSent(t); // #1 未達 → 未達 1 回目
            Assert.IsFalse(tracker.IsStale);

            t += 1d;
            tracker.OnPingSent(t); // #2 未達 → 未達 2 回目
            Assert.IsFalse(tracker.IsStale);

            t += 1d;
            tracker.OnPingSent(t); // #3 未達 → 未達 3 回目(閾値到達)
            Assert.IsTrue(tracker.IsStale, "連続 3 回 Pong が返っていないため通信途絶の疑いとして true になる");
        }

        // 切断(Reset)後は、経過時間による推定値も stale フラグも初期状態(null/false)に戻ること。
        [Test]
        public void Reset_ClearsMeasuredValue_AndStaleFlag_AndUnansweredStreak()
        {
            var tracker = new AppRoundTripTracker(staleThreshold: 2);
            var t = 0d;

            t += 1d;
            tracker.OnPingSent(t);
            t += 1d;
            tracker.OnPingSent(t); // 未達 1 回目
            t += 1d;
            tracker.OnPingSent(t); // 未達 2 回目 → stale

            Assert.IsTrue(tracker.IsStale);
            Assert.IsTrue(tracker.GetRoundTripMs(t + 0.5d).HasValue);

            tracker.Reset();

            Assert.IsFalse(tracker.IsStale, "Reset 後は stale ではない");
            Assert.IsFalse(tracker.GetRoundTripMs(t + 10d).HasValue, "Reset 後は推定値も無い(n/a)");
        }

        // [44_review_2026-09-19.md] P2-1 — 偽造 NetPongMsg の再発防止テスト。CatalogContentHashGateTests の
        // ClientSide_ForgedResultFromNonHostSender_IsIgnored と同じ手口(信頼できる送信元と違う senderId で
        // 受信させる)。NgoNetBridge は NetworkBehaviour 派生で EditMode から直接テストできないため、
        // 送信元検証込みのオーバーロード(AppRoundTripTracker.OnPongReceived(double, ulong, ulong))を対象にする。
        [Test]
        public void OnPongReceived_WithSenderValidation_IgnoresPongFromUntrustedSender()
        {
            const ulong hostClientId = 0UL;
            const ulong forgedSenderId = 99UL;

            var tracker = new AppRoundTripTracker(staleThreshold: 3);
            var t = 0d;

            t += 1d;
            tracker.OnPingSent(t); // Ping #1(まだ未達 0 回目)
            t += 1d;
            tracker.OnPingSent(t); // #1 未達 → 未達 1 回目

            var beforeRoundTripMs = tracker.GetRoundTripMs(t + 0.5d);
            var beforeStale = tracker.IsStale;

            // 改造 Client(forgedSenderId)からの偽造 Pong は無視され、RTT/未達カウントとも変化しない。
            tracker.OnPongReceived(1d, forgedSenderId, hostClientId);

            Assert.AreEqual(beforeRoundTripMs, tracker.GetRoundTripMs(t + 0.5d), "偽造 Pong では RTT が変わらない");
            Assert.AreEqual(beforeStale, tracker.IsStale, "偽造 Pong では stale 判定が変わらない");

            // 正当な送信元(Host)からの Pong は通常どおり反映される。
            tracker.OnPongReceived(123d, hostClientId, hostClientId);
            Assert.AreEqual(123d, tracker.GetRoundTripMs(t + 0.001d).Value, 0.001d, "正当な送信元の Pong は反映される");
            Assert.IsFalse(tracker.IsStale, "正当な Pong を受信すれば未達カウントはリセットされる");
        }

        // 実測値(Pong 到達済み)が経過時間による下限推定を上回っている間は、実測値をそのまま返す
        // (応答待ちの直後の一瞬だけ経過時間が実測値を下回ることがあり、その間は実測値のままでよい)。
        [Test]
        public void GetRoundTripMs_ReturnsLastMeasuredValue_WhenElapsedTimeHasNotExceededIt()
        {
            var tracker = new AppRoundTripTracker();
            var t = 0d;

            t += 1d;
            tracker.OnPingSent(t);
            tracker.OnPongReceived(180d); // 実測 180ms

            t += 1d;
            tracker.OnPingSent(t); // 次の Ping。まだ応答待ちだが経過時間はごく僅か。

            var sample = tracker.GetRoundTripMs(t + 0.01d); // 10ms 経過(実測値 180ms をまだ下回る)
            Assert.AreEqual(180d, sample.Value, 0.001d, "経過時間が最後の実測値を下回っている間は実測値をそのまま返す");
        }
    }
}
