using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Loop;
using DDrive.Runtime.Net;
using DDrive.Runtime.Presentation;
using ExternalPackage.Fake;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] 外部拡張の契約(Timeline)。E-2(A-2)・E-3(A-3 + R-3)・E-5(A-5)。
    // 外部アセンブリの TrackAsset / PlayableAsset クリップ / Marker(ExternalPackage.Fake)を D-Drive の公開 API だけで再生・検証する。
    public class ExternalContractTimelineTests
    {
        private const double Duration = 10.0;
        private const string TrackName = "ExtHero";

        private readonly List<Object> _cleanup = new();
        private GameObject _actor;
        private Animator _animator;

        [SetUp]
        public void SetUp()
        {
            ExternalProbeLog.Reset();
            _actor = Own(new GameObject("ExternalContractActor"));
            _animator = _actor.AddComponent<Animator>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _cleanup)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _cleanup.Clear();
            ExternalProbeLog.Reset();
        }

        private T Own<T>(T o) where T : Object
        {
            _cleanup.Add(o);
            return o;
        }

        private TimelineAsset BuildTimeline()
        {
            var timeline = Own(ScriptableObject.CreateInstance<TimelineAsset>());
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = Duration;
            var track = timeline.CreateTrack<ExternalProbeTrack>(null, TrackName);
            var clip = track.CreateClip<ExternalProbeClip>();
            clip.start = 0d;
            clip.duration = Duration;
            track.CreateMarker<ExternalProbeMarker>(1.0);
            return timeline;
        }

        private CutsceneData BuildData(TimelineAsset timeline, bool bindSelf = true)
        {
            var data = Own(ScriptableObject.CreateInstance<CutsceneData>());
            data.DisplayName = "ExternalContractCutscene";
            data.Timeline = timeline;
            data.Origin = CutsceneOrigin.World;
            data.Skip = CutsceneSkip.Immediate;
            data.Bindings = bindSelf
                ? new[] { new CutsceneBinding { TrackName = TrackName, Target = CutsceneBindTarget.Self } }
                : new CutsceneBinding[0];
            return data;
        }

        private static CutsceneManager NewManager() => new(new AssetRegistry(new ExternalContractLoader()));

        private PlayContext Ctx() => new() { Self = _actor.transform };

        private static ExternalProbeLog.Frame Last() => ExternalProbeLog.Frames[ExternalProbeLog.Frames.Count - 1];

        // E-2: 外部 Track の Mixer が毎 Tick で ProcessFrame され、playerData がバインド先の Animator。
        //      Evaluate は Update(GameLoop 経由)で行われ、同じフレームの後の LateUpdate から結果を読める。
        [UnityTest]
        public IEnumerator E2_ExternalTrack_IsProcessedEveryFrame_BoundToAnimator_AndReadableInLateUpdate()
        {
            var driverGo = Own(new GameObject("ExternalContractDriver"));
            var driver = driverGo.AddComponent<GameLoopDriver>();
            var manager = NewManager();
            driver.GameLoop.Register(manager);
            var reader = _actor.AddComponent<ExternalLateFrameReader>();

            var handle = manager.PlayData(BuildData(BuildTimeline()), Ctx());
            Assert.IsTrue(manager.IsPlaying(handle));
            var baseline = ExternalProbeLog.Frames.Count;
            Assert.GreaterOrEqual(baseline, 1, "再生開始時の初回 Evaluate でも ProcessFrame が来る");

            for (var i = 0; i < 6; i++)
            {
                yield return null;
            }

            Assert.GreaterOrEqual(ExternalProbeLog.Frames.Count - baseline, 4, "Tick ごとに ProcessFrame が来る");
            foreach (var frame in ExternalProbeLog.Frames)
            {
                Assert.AreSame(_animator, frame.PlayerData, "playerData はバインド先の Animator");
            }

            for (var i = 1; i < ExternalProbeLog.Frames.Count; i++)
            {
                Assert.GreaterOrEqual(ExternalProbeLog.Frames[i].Time, ExternalProbeLog.Frames[i - 1].Time, "時刻は前進する");
            }

            Assert.Greater(Last().Time, ExternalProbeLog.Frames[0].Time);
            Assert.GreaterOrEqual(reader.ProcessedThisFrame.Count, 4);
            Assert.IsTrue(reader.ProcessedThisFrame.TrueForAll(v => v), "同じフレームの LateUpdate 時点で、Update 内の Evaluate は済んでいる");

            manager.Cancel(handle);
            Object.DestroyImmediate(driverGo);
        }

        // E-3: Seek は即 Evaluate、Skip は末尾で Evaluate、SetSpeed は dt に乗る。
        [Test]
        public void E3_Seek_Skip_SetSpeed_FollowImmediately()
        {
            var manager = NewManager();
            var handle = manager.PlayData(BuildData(BuildTimeline()), Ctx());

            ExternalProbeLog.Reset();
            manager.Seek(handle, 2.5f);
            Assert.AreEqual(1, ExternalProbeLog.Frames.Count, "Seek は即 Evaluate する");
            Assert.AreEqual(2.5, Last().Time, 1e-4);

            manager.SetSpeed(handle, 2f);
            ExternalProbeLog.Reset();
            manager.Tick(0.25f);
            Assert.AreEqual(3.0, Last().Time, 1e-3, "速度 2 倍: 2.5 + 0.25 * 2");

            ExternalProbeLog.Reset();
            manager.Skip(handle);
            Assert.GreaterOrEqual(ExternalProbeLog.Frames.Count, 1, "Skip(Immediate)は末尾へ Evaluate する");
            Assert.AreEqual(Duration, Last().Time, 1e-3);

            manager.Cancel(handle);
        }

        // E-3 / R-3: インスタンス単位の一時停止中は Evaluate されない(現挙動を契約として固定。docs/51 U-8 = (a))。
        //            Seek は Paused でも Evaluate する。
        [Test]
        public void E3_PausedInstance_IsNotEvaluated_ButSeekStillIs()
        {
            var manager = NewManager();
            var handle = manager.PlayData(BuildData(BuildTimeline()), Ctx());
            manager.Tick(0.1f);

            manager.SetPaused(handle, true);
            ExternalProbeLog.Reset();
            manager.Tick(0.5f);
            manager.Tick(0.5f);
            Assert.AreEqual(0, ExternalProbeLog.Frames.Count, "一時停止中は Tick しても ProcessFrame が来ない");

            manager.Seek(handle, 4f);
            Assert.AreEqual(1, ExternalProbeLog.Frames.Count, "Seek は一時停止中でも Evaluate する");
            Assert.AreEqual(4.0, Last().Time, 1e-4);

            manager.SetPaused(handle, false);
            manager.Tick(0.1f);
            Assert.AreEqual(2, ExternalProbeLog.Frames.Count, "再開すると Evaluate が再び来る");
            Assert.AreEqual(4.1, Last().Time, 1e-3);

            manager.Cancel(handle);
        }

        // E-3: ネット受信側は elapsedSeek(NetworkTime - StartNetTime)の位置から始まり、初回 Evaluate がその時刻。
        [Test]
        public void E3_NetworkReceive_StartsAtElapsedSeek()
        {
            const ulong CutId = 940001UL;
            var loader = new ExternalContractLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new ExternalContractBridge { IsServer = false, LocalClientId = 1UL, NetworkTime = 3.0 };
            var manager = new CutsceneManager(registry, netBridge: bridge);

            var data = BuildData(BuildTimeline(), bindSelf: false);
            data.Id = CutId;
            var flags = data.Flags;
            flags.Net = NetMode.Cosmetic;
            data.Flags = flags;
            ExternalContractRegistry.Register(loader, registry, AssetType.Cutscene, data);

            ExternalProbeLog.Reset();
            bridge.InjectReceive(0UL, new CutscenePlayMsg { CutId = CutId, HandleNetKey = 0x44444444u, StartNetTime = 1.0 });

            var active = manager.DebugActiveHandles();
            Assert.AreEqual(1, active.Count, "受信側で再生が始まる");
            Assert.GreaterOrEqual(ExternalProbeLog.Frames.Count, 1);
            Assert.AreEqual(2.0, ExternalProbeLog.Frames[0].Time, 1e-3, "初回 ProcessFrame の時刻 = NetworkTime - StartNetTime");

            manager.Cancel(active[0]);
        }

        // E-5: 知らない Track / Clip / Marker を持つ Timeline の CutsceneDataValidator 結果は 0 件(外部拡張で警告が出ない)。
        [Test]
        public void E5_UnknownTrackClipMarker_ProducesNoValidationResults()
        {
            var timeline = BuildTimeline();
            var data = BuildData(timeline);
            var results = new CutsceneDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })).ToList();
            Assert.IsEmpty(results, string.Join(" / ", results.Select(r => r.Message)));
        }

        // E-5 の対照: 同じ検査が標準の AudioTrack には反応する(= 上の 0 件が「検査が空振り」ではないこと)。
        [Test]
        public void E5_Control_StandardAudioTrack_IsStillWarned()
        {
            var timeline = BuildTimeline();
            timeline.CreateTrack<AudioTrack>(null, "StdAudio");
            var data = BuildData(timeline);
            var results = new CutsceneDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })).ToList();
            Assert.IsNotEmpty(results);
        }
    }
}
