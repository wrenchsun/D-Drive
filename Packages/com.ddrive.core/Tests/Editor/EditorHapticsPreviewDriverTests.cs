using System.Reflection;
using DDrive.Editor.CameraFx;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Values;
using DDrive.Runtime.Haptics;
using NUnit.Framework;
using UnityEditor;
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

        // P5 レビュー第 1 弾 5-4 追補(b、2026-09-14) — PresentationEditor の統合プレビューから
        // TimeService(HitStop 中は TimeScale=0)を渡された場合、自前の Unscaled dt(EditorApplication.update
        // 由来)にも ScaledDeltaTime を掛けてから Tick するようになったことを確認する。
        // EditorTick は EditorApplication.timeSinceStartup 由来の実時間で dt を測るため、直接は待てない。
        // 代わりに private の _lastTick を「十分前」に書き換えてから private の EditorTick を
        // リフレクション経由で 1 回呼び、Mathf.Clamp(..., 0f, 0.25f) により dt が確定的に 0.25s になる
        // ことを利用する(TimeScale=0 ならスケール後は 0 になり、短い Duration の Instance でも失効しない)。
        [Test]
        public void HitStop_ScalesEditorTick_ToZero_PreventsShortInstanceFromExpiring()
        {
            var time = new TimeService();
            time.HitStop(10f, scale: 0f); // このテストの間はずっと HitStop 中(TimeScale=0)

            var driver = new EditorHapticsPreviewDriver(output: _output, timeService: time);
            HapticsData shortData = null;
            try
            {
                shortData = ScriptableObject.CreateInstance<HapticsData>();
                shortData.LowFreq = new ValueDef
                {
                    Mode = ValueMode.Constant,
                    Constant = 1f,
                    Time = new TimeDef { Mode = TimeMode.Duration, Value = 0.1f, SpeedScale = 1f },
                };
                shortData.HighFreq = ValueDef.Constant01(0f);

                var handle = driver.Play(shortData);
                Assert.IsTrue(driver.Manager.IsPlaying(handle));

                SetLastTickSecondsAgo(driver, 10.0); // dt は Mathf.Clamp で 0.25s に確定する

                InvokePrivateVoid(driver, "EditorTick");

                Assert.IsTrue(driver.Manager.IsPlaying(handle),
                    "HitStop(TimeScale=0)中は 0.25s 分の Unscaled dt もスケールされて 0 になるため、" +
                    "Duration=0.1s の Instance でも失効しない");
            }
            finally
            {
                driver.Dispose();
                if (shortData != null)
                {
                    UnityEngine.Object.DestroyImmediate(shortData);
                }
            }
        }

        [Test]
        public void WithoutTimeService_EditorTick_UsesRawDt_ExpiresShortInstance()
        {
            // 上のテストの対照実験: timeService を渡さない(既定)場合は従来どおり Unscaled のままなので、
            // 同じ 0.25s 分の dt で Duration=0.1s の Instance は失効する。
            var shortData = ScriptableObject.CreateInstance<HapticsData>();
            shortData.LowFreq = new ValueDef
            {
                Mode = ValueMode.Constant,
                Constant = 1f,
                Time = new TimeDef { Mode = TimeMode.Duration, Value = 0.1f, SpeedScale = 1f },
            };
            shortData.HighFreq = ValueDef.Constant01(0f);

            var handle = _driver.Play(shortData);
            Assert.IsTrue(_driver.Manager.IsPlaying(handle));

            SetLastTickSecondsAgo(_driver, 10.0);
            InvokePrivateVoid(_driver, "EditorTick");

            Assert.IsFalse(_driver.Manager.IsPlaying(handle), "timeService 無しは従来どおり Unscaled なので失効する");
            UnityEngine.Object.DestroyImmediate(shortData);
        }

        private static void SetLastTickSecondsAgo(EditorHapticsPreviewDriver driver, double secondsAgo)
        {
            var field = typeof(EditorHapticsPreviewDriver).GetField("_lastTick", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "EditorHapticsPreviewDriver._lastTick が見つかりません(実装が変わった場合はテストを追従させてください)");
            field.SetValue(driver, EditorApplication.timeSinceStartup - secondsAgo);
        }

        private static void InvokePrivateVoid(object instance, string methodName)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{instance.GetType().Name}.{methodName} が見つかりません(実装が変わった場合はテストを追従させてください)");
            method.Invoke(instance, null);
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
