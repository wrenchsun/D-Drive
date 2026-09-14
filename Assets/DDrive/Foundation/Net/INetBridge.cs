using System;
using UnityEngine;

namespace DDrive.Foundation.Net
{
    // トランスポート抽象。シングルプレイは LocalLoopbackBridge、v1 のマルチプレイは NGO アダプタ(0-14)。
    public interface INetBridge
    {
        bool IsServer { get; }
        bool IsClient { get; }
        double NetworkTime { get; }

        void Broadcast<T>(in T msg, NetChannel channel) where T : INetMessage;
        void SendTo<T>(ulong clientId, in T msg, NetChannel channel) where T : INetMessage;
        IDisposable Subscribe<T>(Action<ulong, T> handler) where T : INetMessage;
        Transform ResolveNetObject(ulong netId);

        // [14_networking.md] §5(5-9) — 新規接続通知(Host 視点)。Late Join でアクティブな演出リストを
        // スナップショット送信するための最小限の口。シングルプレイ(LocalLoopbackBridge)では通常発火しない。
        event Action<ulong> ClientConnected;
    }
}
