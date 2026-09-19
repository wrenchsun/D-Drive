using UnityEngine;

namespace DDrive.Foundation.Pool
{
    // Rent が返す実体のラッパー。Priority は上限超過時の強制回収対象選定に使う(呼び出し側が設定)。
    public sealed class PooledObject
    {
        public GameObject GameObject { get; }
        public int Priority;
        internal GameObject PrefabKey { get; }

        internal PooledObject(GameObject gameObject, GameObject prefabKey)
        {
            GameObject = gameObject;
            PrefabKey = prefabKey;
        }
    }
}
