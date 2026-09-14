using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Net;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using R3;
using UnityEngine;
using HapticId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Haptics.HapticMarker>;

namespace DDrive.Tests.Runtime
{
    // [11_tasks.md] 5-8/5-9 — Presentation のネット再生(開始時刻シーク/Signal 中継/予測再生)と
    // Late Join 復元を検証する。NgoNetBridge の実接続テストは 6-7 まで無いため([14_networking.md] §11)、
    // ここでは「配送を指定秒数遅延させるキュー + 共有 NetworkTime」を持つ Fake ブリッジ(Host/Client)を
    // PresentationManager インスタンスに繋いで、メッセージのシーク/重複抑制ロジックだけを検証する
    // (実ネットワークの往復・パケットロス・NGO 固有の RPC 経路は対象外。それらは Phase 6/6-7)。

    // Broadcast は送信者が Host/Client のどちらでも、登録済みの全ブリッジへ単一ホップの遅延で配送する
    // (実際の NGO は Client→Host→全員の 2 ホップだが、Manager 側のシーク/重複抑制ロジックの検証には
    // 影響しないため簡略化した。要判断は docs/28 参照)。
    internal sealed class DelayedNetworkRelay
    {
        private struct Envelope
        {
            public double DeliverAt;
            public Action Deliver;
        }

        private readonly List<Envelope> _queue = new();
        private readonly Dictionary<ulong, DelayedNetBridge> _bridges = new();

        public double NetworkTime { get; private set; }
        public double LatencySeconds = 0.2;

        public void Register(ulong clientId, DelayedNetBridge bridge) => _bridges[clientId] = bridge;

        public void EnqueueBroadcast<T>(ulong senderId, T msg) where T : INetMessage
        {
            var deliverAt = NetworkTime + LatencySeconds;
            foreach (var kv in _bridges)
            {
                var target = kv.Value;
                _queue.Add(new Envelope { DeliverAt = deliverAt, Deliver = () => target.Receive(senderId, msg) });
            }
        }

        public void EnqueueSendTo<T>(ulong senderId, ulong targetClientId, T msg) where T : INetMessage
        {
            if (!_bridges.TryGetValue(targetClientId, out var target))
            {
                return;
            }

            var deliverAt = NetworkTime + LatencySeconds;
            _queue.Add(new Envelope { DeliverAt = deliverAt, Deliver = () => target.Receive(senderId, msg) });
        }

        public void Advance(double dt)
        {
            NetworkTime += dt;

            var progressed = true;
            while (progressed)
            {
                progressed = false;
                for (var i = 0; i < _queue.Count; i++)
                {
                    if (_queue[i].DeliverAt > NetworkTime)
                    {
                        continue;
                    }

                    var env = _queue[i];
                    _queue.RemoveAt(i);
                    env.Deliver();
                    progressed = true;
                    break;
                }
            }
        }

        // [11_tasks.md] 6-0 修正6(オーケストレーター追加指示) — まだ配送されていないキュー中の全メッセージを
        // 破棄する(NgoNetBridge が切断時にアプリ層遅延キューの CancellationTokenSource を Cancel する挙動を
        // 抽象化した相当品。実際の NgoNetBridge/NetworkManager は本テストダブルの対象外だが、
        // 「切断後は古い Play が処理されない」という PresentationManager 側から見た契約はここで検証できる)。
        public void DiscardAllPending() => _queue.Clear();

        // [11_tasks.md] 6-0 修正6 — 切断で NgoNetBridge.NetworkTime が 0 に巻き戻る(NetworkManager.
        // ServerTime が未接続時に 0 を返すため)ことを模す。DiscardAllPending と組み合わせて、
        // 「配送前に破棄されたメッセージは、巻き戻った NetworkTime のもとで再計算されても復活しない」を
        // 検証するために使う。
        public void RewindNetworkTimeToZero() => NetworkTime = 0d;
    }

    internal sealed class DelayedNetBridge : INetBridge
    {
        private sealed class Subscription : IDisposable
        {
            private readonly Action _dispose;
            public Subscription(Action dispose) => _dispose = dispose;
            public void Dispose() => _dispose();
        }

        private readonly DelayedNetworkRelay _relay;
        private readonly ulong _selfClientId;
        private readonly double _clockJitterSeconds;
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();

        public bool IsServer { get; }
        public bool IsClient => true;

        // P2-6(6-0 レビュー対応) — Host/Client の NetworkTime を完全に同一の値にせず、小さなジッターを
        // 乗せて実態(各ピアが独立した NetworkTime を持つ)に近づける。既定 0(既存テストは影響を受けない)。
        public double NetworkTime => _relay.NetworkTime + _clockJitterSeconds;
        public ulong LocalClientId => _selfClientId;
        public event Action<ulong> ClientConnected;

        public DelayedNetBridge(DelayedNetworkRelay relay, ulong selfClientId, bool isServer, double clockJitterSeconds = 0d)
        {
            _relay = relay;
            _selfClientId = selfClientId;
            IsServer = isServer;
            _clockJitterSeconds = clockJitterSeconds;
            relay.Register(selfClientId, this);
        }

        // 5-9: 実 NGO の OnClientConnectedCallback を模した手動発火(テスト専用)。
        public void RaiseClientConnected(ulong clientId) => ClientConnected?.Invoke(clientId);

        public void Broadcast<T>(in T msg, NetChannel channel) where T : INetMessage
            => _relay.EnqueueBroadcast(_selfClientId, msg);

        public void SendTo<T>(ulong clientId, in T msg, NetChannel channel) where T : INetMessage
            => _relay.EnqueueSendTo(_selfClientId, clientId, msg);

        public IDisposable Subscribe<T>(Action<ulong, T> handler) where T : INetMessage
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = new List<Delegate>();
                _handlers[typeof(T)] = list;
            }

            list.Add(handler);
            return new Subscription(() => list.Remove(handler));
        }

        public Transform ResolveNetObject(ulong netId) => null;

        public ulong ResolveNetId(Transform transform) => 0UL;

        public bool IsLocalPlayerObject(Transform transform) => false;

        public ulong SpawnNetworked(GameObject root) => 0UL;

        public void DespawnNetworked(ulong netId, bool destroy)
        {
        }

        // 6-0(P2-5) — 偽造メッセージのテスト用: 実際の Broadcast() 経路を経由せず、任意の(偽の)senderId で
        // 直接この Bridge の Subscribe ハンドラへ配送する(NGO の RequestBroadcastRpc が中継してしまった後、
        // かつ HandleNetKey の発行者検証だけが最後の防波堤になるケースを模擬する)。
        public void InjectForged<T>(ulong forgedSenderId, T msg) where T : INetMessage => Receive(forgedSenderId, msg);

        internal void Receive<T>(ulong senderId, T msg) where T : INetMessage
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                return;
            }

            var snapshot = list.ToArray();
            foreach (var d in snapshot)
            {
                ((Action<ulong, T>)d).Invoke(senderId, msg);
            }
        }
    }

    // Host/Client 1 人分の Registry/Loader/Bridge を束ねる(PresentationManager は各テストが必要な
    // Manager(Vfx/Haptics 等)を組み立ててから AttachManager() で最後に作る。フィクスチャ全体で 1 つの
    // Bridge を共有すると Subscribe が積み重なって別テストの Manager まで反応してしまうため、
    // わざと毎テストで作り直す設計にした)。
    internal sealed class NetPeer
    {
        public PoolService Pool;
        public FakeAssetLoader Loader;
        public AssetRegistry Registry;
        public TimeService Time;
        public DelayedNetBridge Bridge;
        public PresentationManager Manager;

        public static NetPeer Create(DelayedNetworkRelay relay, ulong clientId, bool isServer, double clockJitterSeconds = 0d)
        {
            var pool = new PoolService();
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var time = new TimeService();
            var bridge = new DelayedNetBridge(relay, clientId, isServer, clockJitterSeconds);

            return new NetPeer { Pool = pool, Loader = loader, Registry = registry, Time = time, Bridge = bridge };
        }

        public PresentationManager AttachManager(VfxManager vfx = null, HapticsManager haptics = null)
        {
            Manager = new PresentationManager(Registry, Time, vfx: vfx, haptics: haptics, netBridge: Bridge);
            return Manager;
        }

        public void RegisterPresentation(ulong id, PresentationData data)
        {
            data.Id = id;
            var address = "presentation/" + id;
            Loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = id, Type = AssetType.Presentation, Address = address } });
            Registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            Registry.ResolveAsync<PresentationData>(id).GetAwaiter().GetResult();
        }

        // lifeMode の既定は Loop(既存の Late Join テストが常駐 VFX を想定しているため、既存呼び出し元の
        // 挙動を変えない)。6-0 修正6 のテスト(RemoteOneShotGraceTests)は明示的に OneShot を渡す。
        public void RegisterVfx(ulong id, GameObject prefab, VfxLifeMode lifeMode = VfxLifeMode.Loop)
        {
            var address = "vfx/" + id;
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.Id = id;
            data.Prefab = prefab;
            data.LifeMode = lifeMode;

            Loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = id, Type = AssetType.Vfx, Address = address } });
            Registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            Registry.ResolveAsync<VfxData>(id).GetAwaiter().GetResult();
        }

        public void RegisterHaptic(ulong id, bool localPlayerOnly)
        {
            var address = "haptic/" + id;
            var data = ScriptableObject.CreateInstance<HapticsData>();
            data.Id = id;
            data.LocalPlayerOnly = localPlayerOnly;

            Loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = id, Type = AssetType.Haptics, Address = address } });
            Registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            Registry.ResolveAsync<HapticsData>(id).GetAwaiter().GetResult();
        }

        public PresentationData Resolve(ulong id) => Registry.TryResolveSync<PresentationData>(id, out var d) ? d : null;

        public void Cleanup() => Pool.Clear(PoolScope.Global);
    }

    public class PresentationNetTests
    {
        private const ulong HostId = 0UL;
        private const ulong ClientId = 1UL;

        private ulong _nextId = 500001;
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

        private NetPeer NewPeer(DelayedNetworkRelay relay, ulong clientId, bool isServer, double clockJitterSeconds = 0d)
        {
            var peer = NetPeer.Create(relay, clientId, isServer, clockJitterSeconds);
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

        // ── 5-8: 開始時刻シークで 2 クライアントの位相が揃う ──

        [Test]
        public void NonPredicted_200msLatency_BothPeers_ReachSameNormalizedTime()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2 };
            // P2-6(6-0 レビュー対応) — Host/Client の NetworkTime を完全に同一の値にしない(実際の NGO では
            // 各ピアが独立した NetworkTime を持つ)。数 ms の小さなジッターを乗せ、許容誤差(0.001 → 0.02)を
            // 実態に合わせて緩める(以前は「同一計算の一致」を見ているだけだった)。
            var host = NewPeer(relay, HostId, isServer: true, clockJitterSeconds: 0.003);
            var client = NewPeer(relay, ClientId, isServer: false, clockJitterSeconds: -0.004);
            host.AttachManager();
            client.AttachManager();

            var presId = _nextId++;
            var t0 = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "start" };
            var hostData = CreateData(t0);
            hostData.TotalDuration = 2f;
            host.RegisterPresentation(presId, hostData);
            client.RegisterPresentation(presId, CloneForClient(hostData));

            var handle = host.Manager.PlayData(host.Resolve(presId), new PlayContext());
            Assert.AreEqual(Handle<PresentationMarker>.Invalid, handle, "PredictLocal=false は自分の Broadcast を受信するまで再生しない");
            Assert.IsEmpty(host.Manager.DebugActiveHandles(), "Broadcast が届く前はまだ何も再生していない");

            // 200ms(+マージン)だけ進める。Host 自身への確定 Broadcast も同じ 1 ホップ遅延で届く。
            relay.Advance(0.25);

            var hostHandles = host.Manager.DebugActiveHandles();
            var clientHandles = client.Manager.DebugActiveHandles();
            Assert.AreEqual(1, hostHandles.Count);
            Assert.AreEqual(1, clientHandles.Count);

            var hostNorm0 = host.Manager.GetNormalizedTime(hostHandles[0]);
            var clientNorm0 = client.Manager.GetNormalizedTime(clientHandles[0]);
            Assert.AreEqual(hostNorm0, clientNorm0, 0.02f, "200ms 遅延環境でも Host/Client の開始シーク位置(NormalizedTime)がほぼ一致する(数msのクロックジッターは許容)");
            Assert.Greater(hostNorm0, 0f, "遅延分だけ既にシークされて開始している");

            // 以後も同じだけ進行させれば位相が揃ったまま進む。
            host.Manager.Tick(0.3f);
            client.Manager.Tick(0.3f);

            var hostNorm1 = host.Manager.GetNormalizedTime(hostHandles[0]);
            var clientNorm1 = client.Manager.GetNormalizedTime(clientHandles[0]);
            Assert.AreEqual(hostNorm1, clientNorm1, 0.02f, "以後の進行も位相がほぼ揃ったまま進む(数msのクロックジッターは許容)");
            Assert.Greater(hostNorm1, hostNorm0);
        }

        // ── 予測再生: 行為者は即ローカル再生し、確定 Broadcast と二重発火しない ──

        [Test]
        public void PredictLocal_ActorPlaysImmediately_AndDoesNotDoubleFireOnConfirm()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2 };
            var host = NewPeer(relay, HostId, isServer: true);
            host.AttachManager();

            var presId = _nextId++;
            var t0 = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "go" };
            var hostData = CreateData(t0);
            hostData.TotalDuration = 1f;
            hostData.PredictLocal = true;
            host.RegisterPresentation(presId, hostData);

            var markerCount = 0;
            var handle = host.Manager.PlayData(host.Resolve(presId), new PlayContext());

            Assert.AreNotEqual(Handle<PresentationMarker>.Invalid, handle, "PredictLocal=true は即座に有効な Handle を返す");
            Assert.IsTrue(host.Manager.IsPlaying(handle), "予測再生は Broadcast を待たずローカルで再生している");
            host.Manager.OnMarker(handle).Subscribe(_ => { markerCount++; });

            // Host 自身への確定 Broadcast が届くまで進める。
            relay.Advance(0.25);

            Assert.AreEqual(0, markerCount, "確定 Broadcast の受信で Marker が再発火しない(二重発火しない)");
            Assert.IsTrue(host.Manager.IsPlaying(handle), "予測 Instance がそのまま生き続ける(別 Instance に置き換わらない)");
            Assert.AreEqual(1, host.Manager.DebugActiveHandles().Count, "確定通知で新しい Instance が増えない");
        }

        // ── Signal は Host 権威で中継され、両クライアントの HitStop が発火する ──

        [Test]
        public void Signal_RelaysThroughHost_BothPeers_ApplyHitStop()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2 };
            var host = NewPeer(relay, HostId, isServer: true);
            var client = NewPeer(relay, ClientId, isServer: false);
            host.AttachManager();
            client.AttachManager();

            var presId = _nextId++;
            var onHit = new PresentationTrack { Trigger = TrackTrigger.OnSignal, SignalKey = "hit", Kind = TrackKind.HitStop, Params = new[] { ParamValue.Of(0.1f) } };
            var hostData = CreateData(onHit);
            hostData.TotalDuration = 5f;
            host.RegisterPresentation(presId, hostData);
            client.RegisterPresentation(presId, CloneForClient(hostData));

            host.Manager.PlayData(host.Resolve(presId), new PlayContext());
            relay.Advance(0.25); // Host 自身への確定 Broadcast を届かせ、実際に再生を開始させる

            var hostHandle = host.Manager.DebugActiveHandles()[0];

            // Host(当たり判定を持つ側)が Signal を発行する。1v1 前提のため観戦者区別はせず、
            // 両ピア(Host/Client)とも HitStop する既定にした([14] §6 実装メモ、要判断は docs/28 参照)。
            host.Manager.Signal(hostHandle, "hit");
            Assert.AreEqual(1f, host.Time.TimeScale, "Signal 直後はまだ Broadcast 未到達");

            relay.Advance(0.25);

            Assert.AreEqual(0f, host.Time.TimeScale, "Host 自身も Broadcast 経由で HitStop する(直接発火しない)");
            Assert.AreEqual(0f, client.Time.TimeScale, "Client も Signal 中継で HitStop する");
        }

        // ── Haptic の LocalPlayerOnly: リモート受信では誤爆を避けて再生しない ──

        [Test]
        public void Haptic_LocalPlayerOnly_DoesNotFire_OnRemoteReceivedInstance_ButFiresOnPredictedLocal()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2 };
            var host = NewPeer(relay, HostId, isServer: true);
            var client = NewPeer(relay, ClientId, isServer: false);

            var hostOutput = new FakeHapticOutput();
            var clientOutput = new FakeHapticOutput();
            var hostHaptics = new HapticsManager(host.Registry, hostOutput);
            var clientHaptics = new HapticsManager(client.Registry, clientOutput);
            host.AttachManager(haptics: hostHaptics);
            client.AttachManager(haptics: clientHaptics);

            var hapticId = _nextId++;
            host.RegisterHaptic(hapticId, localPlayerOnly: true);
            client.RegisterHaptic(hapticId, localPlayerOnly: true);

            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Haptic, Asset = AssetRef.From(new HapticId(hapticId, AssetType.Haptics)) };
            var presId = _nextId++;
            var hostData = CreateData(track);
            hostData.TotalDuration = 1f;
            hostData.PredictLocal = true;
            host.RegisterPresentation(presId, hostData);
            client.RegisterPresentation(presId, CloneForClient(hostData));

            host.Manager.PlayData(host.Resolve(presId), new PlayContext());
            hostHaptics.Tick(0.01f);
            Assert.Greater(hostOutput.Low + hostOutput.High, 0f, "予測再生した行為者自身では LocalPlayerOnly の Haptic が普通に鳴る");

            relay.Advance(0.25);
            clientHaptics.Tick(0.01f);
            Assert.AreEqual(0f, clientOutput.Low + clientOutput.High, "リモート受信した Instance では LocalPlayerOnly=true の Haptic を鳴らさない(誤爆防止)");
        }

        private sealed class FakeHapticOutput : IHapticOutput
        {
            public float Low;
            public float High;
            public void SetMotors(float low, float high)
            {
                Low = low;
                High = high;
            }
        }

        // ── netBridge==null は今までどおり完全ローカル(regression) ──

        [Test]
        public void NoNetBridge_CosmeticPresentation_PlaysFullyLocally()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var manager = new PresentationManager(registry, new TimeService());

            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "m" };
            var data = CreateData(track); // Flags.Net = Cosmetic だが netBridge が無い

            var handle = manager.PlayData(data, new PlayContext());

            Assert.AreNotEqual(Handle<PresentationMarker>.Invalid, handle, "netBridge==null は Cosmetic でも常にローカル再生する([14] §1)");
        }
    }
}
