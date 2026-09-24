using System;
using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Foundation.Handle
{
    // Manager 内部専用の Instance 配列 + フリーリスト。走査は for のみ(LINQ 禁止)。
    // Remove 時に世代を進めるため、破棄後の古い Handle は自動的に無効化される(ABA 対策)。
    public sealed class InstanceStore<TMarker, TInstance> where TInstance : class
    {
        private TInstance[] _items = new TInstance[16];
        private int[] _generations = new int[16];
        private readonly Stack<int> _freeIndices = new();
        private int _count;

        public event Action<int> OnInvalidAccess;
        public int InvalidAccessCount { get; private set; }

        public Handle<TMarker> Add(TInstance instance)
        {
            int index;
            if (_freeIndices.Count > 0)
            {
                index = _freeIndices.Pop();
            }
            else
            {
                index = _count;
                EnsureCapacity(_count + 1);
                _count++;
            }

            // 世代 0 は「未初期化(default)の Handle」と衝突するため使わない。
            // default(Handle<T>) は (index=0, generation=0) であり、これが最初のスロットの
            // 正規ハンドルと一致してしまうと、未代入ハンドル経由で他人の Instance を操作できてしまう。
            if (_generations[index] == 0)
            {
                _generations[index] = 1;
            }

            _items[index] = instance;
            return new Handle<TMarker>(index, _generations[index]);
        }

        public bool TryGet(Handle<TMarker> handle, out TInstance instance)
        {
            if (handle.Index >= 0 && handle.Index < _count &&
                _generations[handle.Index] == handle.Generation &&
                _items[handle.Index] != null)
            {
                instance = _items[handle.Index];
                return true;
            }

            instance = null;

            if (handle.Index >= 0 && handle.Index < _count)
            {
                RecordInvalidAccess(handle);
            }

            return false;
        }

        public bool IsValid(Handle<TMarker> handle) => TryGet(handle, out _);

        // 警告・InvalidAccessCount 無しの有効判定。「まだ再生中か」の問い合わせ(IsPlaying / ポーリング)用。
        // 終了済み Handle を問い合わせるのは正常な使い方なので、操作(Stop 等)と違って不正アクセス扱いにしない。
        public bool IsValidSilent(Handle<TMarker> handle)
            => handle.Index >= 0 && handle.Index < _count &&
               _generations[handle.Index] == handle.Generation &&
               _items[handle.Index] != null;

        // [M-1c、2026-09-25] TryGet の「警告を出さない」版。Close/Stop/Cancel 等の冪等操作は、
        // 「既に破棄済みの Handle で呼ばれたら何もしない」というガードとして先頭で TryGet を使うことが多いが、
        // これは「操作対象として不正」ではなく「(二重呼び出し等で)既に完了している」正常系のため、
        // IsValidSilent と同じ考え方で警告を出さずに Instance を取得する(実害の無い「Invalid handle access」
        // ノイズの原因。[TeamNotes 2026-09-25]「Invalid handle access」参照)。
        public bool TryGetQuiet(Handle<TMarker> handle, out TInstance instance)
        {
            if (IsValidSilent(handle))
            {
                instance = _items[handle.Index];
                return true;
            }

            instance = null;
            return false;
        }

        // 冪等: 既に外れている Handle の Remove は何もしない(警告も出さない。Pool 返却コールバックとの二重掃除に備える)。
        public void Remove(Handle<TMarker> handle)
        {
            if (!IsValidSilent(handle))
            {
                return;
            }

            _items[handle.Index] = null;
            _generations[handle.Index]++;
            _freeIndices.Push(handle.Index);
        }

        private void RecordInvalidAccess(Handle<TMarker> handle)
        {
            InvalidAccessCount++;
            OnInvalidAccess?.Invoke(handle.Index);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.LogWarning($"[DDrive] Invalid handle access (index={handle.Index}, generation={handle.Generation}).");
#endif
        }

        private void EnsureCapacity(int min)
        {
            if (min <= _items.Length)
            {
                return;
            }

            var newSize = Mathf.Max(_items.Length * 2, min);
            Array.Resize(ref _items, newSize);
            Array.Resize(ref _generations, newSize);
        }
    }
}
