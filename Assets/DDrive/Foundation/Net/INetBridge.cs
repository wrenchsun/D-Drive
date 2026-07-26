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
    }
}
