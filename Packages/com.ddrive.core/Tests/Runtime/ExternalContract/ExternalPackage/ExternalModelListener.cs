using DDrive.Runtime.Model;
using UnityEngine;

namespace ExternalPackage.Fake
{
    // Prefab に付ける外部の IModelInstanceListener(FC-12 の公開 API を外部アセンブリから実装できることの確認)。
    public sealed class ExternalModelListener : MonoBehaviour, IModelInstanceListener
    {
        public int SpawnedCount;
        public int ReturningCount;
        public GameObject LastRoot;

        public void OnModelSpawned(in ModelInstanceContext context)
        {
            SpawnedCount++;
            LastRoot = context.Root;
        }

        public void OnModelReturning(in ModelInstanceContext context) => ReturningCount++;
    }
}
