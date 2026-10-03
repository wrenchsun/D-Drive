using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;

namespace ExternalPackage.Fake
{
    // 外部の IAssetManager(GameLoop.Register で駆動される)。
    public sealed class ExternalCountingManager : IAssetManager
    {
        public int TickCount;
        public float LastDt;
        public int PauseCount;
        public PauseChannel LastPauseChannel;
        public bool LastPaused;
        public int StopAllCount;
        public StopReason LastStopReason;
        public int SceneUnloadCount;

        public AssetType Type => AssetType.None;

        public void Tick(float dt)
        {
            TickCount++;
            LastDt = dt;
        }

        public void OnPause(PauseChannel channel, bool paused)
        {
            PauseCount++;
            LastPauseChannel = channel;
            LastPaused = paused;
        }

        public void StopAll(StopReason reason)
        {
            StopAllCount++;
            LastStopReason = reason;
        }

        public void OnSceneUnload() => SceneUnloadCount++;
    }
}
