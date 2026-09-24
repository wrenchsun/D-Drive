using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Net;
using DDrive.Runtime.Prefab;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // [14_networking.md] §18(N-5、2026-09-24) — MS2026 の Host 引き継ぎ(同一プロセスで
    // DDriveRuntimeBootstrap.StopNetworking() → 別ロールで StartHost/StartClient をやり直す)に備えて
    // 追加した各 Manager の ResetNetworkedState() を検証する。FakeNetBridge(Tests/Runtime/FakeNetBridge.cs)は
    // Broadcast/SendTo を自分自身へ同期配送するため、Host 役 1 プロセスの中で「ネット経由の再生」を
    // 再現できる(実 NGO 接続は対象外。NgoNetBridge.ResetSessionState は AppRoundTripTracker.Reset の
    // 既存テスト〔Tests/Editor/AppRoundTripTrackerTests.cs〕に委ねる、[14] §18 実装メモ参照)。
    public class NetResetForHostMigrationTests
    {
        private readonly List<GameObject> _spawnedGameObjects = new();
        private readonly List<PoolService> _pools = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var pool in _pools)
            {
                pool.Clear(PoolScope.Global);
            }

            _pools.Clear();

            foreach (var go in _spawnedGameObjects)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawnedGameObjects.Clear();
        }

        private PoolService NewPool()
        {
            var pool = new PoolService();
            _pools.Add(pool);
            return pool;
        }

        private GameObject NewLoopingVfxPrefab(string name)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = 5f;
            _spawnedGameObjects.Add(go);
            return go;
        }

        private static void RegisterVfx(AssetRegistry registry, FakeAssetLoader loader, ulong id, GameObject prefab, VfxLifeMode lifeMode)
        {
            var address = "vfx/" + id;
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.Id = id;
            data.Prefab = prefab;
            data.LifeMode = lifeMode;

            loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = id, Type = AssetType.Vfx, Address = address } });
            registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            registry.ResolveAsync<VfxData>(id).GetAwaiter().GetResult();
        }

        // ── (a) PresentationManager: 台帳が空になり、Fired VFX が止まり、ローカル再生は影響を受けない ──

        [Test]
        public void PresentationManager_ResetNetworkedState_StopsNetworkedVfx_ButNotLocalPresentation()
        {
            var pool = NewPool();
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, LocalClientId = 0UL };
            var vfx = new VfxManager(pool, registry, bridge);
            var manager = new PresentationManager(registry, new TimeService(), vfx: vfx, netBridge: bridge);

            var vfxPrefab = NewLoopingVfxPrefab("NetResetTestLoopingVfx");
            var vfxId = 910001UL;
            RegisterVfx(registry, loader, vfxId, vfxPrefab, VfxLifeMode.Loop);

            var networkedTrack = new PresentationTrack
            {
                Trigger = TrackTrigger.AtTime,
                Time = 0f,
                Kind = TrackKind.Vfx,
                Asset = AssetRef.From(new AssetId<VfxMarker>(vfxId, AssetType.Vfx)),
                StopOnCancel = true,
            };
            var networkedData = ScriptableObject.CreateInstance<PresentationData>();
            networkedData.Tracks = new[] { networkedTrack };
            networkedData.TotalDuration = 30f;
            networkedData.Interruptible = false; // Reset は Interruptible に関係なく強制終了することも確認する
            networkedData.PredictLocal = true; // Host が即座に Handle を得られるようにする(FakeNetBridge は同期配送のため無くても echo は届くが、戻り値で検証したい)
            networkedData.Flags.Net = NetMode.Cosmetic;

            var networkedHandle = manager.PlayData(networkedData, new PlayContext());
            Assert.IsTrue(manager.IsPlaying(networkedHandle), "ネット経由の Presentation が再生中");
            Assert.AreEqual(1, vfx.ActiveCount, "ネット経由の Loop VFX が再生中");

            var localTrack = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "local" };
            var localData = ScriptableObject.CreateInstance<PresentationData>();
            localData.Tracks = new[] { localTrack };
            localData.TotalDuration = 30f;
            localData.Interruptible = true;
            localData.Flags.Net = NetMode.Local;
            var localHandle = manager.PlayData(localData, new PlayContext());

            Assert.AreEqual(2, manager.DebugActiveHandles().Count, "ネット経由 + ローカルの 2 件が再生中");

            manager.ResetNetworkedState();

            Assert.AreEqual(0, vfx.ActiveCount, "StopOnCancel=true の Loop VFX は Reset で強制停止する(Interruptible=false でも)");
            Assert.AreEqual(1, manager.DebugActiveHandles().Count, "台帳に残るのはローカル再生の 1 件だけ");
            Assert.IsTrue(manager.IsPlaying(localHandle), "ローカル(ネット非経由)の再生は Reset の影響を受けない");
            Assert.IsFalse(manager.IsPlaying(networkedHandle), "ネット経由の Presentation は Reset で終了する");
        }

        // ── (b) Reset 後に別の LocalClientId(役割変更後)で発行した Play/Signal が正常に通る ──

        [Test]
        public void PresentationManager_ResetNetworkedState_ThenLocalClientIdChanges_NewPlayAndSignal_WorkNormally()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = false, IsClient = true, LocalClientId = 1UL };
            var manager = new PresentationManager(registry, new TimeService(), netBridge: bridge);

            var track = new PresentationTrack { Trigger = TrackTrigger.OnSignal, Kind = TrackKind.Marker, SignalKey = "hit" };
            var data = ScriptableObject.CreateInstance<PresentationData>();
            data.Tracks = new[] { track };
            data.TotalDuration = 10f;
            data.Interruptible = true;
            data.PredictLocal = true;
            data.Flags.Net = NetMode.Cosmetic;

            // 役割変更前(Client, id=1)のベースライン再生。HandleNetKey の上位 8bit にこの時点の
            // LocalClientId(1)が埋め込まれる。
            manager.PlayData(data, new PlayContext());
            Assert.AreEqual(1, manager.DebugActiveHandles().Count);

            // Host 引き継ぎ: StopNetworking() → Reset。
            manager.ResetNetworkedState();
            Assert.IsEmpty(manager.DebugActiveHandles(), "Reset で旧セッションの台帳は空になる");

            // 自分が新しい Host(id=0)になる(NgoNetBridge.LocalClientId は NetworkManager.LocalClientId を
            // 動的に返すため、役割変更後は同じ bridge インスタンスでも値が変わる。FakeNetBridge は
            // setter 付きプロパティでこれを模す)。
            bridge.LocalClientId = 0UL;
            bridge.IsServer = true;

            manager.PlayData(data, new PlayContext());
            var handles = manager.DebugActiveHandles();
            Assert.AreEqual(1, handles.Count, "役割変更後の新しい発行も正常に再生できる(古い鍵の残骸に阻害されない)");

            var handle = handles[0];
            var fired = false;
            using (manager.OnMarker(handle).Subscribe(_ => fired = true))
            {
                manager.Signal(handle, "hit");
            }

            Assert.IsTrue(fired, "役割変更後に発行した鍵で Signal が正常に届く");
        }

        // ── (c) 保留キューが捨てられる(Registry 未準備の間に届いたネット受信は Reset で消える) ──

        [Test]
        public void PresentationManager_ResetNetworkedState_DiscardsPendingRegistryQueue()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = false, IsClient = true, LocalClientId = 1UL };
            var manager = new PresentationManager(registry, new TimeService(), netBridge: bridge);

            manager.SetRegistryReady(false);
            // Host(id=0)から届いた PlayMsg。Registry 未準備のため _pendingNetMessages へ保留される。
            bridge.InjectReceive(0UL, new PresentationPlayMsg { PresId = 999999UL, HandleNetKey = 0x00000001u, StartNetTime = 0d });

            manager.ResetNetworkedState();

            Assert.DoesNotThrow(() => manager.SetRegistryReady(true));
            Assert.IsEmpty(manager.DebugActiveHandles(), "保留キューが Reset で捨てられているため、Ready 後も何も再生されない");
        }

        [Test]
        public void CutsceneManager_ResetNetworkedState_DiscardsPendingRegistryQueue()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = false, IsClient = true, LocalClientId = 1UL };
            var cutscene = new CutsceneManager(registry, netBridge: bridge);

            cutscene.SetRegistryReady(false);
            bridge.InjectReceive(0UL, new CutscenePlayMsg { CutId = 999999UL, HandleNetKey = 0x00000001u, StartNetTime = 0d });

            cutscene.ResetNetworkedState();

            Assert.DoesNotThrow(() => cutscene.SetRegistryReady(true));
            Assert.IsEmpty(cutscene.DebugActiveHandles(), "保留キューが Reset で捨てられているため、Ready 後も何も再生されない");
        }

        // ── PrefabsManager: Simulated 台帳(NetworkManager.Shutdown で NetworkObject ごと消える想定)だけを
        //    捨て、ローカル(NetMode!=Simulated)の Instance には触れない ──

        [Test]
        public void PrefabsManager_ResetNetworkedState_ClearsSimulatedLedger_ButNotLocalInstances()
        {
            var pool = NewPool();
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, LocalClientId = 0UL };
            var prefabs = new PrefabsManager(pool, registry, netBridge: bridge);

            var simulatedPrefabGo = new GameObject("NetResetTestSimulatedPrefab");
            _spawnedGameObjects.Add(simulatedPrefabGo);
            var localPrefabGo = new GameObject("NetResetTestLocalPrefab");
            _spawnedGameObjects.Add(localPrefabGo);

            var simulatedData = ScriptableObject.CreateInstance<PrefabData>();
            simulatedData.Id = 920001UL;
            simulatedData.Prefab = simulatedPrefabGo;
            simulatedData.Flags.Net = NetMode.Simulated;

            var localData = ScriptableObject.CreateInstance<PrefabData>();
            localData.Id = 920002UL;
            localData.Prefab = localPrefabGo;
            localData.Flags.Net = NetMode.Local;

            var simulatedHandle = prefabs.SpawnData(simulatedData, Vector3.zero, Quaternion.identity);
            var localHandle = prefabs.SpawnData(localData, Vector3.zero, Quaternion.identity);

            Assert.IsTrue(prefabs.IsValid(simulatedHandle));
            Assert.IsTrue(prefabs.IsValid(localHandle));
            Assert.AreEqual(2, prefabs.ActiveCount);

            prefabs.ResetNetworkedState();

            Assert.IsFalse(prefabs.IsValid(simulatedHandle), "Simulated 台帳は NetworkManager.Shutdown 相当で破棄済み扱いになる");
            Assert.IsTrue(prefabs.IsValid(localHandle), "ローカル(NetMode!=Simulated)の Instance は影響を受けない");
            Assert.AreEqual(1, prefabs.ActiveCount);
        }

        // ── AudioManager/VfxManager: Tick 内バッチ(まだ Broadcast していない Cosmetic)を破棄する ──

        [Test]
        public void AudioManager_ResetNetworkedState_ClearsPendingCosmeticBatch()
        {
            var pool = NewPool();
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, LocalClientId = 0UL };
            var seTemplate = new GameObject("NetResetTestSeTemplate");
            _spawnedGameObjects.Add(seTemplate);
            seTemplate.AddComponent<AudioSource>();
            seTemplate.SetActive(false);

            var audio = new AudioManager(pool, registry, seTemplate, bridge);

            var data = ScriptableObject.CreateInstance<SeData>();
            data.Id = 930001UL;
            data.Flags.Net = NetMode.Cosmetic;
            data.Volume = 1f;
            data.MaxConcurrent = 1;
            data.Clips = new[] { AudioClip.Create("NetResetTestClip", 100, 1, 44100, false) };

            audio.PlaySeData(data);
            // まだ Tick していないので Broadcast されていない(Cosmetic は Tick でまとめて送る、[14] §3/§4/§8)。

            audio.ResetNetworkedState();
            audio.Tick(0.016f);

            Assert.AreEqual(0, bridge.BroadcastCount, "Reset で破棄した Cosmetic バッチは Tick で送信されない");
        }

        [Test]
        public void VfxManager_ResetNetworkedState_ClearsPendingCosmeticBatch()
        {
            var pool = NewPool();
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, LocalClientId = 0UL };
            var vfxPrefab = new GameObject("NetResetTestVfxPrefab");
            _spawnedGameObjects.Add(vfxPrefab);

            var vfx = new VfxManager(pool, registry, bridge);

            var data = ScriptableObject.CreateInstance<VfxData>();
            data.Id = 930002UL;
            data.Prefab = vfxPrefab;
            data.Flags.Net = NetMode.Cosmetic;

            vfx.SpawnData(data);
            // まだ Tick していないので Broadcast されていない。

            vfx.ResetNetworkedState();
            vfx.Tick(0.016f);

            Assert.AreEqual(0, bridge.BroadcastCount, "Reset で破棄した Cosmetic バッチは Tick で送信されない");
        }
    }
}
