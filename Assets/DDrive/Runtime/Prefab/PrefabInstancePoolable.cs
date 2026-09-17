using System;
using DDrive.Foundation.Pool;
using UnityEngine;

namespace DDrive.Runtime.Prefab
{
    // 2026-09-17 レビュー対応(P1-1 恒久策) — Pool から強制回収(上限超過による Priority 回収)された
    // ときに PrefabsManager の内部台帳を追従させるための橋渡し。VfxInstancePoolable /
    // SeSourcePoolable と同じ作り([02_core_framework.md] §6)。Spawn 時に Instance のルートへ
    // 自動で付与される。
    internal sealed class PrefabInstancePoolable : MonoBehaviour, IPoolable
    {
        public Action OnReturnedToPool;

        public void OnReturn()
        {
            OnReturnedToPool?.Invoke();
            OnReturnedToPool = null;
        }
    }
}
