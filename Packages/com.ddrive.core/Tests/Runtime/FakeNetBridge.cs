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

        private struct RelayBudget
        {
            public double WindowStart;
            public int Count;
        }

        private readonly Dictionary<Type, List<Delegate>> _handlers = new();
        private readonly Dictionary<Type, bool> _knownTypes = new();
        private readonly Dictionary<ulong, RelayBudget> _relayBudgets = new();
        private readonly Dictionary<Transform, ulong> _netIds = new();
        // N-4(2026-09-22) — PresentationManager.IsParticipant() は netId → Transform の順引きを使う
        // (ResolveNetObject)。既存の _netIds(Transform → netId、ResolveNetId 用)とは逆方向のため、
        // SetNetId で両方に登録する(片方だけ更新して食い違わないようにするため専用の辞書にした)。
        private readonly Dictionary<ulong, Transform> _netObjects = new();
        private readonly HashSet<Transform> _localPlayerObjects = new();

        public bool IsServer { get; set; } = true;
        public bool IsClient { get; set; } = true;
        public double NetworkTime { get; set; }
        public ulong LocalClientId { get; set; }

        // NgoNetBridge.ClientRelayLimitPerSecond と同じ既定値([14] §9)。テストで超過挙動を確認する際に変更できる。
        public int ClientRelayLimitPerSecond { get; set; } = 60;

        public int BroadcastCount { get; private set; }
        public int SendToCount { get; private set; }
        public ulong LastSendToClientId { get; private set; }
        public object LastMessage { get; private set; }

        // P2-5(6-0) — Client→Host の中継依頼が Host のレート制限で破棄された回数(NgoNetBridge.RequestBroadcastRpc 相当)。
        public int RelayRejectedCount { get; private set; }

        public ulong NextSpawnNetworkedResult { get; set; }
        public int SpawnNetworkedCallCount { get; private set; }
        public ulong LastDespawnNetworkedId { get; private set; }
        public bool LastDespawnNetworkedDestroy { get; private set; }

        // [14_networking.md] §5(5-9) — Late Join 通知テスト用(手動発火)。
        public event Action<ulong> ClientConnected;
        public void RaiseClientConnected(ulong clientId) => ClientConnected?.Invoke(clientId);

        // 2026-09-17 レビュー対応(P2-2) — 切断通知テスト用(手動発火)。
        public event Action<ulong, string> ClientDisconnected;
        public void RaiseClientDisconnected(ulong clientId, string reason = "test") => ClientDisconnected?.Invoke(clientId, reason);

        public void Broadcast<T>(in T msg, NetChannel channel) where T : INetMessage
        {
            BroadcastCount++;
            LastMessage = msg;
            Dispatch(LocalClientId, msg);
        }

        public void SendTo<T>(ulong clientId, in T msg, NetChannel channel) where T : INetMessage
        {
            SendToCount++;
            LastSendToClientId = clientId;
            LastMessage = msg;
            // 既存テストの慣習(PrefabSimulatedSpawnTests 等): SendTo(clientId, ...) は「この clientId から
            // サーバーへ届いた」を模擬するために clientId をそのまま senderId として自分自身へ配送する。
            Dispatch(clientId, msg);
        }

        // P2-5(6-0) — NgoNetBridge.RequestBroadcastRpc と同じ検証(型登録済みか + クライアント別レート制限)を
        // 経てから、真の発行者(senderClientId)を保ったまま自分自身へ配送する(このテストダブルは Host 役の
        // インスタンス 1 つに Client 役の呼び出しをシミュレートする形で使う。CosmeticDeliveryTests 等の
        // 既存 Broadcast/SendTo は変えず、6-0 の偽造メッセージ/Client 行為者テスト専用の追加口)。
        public bool RequestBroadcastFromClient<T>(ulong senderClientId, in T msg, NetChannel channel) where T : INetMessage
        {
            if (!_knownTypes.ContainsKey(typeof(T)))
            {
                return false;
            }

            if (!ConsumeRelayBudget(senderClientId))
            {
                RelayRejectedCount++;
                return false;
            }

            Dispatch(senderClientId, msg);
            return true;
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

        // テストがマッチしない発行者(senderClientId)や未知の HandleNetKey を模造して受信側だけに
        // 直接配送したいケース向け(実際の中継/検証を経由しない生の注入)。
        public void InjectReceive<T>(ulong senderClientId, in T msg) where T : INetMessage => Dispatch(senderClientId, msg);

        public IDisposable Subscribe<T>(Action<ulong, T> handler) where T : INetMessage
        {
            _knownTypes[typeof(T)] = true;
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = new List<Delegate>();
                _handlers[typeof(T)] = list;
            }

            list.Add(handler);
            return new Subscription(() => list.Remove(handler));
        }

        public Transform ResolveNetObject(ulong netId)
            => netId != 0 && _netObjects.TryGetValue(netId, out var transform) ? transform : null;

        public ulong ResolveNetId(Transform transform)
            => transform != null && _netIds.TryGetValue(transform, out var id) ? id : 0UL;

        public void SetNetId(Transform transform, ulong netId)
        {
            if (transform == null)
            {
                return;
            }

            _netIds[transform] = netId;
            if (netId != 0)
            {
                _netObjects[netId] = transform;
            }
        }

        public bool IsLocalPlayerObject(Transform transform) => transform != null && _localPlayerObjects.Contains(transform);

        public void SetLocalPlayerObject(Transform transform, bool isLocal)
        {
            if (transform == null)
            {
                return;
            }

            if (isLocal)
            {
                _localPlayerObjects.Add(transform);
            }
            else
            {
                _localPlayerObjects.Remove(transform);
            }
        }

        public ulong SpawnNetworked(GameObject root)
        {
            SpawnNetworkedCallCount++;
            return NextSpawnNetworkedResult;
        }

        public void DespawnNetworked(ulong netId, bool destroy)
        {
            LastDespawnNetworkedId = netId;
            LastDespawnNetworkedDestroy = destroy;
        }

        // 6-5(ContentHash) — 呼び出し記録のみ(実際の切断は模擬しない。テストは呼び出しの有無/引数を見る)。
        public int DisconnectClientCallCount { get; private set; }
        public ulong LastDisconnectedClientId { get; private set; }
        public string LastDisconnectReason { get; private set; }

        public void DisconnectClient(ulong clientId, string reason)
        {
            DisconnectClientCallCount++;
            LastDisconnectedClientId = clientId;
            LastDisconnectReason = reason;
        }

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
