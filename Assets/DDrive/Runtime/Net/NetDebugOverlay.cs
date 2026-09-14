using DDrive.Foundation.Net;
using Unity.Netcode;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // [11_tasks.md] 6-0(B) — 実機確認用の最小デバッグオーバーレイ。画面左上に役割/接続状態/RTT/
    // NetworkTime/受信メッセージ数を表示する(OnGUI。専用の uGUI Canvas は使わない。確認用のためだけの
    // ものなので、実 Manager を Editor から駆動する ADR-4 とは無関係)。
    // DDriveRuntimeBootstrap が NetBridgeMode.Ngo で起動したときだけ自動で追加する。
    public sealed class NetDebugOverlay : MonoBehaviour
    {
        public INetBridge Bridge;
        public NetworkManager NetworkManagerRef;

        [Tooltip("OFF にすると OnGUI を描画しない(実機確認が終わったら切る用)")]
        public bool Visible = true;

        private GUIStyle _style;
        private int _receivedAtLastCheck;
        private float _rateWindowStart;
        private int _messagesInWindow;
        private float _lastRatePerSecond;

        private void OnGUI()
        {
            if (!Visible || Bridge == null)
            {
                return;
            }

            _style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, wordWrap = false };

            var role = Bridge.IsServer ? (Bridge.IsClient ? "Host" : "Server") : (Bridge.IsClient ? "Client" : "-");
            var rtt = NetworkManagerRef != null ? NgoTransportConfigurator.TryGetRoundTripTimeMs(NetworkManagerRef, NetworkManager.ServerClientId) : null;
            var rttText = rtt.HasValue ? $"{rtt.Value:F0} ms" : "n/a";

            // [11_tasks.md] 6-0 修正1 — トランスポートの RTT はシミュレーター遅延(-ddrive-sim-latency)を
            // 反映しない(NgoTransportConfigurator.cs 参照。UnityTransport.SetDebugSimulatorParameters が
            // Obsolete/no-op のため)。アプリ層で計測した往復時間(NgoNetBridge.AppRoundTripMs、Ping/Pong)を
            // 併記し、シミュレーター遅延が実際に効いているかをこちらで判定できるようにする。
            var appRtt = Bridge is NgoNetBridge ngoForRtt ? ngoForRtt.AppRoundTripMs : null;
            var appRttText = appRtt.HasValue ? $"{appRtt.Value:F0} ms" : "n/a";

            var text =
                $"[DDrive Net]\n" +
                $"Role: {role} (ClientId={Bridge.LocalClientId})\n" +
                $"NetworkTime: {Bridge.NetworkTime:F2}\n" +
                $"RTT: {rttText} / App RTT: {appRttText}\n" +
                $"Received: {ReceivedCount()} ({_lastRatePerSecond:F1}/s)";

            GUI.Box(new Rect(8, 8, 260, 110), text, _style);
        }

        private int ReceivedCount()
        {
            var current = Bridge is NgoNetBridge ngo ? ngo.ReceivedMessageCount : (Bridge is LocalLoopbackBridge loop ? loop.ReceivedMessageCount : 0);

            var now = Time.unscaledTime;
            if (now - _rateWindowStart >= 1f)
            {
                _lastRatePerSecond = current - _receivedAtLastCheck;
                _receivedAtLastCheck = current;
                _rateWindowStart = now;
            }

            return current;
        }
    }
}
