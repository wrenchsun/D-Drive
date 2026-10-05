using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Runtime.Loop;
using UnityEngine;

namespace ExternalPackage.Fake
{
    // 持ち込み先ガイド(docs/50_consumer_guide/operation.html「禁止 API の指摘への対処」)のコード例と同じ形。
    // OnEnable で登録・OnDisable で必ず解除・Instance / Loop が null のときは何もしない。
    public sealed class ExternalGameTimeBehaviour : MonoBehaviour, IAssetManager
    {
        public int TickCount;
        public float LastDt;
        public bool Registered => _loop != null;

        private GameLoop _loop;

        public AssetType Type => AssetType.None;

        private void OnEnable()
        {
            var boot = DDriveRuntimeBootstrap.Instance;
            if (boot == null || boot.Loop == null)
            {
                return;
            }

            _loop = boot.Loop.GameLoop;
            _loop.Register(this);
        }

        private void OnDisable()
        {
            if (_loop != null)
            {
                _loop.Unregister(this);
                _loop = null;
            }
        }

        public void Tick(float dt)
        {
            TickCount++;
            LastDt = dt;
        }

        public void OnPause(PauseChannel channel, bool paused) { }
        public void StopAll(StopReason reason) { }
        public void OnSceneUnload() { }
    }
}
