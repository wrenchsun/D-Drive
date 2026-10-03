using UnityEngine;

namespace ExternalPackage.Fake
{
    // Prefab に付ける外部 MonoBehaviour。プール往復で「同じインスタンスが生きている」ことの確認用。
    public sealed class ExternalLifecycleProbe : MonoBehaviour
    {
        [System.NonSerialized] public int AwakeCount;
        [System.NonSerialized] public int EnableCount;
        [System.NonSerialized] public int DisableCount;
        [System.NonSerialized] public int DestroyCount;

        private void Awake() => AwakeCount++;

        private void OnEnable() => EnableCount++;

        private void OnDisable() => DisableCount++;

        private void OnDestroy() => DestroyCount++;
    }
}
