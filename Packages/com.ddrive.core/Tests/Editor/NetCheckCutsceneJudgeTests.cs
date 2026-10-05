using System.Collections.Generic;
using System.Globalization;
using DDrive.Runtime.Net;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [14_networking.md] §22(N-8、2026-10-06) — NetCheckCutsceneJudge(Cutscene のマーカーの NetCheck 判定)の EditMode テスト。
    // Unity API 非依存の純関数なので NetCheckJudgeTests と同じ方針。
    public class NetCheckCutsceneJudgeTests
    {
        private static readonly string[] Keys = { "m0", "m1", "m4", "m6", "m15" };
        private static readonly double[] Times = { 0.0, 0.1, 0.4, 0.6, 1.5 };

        private static string Inv(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

        private static List<string> Config(string role, int plays, int expectPlays)
            => new()
            {
                $"[NetCheck] cutscene_config role={role} plays={plays} interval=4.0 startDelay=2.0 expectPlays={expectPlays} kinds=2 markers=m0:0.0,m1:0.1,m4:0.4,m6:0.6,m15:1.5 grace=0.5",
            };

        private static void AddMarkers(List<string> lines, string handle, params string[] keys)
        {
            foreach (var key in keys)
            {
                lines.Add($"[NetCheck] cutscene_marker key={key} markerTime=0.000 elapsed=0.000 handle={handle}");
                lines.Add($"[NetCheck] cutscene_signal key={key} handle={handle}");
            }
        }

        private static List<string> Receiver(double s, int silent, params string[] fired)
        {
            var lines = Config("observe", 0, -1);
            lines.Add($"[NetCheck] cutscene_recv netKey=0x01000001 s={Inv(s)} silent={silent} handle=3.1");
            AddMarkers(lines, "3.1", fired);
            return lines;
        }

        // ── 期待の計算 ──

        private static int[] Expected(double s, bool sender = false)
        {
            var result = new int[Times.Length];
            for (var i = 0; i < Times.Length; i++)
            {
                NetCheckCutsceneJudge.ExpectedCount(Times[i], sender, s, out var min, out var max);
                Assert.AreEqual(min, max, $"s={s} t={Times[i]} は曖昧であってはならない");
                result[i] = min;
            }

            return result;
        }

        [Test]
        public void ExpectedCount_S0_AllFireOnce() => CollectionAssert.AreEqual(new[] { 1, 1, 1, 1, 1 }, Expected(0.0));

        [Test]
        public void ExpectedCount_S03_AllFireOnce() => CollectionAssert.AreEqual(new[] { 1, 1, 1, 1, 1 }, Expected(0.3));

        [Test]
        public void ExpectedCount_S07_OldMarkersSilent()
        {
            // s=0.7: 0(0.7)・0.1(0.6) は 0.5 超で無音、0.4(0.3)は発火、0.6(0.1)は発火、1.5 は t > s。
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 1, 1 }, Expected(0.7));
        }

        [Test]
        public void ExpectedCount_S20_OldMarkersSilent_And1p5IsOnTheBoundary()
        {
            // s=2.0: 0 / 0.1 / 0.4 / 0.6 は無音。1.5 は s − t = 0.5 ちょうど(本体の規則では発火。F3 丸めで判別できないので 0〜1)。
            for (var i = 0; i < 4; i++)
            {
                NetCheckCutsceneJudge.ExpectedCount(Times[i], false, 2.0, out var min, out var max);
                Assert.AreEqual(0, max, $"t={Times[i]}");
                Assert.AreEqual(0, min);
            }

            NetCheckCutsceneJudge.ExpectedCount(1.5, false, 2.0, out var bmin, out var bmax);
            Assert.AreEqual(0, bmin);
            Assert.AreEqual(1, bmax);
        }

        [Test]
        public void ExpectedCount_ExactlyGrace_IsFiredOrAmbiguous()
        {
            // s=0.5: t=0 は s − t = 0.5 ちょうど → 本体の規則(> 0.5 だけ無音)では発火。F3 丸めで判別できないので 0〜1 を許す。
            NetCheckCutsceneJudge.ExpectedCount(0.0, false, 0.5, out var min, out var max);
            Assert.AreEqual(0, min);
            Assert.AreEqual(1, max);

            // 0.4 は 0.1 差 → 発火。0.6 は t > s → 発火。
            NetCheckCutsceneJudge.ExpectedCount(0.4, false, 0.5, out min, out max);
            Assert.AreEqual(1, min);
            Assert.AreEqual(1, max);
        }

        [Test]
        public void ExpectedCount_Sender_AlwaysOnce()
        {
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 1, 1 }, Expected(2.0, sender: true));
        }

        [Test]
        public void ExpectedSilentCount_CountsKindsPerTime()
        {
            var markers = NetCheckCutsceneJudge.DefaultMarkers();
            NetCheckCutsceneJudge.ExpectedSilentCount(markers, 0.7, 2, out var min, out var max);
            Assert.AreEqual(4, min); // 0 と 0.1 の 2 時刻 x 2 種類
            Assert.AreEqual(4, max);
            NetCheckCutsceneJudge.ExpectedSilentCount(markers, 0.0, 2, out min, out max);
            Assert.AreEqual(0, min);
            Assert.AreEqual(0, max);
        }

        // ── ログ行の解析 ──

        [Test]
        public void TryParseLine_ParsesKeyValues()
        {
            var kv = new Dictionary<string, string>();
            Assert.IsTrue(NetCheckCutsceneJudge.TryParseLine("UnityEngine.Debug:Log [NetCheck] cutscene_marker key=m4 markerTime=0.400 elapsed=0.412 handle=3.1", out var kind, kv));
            Assert.AreEqual("cutscene_marker", kind);
            Assert.AreEqual("m4", kv["key"]);
            Assert.AreEqual("0.412", kv["elapsed"]);
            Assert.AreEqual("3.1", kv["handle"]);
            Assert.IsFalse(NetCheckCutsceneJudge.TryParseLine("[DDriveNetCheck] heartbeat=1", out _, kv));
        }

        [Test]
        public void TryParseEngineReceiveLine_ParsesStartPositionSilentAndKey()
        {
            const string line = "[Net/Client] Cutscene: 受信した再生の開始位置 s=0.234 秒(NetworkTime − StartNetTime)・猶予(0.5 秒)を超えて無音にしたマーカー 4 件(HandleNetKey=0x0100ABCD、'NetCheck Markers')。";
            Assert.IsTrue(NetCheckCutsceneJudge.TryParseEngineReceiveLine(line, out var s, out var silent, out var netKey));
            Assert.AreEqual(0.234, s, 1e-9);
            Assert.AreEqual(4, silent);
            Assert.AreEqual("0x0100ABCD", netKey);
            Assert.IsFalse(NetCheckCutsceneJudge.TryParseEngineReceiveLine("[Net/Client] 別のログ", out _, out _, out _));
        }

        // ── ログ全体の判定 ──

        [Test]
        public void EvaluateLog_Sender_AllFiredOnce_Passes()
        {
            var lines = Config("trigger", 1, -1);
            lines.Add("[NetCheck] cutscene_play seq=1 netKey=pending localTime=10.000 handle=2.1");
            AddMarkers(lines, "2.1", Keys);
            var summary = NetCheckCutsceneJudge.EvaluateLog(lines);
            Assert.IsTrue(summary.Pass, summary.Reason);
            Assert.AreEqual(1, summary.SenderPlays);
            Assert.AreEqual(0, summary.ReceivedPlays);
        }

        [Test]
        public void EvaluateLog_Sender_MissingZeroMarker_Fails()
        {
            var lines = Config("trigger", 1, -1);
            lines.Add("[NetCheck] cutscene_play seq=1 netKey=pending localTime=10.000 handle=2.1");
            AddMarkers(lines, "2.1", "m1", "m4", "m6", "m15");
            var summary = NetCheckCutsceneJudge.EvaluateLog(lines);
            Assert.IsFalse(summary.Pass);
            StringAssert.Contains("marker_mismatch key=m0", summary.Reason);
        }

        [Test]
        public void EvaluateLog_Sender_PlaysCountMismatch_Fails()
        {
            var lines = Config("trigger", 2, -1);
            lines.Add("[NetCheck] cutscene_play seq=1 netKey=pending localTime=10.000 handle=2.1");
            AddMarkers(lines, "2.1", Keys);
            var summary = NetCheckCutsceneJudge.EvaluateLog(lines);
            Assert.IsFalse(summary.Pass);
            StringAssert.Contains("sender_plays_mismatch", summary.Reason);
        }

        [Test]
        public void EvaluateLog_Receiver_S03_AllFired_Passes()
        {
            var summary = NetCheckCutsceneJudge.EvaluateLog(Receiver(0.3, 0, Keys));
            Assert.IsTrue(summary.Pass, summary.Reason);
            Assert.AreEqual(0.3, summary.MinS, 1e-9);
            Assert.AreEqual(0.3, summary.MaxS, 1e-9);
        }

        [Test]
        public void EvaluateLog_Receiver_S07_SilencesOldOnes_Passes()
        {
            // 0 と 0.1 が無音(2 時刻 x 2 種類 = 4 件)。残りは 1 回ずつ。
            var summary = NetCheckCutsceneJudge.EvaluateLog(Receiver(0.7, 4, "m4", "m6", "m15"));
            Assert.IsTrue(summary.Pass, summary.Reason);
        }

        [Test]
        public void EvaluateLog_Receiver_S07_FiredSilencedMarker_Fails()
        {
            var summary = NetCheckCutsceneJudge.EvaluateLog(Receiver(0.7, 4, "m0", "m4", "m6", "m15"));
            Assert.IsFalse(summary.Pass);
            StringAssert.Contains("marker_mismatch key=m0", summary.Reason);
        }

        [Test]
        public void EvaluateLog_Receiver_S20_AllSilent_Passes()
        {
            var summary = NetCheckCutsceneJudge.EvaluateLog(Receiver(2.0, 10));
            Assert.IsTrue(summary.Pass, summary.Reason);
        }

        [Test]
        public void EvaluateLog_Receiver_SilentCountDisagreesWithEngineLog_Fails()
        {
            var summary = NetCheckCutsceneJudge.EvaluateLog(Receiver(0.7, 2, "m4", "m6", "m15"));
            Assert.IsFalse(summary.Pass);
            StringAssert.Contains("silent_count_mismatch", summary.Reason);
        }

        [Test]
        public void EvaluateLog_Receiver_S05Exactly_BoundaryAcceptsEither()
        {
            // s=0.500: t=0 は境界(発火 or 無音を許す)。無音 = 2 件、発火 = 0 件。
            Assert.IsTrue(NetCheckCutsceneJudge.EvaluateLog(Receiver(0.5, 0, Keys)).Pass);
            Assert.IsTrue(NetCheckCutsceneJudge.EvaluateLog(Receiver(0.5, 2, "m1", "m4", "m6", "m15")).Pass);
        }

        [Test]
        public void EvaluateLog_DoubleFire_Fails()
        {
            var lines = Receiver(0.3, 0, Keys);
            lines.Add("[NetCheck] cutscene_marker key=m4 markerTime=0.400 elapsed=0.500 handle=3.1");
            var summary = NetCheckCutsceneJudge.EvaluateLog(lines);
            Assert.IsFalse(summary.Pass);
            StringAssert.Contains("duplicate_fire key=m4", summary.Reason);
        }

        [Test]
        public void EvaluateLog_DoubleSignal_Fails()
        {
            var lines = Receiver(0.3, 0, Keys);
            lines.Add("[NetCheck] cutscene_signal key=m6 handle=3.1");
            var summary = NetCheckCutsceneJudge.EvaluateLog(lines);
            Assert.IsFalse(summary.Pass);
            StringAssert.Contains("duplicate_signal key=m6", summary.Reason);
        }

        [Test]
        public void EvaluateLog_Observer_ExpectedPlaysMismatch_Fails()
        {
            var lines = Config("observe", 0, 3);
            lines.Add("[NetCheck] cutscene_recv netKey=0x01000001 s=0.300 silent=0 handle=3.1");
            AddMarkers(lines, "3.1", Keys);
            var summary = NetCheckCutsceneJudge.EvaluateLog(lines);
            Assert.IsFalse(summary.Pass);
            StringAssert.Contains("received_plays_mismatch", summary.Reason);
        }

        [Test]
        public void EvaluateLog_Observer_ZeroPlaysExpectedZero_Passes()
        {
            Assert.IsTrue(NetCheckCutsceneJudge.EvaluateLog(Config("observe", 0, 0)).Pass);
        }

        [Test]
        public void EvaluateLog_Observer_NothingObserved_Fails()
        {
            var summary = NetCheckCutsceneJudge.EvaluateLog(Config("observe", 0, -1));
            Assert.IsFalse(summary.Pass);
            Assert.AreEqual("no_play_observed", summary.Reason);
        }

        [Test]
        public void EvaluateLog_TimelineMarkersNotLoaded_Fails()
        {
            var lines = Receiver(0.3, 0, Keys);
            lines.Insert(1, "[NetCheck] cutscene_timeline ok=0 tracks=null:1 markers=none");
            var summary = NetCheckCutsceneJudge.EvaluateLog(lines);
            Assert.IsFalse(summary.Pass);
            Assert.AreEqual("timeline_markers_not_loaded", summary.Reason);
        }

        [Test]
        public void EvaluateLog_SignalNotLoaded_Fails()
        {
            // M-6: Player で Signal マーカーが読めない(signal=0)のは本体の不具合(MonoScript が無い型)。判定は FAIL にする。
            var lines = Config("observe", 0, -1);
            lines.Add("[NetCheck] cutscene_timeline ok=1 signal=0 duration=3.000 tracks=MarkerTrack:1 markers=NetCheckCutsceneMarker:5 clips=none missing=CutsceneSignalTrack,CutsceneSignalNotification");
            lines.Add("[NetCheck] cutscene_recv netKey=0x01000001 s=0.300 silent=0 handle=3.1");
            foreach (var key in Keys)
            {
                lines.Add($"[NetCheck] cutscene_marker key={key} markerTime=0.000 elapsed=0.000 handle=3.1");
            }

            var summary = NetCheckCutsceneJudge.EvaluateLog(lines);
            Assert.IsFalse(summary.Pass);
            Assert.AreEqual("timeline_signal_not_loaded", summary.Reason);
            Assert.IsFalse(summary.SignalLoaded);
            StringAssert.Contains("signal=NOT_LOADED", NetCheckCutsceneJudge.FormatSummaryLine(summary));
        }

        [Test]
        public void EvaluateLog_KindsMissing_Fails_AndAllLoaded_Passes()
        {
            var lines = Config("observe", 0, -1);
            lines.Add("[NetCheck] cutscene_timeline ok=0 signal=1 duration=3.000 tracks=MarkerTrack:1 markers=NetCheckCutsceneMarker:5 clips=none missing=CutsceneCameraTrack");
            lines.Add("[NetCheck] cutscene_recv netKey=0x01000001 s=0.300 silent=0 handle=3.1");
            Assert.IsFalse(NetCheckCutsceneJudge.EvaluateLog(lines).Pass);

            // ok=1 でも missing に型名があれば FAIL(理由に型名を出す)
            lines[1] = "[NetCheck] cutscene_timeline ok=1 signal=1 duration=3.000 tracks=MarkerTrack:1 markers=NetCheckCutsceneMarker:5 clips=none missing=CutsceneCameraTrack";
            var summary = NetCheckCutsceneJudge.EvaluateLog(lines);
            Assert.IsFalse(summary.Pass);
            Assert.AreEqual("timeline_kinds_missing:CutsceneCameraTrack", summary.Reason);

            // missing=none なら読み込みは OK(発火の判定は別)
            lines[1] = "[NetCheck] cutscene_timeline ok=1 signal=1 duration=3.000 tracks=MarkerTrack:1 markers=NetCheckCutsceneMarker:5 clips=none missing=none";
            foreach (var key in Keys)
            {
                lines.Add($"[NetCheck] cutscene_marker key={key} markerTime=0.000 elapsed=0.000 handle=3.1");
                lines.Add($"[NetCheck] cutscene_signal key={key} handle=3.1");
            }

            Assert.IsTrue(NetCheckCutsceneJudge.EvaluateLog(lines).Pass);
        }

        [Test]
        public void EvaluateLog_Observer_PlayWithoutReceiveLog_Fails_UnmatchedPlay()
        {
            // GD-R-12: observe のプロセスに送信者はありえない。受信ログの無い再生は判定を飛ばさず FAIL
            var lines = Config("observe", 0, -1);
            AddMarkers(lines, "3.1", Keys);
            var summary = NetCheckCutsceneJudge.EvaluateLog(lines);
            Assert.IsFalse(summary.Pass);
            StringAssert.Contains("unmatched_play", summary.Reason);
        }

        [Test]
        public void EvaluateLog_Observer_SameNetKeyTwice_Fails()
        {
            var lines = Receiver(0.0, 0, Keys);
            lines.Add("[NetCheck] cutscene_recv netKey=0x01000001 s=0.000 silent=0 handle=3.2");
            AddMarkers(lines, "3.2", Keys);
            var summary = NetCheckCutsceneJudge.EvaluateLog(lines);
            Assert.IsFalse(summary.Pass);
            StringAssert.Contains("duplicate_netkey", summary.Reason);
        }

        [Test]
        public void ExpectedKinds_CoverEveryDDriveTrackType()
        {
            // 新しい D-Drive のトラック種別を足したら、NetCheck の Timeline と Expected* にも足す(M-6)。
            CollectionAssert.Contains(NetCheckCutsceneJudge.ExpectedTrackTypes, "CutsceneSignalTrack");
            Assert.AreEqual(11, NetCheckCutsceneJudge.ExpectedTrackTypes.Length);
            Assert.AreEqual(5, NetCheckCutsceneJudge.ExpectedMarkerTypes.Length);
            Assert.AreEqual(6, NetCheckCutsceneJudge.ExpectedClipTypes.Length);
        }

        [Test]
        public void EvaluateLog_SignalLoaded_MissingSignals_Fails()
        {
            var lines = Config("observe", 0, -1);
            lines.Add("[NetCheck] cutscene_timeline ok=1 signal=1 duration=3.000 tracks=CutsceneSignalTrack:1,MarkerTrack:1 markers=CutsceneSignalNotification:5,NetCheckCutsceneMarker:5");
            lines.Add("[NetCheck] cutscene_recv netKey=0x01000001 s=0.300 silent=0 handle=3.1");
            foreach (var key in Keys)
            {
                lines.Add($"[NetCheck] cutscene_marker key={key} markerTime=0.000 elapsed=0.000 handle=3.1");
            }

            var summary = NetCheckCutsceneJudge.EvaluateLog(lines);
            Assert.IsFalse(summary.Pass);
            StringAssert.Contains("signal_mismatch", summary.Reason);
        }

        [Test]
        public void EvaluateLog_SummaryStatistics_MinMaxMean()
        {
            var lines = Config("observe", 0, 3);
            var ss = new[] { 0.2, 0.4, 0.6 };
            for (var i = 0; i < ss.Length; i++)
            {
                var h = $"4.{i}";
                lines.Add($"[NetCheck] cutscene_recv netKey=0x0100000{i} s={Inv(ss[i])} silent={(i == 2 ? 2 : 0)} handle={h}");
                AddMarkers(lines, h, i == 2 ? new[] { "m1", "m4", "m6", "m15" } : Keys);
            }

            var summary = NetCheckCutsceneJudge.EvaluateLog(lines);
            Assert.IsTrue(summary.Pass, summary.Reason);
            Assert.AreEqual(0.2, summary.MinS, 1e-9);
            Assert.AreEqual(0.6, summary.MaxS, 1e-9);
            Assert.AreEqual(0.4, summary.MeanS, 1e-9);
            StringAssert.Contains("verdict=PASS", NetCheckCutsceneJudge.FormatSummaryLine(summary));
        }
    }
}
