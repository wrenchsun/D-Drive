using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Net;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using UnityEngine;
using HapticId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Haptics.HapticMarker>;

namespace DDrive.Tests.Runtime
{
    // [11_tasks.md] N-4 — PresentationTrack.Scope(ParticipantsOnly)が HitStop/CameraShake/Haptic の
    // ネット受信 Instance(PlayedViaNetworkReceive=true)に対して「当事者(Self/Target が自分のプレイヤー
    // オブジェクト)だけ発火する」ことを検証する。[14_networking.md] §5 実装メモ(5-8)「HitStop は全員が
    // 実行する(観戦者を区別しない、既定)」の要判断(2026-09-14)を解消する。
    //
    // PresentationNetTests.cs の DelayedNetBridge/NetPeer(Host↔Client の遅延配送)とは別に、ここでは
    // 「自分(1 ピア)の PresentationManager が Host からの PresentationPlayMsg を受信した」状況を
    // FakeNetBridge.InjectReceive で直接模擬する(senderId=0=Host は発行者検証を常に通過するため、
    // HandleNetKey の生成規則を気にせず任意の値で試せる)。IsParticipant() の当事者判定だけを対象にするには
    // これで十分で、Late Join・レート制限等 N-4 と無関係な経路を通さずに済む。
    public class PresentationParticipantScopeTests
    {
        private const ulong TrustedHostSenderId = 0UL;

        private ulong _nextId = 800001;
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
        }

        private GameObject NewActor(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
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

        private sealed class Fixture
        {
            public FakeAssetLoader Loader;
            public AssetRegistry Registry;
            public TimeService Time;
            public FakeNetBridge Bridge;
            public CameraFxManager CameraFx;
            public HapticsManager Haptics;
            public FakeHapticOutput HapticOutput;
            public PresentationManager Manager;
        }

        // このフィクスチャ自身が「1 ピア(自分の画面)」を表す。LocalClientId は Host/Client どちらでもよい
        // (発行者検証は常に Host=TrustedHostSenderId から受信する体で送るため無関係)。
        private Fixture NewFixture()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var time = new TimeService();
            var bridge = new FakeNetBridge { IsServer = false, IsClient = true, LocalClientId = 9UL };
            var cameraFx = new CameraFxManager(registry);
            var hapticOutput = new FakeHapticOutput();
            var haptics = new HapticsManager(registry, hapticOutput);
            var manager = new PresentationManager(registry, time, cameraFx: cameraFx, haptics: haptics, netBridge: bridge);

            return new Fixture
            {
                Loader = loader,
                Registry = registry,
                Time = time,
                Bridge = bridge,
                CameraFx = cameraFx,
                Haptics = haptics,
                HapticOutput = hapticOutput,
                Manager = manager,
            };
        }

        private static PresentationData CreateData(params PresentationTrack[] tracks)
        {
            var data = ScriptableObject.CreateInstance<PresentationData>();
            data.Tracks = tracks;
            data.TotalDuration = 5f;
            data.Interruptible = true;
            data.Flags.Net = NetMode.Cosmetic;
            return data;
        }

        private ulong RegisterPresentation(Fixture fx, PresentationData data)
        {
            var id = _nextId++;
            data.Id = id;
            var address = "presentation/" + id;
            fx.Loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = id, Type = AssetType.Presentation, Address = address } });
            fx.Registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            fx.Registry.ResolveAsync<PresentationData>(id).GetAwaiter().GetResult();
            return id;
        }

        private ulong RegisterHaptic(Fixture fx)
        {
            var id = _nextId++;
            var data = ScriptableObject.CreateInstance<HapticsData>();
            data.Id = id;
            // LowFreq/HighFreq は既定(From=1→To=0, 0.2s)のまま。LocalPlayerOnly は既定 true のままだと
            // N-4 と無関係な誤爆防止(6-0)が同時に効いてしまうため、Scope 単体で検証できるよう false にする。
            data.LocalPlayerOnly = false;

            var address = "haptic/" + id;
            fx.Loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = id, Type = AssetType.Haptics, Address = address } });
            fx.Registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            fx.Registry.ResolveAsync<HapticsData>(id).GetAwaiter().GetResult();
            return id;
        }

        // Host(senderId=0=TrustedRelayClientId)から PresentationPlayMsg を受信したことにする
        // (IsAuthorizedSender は Host からの送信を常に許可するため、HandleNetKey は任意の非 0 値でよい)。
        private static void ReceivePlay(Fixture fx, ulong presId, uint handleNetKey, ulong selfNetId, ulong targetNetId)
        {
            fx.Bridge.InjectReceive(TrustedHostSenderId, new PresentationPlayMsg
            {
                PresId = presId,
                SelfNetId = selfNetId,
                TargetNetId = targetNetId,
                Position = Vector3.zero,
                StartNetTime = 0d,
                Seed = 0,
                HandleNetKey = handleNetKey,
            });
        }

        private static PresentationTrack MakeTrack(TrackKind kind, PresentationEffectScope scope, AssetRef asset = default)
            => new PresentationTrack
            {
                Trigger = TrackTrigger.AtTime,
                Time = 0f,
                Kind = kind,
                Asset = asset,
                Scope = scope,
                Params = kind == TrackKind.HitStop ? new[] { ParamValue.Of(0.1f) } : null,
            };

        // ── (a) Everyone は従来どおり全員発火(回帰) ──

        [Test]
        public void Everyone_ThirdParty_StillFiresAll_Regression()
        {
            var fx = NewFixture();
            var thirdParty = NewActor("ThirdParty"); // このピアのローカルプレイヤーではない
            fx.Bridge.SetNetId(thirdParty.transform, 999UL);

            var hapticId = RegisterHaptic(fx);
            var tracks = new[]
            {
                MakeTrack(TrackKind.HitStop, PresentationEffectScope.Everyone),
                MakeTrack(TrackKind.CameraShake, PresentationEffectScope.Everyone),
                MakeTrack(TrackKind.Haptic, PresentationEffectScope.Everyone, AssetRef.From(new HapticId(hapticId, AssetType.Haptics))),
            };
            var data = CreateData(tracks);
            var presId = RegisterPresentation(fx, data);

            // Self/Target とも「自分ではない」第三者の netId(誰も所有していないが解決自体はできる)。
            ReceivePlay(fx, presId, handleNetKey: 1u, selfNetId: 999UL, targetNetId: 0UL);
            fx.Haptics.Tick(0.01f);

            Assert.AreEqual(0f, fx.Time.TimeScale, "Everyone は当事者でなくても HitStop が発火する(既定・回帰)");
            Assert.AreEqual(1, fx.CameraFx.ActiveCount, "Everyone は当事者でなくても CameraShake が発火する(既定・回帰)");
            Assert.Greater(fx.HapticOutput.Low + fx.HapticOutput.High, 0f, "Everyone は当事者でなくても Haptic が発火する(既定・回帰)");
        }

        // ── (b) ParticipantsOnly + 自分が Self → 発火 ──

        [Test]
        public void ParticipantsOnly_LocalIsSelf_Fires()
        {
            var fx = NewFixture();
            var self = NewActor("Self");
            fx.Bridge.SetNetId(self.transform, 111UL);
            fx.Bridge.SetLocalPlayerObject(self.transform, true);

            var hapticId = RegisterHaptic(fx);
            var tracks = new[]
            {
                MakeTrack(TrackKind.HitStop, PresentationEffectScope.ParticipantsOnly),
                MakeTrack(TrackKind.CameraShake, PresentationEffectScope.ParticipantsOnly),
                MakeTrack(TrackKind.Haptic, PresentationEffectScope.ParticipantsOnly, AssetRef.From(new HapticId(hapticId, AssetType.Haptics))),
            };
            var data = CreateData(tracks);
            var presId = RegisterPresentation(fx, data);

            ReceivePlay(fx, presId, handleNetKey: 2u, selfNetId: 111UL, targetNetId: 0UL);
            fx.Haptics.Tick(0.01f);

            Assert.AreEqual(0f, fx.Time.TimeScale, "自分が Self なら当事者として HitStop が発火する");
            Assert.AreEqual(1, fx.CameraFx.ActiveCount, "自分が Self なら当事者として CameraShake が発火する");
            Assert.Greater(fx.HapticOutput.Low + fx.HapticOutput.High, 0f, "自分が Self なら当事者として Haptic が発火する");
        }

        // ── (c) ParticipantsOnly + 自分が Target → 発火 ──

        [Test]
        public void ParticipantsOnly_LocalIsTarget_Fires()
        {
            var fx = NewFixture();
            var target = NewActor("Target");
            fx.Bridge.SetNetId(target.transform, 222UL);
            fx.Bridge.SetLocalPlayerObject(target.transform, true);

            var hapticId = RegisterHaptic(fx);
            var tracks = new[]
            {
                MakeTrack(TrackKind.HitStop, PresentationEffectScope.ParticipantsOnly),
                MakeTrack(TrackKind.CameraShake, PresentationEffectScope.ParticipantsOnly),
                MakeTrack(TrackKind.Haptic, PresentationEffectScope.ParticipantsOnly, AssetRef.From(new HapticId(hapticId, AssetType.Haptics))),
            };
            var data = CreateData(tracks);
            var presId = RegisterPresentation(fx, data);

            // Self は誰か別の(未登録の)netId、Target が自分。
            ReceivePlay(fx, presId, handleNetKey: 3u, selfNetId: 333UL, targetNetId: 222UL);
            fx.Haptics.Tick(0.01f);

            Assert.AreEqual(0f, fx.Time.TimeScale, "自分が Target なら当事者として HitStop が発火する");
            Assert.AreEqual(1, fx.CameraFx.ActiveCount, "自分が Target なら当事者として CameraShake が発火する");
            Assert.Greater(fx.HapticOutput.Low + fx.HapticOutput.High, 0f, "自分が Target なら当事者として Haptic が発火する");
        }

        // ── (d) ParticipantsOnly + 第三者 → 発火しない ──

        [Test]
        public void ParticipantsOnly_ThirdParty_DoesNotFire()
        {
            var fx = NewFixture();
            var self = NewActor("Self");
            var target = NewActor("Target");
            fx.Bridge.SetNetId(self.transform, 111UL);
            fx.Bridge.SetNetId(target.transform, 222UL);
            // どちらも SetLocalPlayerObject していない = このピアは当事者ではない第三者(観戦者)。

            var hapticId = RegisterHaptic(fx);
            var tracks = new[]
            {
                MakeTrack(TrackKind.HitStop, PresentationEffectScope.ParticipantsOnly),
                MakeTrack(TrackKind.CameraShake, PresentationEffectScope.ParticipantsOnly),
                MakeTrack(TrackKind.Haptic, PresentationEffectScope.ParticipantsOnly, AssetRef.From(new HapticId(hapticId, AssetType.Haptics))),
            };
            var data = CreateData(tracks);
            var presId = RegisterPresentation(fx, data);

            ReceivePlay(fx, presId, handleNetKey: 4u, selfNetId: 111UL, targetNetId: 222UL);
            fx.Haptics.Tick(0.01f);

            Assert.AreEqual(1f, fx.Time.TimeScale, "第三者は ParticipantsOnly の HitStop を実行しない");
            Assert.AreEqual(0, fx.CameraFx.ActiveCount, "第三者は ParticipantsOnly の CameraShake を実行しない");
            Assert.AreEqual(0f, fx.HapticOutput.Low + fx.HapticOutput.High, "第三者は ParticipantsOnly の Haptic を実行しない");
        }

        // ── (e) ParticipantsOnly + SelfNetId/TargetNetId が両方 0 → 発火(安全側) ──

        [Test]
        public void ParticipantsOnly_BothNetIdsUnresolved_StillFires_SafeDefault()
        {
            var fx = NewFixture();

            var hapticId = RegisterHaptic(fx);
            var tracks = new[]
            {
                MakeTrack(TrackKind.HitStop, PresentationEffectScope.ParticipantsOnly),
                MakeTrack(TrackKind.CameraShake, PresentationEffectScope.ParticipantsOnly),
                MakeTrack(TrackKind.Haptic, PresentationEffectScope.ParticipantsOnly, AssetRef.From(new HapticId(hapticId, AssetType.Haptics))),
            };
            var data = CreateData(tracks);
            var presId = RegisterPresentation(fx, data);

            ReceivePlay(fx, presId, handleNetKey: 5u, selfNetId: 0UL, targetNetId: 0UL);
            fx.Haptics.Tick(0.01f);

            Assert.AreEqual(0f, fx.Time.TimeScale, "SelfNetId/TargetNetId が未解決(0)なら安全側で HitStop が発火する");
            Assert.AreEqual(1, fx.CameraFx.ActiveCount, "SelfNetId/TargetNetId が未解決(0)なら安全側で CameraShake が発火する");
            Assert.Greater(fx.HapticOutput.Low + fx.HapticOutput.High, 0f, "SelfNetId/TargetNetId が未解決(0)なら安全側で Haptic が発火する");
        }

        // ── (f) 予測再生(PredictLocal)の行為者自身は Scope に関わらず発火 ──

        [Test]
        public void ParticipantsOnly_PredictLocalActor_AlwaysFires_RegardlessOfScope()
        {
            var fx = NewFixture();
            // 行為者自身のピア。PredictLocal の Instance は PlayedViaNetworkReceive=false のため、
            // Self/Target の netId を一切登録していなくても(=第三者と同じ「未解決」条件でも)必ず発火する。

            var hapticId = RegisterHaptic(fx);
            var tracks = new[]
            {
                MakeTrack(TrackKind.HitStop, PresentationEffectScope.ParticipantsOnly),
                MakeTrack(TrackKind.CameraShake, PresentationEffectScope.ParticipantsOnly),
                MakeTrack(TrackKind.Haptic, PresentationEffectScope.ParticipantsOnly, AssetRef.From(new HapticId(hapticId, AssetType.Haptics))),
            };
            var data = CreateData(tracks);
            data.PredictLocal = true;
            RegisterPresentation(fx, data);

            var handle = fx.Manager.PlayData(data, new PlayContext());
            fx.Haptics.Tick(0.01f);

            Assert.IsTrue(fx.Manager.IsPlaying(handle), "PredictLocal は即座にローカル再生する");
            Assert.AreEqual(0f, fx.Time.TimeScale, "予測再生の行為者自身は Scope=ParticipantsOnly でも HitStop が発火する");
            Assert.AreEqual(1, fx.CameraFx.ActiveCount, "予測再生の行為者自身は Scope=ParticipantsOnly でも CameraShake が発火する");
            Assert.Greater(fx.HapticOutput.Low + fx.HapticOutput.High, 0f, "予測再生の行為者自身は Scope=ParticipantsOnly でも Haptic が発火する");
        }
    }
}
