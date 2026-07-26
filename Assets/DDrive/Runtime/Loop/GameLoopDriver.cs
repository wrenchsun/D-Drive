using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using UnityEngine;

namespace DDrive.Runtime.Loop
{
    // シーンに 1 つ置く想定の唯一の Update 入口。Manager 側は Tick 駆動のため Update を持たない。
    public sealed class GameLoopDriver : MonoBehaviour
    {
        public GameLoop GameLoop { get; } = new();
        public TimeService TimeService { get; } = new();
        public PauseService PauseService { get; } = new();

        private void Awake()
        {
            PauseService.OnPauseChanged += GameLoop.BroadcastPause;
        }

        private void Update()
        {
            var unscaledDt = Time.unscaledDeltaTime;
            TimeService.Tick(unscaledDt);
            GameLoop.Tick(TimeService.ScaledDeltaTime(unscaledDt));
        }

        private void OnDestroy()
        {
            PauseService.OnPauseChanged -= GameLoop.BroadcastPause;
            GameLoop.NotifySceneUnload();
        }
    }
}
