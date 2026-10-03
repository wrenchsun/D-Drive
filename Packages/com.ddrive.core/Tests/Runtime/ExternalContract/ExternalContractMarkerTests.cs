using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Net;
using DDrive.Runtime.Presentation;
using ExternalPackage.Fake;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] 外部拡張の契約(マーカー)。E-20(FC-4、docs/51 §4.5)。
    // 外部アセンブリの Marker + ICutsceneMarker(ExternalPackage.Fake)を D-Drive の公開 API だけで再生する。
    public class ExternalContractMarkerTests
    {
        private const double Duration = 10.0;

        private readonly List<Object> _cleanup = new();
        private GameObject _actor;

        [SetUp]
        public void SetUp()
        {
            ExternalFireMarker.ClearCalls();
            _actor = Own(new GameObject("ExternalMarkerActor"));
            _actor.AddComponent<Animator>();
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
            ExternalFireMarker.ClearCalls();
        }

        private T Own<T>(T o) where T : Object
        {
            _cleanup.Add(o);
            return o;
        }

        // 1.0 / 2.0 / 3.0 秒に ICutsceneMarker を置く(2.0 は throwAt2 のとき例外を投げる)。
        // 加えて 1.5 秒に ICutsceneMarker を実装しない外部 Marker(従来どおり無視される)を置く。
        private CutsceneData BuildData(bool throwAt2 = false)
        {
            var timeline = Own(ScriptableObject.CreateInstance<TimelineAsset>());
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = Duration;
            var track = timeline.CreateTrack<ExternalProbeTrack>(null, "ExtMarkers");
            track.CreateMarker<ExternalFireMarker>(1.0);
            track.CreateMarker<ExternalProbeMarker>(1.5);
            var second = track.CreateMarker<ExternalFireMarker>(2.0);
            second.Throw = throwAt2;
            track.CreateMarker<ExternalFireMarker>(3.0);

            var data = Own(ScriptableObject.CreateInstance<CutsceneData>());
            data.DisplayName = "ExternalMarkerCutscene";
            data.Timeline = timeline;
            data.Origin = CutsceneOrigin.World;
            data.Skip = CutsceneSkip.Immediate;
            data.Bindings = new CutsceneBinding[0];
            return data;
        }

        private static CutsceneManager NewManager() => new(new AssetRegistry(new ExternalContractLoader()));

        private PlayContext Ctx() => new() { Self = _actor.transform };

        // E-20: 時刻を跨いだ Tick で 1 回だけ Fire(跨いでいない Tick・巻き戻しでは呼ばれない)。ICutsceneMarker を実装しない
        //       外部 Marker は従来どおり黙って無視される。文脈は MarkerTime / Elapsed / Director / 有効な Handle / IsEditPreview=false。
        [Test]
        public void E20_ExternalMarker_FiresOncePerCrossing_WithContext()
        {
            var manager = NewManager();
            var handle = manager.PlayData(BuildData(), Ctx());
            Assert.AreEqual(0, ExternalFireMarker.Calls.Count);

            manager.Tick(0.5f);
            Assert.AreEqual(0, ExternalFireMarker.Calls.Count, "まだ 1.0 秒に達していない");

            manager.Tick(0.6f);
            Assert.AreEqual(1, ExternalFireMarker.Calls.Count, "1.0 秒を跨いだ Tick で 1 回");
            var call = ExternalFireMarker.Calls[0];
            Assert.AreEqual(1.0, call.MarkerTime, 1e-6);
            Assert.AreEqual(1.1, call.Elapsed, 1e-3);
            Assert.IsTrue(call.HasDirector);
            Assert.IsTrue(call.HasHandle);
            Assert.IsFalse(call.IsEditPreview);

            manager.Tick(0.1f);
            Assert.AreEqual(1, ExternalFireMarker.Calls.Count, "同じマーカーは再発火しない");

            manager.Seek(handle, 0f);
            manager.Tick(0.1f);
            Assert.AreEqual(1, ExternalFireMarker.Calls.Count, "巻き戻しても通過済みのカーソルは戻らず再発火しない");

            manager.Cancel(handle);
        }

        // E-20: 1 Tick で複数跨いだら時刻順に全部呼ばれる。
        [Test]
        public void E20_ExternalMarkers_CrossedInOneTick_FireInTimeOrder()
        {
            var manager = NewManager();
            var handle = manager.PlayData(BuildData(), Ctx());

            manager.Tick(3.5f);
            Assert.AreEqual(3, ExternalFireMarker.Calls.Count);
            Assert.AreEqual(1.0, ExternalFireMarker.Calls[0].MarkerTime, 1e-6);
            Assert.AreEqual(2.0, ExternalFireMarker.Calls[1].MarkerTime, 1e-6);
            Assert.AreEqual(3.0, ExternalFireMarker.Calls[2].MarkerTime, 1e-6);

            manager.Cancel(handle);
        }

        // E-20: Seek / Skip で跨いだ分は無音(Seek 後に Tick しても過去の分は呼ばれない)。
        [Test]
        public void E20_SeekAndSkip_AreSilent()
        {
            var manager = NewManager();
            var handle = manager.PlayData(BuildData(), Ctx());

            manager.Seek(handle, 2.5f);
            Assert.AreEqual(0, ExternalFireMarker.Calls.Count, "Seek で跨いだ 1.0 / 2.0 は無音");

            manager.Tick(0.6f);
            Assert.AreEqual(1, ExternalFireMarker.Calls.Count, "Seek 後に新しく跨いだ 3.0 だけ呼ばれる");
            Assert.AreEqual(3.0, ExternalFireMarker.Calls[0].MarkerTime, 1e-6);

            manager.Cancel(handle);

            ExternalFireMarker.ClearCalls();
            var handle2 = manager.PlayData(BuildData(), Ctx());
            manager.Skip(handle2);
            Assert.AreEqual(0, ExternalFireMarker.Calls.Count, "Skip で跨いだ分は無音");
            manager.Cancel(handle2);
        }

        // E-20: CutsceneDirectorContext.FireEnabled が false の間は呼ばれない(跨いだ分は無音で消費され、true に戻しても再発火しない)。
        [Test]
        public void E20_FireEnabledFalse_DoesNotFire()
        {
            var manager = NewManager();
            var handle = manager.PlayData(BuildData(), Ctx());
            var context = Object.FindFirstObjectByType<CutsceneDirectorContext>();
            Assert.IsNotNull(context, "再生中の Director には CutsceneDirectorContext が付いている");

            context.FireEnabled = false;
            manager.Tick(1.5f);
            Assert.AreEqual(0, ExternalFireMarker.Calls.Count, "FireEnabled=false では呼ばれない");

            context.FireEnabled = true;
            manager.Tick(0.1f);
            Assert.AreEqual(0, ExternalFireMarker.Calls.Count, "無音で通過した 1.0 は true に戻しても再発火しない");
            manager.Tick(2.0f);
            Assert.AreEqual(2, ExternalFireMarker.Calls.Count, "戻したあとに跨いだ 2.0 / 3.0 は呼ばれる");

            manager.Cancel(handle);
        }

        // E-20: 例外を投げるマーカーがあっても、後続のマーカーと Tick は継続する(例外は Debug.LogException で隔離)。
        [Test]
        public void E20_ThrowingMarker_IsIsolated_OthersAndTickContinue()
        {
            var manager = NewManager();
            var handle = manager.PlayData(BuildData(throwAt2: true), Ctx());

            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("ExternalFireMarker"));
            manager.Tick(3.5f);

            Assert.AreEqual(3, ExternalFireMarker.Calls.Count, "2.0 の例外のあとも 3.0 が呼ばれる");
            Assert.IsTrue(manager.IsPlaying(handle), "Tick は止まらない");
            manager.Tick(0.1f);
            Assert.IsTrue(manager.IsPlaying(handle));

            manager.Cancel(handle);
        }

        // E-20(定常経路): 外部マーカーを跨ぐ Tick は、跨がない Tick と同じ割り当て量(外部マーカー発火のための割り当て 0)。
        [Test]
        public void E20_CrossingExternalMarker_AllocatesNothingBeyondPlainTick()
        {
            var manager = NewManager();
            var handle = manager.PlayData(BuildData(), Ctx());
            ExternalFireMarker.Calls.Capacity = 64;

            // ウォームアップ(初回の JIT・Evaluate の遅延確保を済ませる)。0.0 → 0.5(跨がない)を数回。
            manager.Tick(0.1f);
            manager.Tick(0.1f);
            manager.Tick(0.1f);

            var before = System.GC.GetAllocatedBytesForCurrentThread();
            manager.Tick(0.1f);
            var plain = System.GC.GetAllocatedBytesForCurrentThread() - before;

            manager.Tick(0.5f); // 0.4 → 0.9。まだ跨がない
            before = System.GC.GetAllocatedBytesForCurrentThread();
            manager.Tick(0.2f); // 0.9 → 1.1: 1.0 を跨ぐ
            var crossing = System.GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(1, ExternalFireMarker.Calls.Count, "跨いだ Tick で 1 回呼ばれた");
            Assert.LessOrEqual(crossing, plain, $"跨ぐ Tick の割り当て({crossing} B)が跨がない Tick({plain} B)を超えない");

            manager.Cancel(handle);
        }

        // E-20: ネット受信側(遅延復元 = elapsedSeek > 0)で始まった再生では、既に過ぎたマーカーは無音(Late Join)。
        //       発火は各クライアントのローカル処理(受信側が自分の Tick で跨いだ分だけ呼ぶ)。
        [Test]
        public void E20_LateJoin_PastMarkersAreSilent_FutureOnesFireLocally()
        {
            const ulong CutId = 940101UL;
            var loader = new ExternalContractLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new ExternalContractBridge { IsServer = false, LocalClientId = 1UL, NetworkTime = 2.5 };
            var manager = new CutsceneManager(registry, netBridge: bridge);

            var data = BuildData();
            data.Id = CutId;
            var flags = data.Flags;
            flags.Net = NetMode.Cosmetic;
            data.Flags = flags;
            ExternalContractRegistry.Register(loader, registry, AssetType.Cutscene, data);

            bridge.InjectReceive(0UL, new CutscenePlayMsg { CutId = CutId, HandleNetKey = 0x55555555u, StartNetTime = 0.0 });
            var active = manager.DebugActiveHandles();
            Assert.AreEqual(1, active.Count, "受信側で再生が始まる");
            Assert.AreEqual(0, ExternalFireMarker.Calls.Count, "2.5 秒からの途中参加: 1.0 / 2.0 は無音");

            manager.Tick(0.6f);
            Assert.AreEqual(1, ExternalFireMarker.Calls.Count, "以降に跨いだ 3.0 だけ受信側のローカル Tick で呼ばれる");
            Assert.AreEqual(3.0, ExternalFireMarker.Calls[0].MarkerTime, 1e-6);

            manager.Cancel(active[0]);
        }
    }
}
