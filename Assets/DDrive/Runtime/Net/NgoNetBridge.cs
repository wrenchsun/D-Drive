using System;
using System.Collections.Generic;
using DDrive.Foundation.Net;
using Unity.Netcode;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // INetBridge の Netcode for GameObjects 実装。シーンに 1 つ NetworkObject として配置する。
    // LocalLoopbackBridge と差し替えるだけでゲームコード・データ側は無改修で動く(FR-13.1)。
    //
    // 制約: Broadcast/SendTo はサーバー権威のシナリオ([14_networking.md] の想定どおり、
    // サーバー/権威側から呼ぶ)にのみ対応する。クライアント発の送信は本チケットのスコープ外
    // (ServerRpc 中継が必要になった時点で追加する)。
    // また com.unity.netcode.gameobjects パッケージの解決(要インターネット接続)と
    // 実機 2 クライアントでの動作確認はこのコード単体では検証できない(手動確認が必要)。
    public sealed class NgoNetBridge : NetworkBehaviour, INetBridge
    {
        private sealed class Subscription : IDisposable
        {
            private readonly Action _onDispose;
            public Subscription(Action onDispose) => _onDispose = onDispose;
            public void Dispose() => _onDispose();
        }

        private readonly Dictionary<string, Type> _keyToType = new();
        private readonly Dictionary<string, List<Delegate>> _handlers = new();

        public double NetworkTime => NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;

        public void Broadcast<T>(in T msg, NetChannel channel) where T : INetMessage
        {
            if (!IsServer)
            {
                Debug.LogWarning("[DDrive] NgoNetBridge.Broadcast must be called from the server.");
                return;
            }

            ReceiveClientRpc(KeyOf<T>(), JsonUtility.ToJson(msg));
        }

        public void SendTo<T>(ulong clientId, in T msg, NetChannel channel) where T : INetMessage
        {
            if (!IsServer)
            {
                Debug.LogWarning("[DDrive] NgoNetBridge.SendTo must be called from the server.");
                return;
            }

            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } },
            };

            ReceiveClientRpc(KeyOf<T>(), JsonUtility.ToJson(msg), rpcParams);
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

        [ClientRpc]
        private void ReceiveClientRpc(string typeKey, string json, ClientRpcParams rpcParams = default)
        {
            Dispatch(typeKey, json);
        }

        private void Dispatch(string key, string json)
        {
            if (!_keyToType.TryGetValue(key, out var type) || !_handlers.TryGetValue(key, out var list))
            {
                return;
            }

            var msg = JsonUtility.FromJson(json, type);
            var senderId = NetworkManager != null ? NetworkManager.ServerClientId : 0UL;

            foreach (var d in list)
            {
                d.DynamicInvoke(senderId, msg);
            }
        }

        private static string KeyOf<T>() => typeof(T).FullName;
    }
}
