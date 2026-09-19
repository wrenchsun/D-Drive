using System;
using DDrive.Foundation.Pool;
using UnityEngine;

namespace DDrive.Runtime.Vfx
{
    // Pool から強制回収(上限超過による Priority 回収)された時に VfxManager の内部台帳を
    // 追従させつつ、残留パーティクル/Trail をリセットする橋渡し([04_vfx.md] §3 内部実装)。
    // VFX Prefab のルートに自動で付与される。
    internal sealed class VfxInstancePoolable : MonoBehaviour, IPoolable
    {
        public Action OnReturnedToPool;
        public ParticleSystem[] ParticleSystems;

        public void OnReturn()
        {
            if (ParticleSystems != null)
            {
                foreach (var ps in ParticleSystems)
                {
                    if (ps != null)
                    {
                        ps.Clear(true);
                    }
                }
            }

            OnReturnedToPool?.Invoke();
            OnReturnedToPool = null;
        }
    }
}
