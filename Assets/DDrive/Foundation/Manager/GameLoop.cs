using System.Collections.Generic;
using DDrive.Foundation.Pause;

namespace DDrive.Foundation.Manager
{
    // 全 IAssetManager の唯一の駆動源。各 Manager は自前の Update を持たない。
    public sealed class GameLoop
    {
        private readonly List<IAssetManager> _managers = new();

        public void Register(IAssetManager manager)
        {
            if (!_managers.Contains(manager))
            {
                _managers.Add(manager);
            }
        }

        public void Unregister(IAssetManager manager) => _managers.Remove(manager);

        public void Tick(float dt)
        {
            for (var i = 0; i < _managers.Count; i++)
            {
                _managers[i].Tick(dt);
            }
        }

        public void BroadcastPause(PauseChannel channel, bool paused)
        {
            for (var i = 0; i < _managers.Count; i++)
            {
                _managers[i].OnPause(channel, paused);
            }
        }

        public void StopAll(StopReason reason)
        {
            for (var i = 0; i < _managers.Count; i++)
            {
                _managers[i].StopAll(reason);
            }
        }

        public void NotifySceneUnload()
        {
            for (var i = 0; i < _managers.Count; i++)
            {
                _managers[i].OnSceneUnload();
            }
        }
    }
}
