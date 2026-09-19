using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DDrive.Foundation.Loader
{
    // 同一キーへの多重ロードを防ぎ、参照カウントが 0 になった時点で dispose 通知する汎用キャッシュ。
    // AssetLoader(0-5) の中核ロジックを Addressables から切り離してテスト可能にするために存在する。
    public sealed class RefCountedAsyncCache<TKey, TValue>
    {
        private sealed class Entry
        {
            public int RefCount;
            public UniTask<TValue> Task;
        }

        private readonly Dictionary<TKey, Entry> _entries = new();
        private readonly Func<TKey, UniTask<TValue>> _factory;
        private readonly Action<TKey, TValue> _onDisposed;

        public RefCountedAsyncCache(Func<TKey, UniTask<TValue>> factory, Action<TKey, TValue> onDisposed)
        {
            _factory = factory;
            _onDisposed = onDisposed;
        }

        public int Count => _entries.Count;

        public int RefCountOf(TKey key) => _entries.TryGetValue(key, out var entry) ? entry.RefCount : 0;

        public UniTask<TValue> Acquire(TKey key)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                // ロード失敗が永続キャッシュされないよう、失敗済みエントリは作り直す(リトライ可能に)。
                if (entry.Task.Status == UniTaskStatus.Faulted)
                {
                    _entries.Remove(key);
                }
                else
                {
                    entry.RefCount++;
                    return entry.Task;
                }
            }

            // UniTask は 1 回しか await できないため、複数の呼び出し元へ返す Task は必ず Preserve する
            // (これが無いと「同一キーの多重ロード防止」というこのクラスの主目的の場面で例外になる)。
            var newEntry = new Entry { RefCount = 1, Task = _factory(key).Preserve() };
            _entries[key] = newEntry;
            return newEntry.Task;
        }

        public void Release(TKey key)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                Debug.LogWarning($"[DDrive] Release called for a key with no active reference: {key}");
                return;
            }

            entry.RefCount--;
            if (entry.RefCount > 0)
            {
                return;
            }

            _entries.Remove(key);
            NotifyDisposedWhenReady(key, entry.Task).Forget();
        }

        private async UniTaskVoid NotifyDisposedWhenReady(TKey key, UniTask<TValue> task)
        {
            TValue value;
            try
            {
                value = await task;
            }
            catch
            {
                return;
            }

            _onDisposed?.Invoke(key, value);
        }
    }
}
