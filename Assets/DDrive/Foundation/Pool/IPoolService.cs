using UnityEngine;

namespace DDrive.Foundation.Pool
{
    public interface IPoolService
    {
        PooledObject Rent(GameObject prefab);
        void Return(PooledObject obj);
        void Prewarm(GameObject prefab, int count);
        void Clear(PoolScope scope);
    }
}
