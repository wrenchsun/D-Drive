using System.Collections.Generic;
using System.Text.RegularExpressions;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Net;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    // [11_tasks.md] 6-0(実機確認で見つかった課題の修正) — docs/29_network_device_test.md §8 の
    // 「実機確認で見つかった課題」2/3/4 の回帰テスト。課題1(遅延シミュレーター)/5(切断ログ)は
    // NgoNetBridge(実 NGO 接続)に閉じた変更のため、ここでは PresentationManager 側のロジックだけを
    // FakeNetBridge で検証する(NGO 実接続そのものはローカル結合確認(docs/29 §7)で確認する)。
    public class PresentationNetDeviceFixTests
    {
        private ulong _nextId = 800001;

        // ── 6-0 修正6(実機確認 v2 で発見した実バグ)用のフィクスチャ ──
        // NetPeer/DelayedNetworkRelay(PresentationNetTests.cs で定義、同アセンブリの internal)を再利用する。
        private readonly List<NetPeer> _netPeers = new();
        private readonly List<GameObject> _spawnedGameObjects = new();

        [TearDown]
        public void TearDownNetPeers()
        {
            foreach (var peer in _netPeers)
            {
                peer.Cleanup();
            }

            _netPeers.Clear();

            foreach (var go in _spawnedGameObjects)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawnedGameObjects.Clear();
        }

        private NetPeer NewNetPeer(DelayedNetworkRelay relay, ulong clientId, bool isServer)
        {
            var peer = NetPeer.Create(relay, clientId, isServer);
            _netPeers.Add(peer);
            return peer;
        }

        // PresentationNetTests.CloneForClient と同じ意図(Client 側は別インスタンスの Data を持つ実態を
        // 模す)の複製。private のため各テストファイルで個別に持つ既存の慣習(PresentationLateJoinTests.cs
        // 参照)に合わせる。
        private static PresentationData CloneForNetPeerClient(PresentationData source)
        {
            var clone = ScriptableObject.CreateInstance<PresentationData>();
            clone.Tracks = source.Tracks;
            clone.TotalDuration = source.TotalDuration;
            clone.Interruptible = source.Interruptible;
            clone.PredictLocal = source.PredictLocal;
            clone.Flags = source.Flags;
            return clone;
        }

        private GameObject CreateOneShotVfxPrefab()
        {
            var go = new GameObject("PresentationNetDeviceFixTestVfxPrefab");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 3f;
            main.loop = false;
            main.startLifetime = 3f;
            _spawnedGameObjects.Add(go);
            return go;
        }

        private static PresentationData CreateData(params PresentationTrack[] tracks)
        {
            var data = ScriptableObject.CreateInstance<PresentationData>();
            data.Tracks = tracks;
            data.Interruptible = true;
            data.Flags.Net = NetMode.Cosmetic;
            return data;
        }

        private static void RegisterPresentation(AssetRegistry registry, FakeAssetLoader loader, ulong id, PresentationData data)
        {
            data.Id = id;
            var address = "presentation/" + id;
            loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new System.Collections.Generic.List<CatalogEntry> { new() { Id = id, Type = AssetType.Presentation, Address = address } });
            registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            // ResolveOrPlaceholder は _loaded にキャッシュ済みでなければ常に Placeholder を返す(同期経路)。
            // カタログに登録しただけでは足りず、NetPeer.RegisterPresentation と同じく明示的に解決させておく。
            registry.ResolveAsync<PresentationData>(id).GetAwaiter().GetResult();
        }

        // ── 課題3: Registry 準備完了までネット受信の Play をキューへ保留する ──

        [Test]
        public void OnReceivePlayMsg_BeforeRegistryReady_IsQueued_AndFlushedWithoutPlaceholder_AfterReady()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = false, LocalClientId = 1UL };
            var manager = new PresentationManager(registry, new TimeService(), netBridge: bridge);

            var placeholderUsed = false;
            registry.OnPlaceholderUsed += (_, _) => placeholderUsed = true;

            manager.SetRegistryReady(false);

            var presId = _nextId++;
            const uint handleNetKey = 0x11111111u;

            // Registry にまだ presId が登録されていない状態で PlayMsg を受信させる(Late Join のスナップ
            // ショットがカタログ登録完了より先に届くケースを模す)。senderId=0(Host、TrustedRelayClientId)
            // なので発行者検証は常に通る。
            bridge.InjectReceive(0UL, new PresentationPlayMsg { PresId = presId, HandleNetKey = handleNetKey, StartNetTime = 0d });

            Assert.AreEqual(0, manager.DebugActiveHandles().Count, "Registry 未 ready の間は即座に処理されず保留される");
            Assert.IsFalse(placeholderUsed, "保留中はまだ ResolveOrPlaceholder を呼んでいない");

            // カタログ登録がようやく完了する(DDriveRuntimeBootstrap.RegisterCatalogsAsync 完了に相当)。
            var t0 = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "go" };
            var data = CreateData(t0);
            data.TotalDuration = 5f;
            RegisterPresentation(registry, loader, presId, data);

            manager.SetRegistryReady(true);

            Assert.AreEqual(1, manager.DebugActiveHandles().Count, "ready になった時点で保留分がまとめて処理される");
            Assert.IsFalse(placeholderUsed, "登録済みの ID を正しく解決できており、Placeholder には落ちていない(修正前は Unregistered AssetId 警告が出ていた)");
        }

        // ── 課題2: ネット受信で新規生成された Instance だけ OnNetworkReceivedPlay が発火する ──

        [Test]
        public void OnNetworkReceivedPlay_FiresOnce_ForNewInstance_NotForDuplicateEcho()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = false, LocalClientId = 1UL };
            var manager = new PresentationManager(registry, new TimeService(), netBridge: bridge);

            var presId = _nextId++;
            var t0 = new PresentationTrack { Trigger = TrackTrigger.OnSignal, SignalKey = "hit", Kind = TrackKind.Marker };
            var data = CreateData(t0);
            data.TotalDuration = 5f;
            RegisterPresentation(registry, loader, presId, data);

            var firedCount = 0;
            var firedKey = 0u;
            manager.OnNetworkReceivedPlay += (handle, key) =>
            {
                firedCount++;
                firedKey = key;
                Assert.IsTrue(manager.IsPlaying(handle), "通知される Handle は実際に再生中の Instance を指す");
            };

            const uint handleNetKey = 0x22222222u;
            bridge.InjectReceive(0UL, new PresentationPlayMsg { PresId = presId, HandleNetKey = handleNetKey, StartNetTime = 0d });

            Assert.AreEqual(1, firedCount, "ネット受信で新規生成された Instance について 1 回発火する(6-0 修正2)");
            Assert.AreEqual(handleNetKey, firedKey);

            // 同じ HandleNetKey の確定エコー(既に予測再生/受信済みの分岐)では新規生成が起きないため
            // 再発火しない。
            bridge.InjectReceive(0UL, new PresentationPlayMsg { PresId = presId, HandleNetKey = handleNetKey, StartNetTime = 0d });
            Assert.AreEqual(1, firedCount, "既存エントリの確定エコーでは再発火しない(二重通知しない)");
        }

        // ── 課題4: 未知の HandleNetKey(または対象が完了済み)の Cancel/Signal は開発ビルドで破棄ログを出す ──

        [Test]
        public void UnknownHandleNetKey_Cancel_LogsDiscardWarning()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = true, LocalClientId = 0UL };
            _ = new PresentationManager(registry, new TimeService(), netBridge: bridge);

            LogAssert.Expect(LogType.Warning, new Regex(@"PresentationCancelMsg.*未知のキー"));
            bridge.InjectReceive(0UL, new PresentationCancelMsg { HandleNetKey = 0x33333333u });
        }

        [Test]
        public void UnknownHandleNetKey_Signal_LogsDiscardWarning()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = true, LocalClientId = 0UL };
            _ = new PresentationManager(registry, new TimeService(), netBridge: bridge);

            LogAssert.Expect(LogType.Warning, new Regex(@"PresentationSignalMsg.*未知のキー"));
            bridge.InjectReceive(0UL, new PresentationSignalMsg { HandleNetKey = 0x44444444u, SignalKeyHash = 1 });
        }

        // ── 6-0 修正6(実機確認 v2 で発見): 遅延があるとリモートの開始直後ワンショットが一切発火しない ──
        // docs/29 §8「修正版 v2 での再確認」で見つかった実バグの回帰テスト。猶予(既定 0.5秒)以内の遅れは
        // 遅れて発火し、それを超える遅れはスキップする。NetPeer/DelayedNetworkRelay(PresentationNetTests.cs)
        // を再利用し、実際に VfxManager.ActiveCount で「本当に発火したか」を確認する(FakeNetBridge だけでは
        // 200ms 級の実時間遅延を elapsed に反映させにくいため、Advance(dt) で明示的に進められる Relay を使う)。

        [Test]
        public void RemoteOneShotVfx_WithinGrace_FiresLate_OnBothPeers()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.1 }; // 既定猶予0.5s以内
            var host = NewNetPeer(relay, 0UL, isServer: true);
            var client = NewNetPeer(relay, 1UL, isServer: false);
            var hostVfx = new VfxManager(host.Pool, host.Registry);
            var clientVfx = new VfxManager(client.Pool, client.Registry);
            host.AttachManager(vfx: hostVfx);
            client.AttachManager(vfx: clientVfx);

            var vfxPrefab = CreateOneShotVfxPrefab();
            var vfxId = _nextId++;
            host.RegisterVfx(vfxId, vfxPrefab, VfxLifeMode.OneShot);
            client.RegisterVfx(vfxId, vfxPrefab, VfxLifeMode.OneShot);

            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Vfx, Asset = AssetRef.From(new AssetId<VfxMarker>(vfxId, AssetType.Vfx)) };
            var presId = _nextId++;
            var hostData = CreateData(track);
            hostData.TotalDuration = 5f; // 尺は十分残っている(到着時点で終わっていない)
            host.RegisterPresentation(presId, hostData);
            client.RegisterPresentation(presId, CloneForNetPeerClient(hostData));

            host.Manager.PlayData(host.Resolve(presId), new PlayContext());
            relay.Advance(0.15); // 0.1s 遅延 + マージン。Host 自身への確定 Broadcast も同じ遅延で届く

            Assert.AreEqual(1, hostVfx.ActiveCount, "Host 自身も(PredictLocal=false なので)確定 Broadcast 経由で遅れて発火する(猶予内)");
            Assert.AreEqual(1, clientVfx.ActiveCount, "6-0 修正6: 猶予(0.5s)以内の遅れなら Client でもワンショット VFX が遅れて発火する(修正前は一切発火しなかった)");
        }

        [Test]
        public void RemoteOneShotVfx_ExceedsGrace_IsSkipped_AndRaisesOnRemoteOneShotSkipped()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.6 }; // 既定猶予0.5sを超える
            var host = NewNetPeer(relay, 0UL, isServer: true);
            var client = NewNetPeer(relay, 1UL, isServer: false);
            var hostVfx = new VfxManager(host.Pool, host.Registry);
            var clientVfx = new VfxManager(client.Pool, client.Registry);
            host.AttachManager(vfx: hostVfx);
            client.AttachManager(vfx: clientVfx);

            var vfxPrefab = CreateOneShotVfxPrefab();
            var vfxId = _nextId++;
            host.RegisterVfx(vfxId, vfxPrefab, VfxLifeMode.OneShot);
            client.RegisterVfx(vfxId, vfxPrefab, VfxLifeMode.OneShot);

            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Vfx, Asset = AssetRef.From(new AssetId<VfxMarker>(vfxId, AssetType.Vfx)) };
            var presId = _nextId++;
            var hostData = CreateData(track);
            hostData.TotalDuration = 5f; // 猶予を超えるが尺はまだ残っている(復元不能な尺切れとは別条件)
            host.RegisterPresentation(presId, hostData);
            client.RegisterPresentation(presId, CloneForNetPeerClient(hostData));

            var skippedCount = 0;
            TrackKind? skippedKind = null;
            float lastLateSec = 0f;
            client.Manager.OnRemoteOneShotSkipped += (track2, key, lateSec) =>
            {
                skippedCount++;
                skippedKind = track2.Kind;
                lastLateSec = lateSec;
            };

            host.Manager.PlayData(host.Resolve(presId), new PlayContext());
            relay.Advance(0.65);

            Assert.AreEqual(0, clientVfx.ActiveCount, "6-0 修正6: 猶予(0.5s)を超えた遅れのワンショットは従来どおりスキップされる");
            Assert.AreEqual(1, skippedCount, "スキップ時に OnRemoteOneShotSkipped が 1 回発火する(NetCheckRunner の track_skipped ログ用)");
            Assert.AreEqual(TrackKind.Vfx, skippedKind);
            Assert.Greater(lastLateSec, 0.5f, "猶予を超えた分だけ late がプラスになる");
        }

        // ── 6-0 修正6(オーケストレーター追加指示、切断確認で発見): 切断後は遅延キューの古い Play を処理しない ──
        // 実際の NgoNetBridge(CancellationTokenSource による破棄)は NGO 実接続が必要なため単体テストの対象外
        // (docs/29 §7/§9 のローカル結合確認で検証する)。ここでは PresentationManager 側から見た契約
        // ─ 「配送前に破棄されたメッセージは、たとえ後で NetworkTime が巻き戻っていても処理されない」
        // ─ を DelayedNetworkRelay.DiscardAllPending(NgoNetBridge の Cancel 相当)で検証する。

        [Test]
        public void RemotePlay_DiscardedBeforeDelivery_IsNeverProcessed_EvenIfNetworkTimeRewinds()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2 };
            var host = NewNetPeer(relay, 0UL, isServer: true);
            var client = NewNetPeer(relay, 1UL, isServer: false);
            host.AttachManager();
            client.AttachManager();

            var presId = _nextId++;
            var t0 = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "go" };
            var hostData = CreateData(t0);
            hostData.TotalDuration = 5f;
            host.RegisterPresentation(presId, hostData);
            client.RegisterPresentation(presId, CloneForNetPeerClient(hostData));

            host.Manager.PlayData(host.Resolve(presId), new PlayContext());
            // まだ配送されていない(0.2s 未満)うちに「切断」が起きて、キューが破棄される
            // (NgoNetBridge.HandleClientDisconnected が _appLayerQueueCts.Cancel() する挙動に相当)。
            relay.DiscardAllPending();

            // 切断後に NetworkTime が巻き戻る(NgoNetBridge.NetworkTime は NetworkManager 未接続時に 0 を
            // 返す)。巻き戻っていても、破棄されたメッセージがそもそも配送されなければ影響しない
            // (修正前の実バグは、この巻き戻った NetworkTime で elapsed=Max(0, 0-StartNetTime)=0 と
            // 再計算されてワンショットが誤って発火する、というものだった)。
            relay.RewindNetworkTimeToZero();
            relay.Advance(1.0);

            Assert.IsEmpty(client.Manager.DebugActiveHandles(), "配送前に破棄された PlayMsg は NetworkTime が変化しても処理されない(切断後の誤発火を防ぐ)");
        }
    }
}
