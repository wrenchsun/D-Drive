using System;
using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Foundation.Pause
{
    // チャンネルごとに多重 Push/Pop 可能なスタック式ポーズ。深さ 0→1 で Paused、1→0 で Resumed を通知する。
    public sealed class PauseService
    {
        private readonly Dictionary<PauseChannel, int> _depths = new();

        public event Action<PauseChannel, bool> OnPauseChanged;

        public void Push(PauseChannel channel)
        {
            _depths.TryGetValue(channel, out var depth);
            depth++;
            _depths[channel] = depth;

            if (depth == 1)
            {
                OnPauseChanged?.Invoke(channel, true);
            }
        }

        public void Pop(PauseChannel channel)
        {
            if (!_depths.TryGetValue(channel, out var depth) || depth <= 0)
            {
                Debug.LogWarning($"[DDrive] PauseService.Pop called without matching Push for channel {channel}.");
                return;
            }

            depth--;
            _depths[channel] = depth;

            if (depth == 0)
            {
                OnPauseChanged?.Invoke(channel, false);
            }
        }

        public bool IsPaused(PauseChannel channel) => _depths.TryGetValue(channel, out var depth) && depth > 0;

        public bool IsAnyPaused()
        {
            foreach (var depth in _depths.Values)
            {
                if (depth > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
