using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // [11_tasks.md] 5-9 — Late Join(途中参加)でループ VFX/BGM が復元され、完了済みのワンショットは
    // 復元されないことを検証する。5-8 の `DelayedNetworkRelay`/`DelayedNetBridge`/`NetPeer` をそのまま
    // 再利用する(同アセンブリ内 internal のため PresentationNetTests.cs から共有できる)。
    public class PresentationLateJoinTests
    {
        private const ulong HostId = 0UL;
        private const ulong LateClientId = 2UL;

        private ulong _nextId = 600001;
        private readonly System.Collections.Generic.List<NetPeer> _peers = new();
        private readonly System.Collections.Generic.List<GameObject> _spawnedGameObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var peer in _peers)
            {
                peer.Cleanup();
            }

            _peers.Clear();

            foreach (var go in _spawnedGameObjects)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawnedGameObjects.Clear();
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
            data.Flags.Net = DDrive.Foundation.Net.NetMode.Cosmetic;
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

        private GameObject CreateVfxPrefab()
        {
            var go = new GameObject("PresentationLateJoinTestVfxPrefab");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 5f;
            main.loop = false;
            main.startLifetime = 5f;
            _spawnedGameObjects.Add(go);
            return go;
        }

        // ── 5-9: Late Join でループ VFX が復元される / ワンショットは復元されない ──

        [Test]
        public void LateJoin_LoopingVfx_RestoredWithSeek_ForNewlyConnectedClient()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2 };
            var host = NewPeer(relay, HostId, isServer: true);
            var hostVfx = new VfxManager(host.Pool, host.Registry);
            host.AttachManager(vfx: hostVfx);

            var vfxPrefab = CreateVfxPrefab();
            var vfxId = _nextId++;
            host.RegisterVfx(vfxId, vfxPrefab);

            var track = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Vfx, Asset = AssetRef.From(new AssetId<VfxMarker>(vfxId, AssetType.Vfx)) };
            var presId = _nextId++;
            var hostData = CreateData(track);
            hostData.TotalDuration = 100f; // 常駐 VFX を模した長尺
            host.RegisterPresentation(presId, hostData);

            host.Manager.PlayData(host.Resolve(presId), new PlayContext());
            relay.Advance(0.25); // Host 自身の確定 Broadcast を届かせて実際に再生開始させる
            Assert.AreEqual(1, hostVfx.ActiveCount);

            // 5 秒後に新しいクライアントが接続してくる(この間、意図的に何もしない)。
            relay.Advance(5.0);

            var late = NewPeer(relay, LateClientId, isServer: false);
            var lateVfx = new VfxManager(late.Pool, late.Registry);
            late.AttachManager(vfx: lateVfx);
            late.RegisterVfx(vfxId, vfxPrefab);
            late.RegisterPresentation(presId, CloneForClient(hostData));

            host.Bridge.RaiseClientConnected(LateClientId);
            relay.Advance(0.25);

            Assert.AreEqual(1, lateVfx.ActiveCount, "途中参加したクライアントにも常駐 VFX がシーク状態で復元される");
            Assert.AreEqual(1, late.Manager.DebugActiveHandles().Count);
        }

        // [11_tasks.md] 6-0 修正6 — Late Join のスナップショットは OnReceivePlayMsg と同じ経路
        // (SeekInitialTracks)を通るため、猶予(既定 0.5s)ロジックもそのまま適用される。5 秒後(猶予を
        // 大幅に超える)に途中参加したクライアントでは、常駐ループ Vfx はシーク復元されるが、同じ
        // Presentation 内の Time=0 のワンショット Marker は(遠い過去のため)発火しない、という
        // 両立を確認する回帰テスト。
        [Test]
        public void LateJoin_LoopingVfx_IsRestored_ButOldOneShotMarker_IsNotFired_EvenThoughStillWithinDuration()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2 };
            var host = NewPeer(relay, HostId, isServer: true);
            var hostVfx = new VfxManager(host.Pool, host.Registry);
            host.AttachManager(vfx: hostVfx);

            var vfxPrefab = CreateVfxPrefab();
            var vfxId = _nextId++;
            host.RegisterVfx(vfxId, vfxPrefab);

            var loopTrack = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Vfx, Asset = AssetRef.From(new AssetId<VfxMarker>(vfxId, AssetType.Vfx)) };
            var markerTrack = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "once" };
            var presId = _nextId++;
            var hostData = CreateData(loopTrack, markerTrack);
            hostData.TotalDuration = 100f; // 常駐 VFX を模した長尺(まだ尺は残っている)
            host.RegisterPresentation(presId, hostData);

            host.Manager.PlayData(host.Resolve(presId), new PlayContext());
            relay.Advance(0.25);
            Assert.AreEqual(1, hostVfx.ActiveCount);

            // 5 秒後(猶予 0.5s を大幅に超える)に新しいクライアントが接続してくる。
            relay.Advance(5.0);

            var late = NewPeer(relay, LateClientId, isServer: false);
            var lateVfx = new VfxManager(late.Pool, late.Registry);
            late.AttachManager(vfx: lateVfx);
            late.RegisterVfx(vfxId, vfxPrefab);
            late.RegisterPresentation(presId, CloneForClient(hostData));

            var skippedCount = 0;
            late.Manager.OnRemoteOneShotSkipped += (track, key, lateSec) => skippedCount++;

            host.Bridge.RaiseClientConnected(LateClientId);
            relay.Advance(0.25);

            Assert.AreEqual(1, lateVfx.ActiveCount, "ループ Vfx は猶予に関わらずシーク状態で復元される(continuous 系は常に対象)");
            Assert.AreEqual(1, late.Manager.DebugActiveHandles().Count, "Presentation 自体は(尺がまだ残っているため)復元される");
            Assert.AreEqual(1, skippedCount, "同じ Presentation 内の Time=0 のワンショット Marker は猶予を大幅に超えるためスキップされる(OnRemoteOneShotSkipped が発火)");
        }

        [Test]
        public void LateJoin_OneShotPresentation_IsNotRestored_AfterItCompletes()
        {
            var relay = new DelayedNetworkRelay { LatencySeconds = 0.2 };
            var host = NewPeer(relay, HostId, isServer: true);
            host.AttachManager();

            var presId = _nextId++;
            var t0 = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "once" };
            var hostData = CreateData(t0);
            hostData.TotalDuration = 0.1f; // 短命なワンショット
            // PredictLocal=true にして、テストの簡略化した「自分への Broadcast も 1 ホップ遅延する」relay の
            // 都合で 200ms 待つ間に尺(0.1s)を超えてしまい Host 自身が再生できない、という relay 側の
            // 制約を回避する(実際の NGO は Host→自分は遅延ゼロなので発生しない。要判断は docs/28 参照)。
            hostData.PredictLocal = true;
            host.RegisterPresentation(presId, hostData);

            host.Manager.PlayData(host.Resolve(presId), new PlayContext());
            relay.Advance(0.25);
            Assert.AreEqual(1, host.Manager.DebugActiveHandles().Count);

            host.Manager.Tick(0.2f); // 尺を超えさせて Complete() させる(= アクティブ演出リストから外れる)
            Assert.AreEqual(0, host.Manager.DebugActiveHandles().Count, "尺を超えたワンショットは Complete し、アクティブ演出リストから外れる");

            var late = NewPeer(relay, LateClientId, isServer: false);
            late.AttachManager();
            late.RegisterPresentation(presId, CloneForClient(hostData));

            host.Bridge.RaiseClientConnected(LateClientId);
            relay.Advance(0.25);

            Assert.IsEmpty(late.Manager.DebugActiveHandles(), "完了済みのワンショットは Late Join で復元されない");
        }
    }
}
