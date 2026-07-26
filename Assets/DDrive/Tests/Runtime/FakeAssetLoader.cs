using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Loader;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    internal sealed class FakeAssetLoader : IAssetLoader
    {
        public readonly Dictionary<string, UnityEngine.Object> Assets = new();
        public int LoadCallCount;

        public UniTask<T> LoadAsync<T>(string address, CancellationToken ct) where T : UnityEngine.Object
        {
            LoadCallCount++;
            Assets.TryGetValue(address, out var obj);
            return UniTask.FromResult(obj as T);
        }

        public void Release(string address)
        {
        }

        public UniTask PreloadAsync(IEnumerable<string> addresses, IProgress<float> progress) => UniTask.CompletedTask;
    }
}
