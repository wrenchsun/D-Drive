using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using DDrive.Runtime.Haptics;
using NUnit.Framework;
using UnityEngine;
using HapticId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Haptics.HapticMarker>;

namespace DDrive.Tests.Runtime
{
    // [16_camera_haptics.md] Part B / [11_tasks.md] 5-2b — Max 合成(加算しない)・GlobalScale・Pause・
    // パッド未接続時の no-op を検証する。実カタログ・実 GameData には触れない。
    public class HapticsManagerTests
    {
        private sealed class FakeHapticOutput : IHapticOutput
        {
            public float Low;
            public float High;
            public int CallCount;

            public void SetMotors(float low, float high)
            {
                Low = low;
                High = high;
                CallCount++;
            }
        }

        private FakeAssetLoader _loader;
        private AssetRegistry _registry;
        private FakeHapticOutput _output;
        private HapticsManager _manager;
        private ulong _nextId = 960001;

        [SetUp]
        public void SetUp()
        {
            _loader = new FakeAssetLoader();
            _registry = new AssetRegistry(_loader);
            _output = new FakeHapticOutput();
            _manager = new HapticsManager(_registry, _output);
        }

        // ValueDef.Constant01 は Time.Value=0(=Duration 0)のままなので、そのまま使うと初回 Tick で
        // 即座に IsExpired 判定されてしまう。テストでは十分長い Duration を明示する。
        private static HapticsData CreateData(ulong id, float lowConstant, float highConstant, float durationSec = 1f)
        {
            var timeDef = new TimeDef { Mode = TimeMode.Duration, Value = durationSec, SpeedScale = 1f };
            var data = ScriptableObject.CreateInstance<HapticsData>();
            data.Id = id;
            data.LowFreq = new ValueDef { Mode = ValueMode.Constant, Constant = lowConstant, Time = timeDef };
            data.HighFreq = new ValueDef { Mode = ValueMode.Constant, Constant = highConstant, Time = timeDef };
            return data;
        }

        [Test]
        public void PlayData_SingleInstance_OutputsItsMotorValues()
        {
            var data = CreateData(_nextId++, 0.4f, 0.6f);
            _manager.PlayData(data);

            _manager.Tick(0.01f);

            Assert.AreEqual(0.4f, _output.Low, 1e-4f);
            Assert.AreEqual(0.6f, _output.High, 1e-4f);
        }

        [Test]
        public void PlayData_OverlappingInstances_ComposeWithMax_NotSum()
        {
            // 同時再生で飽和しない(AC): 加算だと 0.9 になるところ、Max 合成なら 0.6 のまま。
            var weak = CreateData(_nextId++, 0.3f, 0.6f);
            var strong = CreateData(_nextId++, 0.6f, 0.2f);

            _manager.PlayData(weak);
            _manager.PlayData(strong);
            _manager.Tick(0.01f);

            Assert.AreEqual(0.6f, _output.Low, 1e-4f, "Low は 2 つのうち大きい方(0.6)");
            Assert.AreEqual(0.6f, _output.High, 1e-4f, "High も 2 つのうち大きい方(0.6)。加算(0.8)にはならない");
        }

        [Test]
        public void SetGlobalScale_Zero_ProducesZeroOutput()
        {
            var data = CreateData(_nextId++, 1f, 1f);
            _manager.PlayData(data);
            _manager.SetGlobalScale(0f);

            _manager.Tick(0.01f);

            Assert.AreEqual(0f, _output.Low);
            Assert.AreEqual(0f, _output.High);
        }

        [Test]
        public void OnPause_OutputsZero_AndDoesNotAdvance()
        {
            var data = CreateData(_nextId++, 1f, 1f);
            _manager.PlayData(data);

            _manager.OnPause(PauseChannel.Gameplay, true);

            Assert.AreEqual(0f, _output.Low, "Pause 直後にモーターを 0 に戻す");
            Assert.AreEqual(0f, _output.High);

            _manager.Tick(10f); // Pause 中は進行せず、再合成もしない
            Assert.AreEqual(0f, _output.Low);
            Assert.AreEqual(0f, _output.High);

            _manager.OnPause(PauseChannel.Gameplay, false);
            _manager.Tick(0.01f);
            Assert.AreEqual(1f, _output.Low, "Pause 解除後は再生中の Instance がまだ生きていて出力が戻る");
        }

        [Test]
        public void StopAll_StopReason_ResetsOutputImmediately()
        {
            var data = CreateData(_nextId++, 1f, 1f);
            _manager.PlayData(data);
            _manager.Tick(0.01f);
            Assert.Greater(_output.Low, 0f);

            _manager.StopAll(StopReason.SceneUnload);

            Assert.AreEqual(0f, _output.Low);
            Assert.AreEqual(0f, _output.High);
        }

        [Test]
        public void ResetOutput_ForcesMotorsToZero_WithoutRemovingInstances()
        {
            var data = CreateData(_nextId++, 1f, 1f);
            var handle = _manager.PlayData(data);
            _manager.Tick(0.01f);

            _manager.ResetOutput();

            Assert.AreEqual(0f, _output.Low);
            Assert.IsTrue(_manager.IsPlaying(handle), "ResetOutput は Instance を止めない(アプリ終了/フォーカス喪失用の緊急停止)");
        }

        [Test]
        public void Facade_UnboundHaptics_PlayReturnsInvalidHandle_AndIsNoOp()
        {
            Haptics.Bind(null);

            var handle = Haptics.Play(default(HapticId));

            Assert.IsFalse(handle.IsPlaying());
            Assert.DoesNotThrow(() =>
            {
                handle.Stop();
                Haptics.SetGlobalScale(0.5f);
                Haptics.StopAll();
            });
        }
    }
}
