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
        public ulong LocalClientId => 0UL;

        public void Broadcast<T>(in T msg, NetChannel channel) where T : INetMessage => BroadcastCount++;

        public void SendTo<T>(ulong clientId, in T msg, NetChannel channel) where T : INetMessage
        {
        }

        public IDisposable Subscribe<T>(Action<ulong, T> handler) where T : INetMessage => new NoopSubscription();

        public Transform ResolveNetObject(ulong netId) => null;

        public ulong ResolveNetId(Transform transform) => 0UL;

        public bool IsLocalPlayerObject(Transform transform) => false;

        public ulong SpawnNetworked(GameObject root) => 0UL;

        public void DespawnNetworked(ulong netId, bool destroy)
        {
        }

        public void DisconnectClient(ulong clientId, string reason)
        {
        }

        public event Action<ulong> ClientConnected;

        // 2026-09-17 レビュー対応(P2-2) — INetBridge に追加された切断通知(このテスト用ブリッジでは未使用)。
        public event Action<ulong, string> ClientDisconnected;

        private sealed class NoopSubscription : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
