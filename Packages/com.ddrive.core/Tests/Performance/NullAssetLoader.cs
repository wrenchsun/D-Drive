using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Loader;

namespace DDrive.Tests.Performance
{
    // [11_tasks.md] 6-2 — AssetRegistry のコンストラクタを満たすためだけの最小 Fake。
    // このテスト asmdef の全テストは Data を直接渡す SpawnData/PlayData/PlaySeData/ShakeData 系 API を
    // 使うため、ID ベースの解決(LoadAsync/PreloadAsync)は実際には呼ばれない想定(呼ばれても no-op)。
    // DDrive.Tests.Runtime.FakeAssetLoader は internal で別アセンブリのため、ここでは参照できず
    // 同等の最小実装を用意している。
    internal sealed class NullAssetLoader : IAssetLoader
    {
        public UniTask<T> LoadAsync<T>(string address, CancellationToken ct) where T : UnityEngine.Object
            => UniTask.FromResult<T>(null);

        public void Release(string address)
        {
        }

        public UniTask PreloadAsync(IEnumerable<string> addresses, IProgress<float> progress) => UniTask.CompletedTask;
    }
}
