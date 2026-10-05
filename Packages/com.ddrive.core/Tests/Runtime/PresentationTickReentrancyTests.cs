using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // 修正ラウンド 4(2026-10-04、docs/56 FY-R-03): PresentationManager.Tick / StopAll / CancelAllNetworked の走査が、
    // 購読者(OnCompleted / OnMarker)や WaitAsync の続きが走査中に他の Presentation を Cancel / Play しても、
    // 添字ずれ(二重進行・飛ばし)・範囲外にならない。CutsceneManager と同じ「写しを走査 + 有効性の確認」方式。
    public class PresentationTickReentrancyTests
    {
        private readonly List<Object> _created = new();
        private PresentationManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new PresentationManager(new AssetRegistry(new FakeAssetLoader()), new TimeService());
        }

        [TearDown]
        public void TearDown()
        {
            _manager.StopAll(StopReason.SceneUnload);
            foreach (var o in _created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _created.Clear();
        }

        private static PresentationTrack Marker(float time, string key)
            => new() { Trigger = TrackTrigger.AtTime, Time = time, Kind = TrackKind.Marker, SignalKey = key };

        private PresentationData Data(float duration, params PresentationTrack[] tracks)
        {
            var data = ScriptableObject.CreateInstance<PresentationData>();
            _created.Add(data);
            data.Tracks = tracks;
            data.Interruptible = true;
            data.TotalDuration = duration;
            return data;
        }

        // 末尾(最後に Play した)が完了し、その OnCompleted の購読者が先頭(古い)を Cancel する。
        // 以前は 1 周で _active が 2 減って次の添字が範囲外になった。
        [Test]
        public void OnCompletedSubscriber_CancelsAnOlderPresentation_DoesNotThrow_AndOthersAdvanceOnce()
        {
            var older = _manager.PlayData(Data(10f), new PlayContext());
            var middleKeys = new List<string>();
            var middle = _manager.PlayData(Data(10f, Marker(0.4f, "early"), Marker(0.7f, "late")), new PlayContext());
            _manager.OnMarker(middle).Subscribe(k => middleKeys.Add(k));
            var last = _manager.PlayData(Data(0.5f), new PlayContext());
            _manager.OnCompleted(last).Subscribe(_ => _manager.Cancel(older));

            Assert.DoesNotThrow(() => _manager.Tick(0.5f));

            Assert.IsFalse(_manager.IsPlaying(older), "購読者が止めた");
            Assert.IsFalse(_manager.IsPlaying(last), "完了した");
            Assert.IsTrue(_manager.IsPlaying(middle));
            CollectionAssert.AreEqual(new[] { "early" }, middleKeys, "中間のものは 0.5 秒だけ進む(同じ Tick で 2 回進まない)");
        }

        // WaitAsync の続き(await したゲームのコード)が、完了前に古い Presentation を Cancel する。
        [Test]
        public void WaitAsyncContinuation_CancelsAnOlderPresentation_DoesNotThrow()
        {
            var older = _manager.PlayData(Data(10f), new PlayContext());
            var last = _manager.PlayData(Data(0.5f), new PlayContext());
            var continued = false;
            _manager.WaitAsync(last, default).GetAwaiter().OnCompleted(() =>
            {
                continued = true;
                _manager.Cancel(older);
            });

            Assert.DoesNotThrow(() => _manager.Tick(0.5f));

            Assert.IsTrue(continued, "続きが走った(UniTaskCompletionSource は同期で続きを走らせる)");
            Assert.IsFalse(_manager.IsPlaying(older));
        }

        // マーカー(Signal 相当)の購読者が、まだ処理していない別の Presentation を Cancel する。
        // 以前は添字がずれて、処理中のものがもう一度(同じ Tick で 2 回ぶん)進んだ。
        [Test]
        public void MarkerSubscriber_CancelsAnotherNotYetTicked_DoesNotAdvanceTheCurrentOneTwice()
        {
            var first = _manager.PlayData(Data(10f), new PlayContext());   // _active[0]
            var second = _manager.PlayData(Data(10f), new PlayContext());  // _active[1](まだ Tick されていない側。Tick は後ろから)
            var thirdKeys = new List<string>();
            var third = _manager.PlayData(Data(10f, Marker(0.2f, "kill"), Marker(0.6f, "twice")), new PlayContext()); // _active[2]
            _manager.OnMarker(third).Subscribe(k =>
            {
                thirdKeys.Add(k);
                if (k == "kill")
                {
                    _manager.Cancel(second);
                }
            });

            Assert.DoesNotThrow(() => _manager.Tick(0.4f));

            CollectionAssert.AreEqual(new[] { "kill" }, thirdKeys, "処理中のものは 0.4 秒だけ進む(0.8 秒ぶん進めば twice も鳴る)");
            Assert.IsFalse(_manager.IsPlaying(second));
            Assert.IsTrue(_manager.IsPlaying(first));
            Assert.IsTrue(_manager.IsPlaying(third));
        }

        // マーカーの購読者が新しい Presentation を Play しても落ちない。新しいものはその Tick では進まず、次の Tick から進む。
        [Test]
        public void MarkerSubscriber_PlaysAnother_DoesNotBreakTick_AndNewOneStartsNextTick()
        {
            var starter = _manager.PlayData(Data(10f, Marker(0.1f, "go")), new PlayContext());
            var newKeys = new List<string>();
            var started = Handle<PresentationMarker>.Invalid;
            _manager.OnMarker(starter).Subscribe(_ =>
            {
                started = _manager.PlayData(Data(10f, Marker(0.1f, "n")), new PlayContext());
                _manager.OnMarker(started).Subscribe(k => newKeys.Add(k));
            });

            Assert.DoesNotThrow(() => _manager.Tick(0.5f));

            Assert.IsTrue(_manager.IsPlaying(started));
            CollectionAssert.IsEmpty(newKeys, "Tick 中に始めたものはその Tick では進まない");
            _manager.Tick(0.5f);
            CollectionAssert.AreEqual(new[] { "n" }, newKeys, "次の Tick から進む");
        }

        // 修正ラウンド 5(2026-10-05、docs/57 FZ-R-07): Marker の購読者が自分の Presentation を止めたら、同じ Tick で
        // 残りのトラック(後ろの Signal)は発火しない(止めた後に取り残さない。CutsceneManager のマーカー段と同じ保護)。
        [Test]
        public void MarkerSubscriber_CancelsItself_RemainingTracksOfThatPresentationDoNotFire()
        {
            var signals = new List<string>();
            var ctx = new PlayContext { OnSignal = k => signals.Add(k) };
            var self = _manager.PlayData(
                Data(10f, Marker(0.1f, "stop"), new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0.1f, Kind = TrackKind.Signal, SignalKey = "after" }),
                ctx);
            _manager.OnMarker(self).Subscribe(_ => _manager.Cancel(self));

            Assert.DoesNotThrow(() => _manager.Tick(0.5f));

            Assert.IsFalse(_manager.IsPlaying(self), "購読者が止めた");
            CollectionAssert.IsEmpty(signals, "止められた後の残りのトラックは発火しない");
        }

        // 修正ラウンド 6(2026-10-06、docs/58 GA-R-02): Signal(OnSignal トラック)の購読者が自分の Presentation を止めたら、
        // 同じ Signal の残りの OnSignal トラックは発火しない。止められなければ従来どおり全部発火する。
        [Test]
        public void Signal_WhenMarkerSubscriberCancelsItself_RemainingOnSignalTracksDoNotFire()
        {
            var signals = new List<string>();
            var ctx = new PlayContext { OnSignal = k => signals.Add(k) };
            var self = _manager.PlayData(
                Data(10f,
                    new PresentationTrack { Trigger = TrackTrigger.OnSignal, Kind = TrackKind.Marker, SignalKey = "go" },
                    new PresentationTrack { Trigger = TrackTrigger.OnSignal, Kind = TrackKind.Signal, SignalKey = "go" }),
                ctx);
            _manager.OnMarker(self).Subscribe(_ => _manager.Cancel(self));

            Assert.DoesNotThrow(() => _manager.Signal(self, "go"));

            Assert.IsFalse(_manager.IsPlaying(self), "購読者が止めた");
            CollectionAssert.IsEmpty(signals, "止められた後の残りの OnSignal トラックは発火しない");
        }

        [Test]
        public void Signal_WhenNotStopped_AllOnSignalTracksFire()
        {
            var signals = new List<string>();
            var ctx = new PlayContext { OnSignal = k => signals.Add(k) };
            var h = _manager.PlayData(
                Data(10f,
                    new PresentationTrack { Trigger = TrackTrigger.OnSignal, Kind = TrackKind.Marker, SignalKey = "go" },
                    new PresentationTrack { Trigger = TrackTrigger.OnSignal, Kind = TrackKind.Signal, SignalKey = "go" }),
                ctx);
            var keys = new List<string>();
            _manager.OnMarker(h).Subscribe(k => keys.Add(k));

            _manager.Signal(h, "go");

            CollectionAssert.AreEqual(new[] { "go" }, keys);
            CollectionAssert.AreEqual(new[] { "go" }, signals);
        }

        // 止められなければ、同じ時刻の後ろのトラックも従来どおり同じ Tick で発火する(保護が通常の発火を妨げない)。
        [Test]
        public void SameTimeTracks_WhenNotStopped_AllFireInOneTick()
        {
            var signals = new List<string>();
            var ctx = new PlayContext { OnSignal = k => signals.Add(k) };
            var h = _manager.PlayData(
                Data(10f, Marker(0.1f, "m"), new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0.1f, Kind = TrackKind.Signal, SignalKey = "after" }),
                ctx);
            var keys = new List<string>();
            _manager.OnMarker(h).Subscribe(k => keys.Add(k));

            _manager.Tick(0.5f);

            CollectionAssert.AreEqual(new[] { "m" }, keys);
            CollectionAssert.AreEqual(new[] { "after" }, signals);
        }

        // StopAll の最中に、中止通知の購読者が別の Presentation を止めても範囲外にならない。
        [Test]
        public void StopAll_WhenCancelSubscriberStopsAnother_DoesNotThrow()
        {
            var a = _manager.PlayData(Data(10f), new PlayContext());
            var b = _manager.PlayData(Data(10f), new PlayContext());
            var c = _manager.PlayData(Data(10f), new PlayContext());
            _manager.OnCancelled(c).Subscribe(_ => _manager.Cancel(a));

            Assert.DoesNotThrow(() => _manager.StopAll(StopReason.SceneUnload));

            Assert.IsFalse(_manager.IsPlaying(a));
            Assert.IsFalse(_manager.IsPlaying(b));
            Assert.IsFalse(_manager.IsPlaying(c));
        }

        // CancelAllNetworked(ネット由来が無ければ何もしない)も、ローカルの Presentation の走査で壊れない。
        [Test]
        public void CancelAllNetworked_WithOnlyLocalPresentations_LeavesThemAlone()
        {
            var a = _manager.PlayData(Data(10f), new PlayContext());
            var b = _manager.PlayData(Data(10f), new PlayContext());

            Assert.DoesNotThrow(() => _manager.CancelAllNetworked());

            Assert.IsTrue(_manager.IsPlaying(a));
            Assert.IsTrue(_manager.IsPlaying(b));
        }

        // 通常の完了の順序(後ろから)・1 Tick で進む量は変わらない。
        [Test]
        public void Tick_NormalCompletion_KeepsOrderAndAmount()
        {
            var order = new List<string>();
            var a = _manager.PlayData(Data(0.3f), new PlayContext());
            var b = _manager.PlayData(Data(0.3f), new PlayContext());
            var c = _manager.PlayData(Data(1.0f, Marker(0.4f, "c")), new PlayContext());
            _manager.OnCompleted(a).Subscribe(_ => order.Add("a"));
            _manager.OnCompleted(b).Subscribe(_ => order.Add("b"));
            _manager.OnMarker(c).Subscribe(k => order.Add(k));

            _manager.Tick(0.5f);

            CollectionAssert.AreEqual(new[] { "c", "b", "a" }, order, "後ろ(新しい)から進む順は従来どおり");
            Assert.IsTrue(_manager.IsPlaying(c));
        }
    }
}
