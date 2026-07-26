using DDrive.Foundation.Identity;
using DDrive.Foundation.Pause;

namespace DDrive.Foundation.Manager
{
    // 具象 API(Play/Spawn/Stop/Preload)は種別ごとに定義するが、GameLoop から見た共通面はこれだけ。
    public interface IAssetManager
    {
        AssetType Type { get; }
        void Tick(float dt);
        void OnPause(PauseChannel channel, bool paused);
        void StopAll(StopReason reason);
        void OnSceneUnload();
    }
}
