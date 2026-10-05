using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Runtime.Loop;
using UnityEngine;

namespace ExternalPackage.Fake
{
    // 持ち込み先ガイド(docs/50_consumer_guide/operation.html「禁止 API の指摘への対処」)のコード例と同じ形。
    // OnEnable で登録(Bootstrap より先に OnEnable が走ったときに備えて Start でもう一度試し、それでも無ければ警告を 1 行出す)・
    // OnDisable で必ず解除・Instance / Loop が null のときは登録しない。
    public sealed class ExternalGameTimeBehaviour : MonoBehaviour, IAssetManager
    {
        public int TickCount;
        public float LastDt;
        public bool Registered => _loop != null;

        private GameLoop _loop;

        public AssetType Type => AssetType.None;

        private void OnEnable() => TryRegister();

        private void Start()
        {
            // 実行順が Bootstrap(-1000)より前などで OnEnable の時点では Bootstrap が無かった場合の再試行。
            if (_loop == null && !TryRegister())
            {
                Debug.LogWarning("[ExternalGameTime] DDriveRuntimeBootstrap が無いため GameLoop に登録できませんでした(Tick は来ません)。", this);
            }
        }

        private bool TryRegister()
        {
            if (_loop != null)
            {
                return true;
            }

            var boot = DDriveRuntimeBootstrap.Instance;
            if (boot == null || boot.Loop == null)
            {
                return false;
            }

            _loop = boot.Loop.GameLoop;
            _loop.Register(this);
            return true;
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
