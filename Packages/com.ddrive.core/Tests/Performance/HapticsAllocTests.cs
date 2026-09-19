using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using DDrive.Runtime.Haptics;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;

namespace DDrive.Tests.Performance
{
    // [11_tasks.md] 6-2 / [16_camera_haptics.md] Part B — HapticsManager.Tick(毎フレーム、
    // Max 合成含む)が 0 alloc であることを検証する。
    public class HapticsAllocTests
    {
        private sealed class NullHapticOutput : IHapticOutput
        {
            public void SetMotors(float low, float high)
            {
            }
        }

        private AssetRegistry _registry;
        private HapticsManager _manager;

        [SetUp]
        public void SetUp()
        {
            _registry = new AssetRegistry(new NullAssetLoader());
            _manager = new HapticsManager(_registry, new NullHapticOutput());
        }

        [TearDown]
        public void TearDown()
        {
            Haptics.Bind(null);
        }

        // ValueDef.Constant01 は Time.Value=0(=Duration 0)のままだと初回 Tick で即座に IsExpired
        // 判定されるため、十分長い Duration を明示する(既存の HapticsManagerTests と同じ理由)。
        private static HapticsData CreateData(ulong id)
        {
            var timeDef = new TimeDef { Mode = TimeMode.Duration, Value = 1000f, SpeedScale = 1f };
            var data = ScriptableObject.CreateInstance<HapticsData>();
            data.Id = id;
            data.LowFreq = new ValueDef { Mode = ValueMode.Constant, Constant = 0.5f, Time = timeDef };
            data.HighFreq = new ValueDef { Mode = ValueMode.Constant, Constant = 0.5f, Time = timeDef };
            return data;
        }

        [Test, Performance]
        public void Tick_ActiveHaptic_AllocatesNothing()
        {
            var data = CreateData(1);
            _manager.PlayData(data);

            AllocProbe.AssertZeroAlloc(
                "Haptics.Tick",
                warmup: () => _manager.Tick(0.016f),
                measured: () => _manager.Tick(0.016f));
        }
    }
}
