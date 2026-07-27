using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Loader;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DDrive.Runtime.Loading
{
    // IAssetLoader の Addressables 実装。参照カウント/多重ロード防止の実ロジックは
    // RefCountedAsyncCache(Foundation)に委譲し、ここは Addressables 固有の配線のみ担当する。
    public sealed class AddressablesAssetLoader : IAssetLoader
    {
        private readonly struct LoadedAsset
        {
            public readonly AsyncOperationHandle Handle;
            public readonly UnityEngine.Object Value;

            public LoadedAsset(AsyncOperationHandle handle, UnityEngine.Object value)
            {
                Handle = handle;
                Value = value;
            }
        }

        private readonly RefCountedAsyncCache<string, LoadedAsset> _cache;

        public AddressablesAssetLoader()
        {
            _cache = new RefCountedAsyncCache<string, LoadedAsset>(LoadInternal, OnDisposed);
        }

        public async UniTask<T> LoadAsync<T>(string address, CancellationToken ct) where T : UnityEngine.Object
        {
            try
            {
                var loaded = await _cache.Acquire(address).AttachExternalCancellation(ct);
                return loaded.Value as T;
            }
            catch (System.OperationCanceledException)
            {
                // Acquire 時点で参照カウントは増えているため、キャンセルした呼び出し元の分は返す
                // (返さないと誰も Release しない参照が残り、アセットが永久にピン留めされる)。
                _cache.Release(address);
                throw;
            }
        }

        public void Release(string address) => _cache.Release(address);

        public async UniTask PreloadAsync(IEnumerable<string> addresses, IProgress<float> progress)
        {
            var list = new List<string>(addresses);
            var completed = 0;
            foreach (var address in list)
            {
                await LoadAsync<UnityEngine.Object>(address, CancellationToken.None);
                completed++;
                progress?.Report(list.Count == 0 ? 1f : (float)completed / list.Count);
            }
        }

        private static async UniTask<LoadedAsset> LoadInternal(string address)
        {
            // UniTask の Addressables 拡張(ToUniTask)有無に依存しないよう、IsDone を素朴に待つ。
            var handle = Addressables.LoadAssetAsync<UnityEngine.Object>(address);
            await UniTask.WaitUntil(() => handle.IsDone);

            // 失敗を null 成功として返すとキャッシュに毒が残る。例外にして呼び出し元へ伝播させる
            // (RefCountedAsyncCache 側が失敗エントリを破棄し、後で再試行できる)。
            if (handle.Status == AsyncOperationStatus.Failed)
            {
                Addressables.Release(handle);
                throw new System.InvalidOperationException($"[DDrive] Addressables load failed for '{address}'.");
            }

            return new LoadedAsset(handle, handle.Result);
        }

        private static void OnDisposed(string address, LoadedAsset loaded)
        {
            if (loaded.Handle.IsValid())
            {
                Addressables.Release(loaded.Handle);
            }
        }
    }
}
