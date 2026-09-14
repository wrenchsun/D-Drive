using System;
using System.Collections.Generic;
using DDrive.Foundation.Net;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // 4-13(Simulated Spawn)検証用の設定可能な INetBridge。IsServer/IsClient を切り替えられ、
    // Broadcast/SendTo は呼び出し回数・最後のメッセージを記録しつつ、渡された senderId で
    // 自分自身の Subscribe ハンドラへ即時配送する(「サーバーが clientId から受信した」を
    // SendTo(clientId, msg, ...) 呼び出しで模擬できる。CountingNetBridge を拡張した設計)。
    internal sealed class FakeNetBridge : INetBridge
    {
        private sealed class Subscription : IDisposable
        {
            private readonly Action _onDispose;
            public Subscription(Action onDispose) => _onDispose = onDispose;
            public void Dispose() => _onDispose();
        }

        private readonly Dictionary<Type, List<Delegate>> _handlers = new();

        public bool IsServer { get; set; } = true;
        public bool IsClient { get; set; } = true;
        public double NetworkTime { get; set; }

        public int BroadcastCount { get; private set; }
        public int SendToCount { get; private set; }
        public ulong LastSendToClientId { get; private set; }
        public object LastMessage { get; private set; }

        // [14_networking.md] §5(5-9) — Late Join 通知テスト用(手動発火)。
        public event Action<ulong> ClientConnected;
        public void RaiseClientConnected(ulong clientId) => ClientConnected?.Invoke(clientId);

        public void Broadcast<T>(in T msg, NetChannel channel) where T : INetMessage
        {
            BroadcastCount++;
            LastMessage = msg;
            Dispatch(0, msg);
        }

        public void SendTo<T>(ulong clientId, in T msg, NetChannel channel) where T : INetMessage
        {
            SendToCount++;
            LastSendToClientId = clientId;
            LastMessage = msg;
            Dispatch(clientId, msg);
        }

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

        public Transform ResolveNetObject(ulong netId) => null;

        private void Dispatch<T>(ulong senderId, T msg) where T : INetMessage
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                return;
            }

            var snapshot = list.ToArray();
            foreach (var d in snapshot)
            {
                ((Action<ulong, T>)d).Invoke(senderId, msg);
            }
        }
    }
}
