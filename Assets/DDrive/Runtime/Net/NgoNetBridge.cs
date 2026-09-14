using System;
using System.Collections.Generic;
using System.Text;
using DDrive.Foundation.Net;
using Unity.Netcode;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // INetBridge の Netcode for GameObjects(2.13.2) 実装。シーンに 1 つ NetworkObject として配置する
    // (NetworkManager と同じシーン。MS2026 側では Host が生成する NetworkPrefab に含めてもよい)。
    // LocalLoopbackBridge と差し替えるだけでゲームコード・データ側は無改修で動く(FR-13.1)。
    //
    // MS2026 移植方針([14_networking.md] §12, 2026-09-08 統一):
    //   - 接続モデルは Host(+Client) / Client の 1v1。D-Drive の「Server」= MS2026 の「Host」。
    //   - 権威は Host。Broadcast/SendTo は Host から呼ぶのが正規経路。
    //   - Client から Cosmetic を出したい場合は Server 宛 RPC で Host に「依頼」し、Host が検証(レート制限)の上で
    //     全員へ配る(MS2026 ルール「入力はクライアントが送る / 表示は両者が受け取って描く」)。
    //   - 配送は NGO 2.x の統一 RPC(`[Rpc(Unity.Netcode.SendTo.*)]`)を使う。旧 `[ClientRpc]` は 2.x では SendTo.NotServer 扱いで
    //     **ホスト自身に届かない**ため、ホストの Manager が Cosmetic を再生できない(2026-09-08 修正)。
    //   - NetChannel.Unreliable は RpcDelivery.Unreliable に対応させる(欠落許容の演出イベント)。
    //     ただし NGO の Unreliable は 1 パケット(MTU)制限があるため、大きいペイロードは Reliable にフォールバックする。
    //   - ログは "[Net/Host]" / "[Net/Client]" プレフィックス(MS2026 Networking.md §5)。
    public sealed class NgoNetBridge : NetworkBehaviour, INetBridge
    {
        // Unreliable RPC で安全に送れるペイロードの目安(bytes)。NGO の Unreliable は 1 パケットに収まる必要がある。
        public const int UnreliablePayloadLimit = 1000;

        // Client からの Cosmetic 依頼のレート制限(1 クライアントあたり / 秒)。超過分は破棄して警告。
        public const int ClientRelayLimitPerSecond = 60;

        private sealed class Subscription : IDisposable
        {
            private readonly Action _onDispose;
            public Subscription(Action onDispose) => _onDispose = onDispose;
            public void Dispose() => _onDispose();
        }

        private struct RelayBudget
        {
            public double WindowStart;
            public int Count;
        }

        private readonly Dictionary<string, Type> _keyToType = new();
        private readonly Dictionary<string, List<Delegate>> _handlers = new();
        private readonly Dictionary<ulong, RelayBudget> _relayBudgets = new();

        public double NetworkTime => NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;

        // [14_networking.md] §2/§12(6-0) — NetworkManager.LocalClientId を橋渡しする。HandleNetKey の発行者
        // 埋め込み・検証([14] §9)に使う。未接続時は 0(ServerClientId と同じ扱い)。
        public ulong LocalClientId => NetworkManager != null ? NetworkManager.LocalClientId : 0UL;

        // [14_networking.md] §5(5-9) — Late Join のアクティブ演出スナップショット送信に使う新規接続通知。
        // NGO の OnClientConnectedCallback は Host/Client 双方で発火する(自分自身の接続も含む)ため、
        // 実際に「Host として送るかどうか」の判定は購読側(PresentationManager)が IsServer を見て行う。
        public event Action<ulong> ClientConnected;

        // [11_tasks.md] 6-0(B) — NetDebugOverlay 用の受信メッセージ数。
        public int ReceivedMessageCount { get; private set; }

        private string LogTag => IsServer ? "[Net/Host]" : "[Net/Client]";

        public override void OnNetworkSpawn()
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            }
        }

        private void HandleClientConnected(ulong clientId) => ClientConnected?.Invoke(clientId);

        public void Broadcast<T>(in T msg, NetChannel channel) where T : INetMessage
        {
            var key = KeyOf<T>();
            var json = JsonUtility.ToJson(msg);

            if (IsServer)
            {
                // Host 自身が行為者。発行者は Host の LocalClientId(P1-2 対応: 6-0 まで全員に常に
                // ServerClientId として配送されていたため、Client 行為者の HandleNetKey 検証が機能しなかった)。
                SendToAll(key, json, channel, LocalClientId);
                return;
            }

            if (!IsClient || !IsSpawned)
            {
                Debug.LogWarning($"{LogTag} NgoNetBridge.Broadcast: 未接続のため送信できません({key})。");
                return;
            }

            // Client 発: Host へ依頼し、Host が検証して全員へ配る(自分も Host からの RPC で受信して再生する)。
            RequestBroadcastRpc(key, json, channel == NetChannel.Unreliable);
        }

        public void SendTo<T>(ulong clientId, in T msg, NetChannel channel) where T : INetMessage
        {
            if (!IsServer)
            {
                Debug.LogWarning($"{LogTag} NgoNetBridge.SendTo は Host からのみ呼べます(Client→特定 Client の直接送信は権威モデル上許可しない)。");
                return;
            }

            var json = JsonUtility.ToJson(msg);
            var target = RpcTarget.Single(clientId, RpcTargetUse.Temp);
            var originClientId = LocalClientId; // 直接送信は常に Host が発行者

            if (channel == NetChannel.Unreliable && FitsUnreliable(json))
            {
                ReceiveUnreliableToRpc(KeyOf<T>(), json, originClientId, target);
            }
            else
            {
                ReceiveToRpc(KeyOf<T>(), json, originClientId, target);
            }
        }

        public IDisposable Subscribe<T>(Action<ulong, T> handler) where T : INetMessage
        {
            var key = KeyOf<T>();
            _keyToType[key] = typeof(T);

            if (!_handlers.TryGetValue(key, out var list))
            {
                list = new List<Delegate>();
                _handlers[key] = list;
            }

            list.Add(handler);
            return new Subscription(() => list.Remove(handler));
        }

        public Transform ResolveNetObject(ulong netId)
        {
            if (NetworkManager != null && NetworkManager.SpawnManager != null &&
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(netId, out var netObj))
            {
                return netObj.transform;
            }

            return null;
        }

        // [14_networking.md] §4/§5(6-0) — ResolveNetObject の逆方向。渡された Transform(またはその親)に
        // NetworkObject が付いていて Spawn 済みなら NetworkObjectId を返す。それ以外は 0(呼び出し元は
        // 既存のとおり Position 等にフォールバックする)。
        public ulong ResolveNetId(Transform transform)
        {
            if (transform == null)
            {
                return 0UL;
            }

            var netObj = transform.GetComponentInParent<NetworkObject>();
            return netObj != null && netObj.IsSpawned ? netObj.NetworkObjectId : 0UL;
        }

        // [14_networking.md] §5 追加指示(2026-09-14, 6-0) — HapticsData.LocalPlayerOnly の誤爆防止に使う。
        // NetworkObject の所有者(OwnerClientId)がローカルクライアントと一致するかどうかを見る。
        public bool IsLocalPlayerObject(Transform transform)
        {
            if (transform == null)
            {
                return false;
            }

            var netObj = transform.GetComponentInParent<NetworkObject>();
            return netObj != null && netObj.IsSpawned && netObj.OwnerClientId == LocalClientId;
        }

        // [14_networking.md] §3/§10(6-0) — NetMode.Simulated な Prefab の Host 権威生成。root は既に
        // ローカルへ Instantiate 済み(PrefabsManager.SpawnData の Pool.Rent 結果)であることを前提にする
        // (NetworkObject を Spawn するだけで、生成そのものは既存の Pool 経路に任せる)。
        public ulong SpawnNetworked(GameObject root)
        {
            if (!IsServer || root == null)
            {
                return 0UL;
            }

            var netObj = root.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                // Validator(PrefabDataValidator)が Error として検出する組み合わせ([14] §10)。
                // ランタイムは例外で止めず、NetObjectId=0 のローカル専用インスタンスとして継続する。
                Debug.LogWarning($"{LogTag} NgoNetBridge.SpawnNetworked: '{root.name}' に NetworkObject が無いため NetworkObjectId を発行できません。");
                return 0UL;
            }

            if (!netObj.IsSpawned)
            {
                netObj.Spawn();
            }

            return netObj.NetworkObjectId;
        }

        public void DespawnNetworked(ulong netId, bool destroy)
        {
            if (!IsServer || NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return;
            }

            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(netId, out var netObj) && netObj.IsSpawned)
            {
                netObj.Despawn(destroy);
            }
        }

        // ── 送信(Host 側) ──

        private void SendToAll(string key, string json, NetChannel channel, ulong originClientId)
        {
            if (channel == NetChannel.Unreliable && FitsUnreliable(json))
            {
                ReceiveUnreliableRpc(key, json, originClientId);
            }
            else
            {
                ReceiveRpc(key, json, originClientId);
            }
        }

        private static bool FitsUnreliable(string json) => Encoding.UTF8.GetByteCount(json) <= UnreliablePayloadLimit;

        // ── RPC(Host → 全員。ホスト自身も含む) ──
    // 注: enum Unity.Netcode.SendTo は本クラスの SendTo<T>() メソッドと名前が衝突するため完全修飾する。
        // originClientId: 本来の発行者(Host 自身、または Client→Host 依頼の送信元)。P1-2 対応(6-0):
        // 以前は Dispatch が常に NetworkManager.ServerClientId を使っていたため、中継された Client 発の
        // メッセージが全ピアで「Host から来た」ものとして扱われ、HandleNetKey の発行者検証が機能しなかった。

        [Rpc(Unity.Netcode.SendTo.ClientsAndHost)]
        private void ReceiveRpc(string typeKey, string json, ulong originClientId)
        {
            Dispatch(typeKey, json, originClientId);
        }

        [Rpc(Unity.Netcode.SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
        private void ReceiveUnreliableRpc(string typeKey, string json, ulong originClientId)
        {
            Dispatch(typeKey, json, originClientId);
        }

        // ── RPC(Host → 特定クライアント) ──

        [Rpc(Unity.Netcode.SendTo.SpecifiedInParams)]
        private void ReceiveToRpc(string typeKey, string json, ulong originClientId, RpcParams rpcParams)
        {
            Dispatch(typeKey, json, originClientId);
        }

        [Rpc(Unity.Netcode.SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable)]
        private void ReceiveUnreliableToRpc(string typeKey, string json, ulong originClientId, RpcParams rpcParams)
        {
            Dispatch(typeKey, json, originClientId);
        }

        // ── RPC(Client → Host の依頼) ──
        // Host は発信者ごとのレート制限を掛けてから中継する([14_networking.md] §9: クライアント発の中継は無条件に行わない)。
        // Simulated な生成はこの経路を通さない(Prefab の Simulated Spawn は Phase 4 で Host 権威の専用 API を用意する)。
        [Rpc(Unity.Netcode.SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestBroadcastRpc(string typeKey, string json, bool unreliable, RpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;
            if (!_keyToType.ContainsKey(typeKey))
            {
                Debug.LogWarning($"{LogTag} 未登録のメッセージ種別 '{typeKey}' の中継依頼を Client {sender} から受信したため破棄しました。");
                return;
            }

            if (!ConsumeRelayBudget(sender))
            {
                Debug.LogWarning($"{LogTag} Client {sender} からの中継依頼がレート制限({ClientRelayLimitPerSecond}/秒)を超えたため破棄しました({typeKey})。");
                return;
            }

            // 真の発行者(sender)を全ピアへ伝える。以後の Presentation 側の発行者検証([14] §9、6-0)が
            // これを使って偽造 Signal/Cancel/Play を破棄できるようにする。
            SendToAll(typeKey, json, unreliable ? NetChannel.Unreliable : NetChannel.ReliableOrdered, sender);
        }

        private bool ConsumeRelayBudget(ulong clientId)
        {
            var now = NetworkTime;
            _relayBudgets.TryGetValue(clientId, out var budget);

            if (now - budget.WindowStart >= 1d)
            {
                budget.WindowStart = now;
                budget.Count = 0;
            }

            budget.Count++;
            _relayBudgets[clientId] = budget;
            return budget.Count <= ClientRelayLimitPerSecond;
        }

        // ── 受信 ──

        private void Dispatch(string key, string json, ulong senderId)
        {
            ReceivedMessageCount++;
            if (!_keyToType.TryGetValue(key, out var type) || !_handlers.TryGetValue(key, out var list))
            {
                return;
            }

            var msg = JsonUtility.FromJson(json, type);

            foreach (var d in list)
            {
                d.DynamicInvoke(senderId, msg);
            }
        }

        private static string KeyOf<T>() => typeof(T).FullName;
    }
}
