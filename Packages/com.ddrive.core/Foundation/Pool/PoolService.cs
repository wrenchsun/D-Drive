using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Foundation.Pool
{
    // Rent/Return/Prewarm/上限回収を担う共通プール。走査は for/foreach のみ(LINQ 禁止)。
    public sealed class PoolService : IPoolService
    {
        private sealed class Pool
        {
            // 6-2(0 alloc 検証)で判明: 以前は Free に GameObject だけを積んでおり、Rent の度に
            // "new PooledObject(...)" していたため、定常経路(Stop→Return→次の Play→Rent)で
            // 毎回クラスインスタンスが 1 つ増えていた([12_review.md] §3、定常経路 0 alloc 方針に反する)。
            // Free に PooledObject 自体(GameObject を積んだラッパー)を積んでおき、Rent 時に
            // ラッパーごと再利用することで、通常の Rent/Return サイクルでは Instantiate 済みの GameObject
            // だけでなく PooledObject ラッパーも再利用し、追加 alloc を無くす。
            public readonly Stack<PooledObject> Free = new();
            public readonly List<PooledObject> Active = new();
            public int MaxCount = int.MaxValue;
            public bool Persistent = true;
        }

        private readonly Dictionary<GameObject, Pool> _pools = new();

        // 生成した Instance を配置する親(任意)。シーン整理のほか、Editor プレビュー(1-6)が
        // 生成物をプレビューシーン内に閉じ込めるためにも使う(親のシーンに Instance が属する)。
        private Transform _instanceParent;

        public void SetInstanceParent(Transform parent) => _instanceParent = parent;

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
                var go = Object.Instantiate(prefab, _instanceParent);
                go.SetActive(false);
                pool.Free.Push(new PooledObject(go, prefab));
            }
        }

        public PooledObject Rent(GameObject prefab)
        {
            var pool = GetOrCreatePool(prefab);

            PooledObject pooled = null;

            // シーン破棄等で死んだ GO を持つラッパーが Free に残っている可能性があるため、
            // 生きているものが出るまで捨てる(ラッパー自体は使い回すため new PooledObject しない)。
            while (pool.Free.Count > 0 && pooled == null)
            {
                var candidate = pool.Free.Pop();
                if (candidate.GameObject != null)
                {
                    pooled = candidate;
                }
            }

            if (pooled == null)
            {
                // シーン破棄等で GO が死んだ Active エントリは上限に数えない(回収対象にもしない)。
                // 死んだエントリを ForceReturn すると Free に何も積まれず、直後の Pop で例外になる。
                PruneDeadActive(pool.Active);

                if (pool.Active.Count >= pool.MaxCount)
                {
                    var evicted = FindLowestPriority(pool.Active);
                    if (evicted != null)
                    {
                        // ForceReturn は Free に積むため、積んだままにすると同じ GO が二重に貸し出される(必ず取り除く)。
                        ForceReturn(pool, evicted);
                        if (pool.Free.Count > 0)
                        {
                            var reclaimed = pool.Free.Pop();
                            // 2026-09-17 レビュー対応(P1-1): 回収したラッパー(reclaimed == evicted)を
                            // そのまま新しい借り手へ渡すと、旧借り手がまだ同じラッパーを握っているため、
                            // 旧借り手の Return/Despawn が「新しい貸出」を取り消してしまう
                            // (旧 A の Despawn で新 B の GameObject が非アクティブ化され Free に積み直される)。
                            // evict は上限超過という例外経路なので、ここだけ PooledObject 1 個の alloc を
                            // 許容して新しいラッパーに差し替える。旧ラッパーは Free/Active のどちらにも
                            // 残らないため、旧借り手の Return は Active.Remove に失敗して no-op になる。
                            // 通常の Return → Rent サイクルは従来どおりラッパーごと再利用で 0 alloc
                            // ([12_review.md] §3 の定常経路 alloc 禁止)。
                            if (reclaimed.GameObject != null)
                            {
                                pooled = new PooledObject(reclaimed.GameObject, prefab);
                            }
                        }
                    }

                    if (pooled == null)
                    {
                        Debug.LogWarning("[DDrive] Pool at capacity with nothing to reclaim; instantiating over limit.");
                        pooled = new PooledObject(Object.Instantiate(prefab, _instanceParent), prefab);
                    }
                }
                else
                {
                    pooled = new PooledObject(Object.Instantiate(prefab, _instanceParent), prefab);
                }
            }

            pooled.GameObject.SetActive(true);
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

        // Pool.Kind == None 用: Active から取り除き、Free に積まずに GameObject を破棄する(Codex レビュー 2026-09-10)。
        // 既に破棄済み/二重 Discard は無視する(冪等)。
        public void Discard(PooledObject obj)
        {
            if (obj == null || !_pools.TryGetValue(obj.PrefabKey, out var pool))
            {
                return;
            }

            if (!pool.Active.Remove(obj))
            {
                return;
            }

            if (obj.GameObject == null)
            {
                return;
            }

            // OnReturn は呼ばない: Discard は「プールに戻って再利用される」のではなく破棄されるため。
            if (Application.isPlaying)
            {
                Object.Destroy(obj.GameObject);
            }
            else
            {
                Object.DestroyImmediate(obj.GameObject);
            }
        }

        // Preload の検証用(IPoolService には含めない): 指定 prefab の Free(待機中)数を返す。
        public int FreeCount(GameObject prefab)
        {
            return _pools.TryGetValue(prefab, out var pool) ? pool.Free.Count : 0;
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
                    var pooled = pool.Free.Pop();
                    if (pooled.GameObject != null)
                    {
                        Object.Destroy(pooled.GameObject);
                    }
                }
            }
        }

        private static void ForceReturn(Pool pool, PooledObject obj)
        {
            // 二重 Return / 回収済み PooledObject の Return を無視する。
            // ここを素通しすると、他の利用者に貸出中の GO を停止・二重登録してしまう。
            if (!pool.Active.Remove(obj))
            {
                return;
            }

            if (obj.GameObject == null)
            {
                return;
            }

            if (obj.GameObject.TryGetComponent<IPoolable>(out var poolable))
            {
                poolable.OnReturn();
            }

            obj.GameObject.SetActive(false);
            // 6-2: ラッパー(obj)自体を Free に積んで次の Rent で再利用する(Priority は次の貸出に
            // 影響しないようクリアしておく。呼び出し側は Rent 直後に必要なら明示的に設定し直す)。
            obj.Priority = 0;
            pool.Free.Push(obj);
        }

        private static void PruneDeadActive(List<PooledObject> active)
        {
            for (var i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].GameObject == null)
                {
                    active.RemoveAt(i);
                }
            }
        }

        private static PooledObject FindLowestPriority(List<PooledObject> active)
        {
            PooledObject lowest = null;
            foreach (var candidate in active)
            {
                if (candidate.GameObject == null)
                {
                    continue;
                }

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
