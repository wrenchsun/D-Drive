using DDrive.Editor.CameraFx;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Values;
using DDrive.Runtime.Haptics;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [16_camera_haptics.md] §C-2(5-2c) — HapticsEditor の「Test on Pad」(EditorHapticsPreviewDriver)。
    // [11_tasks.md] 5-2c AC「止め忘れ防止: 停止・ウィンドウを閉じる・ドメインリロード・Play Mode 切替・
    // フォーカス喪失で必ずモーター 0」を、実パッドに依存せず Fake IHapticOutput で検証する。
    public class EditorHapticsPreviewDriverTests
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

        private FakeHapticOutput _output;
        private EditorHapticsPreviewDriver _driver;

        [SetUp]
        public void SetUp()
        {
            _output = new FakeHapticOutput();
            _driver = new EditorHapticsPreviewDriver(output: _output);
        }

        [TearDown]
        public void TearDown()
        {
            _driver.Dispose();
        }

        private static HapticsData CreateData()
        {
            var data = ScriptableObject.CreateInstance<HapticsData>();
            data.LowFreq = new ValueDef
            {
                Mode = ValueMode.Parametric,
                Parametric = EaseDef.Named(Ease.Linear),
                From = 1f,
                To = 1f,
                Time = new TimeDef { Mode = TimeMode.Duration, Value = 1f, SpeedScale = 1f },
                Loop = LoopMode.Once,
            };
            data.HighFreq = ValueDef.Constant01(0.5f);
            return data;
        }

        [Test]
        public void Play_OutputsNonZeroMotors_ViaFakeOutput()
        {
            _driver.Play(CreateData());
            _driver.Tick(0.016f);

            Assert.Greater(_output.Low, 0f);
            Assert.Greater(_output.High, 0f);
        }

        [Test]
        public void ResetAndStop_ZeroesOutput_AndClearsActiveInstance()
        {
            var handle = _driver.Play(CreateData());
            _driver.Tick(0.016f);
            Assert.IsTrue(_driver.Manager.IsPlaying(handle));

            _driver.ResetAndStop();

            Assert.AreEqual(0f, _output.Low);
            Assert.AreEqual(0f, _output.High);
            Assert.IsFalse(_driver.Manager.IsPlaying(handle));
        }

        [Test]
        public void Dispose_ZeroesOutput_EvenWhilePlaying()
        {
            // ウィンドウを閉じたときに必ずモーターが 0 になることを検証する(止め忘れ防止)。
            _driver.Play(CreateData());
            _driver.Tick(0.016f);

            _driver.Dispose();

            Assert.AreEqual(0f, _output.Low);
            Assert.AreEqual(0f, _output.High);
        }

        [Test]
        public void ResetAndStop_StopsTicking_SoOutputStaysZeroAfterwards()
        {
            // Tick を止めずに出力だけ 0 にすると、次の Tick で Instance が再合成して上書きしてしまう。
            // ResetAndStop は台帳もクリアするため、その後 Tick を呼んでも 0 のままであることを確認する。
            _driver.Play(CreateData());
            _driver.Tick(0.016f);

            _driver.ResetAndStop();
            _driver.Tick(0.016f);

            Assert.AreEqual(0f, _output.Low);
            Assert.AreEqual(0f, _output.High);
        }
    }
}
