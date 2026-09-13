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
        public readonly List<string> PreloadedAddresses = new();
        public readonly List<string> ReleasedAddresses = new();

        public UniTask<T> LoadAsync<T>(string address, CancellationToken ct) where T : UnityEngine.Object
        {
            LoadCallCount++;
            Assets.TryGetValue(address, out var obj);
            return UniTask.FromResult(obj as T);
        }

        public void Release(string address)
        {
            ReleasedAddresses.Add(address);
        }

        // [11_tasks.md] 5-7 — ScenePreload のテストで「どの address が来たか」「進捗が 0→1 で報告されるか」を
        // 検証できるよう、他の Fake と違って実際に一覧を積んで進捗を報告する(単に CompletedTask を返すだけだと
        // AssetRegistry.PreloadIdsAsync の絞り込み・警告ロジックを検証できない)。
        public UniTask PreloadAsync(IEnumerable<string> addresses, IProgress<float> progress)
        {
            var list = new List<string>(addresses);
            PreloadedAddresses.AddRange(list);
            for (var i = 0; i < list.Count; i++)
            {
                progress?.Report((float)(i + 1) / list.Count);
            }

            if (list.Count == 0)
            {
                progress?.Report(1f);
            }

            return UniTask.CompletedTask;
        }
    }
}
