using UnityEngine;

namespace DDrive.Foundation.Pool
{
    public interface IPoolService
    {
        PooledObject Rent(GameObject prefab);
        void Return(PooledObject obj);

        // Pool.Kind == None(プールしない)インスタンスを破棄する。Return と異なり Free に積まず、
        // Active から取り除いた上で GameObject を破棄する(Destroy/DestroyImmediate は実装側が Application.isPlaying で切替)。
        // IPoolable.OnReturn は「プールに戻って再利用される」ことを表す通知なので、破棄では呼ばない。
        void Discard(PooledObject obj);

        void Prewarm(GameObject prefab, int count);
        void Clear(PoolScope scope);
    }
}
