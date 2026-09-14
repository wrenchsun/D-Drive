using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using NUnit.Framework;
using Unity.PerformanceTesting;

namespace DDrive.Tests.Performance
{
    // [11_tasks.md] 6-2 / [02_core_framework.md] §14 — GameLoopDriver.Update() の本体
    // (TimeService.Tick → GameLoop.Tick で全 IAssetManager へディスパッチ)が 0 alloc であることを
    // 検証する。実 MonoBehaviour.Update を実フレームで待つと Time.unscaledDeltaTime が実行環境依存になり
    // CI で不安定になるため、Update() と同じ呼び出し列を直接測る(個々の Manager 自身の Tick は
    // Vfx/Se/CameraFx/Haptics/Presentation の各 *AllocTests で別途検証している)。
    public class GameLoopDriverAllocTests
    {
        private sealed class NoopManager : IAssetManager
        {
            public AssetType Type => AssetType.Se;
            public void Tick(float dt)
            {
            }

            public void OnPause(PauseChannel channel, bool paused)
            {
            }

            public void StopAll(StopReason reason)
            {
            }

            public void OnSceneUnload()
            {
            }
        }

        [Test, Performance]
        public void OneFrame_TimeServiceAndGameLoopTick_AllocatesNothing()
        {
            var time = new TimeService();
            var loop = new GameLoop();
            for (var i = 0; i < 3; i++)
            {
                loop.Register(new NoopManager());
            }

            AllocProbe.AssertZeroAlloc(
                "GameLoopDriver.OneFrame",
                warmup: () =>
                {
                    time.Tick(0.016f);
                    loop.Tick(time.ScaledDeltaTime(0.016f));
                },
                measured: () =>
                {
                    time.Tick(0.016f);
                    loop.Tick(time.ScaledDeltaTime(0.016f));
                });
        }
    }
}
