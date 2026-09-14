using System;
using DDrive.Foundation.Net;

namespace DDrive.Runtime.Net
{
    // [11_tasks.md] 6-0 修正1 — UnityTransport.SetDebugSimulatorParameters は Obsolete化され
    // "no longer supported and has no effect" になっている(Library/PackageCache の
    // com.unity.netcode.gameobjects@.../Runtime/Transports/UTP/UnityTransport.cs で確認済み。
    // DebugSimulator フィールドはドライバ生成時に一切参照されない)。トランスポート側の RTT
    // (NetworkTransport.GetCurrentRtt)はシミュレーター遅延を反映しないため、実際に遅延が
    // 効いているかを判定する手段として「アプリ層の往復時間」を別途計測する必要がある。
    //
    // NgoNetBridge が自律的に(Client→Host→Client の 1 往復)計測するための最小メッセージ。
    // NetDebugOverlay/NetCheckRunner はどちらも NgoNetBridge.AppRoundTripMs を読むだけで、
    // このメッセージ自体を直接扱う必要はない。
    [Serializable]
    public struct NetPingMsg : INetMessage
    {
        public double SentAtNetworkTime;
    }

    [Serializable]
    public struct NetPongMsg : INetMessage
    {
        public double OriginalSentAtNetworkTime;
    }
}
