using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;

namespace DDrive.Tests.Performance
{
    // [11_tasks.md] 6-2 / [08_presentation.md] — PresentationManager の Tick/Signal(毎フレーム
    // 通る定常経路)の 0 alloc と、Play 側の既知課題(下記コメント)を記録する。
    //
    // 6-6(Presentation/Net)が並行で同じファイルを触っているため、PresentationManager.cs 自体への
    // 修正は行っていない(タスク指示どおり最小限に留める)。
    public class PresentationAllocTests
    {
        private AssetRegistry _registry;
        private TimeService _time;
        private PresentationManager _manager;

        [SetUp]
        public void SetUp()
        {
            _registry = new AssetRegistry(new NullAssetLoader());
            _time = new TimeService();
            _manager = new PresentationManager(_registry, _time);
        }

        [TearDown]
        public void TearDown()
        {
            Presentation.Bind(null);
        }

        private static PresentationData CreateData(ulong id)
        {
            var data = ScriptableObject.CreateInstance<PresentationData>();
            data.Id = id;
            data.Tracks = System.Array.Empty<PresentationTrack>();
            data.TotalDuration = 1000f; // Tick で完了(Cleanup)しないようにする
            data.Interruptible = true;
            return data;
        }

        // Play は「1 アクションにつき 1 回」呼ばれる経路であり、毎フレーム走る Tick/Signal とは頻度が
        // 異なる。実際に毎フレーム回るのは Tick/Signal 側であるため、Play は計測ウォームアップ前に
        // 1 回だけ呼び、alloc 計測の対象には含めない。
        [Test, Performance]
        public void TickAndSignal_SingleLongLivedInstance_AllocatesNothing()
        {
            var data = CreateData(1);
            var handle = _manager.PlayData(data, new PlayContext());

            AllocProbe.AssertZeroAlloc(
                "Presentation.TickSignal",
                warmup: () =>
                {
                    _manager.Tick(0.016f);
                    _manager.Signal(handle, "unused");
                },
                measured: () =>
                {
                    _manager.Tick(0.016f);
                    _manager.Signal(handle, "unused");
                });
        }

        // 既知課題(6-2 で判明): PresentationManager.PlayLocalInternal は Play 呼び出しごとに
        // PresentationInstance(class)を 1 個 new し、その中で List<> を 7 個・R3 の Subject<> を
        // 4 個(Completed/Cancelled/Marker/TrackFired)new している。厳密な 0 alloc 化には
        // Instance・Subject のプーリングという大きな設計変更が必要で、6-6(Presentation/Net)と
        // 並行作業中のため本チケットでは対象外とする([12_review.md] §3 に既知課題として記録)。
        // ここでは Performance レポートに記録するだけに留め、CI の fail 条件にはしない。
        [Test, Performance]
        public void PlayData_RecordsAllocForKnownIssue()
        {
            var data = CreateData(2);

            AllocProbe.RecordGc("Presentation.PlayData", () =>
            {
                _manager.PlayData(data, new PlayContext());
            });
        }
    }
}
