// [42_distribution.md] §2.3-9(P-4、2026-09-20) — NGO 実機確認用サンプル。NetworkBehaviour を直接継承するのでファイル全体を DDRIVE_NGO で囲う。
// [14_networking.md] §16(N-3、2026-09-22) — `Samples~/NetCheck/` から `DDrive.Runtime.Ngo` アセンブリ本体
// (`Runtime/Ngo/NetCheck/`)へ移設した(GUID 不変)。名前空間も `DDrive.Samples` から `DDrive.Runtime.Net`
// に揃えた(DDrive.Runtime.Ngo.asmdef の rootNamespace と同じ)。
#if DDRIVE_NGO
using System;
using DDrive.Foundation.Net;
using Unity.Netcode;
using UnityEngine;

namespace DDrive.Runtime.Net
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

        // true にすると Client 側が Broadcast を呼ぶ(= Host への中継依頼経路 [14_networking.md] §2 を確認する)。
        // false(既定)は Host から Broadcast する正規経路。どちらでも Host / Client 両方の Console に受信ログが出れば OK。
        [SerializeField] private bool broadcastFromClient;

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
            var shouldSend = broadcastFromClient ? !bridge.IsServer : bridge.IsServer;
            if (!shouldSend)
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
            Debug.Log($"[NetBridgeSmokeTest] {(bridge.IsServer ? "Host" : "Client")} broadcasting Ping #{_counter}");
            bridge.Broadcast(new PingMessage { Counter = _counter }, NetChannel.Unreliable);
        }

        private void OnPingReceived(ulong senderId, PingMessage msg)
        {
            Debug.Log($"[NetBridgeSmokeTest] ClientId={NetworkManager.LocalClientId} received Ping #{msg.Counter} (senderId={senderId})");
        }
    }
}
#endif // DDRIVE_NGO
