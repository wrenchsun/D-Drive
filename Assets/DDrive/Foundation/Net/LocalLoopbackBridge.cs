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
        private readonly Dictionary<Transform, ulong> _netObjectsReverse = new();

        public bool IsServer => true;
        public bool IsClient => true;
        public double NetworkTime { get; private set; }

        // シングルプレイでは自分が Host 相当。NGO の ServerClientId/LocalClientId と揃えて 0 とする([14] §12)。
        public ulong LocalClientId => 0UL;

        // シングルプレイでは他クライアントが存在しないため通常は発火しない。テスト/将来の
        // マルチウィンドウ運用向けに RaiseClientConnected で手動発火できる([14] §5、5-9)。
        public event Action<ulong> ClientConnected;

        // 同上(2026-09-17 レビュー対応 P2-2)。シングルプレイでは通常発火しない。
        public event Action<ulong, string> ClientDisconnected;

        // [11_tasks.md] 6-0(B) — NetDebugOverlay 用の受信メッセージ数(INetBridge のインタフェースには
        // 含めない。オーバーレイ側は型チェックで見る)。
        public int ReceivedMessageCount { get; private set; }

        public void Tick(double deltaTime) => NetworkTime += deltaTime;

        public void RaiseClientConnected(ulong clientId) => ClientConnected?.Invoke(clientId);

        public void RaiseClientDisconnected(ulong clientId, string reason = null) => ClientDisconnected?.Invoke(clientId, reason);

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

        public void RegisterNetObject(ulong netId, Transform transform)
        {
            _netObjects[netId] = transform;
            if (transform != null)
            {
                _netObjectsReverse[transform] = netId;
            }
        }

        public Transform ResolveNetObject(ulong netId) => _netObjects.TryGetValue(netId, out var t) ? t : null;

        public ulong ResolveNetId(Transform transform)
            => transform != null && _netObjectsReverse.TryGetValue(transform, out var id) ? id : 0UL;

        // シングルプレイは全オブジェクトが自分のもの(誤爆防止の判定自体が不要)。
        public bool IsLocalPlayerObject(Transform transform) => transform != null;

        // シングルプレイに NetworkObject の概念は無い。呼び出し元は 0 のまま(ローカル専用インスタンス)として扱う。
        public ulong SpawnNetworked(GameObject root) => 0UL;

        public void DespawnNetworked(ulong netId, bool destroy)
        {
            // no-op。ローカルの Despawn(Pool.Return/Discard)は呼び出し元(PrefabsManager)が別途行う。
        }

        // [14_networking.md] §7(6-5) — シングルプレイには切断すべき他クライアントが存在しないため no-op。
        public void DisconnectClient(ulong clientId, string reason)
        {
        }

        private void Dispatch<T>(ulong senderId, T msg) where T : INetMessage
        {
            ReceivedMessageCount++;
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
