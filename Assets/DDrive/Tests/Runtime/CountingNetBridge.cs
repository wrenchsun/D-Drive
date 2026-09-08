using System;
using DDrive.Foundation.Net;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // Broadcast 呼び出し回数だけを数えるテスト用 INetBridge(バッチ化の検証用)。
    // Loopback のような自己配送はしない(このテストでは呼び出し回数だけを見る)。
    internal sealed class CountingNetBridge : INetBridge
    {
        public int BroadcastCount { get; private set; }

        public bool IsServer => true;
        public bool IsClient => true;
        public double NetworkTime => 0d;

        public void Broadcast<T>(in T msg, NetChannel channel) where T : INetMessage => BroadcastCount++;

        public void SendTo<T>(ulong clientId, in T msg, NetChannel channel) where T : INetMessage
        {
        }

        public IDisposable Subscribe<T>(Action<ulong, T> handler) where T : INetMessage => new NoopSubscription();

        public Transform ResolveNetObject(ulong netId) => null;

        private sealed class NoopSubscription : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
