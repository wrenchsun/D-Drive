using System;
using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Foundation.Net
{
    // シングルプレイ用実装。Broadcast/SendTo は自分自身へ即時配送する。
    // ゲームコード・データは通信の有無で一切変わらない(NGO アダプタと差し替え可能)。
    public sealed class LocalLoopbackBridge : INetBridge
    {
        private sealed class Subscription : IDisposable
        {
            private readonly Action _onDispose;
            public Subscription(Action onDispose) => _onDispose = onDispose;
            public void Dispose() => _onDispose();
        }

        private readonly Dictionary<Type, List<Delegate>> _handlers = new();
        private readonly Dictionary<ulong, Transform> _netObjects = new();

        public bool IsServer => true;
        public bool IsClient => true;
        public double NetworkTime { get; private set; }

        public void Tick(double deltaTime) => NetworkTime += deltaTime;

        public void Broadcast<T>(in T msg, NetChannel channel) where T : INetMessage => Dispatch(0, msg);

        public void SendTo<T>(ulong clientId, in T msg, NetChannel channel) where T : INetMessage => Dispatch(clientId, msg);

        public IDisposable Subscribe<T>(Action<ulong, T> handler) where T : INetMessage
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = new List<Delegate>();
                _handlers[typeof(T)] = list;
            }

            list.Add(handler);
            return new Subscription(() => list.Remove(handler));
        }

        public void RegisterNetObject(ulong netId, Transform transform) => _netObjects[netId] = transform;

        public Transform ResolveNetObject(ulong netId) => _netObjects.TryGetValue(netId, out var t) ? t : null;

        private void Dispatch<T>(ulong senderId, T msg) where T : INetMessage
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                return;
            }

            // ハンドラ内での Subscribe/Dispose(1回受信して解除するパターン等)でリストが変化しても
            // 安全なように、スナップショットを取ってから配送する。
            var snapshot = list.ToArray();
            foreach (var d in snapshot)
            {
                ((Action<ulong, T>)d).Invoke(senderId, msg);
            }
        }
    }
}
