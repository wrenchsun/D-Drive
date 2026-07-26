using System;
using DDrive.Foundation.Net;
using DDrive.Runtime.Net;
using Unity.Netcode;
using UnityEngine;

namespace DDrive.Samples
{
    [Serializable]
    public struct PingMessage : INetMessage
    {
        public int Counter;
    }

    // 0-14 NGOアダプタの手動疎通確認用デモ。NetworkManager が居るシーンに
    // NetworkObject + NgoNetBridge + この Behaviour を付けたオブジェクトを置き、
    // Multiplayer Play Mode 等でホスト+クライアントを立ち上げて Console を見比べる。
    // 確認できたら削除して構わない(Validation の対象からも除外済み)。
    public sealed class NetBridgeSmokeTest : NetworkBehaviour
    {
        [SerializeField] private NgoNetBridge bridge;
        [SerializeField] private float intervalSeconds = 2f;

        private float _timer;
        private int _counter;
        private IDisposable _subscription;

        private void Awake()
        {
            if (bridge == null)
            {
                bridge = GetComponent<NgoNetBridge>();
            }
        }

        public override void OnNetworkSpawn()
        {
            _subscription = bridge.Subscribe<PingMessage>(OnPingReceived);
            Debug.Log($"[NetBridgeSmokeTest] Spawned. IsServer={bridge.IsServer} IsClient={bridge.IsClient} ClientId={NetworkManager.LocalClientId}");
        }

        public override void OnNetworkDespawn()
        {
            _subscription?.Dispose();
        }

        private void Update()
        {
            if (!bridge.IsServer)
            {
                return;
            }

            _timer += UnityEngine.Time.deltaTime;
            if (_timer < intervalSeconds)
            {
                return;
            }

            _timer = 0f;
            _counter++;
            Debug.Log($"[NetBridgeSmokeTest] Server broadcasting Ping #{_counter}");
            bridge.Broadcast(new PingMessage { Counter = _counter }, NetChannel.Unreliable);
        }

        private void OnPingReceived(ulong senderId, PingMessage msg)
        {
            Debug.Log($"[NetBridgeSmokeTest] ClientId={NetworkManager.LocalClientId} received Ping #{msg.Counter} (senderId={senderId})");
        }
    }
}
