using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using DDrive.Runtime.Haptics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
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

        [TearDown]
        public void TearDown()
        {
            // P5 レビュー対応(2026-09-14) tests P2-3: Facade_UnboundHaptics_... が Haptics.Bind(null) を
            // テスト本体でしか呼んでいなかった(TearDown 自体が無かった)。ScenePreloadTests/TuningTests の
            // 「static facade は毎テスト後に必ず Bind(null) で戻す」流儀に揃える。
            Haptics.Bind(null);
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

        // P5 レビュー第 1 弾 追加テスト(review1_tests.md「追加すべきテスト」⑧)— Pause 復帰で
        // 複数 Instance が正しく再合成されること(Max 合成なので、弱い方が再度合成に参加しても
        // 出力自体は変わらないが、両方が IsPlaying のまま残り、出力が 0 に落ちないことを確認する)。
        [Test]
        public void OnPause_MultipleInstances_BothRemainAndRecomposeAfterResume()
        {
            var weak = CreateData(_nextId++, 0.3f, 0.2f, durationSec: 5f);
            var strong = CreateData(_nextId++, 0.7f, 0.9f, durationSec: 5f);

            var handleWeak = _manager.PlayData(weak);
            var handleStrong = _manager.PlayData(strong);
            _manager.Tick(0.01f);

            _manager.OnPause(PauseChannel.Gameplay, true);
            Assert.AreEqual(0f, _output.Low);
            Assert.AreEqual(0f, _output.High);

            _manager.Tick(1f); // Pause 中は進行も再合成もしない

            Assert.IsTrue(_manager.IsPlaying(handleWeak));
            Assert.IsTrue(_manager.IsPlaying(handleStrong));

            _manager.OnPause(PauseChannel.Gameplay, false);
            _manager.Tick(0.01f);

            Assert.AreEqual(0.7f, _output.Low, 1e-4f, "復帰後も Max 合成で強い方の値が出る");
            Assert.AreEqual(0.9f, _output.High, 1e-4f);
            Assert.IsTrue(_manager.IsPlaying(handleWeak), "弱い方の Instance も消えずに残っている");
            Assert.IsTrue(_manager.IsPlaying(handleStrong));
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

        // P5 レビュー対応(2026-09-14): フォーカス喪失中は ResetOutput の 1 回きりの 0 出力ではなく、
        // SetFocusLost(true) で「出力 0 を固定」にする。以前は Bootstrap.OnApplicationFocus が
        // ResetOutput() を 1 回呼ぶだけだったため、runInBackground=true で喪失中も Tick が回り続けると
        // 次の Tick で振動が復活してしまっていた(この Manager 単体テストで再現・回帰確認する)。
        [Test]
        public void SetFocusLost_True_KeepsOutputZero_AcrossMultipleTicks_UntilRestored()
        {
            var data = CreateData(_nextId++, 1f, 1f, durationSec: 10f);
            var handle = _manager.PlayData(data);
            _manager.Tick(0.01f);
            Assert.Greater(_output.Low, 0f, "前提: フォーカス喪失前は通常どおり出力される");

            _manager.SetFocusLost(true);
            Assert.AreEqual(0f, _output.Low, "SetFocusLost(true) の直後に 0 へ戻る");

            // runInBackground=true を想定し、フォーカス喪失中でも Tick を複数回回す。
            _manager.Tick(0.01f);
            _manager.Tick(0.01f);
            _manager.Tick(0.01f);

            Assert.AreEqual(0f, _output.Low, "フォーカス喪失中は Tick が進んでも出力が 0 に固定される");
            Assert.AreEqual(0f, _output.High);
            Assert.IsTrue(_manager.IsPlaying(handle), "Instance 自体は止めない(復帰後に自然な減衰で終わる)");

            _manager.SetFocusLost(false);
            _manager.Tick(0.01f);

            Assert.Greater(_output.Low, 0f, "フォーカス復帰後は Tick で通常どおり再合成される");
        }

        // P5 レビュー第 1 弾 追加テスト(review1_tests.md「追加すべきテスト」③)—
        // GamepadHapticOutput は Gamepad.current==null(パッド未接続。Test Runner 環境で通常そうなる)でも
        // 例外にならず no-op であることを確認する([16] Part B の既定出力先)。
        [Test]
        public void GamepadHapticOutput_NoGamepadConnected_SetMotorsIsNoOp_DoesNotThrow()
        {
            Assume.That(Gamepad.current, Is.Null, "このテストはパッド未接続の環境を前提にする");

            var output = new GamepadHapticOutput();

            Assert.DoesNotThrow(() => output.SetMotors(0.5f, 0.8f));
            Assert.DoesNotThrow(() => output.SetMotors(0f, 0f));
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
