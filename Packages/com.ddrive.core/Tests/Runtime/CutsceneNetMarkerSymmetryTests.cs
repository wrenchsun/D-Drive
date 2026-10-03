using System.Collections.Generic;
using DDrive.Foundation.Event;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Net;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Net;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Tests.Runtime
{
    // 修正ラウンド 3(2026-10-03、docs/55 FX-R-01 / FX-R-03)・修正ラウンド 4(2026-10-04、docs/56 FY-R-02 / FY-R-06)。
    //   FX-R-01 / FY-R-02: ネット再生の受信側は、開始位置から遡って猶予 RemoteMarkerGraceSec(0.5 秒)以内のマーカー
    //            (開始位置 - マーカーの時刻 <= 0.5。ちょうどは含む)を最初の Tick で 1 回ずつ発火する。それより古いマーカーは無音
    //            (PresentationManager の remoteOneShotGraceSec と同じ規則・同じ値)。Late Join も同じ規則。Seek・Skip は無音。
    //   FX-R-03: マーカーの購読者(ゲームのコード)が Tick 中に自分・他のカットシーンを止めたり、新しく Play したりしても、
    //            Tick の走査が壊れない(同じ Tick で二重に進む・範囲外・止めた後の残りの発火がない)。
    public class CutsceneNetMarkerSymmetryTests
    {
        private const ulong HostId = 0UL;
        private const ulong ClientId = 1UL;
        private const ulong CutId = 900101UL;

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _created.Clear();
        }

        // 0.0 / 0.1 / 1.0 秒に Signal マーカー(キー z0 / z1 / z2)を置いた Cosmetic の CutsceneData。
        private CutsceneData CreateSignalData(NetMode net, bool predictLocal, params (double time, string key)[] markers)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _created.Add(timeline);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 10.0;
            var track = timeline.CreateTrack<CutsceneSignalTrack>(null, "Signal");
            foreach (var (time, key) in markers)
            {
                track.CreateMarker<CutsceneSignalNotification>(time).Key = key;
            }

            var data = ScriptableObject.CreateInstance<CutsceneData>();
            _created.Add(data);
            data.Timeline = timeline;
            data.PredictLocal = predictLocal;
            var flags = data.Flags;
            flags.Net = net;
            data.Flags = flags;
            return data;
        }

        private sealed class Peer
        {
            public FakeAssetLoader Loader;
            public AssetRegistry Registry;
            public DelayedNetBridge Bridge;
            public CutsceneManager Manager;

            public void Register(ulong id, CutsceneData data)
            {
                data.Id = id;
                var address = "cutscene/" + id;
                Loader.Assets[address] = data;
                var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
                catalog.SetEntries(new List<CatalogEntry> { new() { Id = id, Type = AssetType.Cutscene, Address = address } });
                Registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
                Registry.ResolveAsync<CutsceneData>(id).GetAwaiter().GetResult();
                Object.DestroyImmediate(catalog);
            }
        }

        private static Peer CreatePeer(DelayedNetworkRelay relay, ulong clientId, bool isServer)
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new DelayedNetBridge(relay, clientId, isServer);
            var manager = new CutsceneManager(registry, netBridge: bridge);
            return new Peer { Loader = loader, Registry = registry, Bridge = bridge, Manager = manager };
        }

        private static List<string> Collect(CutsceneManager manager, Handle<CutsceneMarker> handle)
        {
            var keys = new List<string>();
            manager.OnMarker(handle).Subscribe(k => keys.Add(k));
            return keys;
        }

        private (Peer host, Peer client, DelayedNetworkRelay relay) Setup(double latency)
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = latency };
            var host = CreatePeer(relay, HostId, isServer: true);
            var client = CreatePeer(relay, ClientId, isServer: false);
            host.Register(CutId, CreateSignalData(NetMode.Cosmetic, true, (0.0, "z0"), (0.1, "z1"), (1.0, "z2")));
            client.Register(CutId, CreateSignalData(NetMode.Cosmetic, true, (0.0, "z0"), (0.1, "z1"), (1.0, "z2")));
            return (host, client, relay);
        }

        // 表のテスト用: 0 / 0.1 / 0.4 / 0.6 秒に Signal(キー m0 / m1 / m4 / m6)。受信側の Client 1 台だけ。
        private (Peer host, Peer client, DelayedNetworkRelay relay) SetupWithMarkers(double latency)
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = latency };
            var host = CreatePeer(relay, HostId, isServer: true);
            var client = CreatePeer(relay, ClientId, isServer: false);
            client.Register(CutId, CreateSignalData(NetMode.Cosmetic, true, (0.0, "m0"), (0.1, "m1"), (0.4, "m4"), (0.6, "m6")));
            return (host, client, relay);
        }

        private CutsceneData CreateEventData(NetMode net, params (double time, string key)[] markers)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _created.Add(timeline);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 10.0;
            var track = timeline.CreateTrack<CutsceneEventTrack>(null, "Event");
            foreach (var (time, key) in markers)
            {
                track.CreateMarker<CutsceneEventNotification>(time).Event = new AssetEvent { Action = EventAction.SendMessage, CustomKey = key };
            }

            var data = ScriptableObject.CreateInstance<CutsceneData>();
            _created.Add(data);
            data.Timeline = timeline;
            data.PredictLocal = true;
            var flags = data.Flags;
            flags.Net = net;
            data.Flags = flags;
            return data;
        }

        // 遅延 0ms: 送信側(予測再生)・受信側とも 0 秒のマーカーが 1 回ずつ。以降の Tick で二重に鳴らない。ネットへ余計に流れない。
        [Test]
        public void Delay0ms_SenderAndReceiver_FireTheSameMarkersOnce()
        {
            var (host, client, relay) = Setup(0d);
            var hostHandle = host.Manager.PlayData(host.Registry.ResolveOrPlaceholder<CutsceneData>(CutId), new PlayContext());
            var hostKeys = Collect(host.Manager, hostHandle);
            relay.Advance(0d);

            var clientActive = client.Manager.DebugActiveHandles();
            Assert.AreEqual(1, clientActive.Count);
            var clientKeys = Collect(client.Manager, clientActive[0]);
            var sent = relay.EnqueuedMessageCount;

            host.Manager.Tick(0.05f);
            client.Manager.Tick(0.05f);
            CollectionAssert.AreEqual(new[] { "z0" }, hostKeys);
            CollectionAssert.AreEqual(hostKeys, clientKeys, "送信側と受信側で同じ(0 秒のマーカーが両方で 1 回)");

            host.Manager.Tick(0.1f);
            client.Manager.Tick(0.1f);
            CollectionAssert.AreEqual(new[] { "z0", "z1" }, hostKeys);
            CollectionAssert.AreEqual(hostKeys, clientKeys, "0.1 秒のマーカーも 1 回ずつ。二重発火しない");

            Assert.AreEqual(sent, relay.EnqueuedMessageCount, "マーカーの発火はローカル処理でネットワークへ何も流さない");
        }

        // 遅延 200ms: 受信側は開始位置 0.2 秒。開始までに過ぎた 0 秒・0.1 秒のマーカーも最初の Tick で 1 回ずつ発火し、送信側と揃う。
        [Test]
        public void Delay200ms_ReceiverFiresMarkersPassedDuringTheDelay_LikeTheSender()
        {
            var (host, client, relay) = Setup(0.2d);
            var hostHandle = host.Manager.PlayData(host.Registry.ResolveOrPlaceholder<CutsceneData>(CutId), new PlayContext());
            var hostKeys = Collect(host.Manager, hostHandle);

            host.Manager.Tick(0.2f); // 送信側は届くまでの 0.2 秒ぶん先に進む
            relay.Advance(0.2d);     // 受信側へ届く(開始位置 = 0.2 秒)
            var clientActive = client.Manager.DebugActiveHandles();
            Assert.AreEqual(1, clientActive.Count);
            var clientKeys = Collect(client.Manager, clientActive[0]);
            Assert.AreEqual(0, clientKeys.Count, "Play の呼び出し自体では発火しない(最初の Tick で発火)");

            client.Manager.Tick(0.016f);
            CollectionAssert.AreEqual(new[] { "z0", "z1" }, hostKeys, "送信側: 0 秒と 0.1 秒");
            CollectionAssert.AreEqual(new[] { "z0", "z1" }, clientKeys, "受信側: 開始までに過ぎた 0 秒と 0.1 秒も 1 回ずつ(送信側と同じ回数)");

            client.Manager.Tick(0.016f);
            host.Manager.Tick(0.016f);
            Assert.AreEqual(2, clientKeys.Count, "二重発火しない");
            Assert.AreEqual(2, hostKeys.Count);
        }

        // 開始位置が猶予(0.5 秒)以内なら新規開始: 開始までに過ぎたマーカーも発火する(境界の内側)。
        [Test]
        public void ReceiverStartPosition_WithinGrace_Fires()
        {
            var (_, client, relay) = Setup(0d);
            relay.Advance(0.4d);
            client.Bridge.Receive(HostId, new CutscenePlayMsg { CutId = CutId, HandleNetKey = 0x10000001u, StartNetTime = 0.0 });
            var active = client.Manager.DebugActiveHandles();
            Assert.AreEqual(1, active.Count);
            var keys = Collect(client.Manager, active[0]);

            client.Manager.Tick(0.016f);

            CollectionAssert.AreEqual(new[] { "z0", "z1" }, keys, "開始位置 0.4 秒(猶予内)は新規開始: 過ぎたマーカーも発火");
        }

        // 開始位置が猶予(0.5 秒)を超えても、開始位置から遡って 0.5 秒以内のマーカーは発火する(滑る窓。FY-R-02)。
        [Test]
        public void ReceiverStartPosition_BeyondGrace_FiresOnlyMarkersWithinGrace()
        {
            var (_, client, relay) = Setup(0d);
            relay.Advance(0.75d);
            client.Bridge.Receive(HostId, new CutscenePlayMsg { CutId = CutId, HandleNetKey = 0x10000002u, StartNetTime = 0.0 });
            var active = client.Manager.DebugActiveHandles();
            Assert.AreEqual(1, active.Count);
            var keys = Collect(client.Manager, active[0]);

            client.Manager.Tick(0.016f);
            Assert.AreEqual(0, keys.Count, "開始位置 0.75 秒: 0 秒(遅れ 0.75)・0.1 秒(遅れ 0.65)は猶予超えで無音");

            client.Manager.Tick(0.5f);
            CollectionAssert.AreEqual(new[] { "z2" }, keys, "以降に跨いだ 1.0 秒だけ発火");
        }

        // 開始位置 × マーカー時刻の表(Signal)。開始位置 − マーカーの時刻 ≤ 0.5 のものだけ最初の Tick で発火する。
        // 開始位置 0.5 ちょうどの 0 秒は含む(Presentation の「遅れ > 猶予 ならスキップ」と同じ)。
        // 0.6 秒のマーカーは開始位置 0.3 / 0.5 では未到達(最初の Tick の 0.016 秒では跨がない)。
        [TestCase(0.3, "m0,m1")]
        [TestCase(0.5, "m0,m1,m4")]
        [TestCase(0.7, "m4,m6")]
        [TestCase(2.0, "")]
        public void ReceiverStartPosition_SignalTable_FiresMarkersWithinGraceOfTheStartPosition(double startPosition, string expectedCsv)
        {
            var (_, client, relay) = SetupWithMarkers(0d);
            relay.Advance(startPosition);
            client.Bridge.Receive(HostId, new CutscenePlayMsg { CutId = CutId, HandleNetKey = 0x10000010u, StartNetTime = 0.0 });
            var active = client.Manager.DebugActiveHandles();
            Assert.AreEqual(1, active.Count);
            var keys = Collect(client.Manager, active[0]);

            client.Manager.Tick(0.016f);

            var expected = expectedCsv.Length == 0 ? new string[0] : expectedCsv.Split(',');
            CollectionAssert.AreEqual(expected, keys);
            client.Manager.Tick(0.016f);
            Assert.AreEqual(expected.Length, keys.Count, "二重発火しない");
        }

        // Event マーカーも同じ規則(受信側 Event は Events.OnEventFired へ)。
        [TestCase(0.5, "m0,m1,m4")]
        [TestCase(0.7, "m4,m6")]
        public void ReceiverStartPosition_EventTable_FiresMarkersWithinGraceOfTheStartPosition(double startPosition, string expectedCsv)
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0d };
            var client = CreatePeer(relay, ClientId, isServer: false);
            client.Register(CutId, CreateEventData(NetMode.Cosmetic, (0.0, "m0"), (0.1, "m1"), (0.4, "m4"), (0.6, "m6")));
            var keys = new List<string>();
            client.Manager.Events.OnEventFired += (_, evt) => keys.Add(evt.CustomKey);
            relay.Advance(startPosition);

            client.Bridge.Receive(HostId, new CutscenePlayMsg { CutId = CutId, HandleNetKey = 0x10000011u, StartNetTime = 0.0 });
            client.Manager.Tick(0.016f);

            CollectionAssert.AreEqual(expectedCsv.Split(','), keys);
        }

        // Late Join(Host の台帳からの再送。元の StartNetTime のまま): 参加時点から遡って 0.5 秒以内のマーカーだけ発火し、それより前は無音。
        [Test]
        public void LateJoin_ResentByHost_FiresOnlyMarkersWithinGraceOfTheJoinPosition()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0d };
            var host = CreatePeer(relay, HostId, isServer: true);
            host.Register(CutId, CreateSignalData(NetMode.Cosmetic, true, (0.0, "z0"), (0.1, "z1"), (1.0, "z2")));
            var hostHandle = host.Manager.PlayData(host.Registry.ResolveOrPlaceholder<CutsceneData>(CutId), new PlayContext());
            var hostKeys = Collect(host.Manager, hostHandle);
            host.Manager.Tick(1.25f);
            relay.Advance(1.25d);
            CollectionAssert.AreEqual(new[] { "z0", "z1", "z2" }, hostKeys);

            var late = CreatePeer(relay, 2UL, isServer: false);
            late.Register(CutId, CreateSignalData(NetMode.Cosmetic, true, (0.0, "z0"), (0.1, "z1"), (1.0, "z2")));
            host.Bridge.RaiseClientConnected(2UL);
            relay.Advance(0d);

            var active = late.Manager.DebugActiveHandles();
            Assert.AreEqual(1, active.Count, "途中参加者に復元される");
            var keys = Collect(late.Manager, active[0]);
            late.Manager.Tick(0.016f);
            CollectionAssert.AreEqual(new[] { "z2" }, keys, "参加位置 1.25 秒: 遡って 0.5 秒以内の 1.0 秒だけ発火。0 / 0.1 秒は無音");
        }

        // 0.5 秒より後の Late Join(直近 0.5 秒にマーカーが無い)は、開始直後のマーカーを鳴らさない。
        [Test]
        public void LateJoin_AfterGrace_NoRecentMarker_IsSilent()
        {
            var (_, client, relay) = Setup(0d);
            relay.Advance(2.0d);
            client.Bridge.Receive(HostId, new CutscenePlayMsg { CutId = CutId, HandleNetKey = 0x10000012u, StartNetTime = 0.0 });
            var active = client.Manager.DebugActiveHandles();
            var keys = Collect(client.Manager, active[0]);
            client.Manager.Tick(0.016f);
            Assert.AreEqual(0, keys.Count);
        }

        // Skip(CutsceneSeekMsg の受信)で跨いだ分は、開始位置 > 0 の新規開始(追いつき発火の対象)でも無音。
        [Test]
        public void Receiver_SkipMessageBeforeFirstTick_IsSilent()
        {
            var (_, client, relay) = SetupWithMarkers(0d);
            relay.Advance(0.3d);
            const uint Key = 0x10000013u;
            client.Bridge.Receive(HostId, new CutscenePlayMsg { CutId = CutId, HandleNetKey = Key, StartNetTime = 0.0 });
            var active = client.Manager.DebugActiveHandles();
            var keys = Collect(client.Manager, active[0]);

            client.Bridge.Receive(HostId, new CutsceneSeekMsg { HandleNetKey = Key, ToTime = 2.0 });
            client.Manager.Tick(0.016f);

            Assert.AreEqual(0, keys.Count, "開始位置 0.3 秒(追いつき発火の対象)でも、最初の Tick より前の Skip で跨いだ分は無音");
        }

        // デバッグ用 Seek も同じ(追いつき発火の対象の開始位置 0.3 秒から)。
        [Test]
        public void Receiver_SeekBeforeFirstTick_IsSilent()
        {
            var (_, client, relay) = SetupWithMarkers(0d);
            relay.Advance(0.3d);
            client.Bridge.Receive(HostId, new CutscenePlayMsg { CutId = CutId, HandleNetKey = 0x10000014u, StartNetTime = 0.0 });
            var active = client.Manager.DebugActiveHandles();
            var keys = Collect(client.Manager, active[0]);

            client.Manager.Seek(active[0], 2.0f);
            client.Manager.Tick(0.016f);

            Assert.AreEqual(0, keys.Count);
            client.Manager.Cancel(active[0]);
        }

        // Client が送信者(Client → Host → 別の Client の 2 区間。実 NGO と同じ経路。FY-R-02)。
        // 片道 0.35 秒: Host へは 0.35 秒・別の Client(B)へは 0.7 秒の開始位置で届く。
        // 送信者 A(予測再生)= 0 秒のマーカーを最初の Tick で、Host = 開始位置 0.35 秒なので [0, 0.35] を全部、
        // B = 開始位置 0.7 秒なので 0 / 0.1 秒は古すぎて無音、0.4 / 0.6 秒は発火(全部が無音にはならない)。
        [Test]
        public void ClientSender_ThroughHost_ToAnotherClient_FiresMarkersWithinGrace()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.35d, RelayThroughHostId = HostId };
            var host = CreatePeer(relay, HostId, isServer: true);
            var a = CreatePeer(relay, ClientId, isServer: false);
            var b = CreatePeer(relay, 2UL, isServer: false);
            foreach (var peer in new[] { host, a, b })
            {
                peer.Register(CutId, CreateSignalData(NetMode.Cosmetic, true, (0.0, "m0"), (0.1, "m1"), (0.4, "m4"), (0.6, "m6")));
            }

            var aHandle = a.Manager.PlayData(a.Registry.ResolveOrPlaceholder<CutsceneData>(CutId), new PlayContext());
            var aKeys = Collect(a.Manager, aHandle);

            a.Manager.Tick(0.016f);
            CollectionAssert.AreEqual(new[] { "m0" }, aKeys, "送信者(予測再生)は 0 秒のマーカーを 1 回");

            relay.Advance(0.35d); // Host へ届く(開始位置 0.35 秒)
            var hostActive = host.Manager.DebugActiveHandles();
            Assert.AreEqual(1, hostActive.Count);
            var hostKeys = Collect(host.Manager, hostActive[0]);
            Assert.AreEqual(0, b.Manager.DebugActiveHandles().Count, "B にはまだ届かない(2 区間)");

            relay.Advance(0.35d); // B へ届く(開始位置 0.7 秒)・A 自身のメッセージも戻る
            var bActive = b.Manager.DebugActiveHandles();
            Assert.AreEqual(1, bActive.Count);
            var bKeys = Collect(b.Manager, bActive[0]);
            Assert.AreEqual(1, a.Manager.DebugActiveHandles().Count, "予測再生中の送信者に自分のメッセージが戻っても二重に再生しない");

            host.Manager.Tick(0.016f);
            b.Manager.Tick(0.016f);
            CollectionAssert.AreEqual(new[] { "m0", "m1" }, hostKeys, "Host: 開始位置 0.35 秒 = [0, 0.35] を全部");
            CollectionAssert.AreEqual(new[] { "m4", "m6" }, bKeys, "B: 開始位置 0.7 秒 = 遡って 0.5 秒以内の 0.4 / 0.6 秒。0 / 0.1 秒は無音");
        }

        // 予測再生した送信者が、自分のメッセージが戻る前にローカルで止めた(StopAll = シーンのアンロード等)とき、
        // 戻ったメッセージで再生し直して冒頭のマーカーが二重に鳴らない(FY-R-02)。
        [Test]
        public void PredictedSender_StoppedLocallyBeforeMessageReturns_DoesNotReplay()
        {
            var (host, client, relay) = Setup(0.2d);
            var hostHandle = host.Manager.PlayData(host.Registry.ResolveOrPlaceholder<CutsceneData>(CutId), new PlayContext());
            var hostKeys = Collect(host.Manager, hostHandle);
            host.Manager.Tick(0.016f);
            CollectionAssert.AreEqual(new[] { "z0" }, hostKeys);

            host.Manager.StopAll(StopReason.SceneUnload);
            relay.Advance(0.2d); // 自分のメッセージが戻る

            Assert.AreEqual(0, host.Manager.DebugActiveHandles().Count, "戻ったメッセージで再生し直さない");
            Assert.AreEqual(1, client.Manager.DebugActiveHandles().Count, "他の端末には通常どおり届く");
        }

        // 予測再生なしの送信者は、戻った自分のメッセージで再生する(従来どおり)。
        [Test]
        public void NonPredictedSender_PlaysFromItsOwnReturnedMessage()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2d };
            var host = CreatePeer(relay, HostId, isServer: true);
            host.Register(CutId, CreateSignalData(NetMode.Cosmetic, false, (0.0, "z0")));
            host.Manager.PlayData(host.Registry.ResolveOrPlaceholder<CutsceneData>(CutId), new PlayContext());
            Assert.AreEqual(0, host.Manager.DebugActiveHandles().Count);

            relay.Advance(0.2d);

            Assert.AreEqual(1, host.Manager.DebugActiveHandles().Count);
        }

        // ── FX-R-03: 購読者が Tick 中に止める・始める ──

        private CutsceneData CreateLocalSignalData(params (double time, string key)[] markers)
            => CreateSignalData(NetMode.Local, false, markers);

        private static CutsceneManager NewLocalManager() => new(new AssetRegistry(new FakeAssetLoader()));

        // 購読者が自分のカットシーンを止めたら、同じ Tick で跨いだ残りの Signal は届かない(破棄済みの Subject へ流れない)。
        [Test]
        public void SignalSubscriber_CancelsItself_RemainingMarkersOfTheSameTickAreNotDelivered()
        {
            var manager = NewLocalManager();
            var handle = manager.PlayData(CreateLocalSignalData((0.1, "a"), (0.2, "b")), new PlayContext());
            var keys = new List<string>();
            manager.OnMarker(handle).Subscribe(k =>
            {
                keys.Add(k);
                manager.Cancel(handle);
            });

            manager.Tick(0.5f);

            CollectionAssert.AreEqual(new[] { "a" }, keys, "止めた後の b は届かない");
            Assert.IsFalse(manager.IsPlaying(handle));
        }

        // 購読者が別のカットシーンを止めても、処理中のカットシーンは同じ Tick に 2 回進まない(添字ずれの回帰)。
        [Test]
        public void SignalSubscriber_CancelsAnotherCutscene_DoesNotAdvanceTheCurrentOneTwice()
        {
            var manager = NewLocalManager();
            var first = manager.PlayData(CreateLocalSignalData((5.0, "never")), new PlayContext());   // _active[0]
            var second = manager.PlayData(CreateLocalSignalData((0.1, "kill")), new PlayContext());   // _active[1]
            var killKeys = new List<string>();
            manager.OnMarker(second).Subscribe(k =>
            {
                killKeys.Add(k);
                manager.Cancel(first);
            });

            manager.Tick(0.5f);

            Assert.IsFalse(manager.IsPlaying(first));
            Assert.IsTrue(manager.IsPlaying(second));
            CollectionAssert.AreEqual(new[] { "kill" }, killKeys, "マーカーも 1 回だけ");
            Assert.AreEqual(0.5f / 10f, manager.GetNormalizedTime(second), 1e-4f, "同じ Tick で 2 回(1.0 秒ぶん)進まず、0.5 秒だけ進む");
            manager.Cancel(second);
        }

        // 購読者が Tick 中に新しいカットシーンを Play しても落ちない。新しいものはその Tick では進まず、次の Tick から進む。
        [Test]
        public void SignalSubscriber_PlaysAnotherCutscene_DoesNotBreakTick()
        {
            var manager = NewLocalManager();
            var starter = manager.PlayData(CreateLocalSignalData((0.1, "go")), new PlayContext());
            var started = Handle<CutsceneMarker>.Invalid;
            manager.OnMarker(starter).Subscribe(_ => started = manager.PlayData(CreateLocalSignalData((9.0, "x")), new PlayContext()));

            manager.Tick(0.5f);

            Assert.IsTrue(manager.IsPlaying(started));
            Assert.AreEqual(0f, manager.GetNormalizedTime(started), 1e-6f, "Tick 中に始めたものはその Tick では進まない");
            manager.Tick(0.5f);
            Assert.AreEqual(0.05f, manager.GetNormalizedTime(started), 1e-4f, "次の Tick から進む");
            manager.StopAll(StopReason.SceneUnload);
        }

        // StopAll の最中に購読者(完了 / 中止通知)が別のカットシーンを止めても、範囲外にならない。
        [Test]
        public void StopAll_WhenCancelSubscriberStopsAnother_DoesNotThrow()
        {
            var manager = NewLocalManager();
            var a = manager.PlayData(CreateLocalSignalData(), new PlayContext());
            var b = manager.PlayData(CreateLocalSignalData(), new PlayContext());
            var c = manager.PlayData(CreateLocalSignalData(), new PlayContext());
            manager.OnCancelled(c).Subscribe(_ => manager.Cancel(a));

            Assert.DoesNotThrow(() => manager.StopAll(StopReason.SceneUnload));

            Assert.IsFalse(manager.IsPlaying(a));
            Assert.IsFalse(manager.IsPlaying(b));
            Assert.IsFalse(manager.IsPlaying(c));
        }
    }
}
