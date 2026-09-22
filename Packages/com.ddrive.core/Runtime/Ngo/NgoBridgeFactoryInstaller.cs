#if DDRIVE_NGO
using Unity.Netcode;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // [42_distribution.md] §2.3-9/§7 A-7(P1-1、2026-09-20) — DDrive.Runtime(NGO 非依存)の
    // NetBridgeFactoryRegistry へ、この NGO アセンブリが自分自身を登録する。ゲーム起動時に一度だけ
    // 実行され(RuntimeInitializeLoadType.SubsystemRegistration = シーンの Awake より前)、
    // DDRIVE_NGO が定義されていない(= NGO 未導入)持ち込み先ではこのファイルごとコンパイル対象外になる
    // ため、登録が一切起きない(Bootstrap 側は null チェックだけで安全にフォールバックできる)。
    internal static class NgoBridgeFactoryInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            NetBridgeFactoryRegistry.Current = new NgoBridgeFactory();
        }
    }

    // [14_networking.md] §12(6-0) — 元 DDriveRuntimeBootstrap.ResolveNetBridge の NGO 分岐をそのまま移設。
    // シーンから NetworkManager/NgoNetBridge を解決し(DDriveNgoBootstrapHook の明示指定を優先)、
    // トランスポート設定・デバッグオーバーレイの生成・StartHost/StartClient の遅延実行を用意する。
    internal sealed class NgoBridgeFactory : INgoBridgeFactory
    {
        // [14_networking.md] §14(N-2、2026-09-22) — リリースビルドで role==Manual が指定された場合の
        // 警告は 1 回だけ出す(CLAUDE.md §0-4「例外で止めない」+ ログを埋めない配慮。NgoTransportConfigurator
        // の WarnOnce と同じ考え方)。
        private static bool _warnedManualOverlaySkippedInRelease;

        public NgoBridgeCreateResult Create(in NgoBridgeCreateArgs args)
        {
            var hook = Object.FindAnyObjectByType<DDriveNgoBootstrapHook>();
            var nm = (hook != null ? hook.NetworkManagerRef : null) ?? Object.FindAnyObjectByType<NetworkManager>();
            var bridge = (hook != null ? hook.NgoBridgeRef : null) ?? (nm != null ? nm.GetComponent<NgoNetBridge>() : null);

            if (nm == null || bridge == null)
            {
                return null;
            }

            // `in` パラメータはローカル関数(クロージャ)の中で直接使えない(CS1628)ため、
            // 必要な値をローカル変数へコピーしてから下のローカル関数で使う。
            var host = args.Host;
            var port = args.Port;
            var role = args.Role;
            var launchOptions = args.LaunchOptions;

            // [14_networking.md] N-1(2026-09-22) — role==Manual は StartHost/StartClient API 呼び出し時まで
            // Transport 設定を遅延する(まだ Host か Client かも決まっていないため、既定の host/port で
            // 設定しても後で上書きされるだけで無意味。DoManualStartHost/DoManualStartClient が呼び出し時に
            // NgoTransportConfigurator.TryConfigure を再利用する)。Host/Client は従来どおりここで設定する
            // (既存の挙動を変えない)。
            if (role == NetLaunchRole.Host || role == NetLaunchRole.Client)
            {
                NgoTransportConfigurator.TryConfigure(nm, host, port, launchOptions.SimLatencyMs, launchOptions.SimLossPercent);

                // [11_tasks.md] 6-0 修正1 — UnityTransport.SetDebugSimulatorParameters は Obsolete/no-op
                // (NgoTransportConfigurator.cs 参照)なので、アプリ層の送受信キュー遅延で代替する。
                bridge.ConfigureAppLayerSimLatency(launchOptions.SimLatencyMs ?? 0);
            }

            NetDebugOverlay overlay = null;
            if (args.ShowDebugOverlay)
            {
                var overlayGo = new GameObject("NetDebugOverlay");
                if (args.ParentTransform != null)
                {
                    overlayGo.transform.SetParent(args.ParentTransform, false);
                }

                overlay = overlayGo.AddComponent<NetDebugOverlay>();
                overlay.Bridge = bridge;
                overlay.NetworkManagerRef = nm;
            }

            // [14_networking.md] §14(N-2、2026-09-22) — role==Manual のときだけ、開発用の手動接続 UI
            // (IP/Port 入力欄 + Host/Client/切断ボタン)を生成する。開発ビルド/エディタ限定
            // (Debug.isDebugBuild は Editor 実行時、または「Development Build」を付けたプレイヤーで true。
            // NgoNetBridge.ConfigureAppLayerSimLatency と同じ判定基準)。リリースビルドで Manual が
            // 指定された場合は生成せず警告だけ 1 回出す(手動接続は開発用途のためリリースに含める理由が無い、
            // かつ操作可能な UI を誤って残さない安全側)。
            if (role == NetLaunchRole.Manual)
            {
                if (Debug.isDebugBuild || Application.isEditor)
                {
                    var manualOverlayGo = new GameObject("NetManualConnectOverlay");
                    if (args.ParentTransform != null)
                    {
                        manualOverlayGo.transform.SetParent(args.ParentTransform, false);
                    }

                    var manualOverlay = manualOverlayGo.AddComponent<NetManualConnectOverlay>();
                    manualOverlay.Initialize(host, port, bridge, DoIsListening, DoManualStartHost, DoManualStartClient, DoManualStop);
                }
                else if (!_warnedManualOverlaySkippedInRelease)
                {
                    _warnedManualOverlaySkippedInRelease = true;
                    Debug.LogWarning("[Net] NgoBridgeFactory: リリースビルドのため手動接続 UI(NetManualConnectOverlay)は生成しません(開発ビルド/エディタ限定、[14_networking.md] §14 N-2)。");
                }
            }

            void PendingStart()
            {
                if (nm.IsListening)
                {
                    return;
                }

                if (role == NetLaunchRole.Host)
                {
                    nm.StartHost();
                    Debug.Log($"[Net/Host] DDriveRuntimeBootstrap: Host として起動しました(port={port})。");
                }
                else
                {
                    nm.StartClient();
                    Debug.Log($"[Net/Client] DDriveRuntimeBootstrap: Client として起動しました(host={host}:{port})。");
                }
            }

            void AssignHashGate(CatalogContentHashGate gate)
            {
                if (overlay != null)
                {
                    overlay.ContentHashGate = gate;
                }
            }

            // [14_networking.md] N-1(2026-09-22) — 開発用の手動接続 API の実処理。DDriveRuntimeBootstrap.
            // StartHost/StartClient/StopNetworking はこれらの delegate を呼ぶだけの薄いラッパー(NetworkManager
            // 型を DDrive.Runtime 側へ露出させないため。既存の PendingStart/AssignHashGate と同じパターン)。
            bool DoManualStartHost(ushort manualPort)
            {
                if (nm.IsListening)
                {
                    Debug.LogWarning("[Net] DDriveRuntimeBootstrap.StartHost: 既に接続中のため無視しました(先に StopNetworking() を呼んでください)。");
                    return false;
                }

                // [14_networking.md] N-1 追記(2026-09-22、レビュー指摘) — 手動 Host は "0.0.0.0" で
                // listen する(全インタフェース)。省略すると SetConnectionData の ServerListenAddress が
                // host(DefaultHostAddress/-ddrive-host、既定 "192.168.137.1" 等)に固定され、別 LAN・LAN 外
                // からのテストプレイで listen に失敗するため(Address 側は従来どおり host のまま)。
                NgoTransportConfigurator.TryConfigure(nm, host, manualPort, launchOptions.SimLatencyMs, launchOptions.SimLossPercent, listenAddress: "0.0.0.0");
                bridge.ConfigureAppLayerSimLatency(launchOptions.SimLatencyMs ?? 0);
                nm.StartHost();
                Debug.Log($"[Net/Host] DDriveRuntimeBootstrap.StartHost: Host として起動しました(listen=0.0.0.0:{manualPort})。");
                return true;
            }

            bool DoManualStartClient(string manualAddress, ushort manualPort)
            {
                if (nm.IsListening)
                {
                    Debug.LogWarning("[Net] DDriveRuntimeBootstrap.StartClient: 既に接続中のため無視しました(先に StopNetworking() を呼んでください)。");
                    return false;
                }

                if (string.IsNullOrEmpty(manualAddress))
                {
                    Debug.LogWarning("[Net] DDriveRuntimeBootstrap.StartClient: address が空のため接続できません。");
                    return false;
                }

                NgoTransportConfigurator.TryConfigure(nm, manualAddress, manualPort, launchOptions.SimLatencyMs, launchOptions.SimLossPercent);
                bridge.ConfigureAppLayerSimLatency(launchOptions.SimLatencyMs ?? 0);
                nm.StartClient();
                Debug.Log($"[Net/Client] DDriveRuntimeBootstrap.StartClient: Client として起動しました(host={manualAddress}:{manualPort})。");
                return true;
            }

            void DoManualStop()
            {
                if (!nm.IsListening)
                {
                    Debug.LogWarning("[Net] DDriveRuntimeBootstrap.StopNetworking: 接続していないため何もしません。");
                    return;
                }

                nm.Shutdown();
                Debug.Log("[Net] DDriveRuntimeBootstrap.StopNetworking: ネットワークを停止しました(NetworkManager.Shutdown)。");
            }

            bool DoIsListening() => nm.IsListening;

            var pendingStart = (role == NetLaunchRole.Host || role == NetLaunchRole.Client) ? (System.Action)PendingStart : null;

            return new NgoBridgeCreateResult
            {
                Bridge = bridge,
                PendingStart = pendingStart,
                AssignHashGate = AssignHashGate,
                IsListening = DoIsListening,
                ManualStartHost = DoManualStartHost,
                ManualStartClient = DoManualStartClient,
                ManualStop = DoManualStop,
            };
        }
    }
}
#endif // DDRIVE_NGO
