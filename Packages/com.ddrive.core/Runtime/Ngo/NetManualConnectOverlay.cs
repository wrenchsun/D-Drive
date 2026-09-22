// [42_distribution.md] §2.3-9 / §7 A-7(P-4、2026-09-20) — NGO(com.unity.netcode.gameobjects)を
// versionDefines(DDRIVE_NGO)で切り離す。ファイル全体が NGO 依存なので、NGO 未導入の持ち込み先
// （DDRIVE_NGO 未定義）ではファイル全体をコンパイル対象外にする（LocalLoopbackBridge だけでコンパイル・動作する）。
#if DDRIVE_NGO
using System;
using System.Globalization;
using DDrive.Foundation.Net;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // [14_networking.md] §14(N-2、2026-09-22) — 開発用の手動接続 UI。IP/Port 入力欄 + Host/Client/切断
    // ボタンを OnGUI で描画する(NetDebugOverlay と同じ「確認用のためだけ」の OnGUI 方式。専用の uGUI
    // Canvas は使わない。ADR-4〔プレビューは実 Manager を Editor から駆動する〕とは無関係)。
    // DDriveRuntimeBootstrap.NetStartMode=Manual(または -ddrive-net manual)のときだけ、NgoBridgeFactory.
    // Create()(NgoBridgeFactoryInstaller.cs)が開発ビルド/エディタ限定で生成する。
    //
    // NetworkManager/NgoNetBridge 型を DDrive.Runtime へ露出させないため、実処理は N-1 で既に用意されている
    // NgoBridgeCreateResult の delegate(ManualStartHost/ManualStartClient/ManualStop/IsListening)をそのまま
    // 受け取って呼ぶ(DDriveRuntimeBootstrap.StartHost/StartClient/StopNetworking/IsNetworkStarted の実体と
    // 同じもの。Bootstrap 型に依存しないことで NgoBridgeFactory.Create() の中だけで配線が完結する)。
    public sealed class NetManualConnectOverlay : MonoBehaviour
    {
        // [14_networking.md] §14(N-2) — 最後に接続した IP/Port を保存し、次回の初期値にする(開発用なので
        // PlayerPrefs の簡易な永続化で十分。DDriveRuntimeBootstrap の Options/OptionStore とは無関係)。
        public const string LastAddressPrefKey = "DDrive.Net.Manual.LastAddress";
        public const string LastPortPrefKey = "DDrive.Net.Manual.LastPort";

        public INetBridge Bridge;
        public Func<bool> IsListeningQuery;
        public Func<ushort, bool> ManualStartHost;
        public Func<string, ushort, bool> ManualStartClient;
        public Action ManualStop;

        [Tooltip("OFF にすると OnGUI を描画しない(実機確認が終わったら切る用)")]
        public bool Visible = true;

        private string _addressInput = string.Empty;
        private string _portInput = string.Empty;
        private string _statusMessage = string.Empty;

        private GUIStyle _boxStyle;

        // OnGUI は毎フレーム呼ばれるため、状態文字列の組み立てはこの 3 値が変わったときだけ行う
        // ([CLAUDE.md] §0-3 の趣旨: 定常経路で無駄な文字列連結・GC を発生させない。NetDebugOverlay の
        // ReceivedCount と同じ「変化検知してから作る」パターン)。
        private bool _cacheValid;
        private bool _cachedIsListening;
        private bool _cachedIsServer;
        private bool _cachedIsClient;
        private bool _cachedIsConnected;
        private string _cachedStateText = string.Empty;

        // NgoBridgeFactory.Create() から生成直後に呼ぶ。既定の接続先(DefaultHostAddress/-ddrive-host、
        // DefaultPort/-ddrive-port)を渡し、PlayerPrefs に前回の入力が残っていればそちらを優先する。
        public void Initialize(string defaultAddress, ushort defaultPort, INetBridge bridge,
            Func<bool> isListeningQuery, Func<ushort, bool> manualStartHost,
            Func<string, ushort, bool> manualStartClient, Action manualStop)
        {
            Bridge = bridge;
            IsListeningQuery = isListeningQuery;
            ManualStartHost = manualStartHost;
            ManualStartClient = manualStartClient;
            ManualStop = manualStop;

            _addressInput = PlayerPrefs.GetString(LastAddressPrefKey, defaultAddress ?? string.Empty);
            var savedPort = PlayerPrefs.GetInt(LastPortPrefKey, defaultPort);
            _portInput = savedPort.ToString(CultureInfo.InvariantCulture);
        }

        private void OnGUI()
        {
            if (!Visible || Bridge == null)
            {
                return;
            }

            _boxStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, wordWrap = false };

            var isListening = IsListeningQuery != null && IsListeningQuery();
            RefreshStateTextIfChanged(isListening);

            const float width = 240f;
            const float height = 150f;
            // NetDebugOverlay は左上(Rect(8,8,260,168))を使うため、重ならないよう左下に置く
            // ([14_networking.md] §14 の要求どおり)。
            var rect = new Rect(8f, Screen.height - height - 8f, width, height);

            GUILayout.BeginArea(rect, _boxStyle);
            GUILayout.Label("[DDrive 手動接続]");

            GUI.enabled = !isListening;
            GUILayout.BeginHorizontal();
            GUILayout.Label("IP", GUILayout.Width(28));
            _addressInput = GUILayout.TextField(_addressInput, GUILayout.Width(150));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Port", GUILayout.Width(28));
            _portInput = GUILayout.TextField(_portInput, GUILayout.Width(80));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host で開始"))
            {
                OnClickStartHost();
            }

            if (GUILayout.Button("Client で接続"))
            {
                OnClickStartClient();
            }

            GUILayout.EndHorizontal();
            GUI.enabled = true;

            if (GUILayout.Button("切断"))
            {
                OnClickStop();
            }

            GUILayout.Label(_cachedStateText);
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUILayout.Label(_statusMessage);
            }

            GUILayout.EndArea();
        }

        private void OnClickStartHost()
        {
            if (!NetManualConnectInput.TryParsePort(_portInput, out var port, out var error))
            {
                _statusMessage = error;
                return;
            }

            if (ManualStartHost == null)
            {
                _statusMessage = "Host 開始 API が使えません(NGO 未導入、またはシーンに NetworkManager/NgoNetBridge がありません)。";
                return;
            }

            if (ManualStartHost(port))
            {
                SaveLastConnection(_addressInput, port);
                _statusMessage = string.Empty;
            }
            else
            {
                _statusMessage = "Host 開始に失敗しました(既に接続中の可能性があります。コンソールを確認してください)。";
            }
        }

        private void OnClickStartClient()
        {
            if (!NetManualConnectInput.TryParse(_addressInput, _portInput, out var address, out var port, out var error))
            {
                _statusMessage = error;
                return;
            }

            if (ManualStartClient == null)
            {
                _statusMessage = "Client 接続 API が使えません(NGO 未導入、またはシーンに NetworkManager/NgoNetBridge がありません)。";
                return;
            }

            if (ManualStartClient(address, port))
            {
                SaveLastConnection(address, port);
                _statusMessage = string.Empty;
            }
            else
            {
                _statusMessage = "Client 接続に失敗しました(既に接続中の可能性があります。コンソールを確認してください)。";
            }
        }

        private void OnClickStop()
        {
            _statusMessage = string.Empty;
            ManualStop?.Invoke();
        }

        private void SaveLastConnection(string address, ushort port)
        {
            PlayerPrefs.SetString(LastAddressPrefKey, address ?? string.Empty);
            PlayerPrefs.SetInt(LastPortPrefKey, port);
            PlayerPrefs.Save();
        }

        // [14_networking.md] §14(N-2) — 状態 1 行(未接続 / Host listening / Client 接続中 / 切断)。
        // IsNetworkStarted(=IsListeningQuery)・Bridge.IsServer/IsClient・NgoNetBridge.IsConnected から導く
        // (NetDebugOverlay の役割・接続状態表示と同じ判定基準を流用する)。
        private void RefreshStateTextIfChanged(bool isListening)
        {
            var isServer = Bridge.IsServer;
            var isClient = Bridge.IsClient;
            var isConnected = Bridge is not NgoNetBridge ngo || ngo.IsConnected;

            if (_cacheValid && _cachedIsListening == isListening && _cachedIsServer == isServer &&
                _cachedIsClient == isClient && _cachedIsConnected == isConnected)
            {
                return;
            }

            _cacheValid = true;
            _cachedIsListening = isListening;
            _cachedIsServer = isServer;
            _cachedIsClient = isClient;
            _cachedIsConnected = isConnected;

            string state;
            if (!isListening)
            {
                state = "未接続";
            }
            else if (isServer)
            {
                state = "Host listening";
            }
            else if (isClient)
            {
                state = isConnected ? "Client 接続中" : "切断";
            }
            else
            {
                state = "未接続";
            }

            _cachedStateText = "状態: " + state;
        }
    }
}
#endif // DDRIVE_NGO
