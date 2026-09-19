using System;
using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Foundation.Pause
{
    // チャンネルごとに dB オフセットをスタックする。Pop で直前の値に戻る(LIFO)。
    public sealed class AudioDuckService
    {
        private readonly Dictionary<DuckChannel, Stack<float>> _stacks = new();

        public event Action<DuckChannel, float> OnDuckChanged;

        public void Push(DuckChannel channel, float attenuationDb)
        {
            if (!_stacks.TryGetValue(channel, out var stack))
            {
                stack = new Stack<float>();
                _stacks[channel] = stack;
            }

            stack.Push(attenuationDb);
            OnDuckChanged?.Invoke(channel, CurrentDb(channel));
        }

        public void Pop(DuckChannel channel)
        {
            if (!_stacks.TryGetValue(channel, out var stack) || stack.Count == 0)
            {
                Debug.LogWarning($"[DDrive] AudioDuckService.Pop called without matching Push for channel {channel}.");
                return;
            }

            stack.Pop();
            OnDuckChanged?.Invoke(channel, CurrentDb(channel));
        }

        public float CurrentDb(DuckChannel channel)
            => _stacks.TryGetValue(channel, out var stack) && stack.Count > 0 ? stack.Peek() : 0f;
    }
}
