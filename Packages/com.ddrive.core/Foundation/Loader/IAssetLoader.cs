using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DDrive.Foundation.Loader
{
    // ゲームコードから直接呼ぶの禁止(Manager 専用)。実装は Addressables ラッパ(DDrive.Runtime)。
    public interface IAssetLoader
    {
        UniTask<T> LoadAsync<T>(string address, CancellationToken ct) where T : UnityEngine.Object;
        void Release(string address);
        UniTask PreloadAsync(IEnumerable<string> addresses, IProgress<float> progress);
    }
}
