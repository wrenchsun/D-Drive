// [42_distribution.md] §2.3-9 / §7 A-7(P-4、2026-09-20) — NGO(com.unity.netcode.gameobjects)を
// versionDefines(DDRIVE_NGO)で切り離す。ファイル全体が NGO 依存なので、NGO 未導入の持ち込み先
// （DDRIVE_NGO 未定義）ではファイル全体をコンパイル対象外にする（LocalLoopbackBridge だけでコンパイル・動作する）。
#if DDRIVE_NGO
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

        // [14_networking.md] §7(6-5) — カタログ ContentHash 照合の状態("検証中..."/"OK"/不一致の詳細)。
        // DDriveRuntimeBootstrap が NetHashGate 生成後に割り当てる(未設定なら行を出さない)。
        public CatalogContentHashGate ContentHashGate;

        [Tooltip("OFF にすると OnGUI を描画しない(実機確認が終わったら切る用)")]
        public bool Visible = true;

        private GUIStyle _style;
        private int _receivedAtLastCheck;
        private float _rateWindowStart;
        private int _messagesInWindow;
        private float _lastRatePerSecond;

        // [14_networking.md] §16(N-3、2026-09-22) — 接続クライアント数の行。OnGUI は毎フレーム呼ばれる
        // ため、NetManualConnectOverlay.RefreshStateTextIfChanged と同じ「値が変わったときだけ文字列を
        // 作る」パターンに従い、Host のときだけ意味を持つ ConnectedClientCount が変化したときだけ
        // 組み立て直す([CLAUDE.md] §0-3 の趣旨)。
        private int _lastClientCount = int.MinValue;
        private string _clientsText = string.Empty;

        private void OnGUI()
        {
            if (!Visible || Bridge == null)
            {
                return;
            }

            _style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, wordWrap = false };

            var role = Bridge.IsServer ? (Bridge.IsClient ? "Host" : "Server") : (Bridge.IsClient ? "Client" : "-");

            // [11_tasks.md] 6-0 修正6(オーケストレーター追加指示) — 接続状態を 1 行表示する。Host/Loopback は
            // 常に接続中扱い(NgoNetBridge.IsConnected は Client が切断されたときだけ false になる)。
            var connected = Bridge.IsServer || (Bridge.IsClient && (Bridge is not NgoNetBridge ngoForConn || ngoForConn.IsConnected));
            var connectedText = connected ? "接続中" : "切断";

            var rtt = NetworkManagerRef != null ? NgoTransportConfigurator.TryGetRoundTripTimeMs(NetworkManagerRef, NetworkManager.ServerClientId) : null;
            var rttText = rtt.HasValue ? $"{rtt.Value:F0} ms" : "n/a";

            // [11_tasks.md] 6-0 修正1 — トランスポートの RTT はシミュレーター遅延(-ddrive-sim-latency)を
            // 反映しない(NgoTransportConfigurator.cs 参照。UnityTransport.SetDebugSimulatorParameters が
            // Obsolete/no-op のため)。アプリ層で計測した往復時間(NgoNetBridge.AppRoundTripMs、Ping/Pong)を
            // 併記し、シミュレーター遅延が実際に効いているかをこちらで判定できるようにする。
            // 6-6(K2 修正、2026-09-18 再修正) — 通信停止中は最後の実測値のまま固着させず、Pong 未受信の
            // 間は経過時間を下限として表示する(基準は「最後に Pong を受信した時刻」。NgoNetBridge.cs 参照)。
            // IsAppRoundTripMsStale=true(連続 3 回 Pong 無応答)の間は「通信途絶の疑い」であることを
            // (途絶疑い) で明示する(旧: (stale)。単なる Ping 送信直後の未応答〔平常時にも起きる〕では
            // 表示しない)。
            var ngoForRtt = Bridge as NgoNetBridge;
            var appRtt = ngoForRtt?.AppRoundTripMs;
            var appRttText = appRtt.HasValue
                ? $"{appRtt.Value:F0} ms{(ngoForRtt != null && ngoForRtt.IsAppRoundTripMsStale ? " (途絶疑い)" : string.Empty)}"
                : "n/a";

            // [14_networking.md] §16(N-3) — Host のときだけ接続クライアント数を表示する(Client では
            // NgoNetBridge.ConnectedClientCount が常に 0 で意味を持たないため、行自体を出さない)。
            var ngoForClients = Bridge as NgoNetBridge;
            var clientCount = Bridge.IsServer && ngoForClients != null ? ngoForClients.ConnectedClientCount : -1;
            if (clientCount != _lastClientCount)
            {
                _lastClientCount = clientCount;
                _clientsText = clientCount >= 0 ? $"\nClients: {clientCount}" : string.Empty;
            }

            var contentHashText = ContentHashGate != null ? $"\nContentHash: {ContentHashGate.LastStatusText}" : string.Empty;

            // [42_distribution.md] §5.6/§6 P-8(2026-09-20) — 自分の D-Drive 版と(分かる範囲での)相手の版を
            // 表示する。相手の版は Host 視点でのみ分かる(CatalogContentHashMsg を受信した Client のもの)。
            // Client 視点で Host の版を受け取る経路は本チケットでは追加していない(§5.6 は
            // CatalogContentHashMsg への追加のみを許可。CatalogContentHashGate.cs のコメント参照)ため
            // "?" のまま表示する。
            var versionText = ContentHashGate != null
                ? $"\nVersion: {ContentHashGate.LocalPackageVersion} (Protocol {DDriveProtocol.Current}) / Remote: " +
                  $"{(string.IsNullOrEmpty(ContentHashGate.LastKnownRemotePackageVersion) ? "?" : ContentHashGate.LastKnownRemotePackageVersion)}"
                : string.Empty;

            var text =
                $"[DDrive Net]\n" +
                $"Role: {role} (ClientId={Bridge.LocalClientId})\n" +
                $"State: {connectedText}\n" +
                $"NetworkTime: {Bridge.NetworkTime:F2}\n" +
                $"RTT: {rttText} / App RTT: {appRttText}\n" +
                $"Received: {ReceivedCount()} ({_lastRatePerSecond:F1}/s)" +
                _clientsText +
                contentHashText +
                versionText;

            GUI.Box(new Rect(8, 8, 260, 168), text, _style);
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
#endif // DDRIVE_NGO
