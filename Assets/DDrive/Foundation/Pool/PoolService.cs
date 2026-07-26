using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Foundation.Pool
{
    // Rent/Return/Prewarm/上限回収を担う共通プール。走査は for/foreach のみ(LINQ 禁止)。
    public sealed class PoolService : IPoolService
    {
        private sealed class Pool
        {
            public readonly Stack<GameObject> Free = new();
            public readonly List<PooledObject> Active = new();
            public int MaxCount = int.MaxValue;
            public bool Persistent = true;
        }

        private readonly Dictionary<GameObject, Pool> _pools = new();

        // IPoolService には無いが、上限超過時の Priority 回収(AC)を機能させるために
        // 呼び出し側(Manager)が AssetFlags.Pool.MaxCount を渡して設定する。
        public void SetLimit(GameObject prefab, int maxCount, bool persistent = true)
        {
            var pool = GetOrCreatePool(prefab);
            pool.MaxCount = maxCount;
            pool.Persistent = persistent;
        }

        public void Prewarm(GameObject prefab, int count)
        {
            var pool = GetOrCreatePool(prefab);
            for (var i = 0; i < count; i++)
            {
                var go = Object.Instantiate(prefab);
                go.SetActive(false);
                pool.Free.Push(go);
            }
        }

        public PooledObject Rent(GameObject prefab)
        {
            var pool = GetOrCreatePool(prefab);

            GameObject go;
            if (pool.Free.Count > 0)
            {
                go = pool.Free.Pop();
            }
            else if (pool.Active.Count >= pool.MaxCount)
            {
                var evicted = FindLowestPriority(pool.Active);
                if (evicted == null)
                {
                    Debug.LogWarning("[DDrive] Pool at capacity with nothing to reclaim; instantiating over limit.");
                    go = Object.Instantiate(prefab);
                }
                else
                {
                    ForceReturn(pool, evicted);
                    go = evicted.GameObject;
                }
            }
            else
            {
                go = Object.Instantiate(prefab);
            }

            go.SetActive(true);
            var pooled = new PooledObject(go, prefab);
            pool.Active.Add(pooled);
            return pooled;
        }

        public void Return(PooledObject obj)
        {
            if (obj == null || !_pools.TryGetValue(obj.PrefabKey, out var pool))
            {
                return;
            }

            ForceReturn(pool, obj);
        }

        public void Clear(PoolScope scope)
        {
            foreach (var pool in _pools.Values)
            {
                if (scope == PoolScope.Scene && pool.Persistent)
                {
                    continue;
                }

                foreach (var active in pool.Active)
                {
                    if (active.GameObject != null)
                    {
                        Object.Destroy(active.GameObject);
                    }
                }

                pool.Active.Clear();

                while (pool.Free.Count > 0)
                {
                    var go = pool.Free.Pop();
                    if (go != null)
                    {
                        Object.Destroy(go);
                    }
                }
            }
        }

        private static void ForceReturn(Pool pool, PooledObject obj)
        {
            pool.Active.Remove(obj);

            if (obj.GameObject.TryGetComponent<IPoolable>(out var poolable))
            {
                poolable.OnReturn();
            }

            obj.GameObject.SetActive(false);
            pool.Free.Push(obj.GameObject);
        }

        private static PooledObject FindLowestPriority(List<PooledObject> active)
        {
            PooledObject lowest = null;
            foreach (var candidate in active)
            {
                if (lowest == null || candidate.Priority < lowest.Priority)
                {
                    lowest = candidate;
                }
            }

            return lowest;
        }

        private Pool GetOrCreatePool(GameObject prefab)
        {
            if (!_pools.TryGetValue(prefab, out var pool))
            {
                pool = new Pool();
                _pools[prefab] = pool;
            }

            return pool;
        }
    }
}
