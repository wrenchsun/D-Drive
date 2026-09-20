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

            NgoTransportConfigurator.TryConfigure(nm, host, port, launchOptions.SimLatencyMs, launchOptions.SimLossPercent);

            // [11_tasks.md] 6-0 修正1 — UnityTransport.SetDebugSimulatorParameters は Obsolete/no-op
            // (NgoTransportConfigurator.cs 参照)なので、アプリ層の送受信キュー遅延で代替する。
            bridge.ConfigureAppLayerSimLatency(launchOptions.SimLatencyMs ?? 0);

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

            return new NgoBridgeCreateResult
            {
                Bridge = bridge,
                PendingStart = PendingStart,
                AssignHashGate = AssignHashGate,
            };
        }
    }
}
#endif // DDRIVE_NGO
