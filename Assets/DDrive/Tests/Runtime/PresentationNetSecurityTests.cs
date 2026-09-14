using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Net;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    // [11_tasks.md] 6-0(F, P1-1/P1-2/P2-3/P2-4/P2-5 レビュー対応) — HandleNetKey の発行者検証・
    // Client 行為者の Play/Signal/Cancel・偽造メッセージの破棄・OnClientConnected の自己接続除外を検証する。
    // 5-8/5-9 の PresentationNetTests.cs と同じ DelayedNetworkRelay/DelayedNetBridge/NetPeer を再利用する
    // (同アセンブリ内 internal のため共有できる)。
    public class PresentationNetSecurityTests
    {
        private const ulong HostId = 0UL;
        private const ulong ClientId = 1UL;
        private const ulong OtherClientId = 2UL;

        private ulong _nextId = 700001;
        private readonly List<NetPeer> _peers = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var peer in _peers)
            {
                peer.Cleanup();
            }

            _peers.Clear();
        }

        private NetPeer NewPeer(DelayedNetworkRelay relay, ulong clientId, bool isServer)
        {
            var peer = NetPeer.Create(relay, clientId, isServer);
            _peers.Add(peer);
            return peer;
        }

        private static PresentationData CreateData(params PresentationTrack[] tracks)
        {
            var data = ScriptableObject.CreateInstance<PresentationData>();
            data.Tracks = tracks;
            data.Interruptible = true;
            data.Flags.Net = NetMode.Cosmetic;
            return data;
        }

        private static PresentationData CloneForClient(PresentationData source)
        {
            var clone = ScriptableObject.CreateInstance<PresentationData>();
            clone.Tracks = source.Tracks;
            clone.TotalDuration = source.TotalDuration;
            clone.Interruptible = source.Interruptible;
            clone.PredictLocal = source.PredictLocal;
            clone.Flags = source.Flags;
            return clone;
        }

        // ── P2-3: Host が PredictLocal で行為者になったとき、確定エコーを待たずに台帳へ即時登録される ──

        [Test]
        public void HostActor_PredictLocal_RegistersLedgerImmediately_BeforeEchoArrives()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2 };
            var host = NewPeer(relay, HostId, isServer: true);
            host.AttachManager();

            var presId = _nextId++;
            var t0 = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "go" };
            var hostData = CreateData(t0);
            hostData.TotalDuration = 5f;
            hostData.PredictLocal = true;
            host.RegisterPresentation(presId, hostData);

            var handle = host.Manager.PlayData(host.Resolve(presId), new PlayContext());
            Assert.AreNotEqual(Handle<PresentationMarker>.Invalid, handle);

            // まだ確定 Broadcast(自分のエコー)は届いていない(relay.Advance していない)。それでも新規参加者に
            // 送るべき台帳には既に載っているはずなので、この時点で接続してきたクライアントにも復元される。
            var late = NewPeer(relay, OtherClientId, isServer: false);
            late.AttachManager();
            late.RegisterPresentation(presId, CloneForClient(hostData));

            host.Bridge.RaiseClientConnected(OtherClientId);
            relay.Advance(0.25);

            Assert.AreEqual(1, late.Manager.DebugActiveHandles().Count, "エコー到着前に接続したクライアントにも台帳から復元される(P2-3)");
        }

        // ── Client 行為者: PredictLocal は即ローカル再生し、Host の台帳にも登録される ──

        [Test]
        public void ClientActor_PredictLocal_PlaysImmediately_AndHostRegistersLedger()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2 };
            var host = NewPeer(relay, HostId, isServer: true);
            var client = NewPeer(relay, ClientId, isServer: false);
            host.AttachManager();
            client.AttachManager();

            var presId = _nextId++;
            var t0 = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "go" };
            var clientData = CreateData(t0);
            clientData.TotalDuration = 1f;
            clientData.PredictLocal = true;
            client.RegisterPresentation(presId, clientData);
            host.RegisterPresentation(presId, CloneForClient(clientData));

            var handle = client.Manager.PlayData(client.Resolve(presId), new PlayContext());
            Assert.AreNotEqual(Handle<PresentationMarker>.Invalid, handle, "Client 行為者でも PredictLocal は即座にローカル再生する");
            Assert.IsTrue(client.Manager.IsPlaying(handle));

            relay.Advance(0.25);

            Assert.AreEqual(1, host.Manager.DebugActiveHandles().Count, "Client 発の Play も Host 側で正しい発行者として受理され、台帳/Instance が作られる");
        }

        // ── Client 行為者: Signal は Host 経由で中継され、両ピアに反映される ──

        [Test]
        public void ClientActor_Signal_RelaysThroughHost_BothPeers_ApplyHitStop()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2 };
            var host = NewPeer(relay, HostId, isServer: true);
            var client = NewPeer(relay, ClientId, isServer: false);
            host.AttachManager();
            client.AttachManager();

            var onHit = new PresentationTrack { Trigger = TrackTrigger.OnSignal, SignalKey = "hit", Kind = TrackKind.HitStop, Params = new[] { ParamValue.Of(0.1f) } };
            var presId = _nextId++;
            var clientData = CreateData(onHit);
            clientData.TotalDuration = 5f;
            client.RegisterPresentation(presId, clientData);
            host.RegisterPresentation(presId, CloneForClient(clientData));

            // PredictLocal=false: 自分の Broadcast が返ってくるまで誰も再生しない(5-8 の既定挙動)。
            client.Manager.PlayData(client.Resolve(presId), new PlayContext());
            relay.Advance(0.25);

            var clientHandle = client.Manager.DebugActiveHandles()[0];
            client.Manager.Signal(clientHandle, "hit");
            Assert.AreEqual(1f, client.Time.TimeScale, "Signal 直後はまだ Host 経由の中継が届いていない");

            relay.Advance(0.25);

            Assert.AreEqual(0f, host.Time.TimeScale, "Host も Client 発の Signal で HitStop する(Host 権威の中継が機能している)");
            Assert.AreEqual(0f, client.Time.TimeScale, "Client 自身も自分の Broadcast の中継受信で HitStop する");
        }

        // ── Client 行為者: Cancel は Host 経由で中継され、両ピアが停止する ──

        [Test]
        public void ClientActor_Cancel_RelaysThroughHost_StopsBothPeers()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2 };
            var host = NewPeer(relay, HostId, isServer: true);
            var client = NewPeer(relay, ClientId, isServer: false);
            host.AttachManager();
            client.AttachManager();

            var t0 = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "go" };
            var presId = _nextId++;
            var clientData = CreateData(t0);
            clientData.TotalDuration = 5f;
            clientData.Interruptible = true;
            client.RegisterPresentation(presId, clientData);
            host.RegisterPresentation(presId, CloneForClient(clientData));

            client.Manager.PlayData(client.Resolve(presId), new PlayContext());
            relay.Advance(0.25);

            var clientHandle = client.Manager.DebugActiveHandles()[0];
            var hostHandle = host.Manager.DebugActiveHandles()[0];

            client.Manager.Cancel(clientHandle);
            relay.Advance(0.25);

            Assert.IsFalse(client.Manager.IsPlaying(clientHandle), "Client 行為者自身の Cancel も中継経由で自分に反映される");
            Assert.IsFalse(host.Manager.IsPlaying(hostHandle), "Host 側の Instance も Client 発の Cancel で止まる");
        }

        // ── P1-1: 偽造 Signal(発行者と異なる ClientId から)は破棄される ──

        [Test]
        public void ForgedSignal_FromNonIssuerClient_IsDiscarded()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2 };
            var host = NewPeer(relay, HostId, isServer: true);
            var client = NewPeer(relay, ClientId, isServer: false);
            host.AttachManager();
            client.AttachManager();

            var onHit = new PresentationTrack { Trigger = TrackTrigger.OnSignal, SignalKey = "hit", Kind = TrackKind.HitStop, Params = new[] { ParamValue.Of(0.1f) } };
            var presId = _nextId++;
            var hostData = CreateData(onHit); // Host が行為者(HandleNetKey の発行者は Host=0)
            hostData.TotalDuration = 5f;
            host.RegisterPresentation(presId, hostData);
            client.RegisterPresentation(presId, CloneForClient(hostData));

            uint capturedKey = 0;
            using var spy = host.Bridge.Subscribe<PresentationPlayMsg>((sender, msg) => capturedKey = msg.HandleNetKey);

            host.Manager.PlayData(host.Resolve(presId), new PlayContext());
            relay.Advance(0.25);
            Assert.AreNotEqual(0u, capturedKey, "実際に使われた HandleNetKey を捕捉できている");

            // 改造 Client(ClientId=1)が Host 発の HandleNetKey を騙って Signal を送ったふりをする
            // (実際の NGO では RequestBroadcastRpc が真の送信元 ClientId=1 を付けて全員へ中継する。
            // DelayedNetBridge.Broadcast も常に呼び出し元の本当の ClientId を送信元にするため、
            // client.Bridge から直接 Broadcast するだけで同じ状況を再現できる)。
            LogAssert.ignoreFailingMessages = true;
            client.Bridge.Broadcast(new PresentationSignalMsg { HandleNetKey = capturedKey, SignalKeyHash = HashSignalKeyForTest("hit") }, NetChannel.ReliableOrdered);
            relay.Advance(0.25);
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(1f, host.Time.TimeScale, "Host 発の演出に対する非発行者(Client)からの Signal は破棄され HitStop しない");
            Assert.AreEqual(1f, client.Time.TimeScale, "Client 自身も偽造 Signal では HitStop しない");
        }

        // ── P1-1: 偽造 Cancel(発行者と異なる ClientId から)は破棄される ──

        [Test]
        public void ForgedCancel_FromNonIssuerClient_IsDiscarded()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2 };
            var host = NewPeer(relay, HostId, isServer: true);
            var client = NewPeer(relay, ClientId, isServer: false);
            host.AttachManager();
            client.AttachManager();

            var t0 = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "go" };
            var presId = _nextId++;
            var hostData = CreateData(t0);
            hostData.TotalDuration = 5f;
            hostData.Interruptible = true;
            host.RegisterPresentation(presId, hostData);
            client.RegisterPresentation(presId, CloneForClient(hostData));

            uint capturedKey = 0;
            using var spy = host.Bridge.Subscribe<PresentationPlayMsg>((sender, msg) => capturedKey = msg.HandleNetKey);

            host.Manager.PlayData(host.Resolve(presId), new PlayContext());
            relay.Advance(0.25);
            var hostHandle = host.Manager.DebugActiveHandles()[0];

            LogAssert.ignoreFailingMessages = true;
            client.Bridge.Broadcast(new PresentationCancelMsg { HandleNetKey = capturedKey }, NetChannel.ReliableOrdered);
            relay.Advance(0.25);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsTrue(host.Manager.IsPlaying(hostHandle), "Host 発の演出に対する非発行者(Client)からの Cancel は破棄され、止まらない");
        }

        // ── P1-1(残り): Interruptible=false は正規の送信元(Host)からの Cancel でも受信側で再度無視される ──

        [Test]
        public void ReceivedCancel_OnNonInterruptiblePresentation_IsIgnored_EvenFromTrustedSender()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2 };
            var host = NewPeer(relay, HostId, isServer: true);
            host.AttachManager();

            var t0 = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "go" };
            var presId = _nextId++;
            var hostData = CreateData(t0);
            hostData.TotalDuration = 5f;
            hostData.Interruptible = false; // 公開 Cancel() は既にここで無視するため、受信経路を直接叩いて検証する

            LogAssert.ignoreFailingMessages = true;
            host.RegisterPresentation(presId, hostData);

            uint capturedKey = 0;
            using var spy = host.Bridge.Subscribe<PresentationPlayMsg>((sender, msg) => capturedKey = msg.HandleNetKey);

            host.Manager.PlayData(host.Resolve(presId), new PlayContext());
            relay.Advance(0.25);
            var handle = host.Manager.DebugActiveHandles()[0];

            // Host 自身(発行者と一致する信頼された送信元)からの Cancel でも、Interruptible=false なら
            // 受信側(OnReceiveCancelMsg → CancelInternal 手前)で再度無視されなければならない。
            host.Bridge.Broadcast(new PresentationCancelMsg { HandleNetKey = capturedKey }, NetChannel.ReliableOrdered);
            relay.Advance(0.25);

            Assert.IsTrue(host.Manager.IsPlaying(handle), "Interruptible=false は正規の送信元からの Cancel でも受信側で無視される");
            LogAssert.ignoreFailingMessages = false;
        }

        // ── P2-4: OnClientConnected の自己接続は早期 return する(FakeNetBridge で SendToCount を直接検証) ──

        [Test]
        public void OnClientConnected_SelfConnection_DoesNotSendAnything()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = true, LocalClientId = 0UL };
            var manager = new PresentationManager(registry, new TimeService(), netBridge: bridge);

            var presId = _nextId++;
            var t0 = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "go" };
            var data = CreateData(t0);
            data.TotalDuration = 5f;
            data.PredictLocal = true;
            data.Id = presId;
            var address = "presentation/" + presId;
            loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = presId, Type = AssetType.Presentation, Address = address } });
            registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            registry.ResolveAsync<PresentationData>(presId).GetAwaiter().GetResult();

            manager.PlayData(data, new PlayContext()); // PredictLocal && IsServer → 即座に台帳登録される(P2-3)

            bridge.RaiseClientConnected(0UL); // 自分自身(Host)の接続通知

            Assert.AreEqual(0, bridge.SendToCount, "自分自身への接続通知では SendTo が一切呼ばれない(P2-4)");
        }

        // OnReceiveSignalMsg のハッシュ計算(PresentationManager.HashSignalKey)と同じ FNV-1a を
        // テスト側でも再現する(private のため直接は呼べない。値そのものの検証は目的ではなく、
        // 「一致するハッシュを送っても発行者不一致で破棄される」ことを示すのが目的)。
        private static ushort HashSignalKeyForTest(string key)
        {
            unchecked
            {
                const uint fnvPrime = 16777619u;
                var hash = 2166136261u;
                foreach (var c in key)
                {
                    hash ^= c;
                    hash *= fnvPrime;
                }

                return (ushort)((hash ^ (hash >> 16)) & 0xFFFFu);
            }
        }
    }
}
