using System;
using DDrive.Foundation.Pool;
using UnityEngine;

namespace DDrive.Runtime.Audio
{
    // Pool から強制回収(上限超過による Priority 回収)された時に AudioManager の
    // 内部台帳を追従させるための橋渡し。SE ソース Prefab に自動で付与される。
    internal sealed class SeSourcePoolable : MonoBehaviour, IPoolable
    {
        public Action OnReturnedToPool;

        public void OnReturn()
        {
            OnReturnedToPool?.Invoke();
            OnReturnedToPool = null;
        }
    }
}
