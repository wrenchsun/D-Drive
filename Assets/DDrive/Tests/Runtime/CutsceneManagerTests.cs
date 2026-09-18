using System.Collections.Generic;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Tests.Runtime
{
    // [26_timeline.md] §4.5/§4.7/§4.5.1(6-10a) — CutsceneManager の基本再生・ネット同期(Cosmetic)・
    // 入力ロックの通知を検証する。D-Drive 独自トラック(SE/VFX 等、6-10b)は対象外。
    public class CutsceneManagerTests
    {
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

        private TimelineAsset CreateTimeline(double durationSeconds)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _created.Add(timeline);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = durationSeconds;
            return timeline;
        }

        private CutsceneData CreateData(double durationSeconds, NetMode net = NetMode.Local, bool predictLocal = false, bool lockInput = false)
        {
            var data = ScriptableObject.CreateInstance<CutsceneData>();
            _created.Add(data);
            data.Timeline = CreateTimeline(durationSeconds);
            data.LockInput = lockInput;
            data.PredictLocal = predictLocal;
            var flags = data.Flags;
            flags.Net = net;
            data.Flags = flags;
            return data;
        }

        // ── ローカル再生の基本 ──

        [Test]
        public void PlayData_WithoutTimeline_CompletesOnFirstTick()
        {
            var manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
            var data = ScriptableObject.CreateInstance<CutsceneData>();
            _created.Add(data);

            var handle = manager.PlayData(data, new PlayContext());
            Assert.IsTrue(manager.IsPlaying(handle));

            manager.Tick(0.001f);
            Assert.IsFalse(manager.IsPlaying(handle));
        }

        [Test]
        public void PlayData_RunsUntilDuration_ThenCompletes()
        {
            var manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
            var data = CreateData(1.0);

            var handle = manager.PlayData(data, new PlayContext());
            manager.Tick(0.5f);
            Assert.IsTrue(manager.IsPlaying(handle));

            manager.Tick(0.6f);
            Assert.IsFalse(manager.IsPlaying(handle));
        }

        [Test]
        public void Cancel_StopsImmediately_AndFiresOnCancelled()
        {
            var manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
            var data = CreateData(10.0);
            var handle = manager.PlayData(data, new PlayContext());

            var cancelled = 0;
            manager.OnCancelled(handle).Subscribe(_ => cancelled++);

            manager.Cancel(handle);
            Assert.IsFalse(manager.IsPlaying(handle));
            Assert.AreEqual(1, cancelled);
        }

        // ── 入力ロック([26] §4.5.1) ──

        [Test]
        public void InputLock_EdgeFires_OnlyOnZeroOneTransition_WithTwoOverlappingCutscenes()
        {
            var manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
            var events = new List<bool>();
            manager.OnInputLockChanged.Subscribe(v => events.Add(v));

            var dataA = CreateData(1.0, lockInput: true);
            var dataB = CreateData(1.0, lockInput: true);

            Assert.IsFalse(manager.IsInputLockedAny);

            var handleA = manager.PlayData(dataA, new PlayContext());
            Assert.IsTrue(manager.IsInputLockedAny);
            Assert.IsTrue(manager.IsInputLocked(handleA));
            CollectionAssert.AreEqual(new[] { true }, events);

            // 2 本目(重ねて再生): depth 1→2 は変化ではないので発火しない。
            var handleB = manager.PlayData(dataB, new PlayContext());
            CollectionAssert.AreEqual(new[] { true }, events);

            // 1 本目終了: depth 2→1 も変化ではない。
            manager.Cancel(handleA);
            CollectionAssert.AreEqual(new[] { true }, events);
            Assert.IsTrue(manager.IsInputLockedAny);

            // 2 本目終了: depth 1→0 でようやく false が発火する。
            manager.Cancel(handleB);
            CollectionAssert.AreEqual(new[] { true, false }, events);
            Assert.IsFalse(manager.IsInputLockedAny);
        }

        [Test]
        public void InputLock_False_WhenLockInputIsFalse()
        {
            var manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
            var data = CreateData(1.0, lockInput: false);
            var handle = manager.PlayData(data, new PlayContext());

            Assert.IsFalse(manager.IsInputLocked(handle));
            Assert.IsFalse(manager.IsInputLockedAny);
        }

        // ── ネット同期(Cosmetic、[26] §4.7) ──

        private static CutscenePeer CreatePeer(DelayedNetworkRelay relay, ulong clientId, bool isServer)
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new DelayedNetBridge(relay, clientId, isServer);
            var manager = new CutsceneManager(registry, netBridge: bridge);
            return new CutscenePeer { Loader = loader, Registry = registry, Bridge = bridge, Manager = manager };
        }

        private sealed class CutscenePeer
        {
            public FakeAssetLoader Loader;
            public AssetRegistry Registry;
            public DelayedNetBridge Bridge;
            public CutsceneManager Manager;

            public void RegisterCutscene(ulong id, CutsceneData data)
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

        [Test]
        public void Cosmetic_TwoInstances_PlayInSamePhase_AndSkipPropagatesToBoth()
        {
            const ulong HostId = 0UL;
            const ulong ClientId = 1UL;
            const ulong CutId = 900001UL;

            var relay = new DelayedNetworkRelay { LatencySeconds = 0d };
            var host = CreatePeer(relay, HostId, isServer: true);
            var client = CreatePeer(relay, ClientId, isServer: false);

            var hostData = CreateData(2.0, net: NetMode.Cosmetic, predictLocal: true);
            var clientData = CreateData(2.0, net: NetMode.Cosmetic, predictLocal: true);
            host.RegisterCutscene(CutId, hostData);
            client.RegisterCutscene(CutId, clientData);

            // PredictLocal=true のため、Play() の戻り値がそのまま行為者(Host)側の Handle になる
            // (PresentationManagerTests の Cosmetic テストと同じ考え方)。
            var hostHandle = host.Manager.PlayData(hostData, new PlayContext());
            relay.Advance(0d);

            var clientActive = client.Manager.DebugActiveHandles();
            Assert.AreEqual(1, clientActive.Count, "Client 側に Cutscene が復元されていること");
            var clientHandle = clientActive[0];

            Assert.IsTrue(host.Manager.IsPlaying(hostHandle));
            Assert.IsTrue(client.Manager.IsPlaying(clientHandle));

            // 同位相であること(同じ dt を与え続ければ NormalizedTime が一致する)。
            for (var i = 0; i < 10; i++)
            {
                host.Manager.Tick(0.05f);
                client.Manager.Tick(0.05f);
            }

            Assert.AreEqual(host.Manager.GetNormalizedTime(hostHandle), client.Manager.GetNormalizedTime(clientHandle), 0.0001f);
            Assert.Greater(host.Manager.GetNormalizedTime(hostHandle), 0f);
            Assert.Less(host.Manager.GetNormalizedTime(hostHandle), 1f);

            // Skip: 行為者(Host)が呼んでも自分だけ先に飛ばず、Broadcast 経由で全員(自分含む)に効く。
            host.Manager.Skip(hostHandle);
            relay.Advance(0d);

            host.Manager.Tick(0.01f);
            client.Manager.Tick(0.01f);

            Assert.IsFalse(host.Manager.IsPlaying(hostHandle), "Skip が Host 自身にも効くこと");
            Assert.IsFalse(client.Manager.IsPlaying(clientHandle), "Skip が Client にも効くこと");
        }
    }
}
