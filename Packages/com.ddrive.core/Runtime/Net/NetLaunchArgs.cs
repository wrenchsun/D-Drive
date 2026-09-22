using System;
using System.Globalization;

namespace DDrive.Runtime.Net
{
    // [14_networking.md] §12 / [11_tasks.md] 6-0(B) — 開発ビルド用のコマンドライン引数。
    // Unspecified は「引数で指定されなかった」を表し、呼び出し側(DDriveRuntimeBootstrap)が
    // Inspector の既定値にフォールバックする。Off は「明示的にシングルプレイ(Loopback)を強制する」。
    public enum NetLaunchRole
    {
        Unspecified,
        Off,
        Host,
        Client,

        // [14_networking.md] N-1(2026-09-22) — NGO ブリッジは解決するが StartHost/StartClient を自動で
        // 呼ばない(開発用の手動接続。DDriveRuntimeBootstrap.StartHost/StartClient/StopNetworking を
        // 呼ぶまで待つ)。既存の Host/Client/Off の判定はどれも変えない([42_distribution.md] §5「追加のみ」)。
        Manual,
    }

    // 純粋なデータ(Unity API 非依存)。EditMode テストで容易に検証できるようにするため、
    // パース処理(NetLaunchArgs.Parse)と Bootstrap への適用処理を分離してある。
    public struct NetLaunchOptions
    {
        public NetLaunchRole Role;
        public string Host;           // -ddrive-host。未指定は null(呼び出し側が既定値を使う)
        public int? Port;             // -ddrive-port
        public int? SimLatencyMs;     // -ddrive-sim-latency
        public float? SimLossPercent; // -ddrive-sim-loss(0-100)
        public string AutoTestName;   // -ddrive-autotest
        public float? AutoTestSeconds; // -ddrive-autotest-seconds([11_tasks.md] 6-7。未指定は NetCheckRunner の既定式にフォールバック)

        // [14_networking.md] §16(N-3、2026-09-22) — Host 1 + Client 3 の接続確認用。NetCheckRunner が
        // Host 役のときだけ「NgoNetBridge.ConnectedClientCount がこの人数に達したか」を PASS 条件に加える
        // (未指定/Client 役では従来どおり判定をスキップする)。
        public int? ExpectedClientCount; // -ddrive-expect-clients
    }

    // [11_tasks.md] 6-0(B) — コマンドライン引数パーサ。Unity API に依存しない純関数のため、
    // 実引数(System.Environment.GetCommandLineArgs())とテスト用の任意配列の両方をそのまま渡せる。
    public static class NetLaunchArgs
    {
        public const string NetFlag = "-ddrive-net";
        public const string HostFlag = "-ddrive-host";
        public const string PortFlag = "-ddrive-port";
        public const string SimLatencyFlag = "-ddrive-sim-latency";
        public const string SimLossFlag = "-ddrive-sim-loss";
        public const string AutoTestFlag = "-ddrive-autotest";
        public const string AutoTestSecondsFlag = "-ddrive-autotest-seconds"; // [11_tasks.md] 6-7
        public const string ExpectClientsFlag = "-ddrive-expect-clients"; // [14_networking.md] §16(N-3)

        public static NetLaunchOptions Parse(string[] args)
        {
            var result = new NetLaunchOptions { Role = NetLaunchRole.Unspecified };
            if (args == null)
            {
                return result;
            }

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case NetFlag:
                        result.Role = ParseRole(NextValue(args, ref i));
                        break;

                    case HostFlag:
                        result.Host = NextValue(args, ref i);
                        break;

                    case PortFlag:
                        if (int.TryParse(NextValue(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
                        {
                            result.Port = port;
                        }

                        break;

                    case SimLatencyFlag:
                        if (int.TryParse(NextValue(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var latency))
                        {
                            result.SimLatencyMs = latency;
                        }

                        break;

                    case SimLossFlag:
                        if (float.TryParse(NextValue(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out var loss))
                        {
                            result.SimLossPercent = loss;
                        }

                        break;

                    case AutoTestFlag:
                        result.AutoTestName = NextValue(args, ref i);
                        break;

                    case AutoTestSecondsFlag:
                        if (float.TryParse(NextValue(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out var autoTestSeconds))
                        {
                            result.AutoTestSeconds = autoTestSeconds;
                        }

                        break;

                    case ExpectClientsFlag:
                        if (int.TryParse(NextValue(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var expectClients))
                        {
                            result.ExpectedClientCount = expectClients;
                        }

                        break;
                }
            }

            return result;
        }

        // 次の要素を値として消費する。次要素が別の -ddrive-* フラグ、または配列の終端なら値なし(null)。
        private static string NextValue(string[] args, ref int i)
        {
            if (i + 1 < args.Length && !LooksLikeFlag(args[i + 1]))
            {
                i++;
                return args[i];
            }

            return null;
        }

        private static bool LooksLikeFlag(string arg) => arg != null && arg.StartsWith("-ddrive-", StringComparison.Ordinal);

        private static NetLaunchRole ParseRole(string value)
        {
            switch (value?.ToLowerInvariant())
            {
                case "host":
                    return NetLaunchRole.Host;
                case "client":
                    return NetLaunchRole.Client;
                case "off":
                    return NetLaunchRole.Off;
                case "manual":
                    return NetLaunchRole.Manual;
                default:
                    return NetLaunchRole.Unspecified;
            }
        }

        // [14_networking.md] N-1(2026-09-22) — DDriveRuntimeBootstrap.ResolveNetBridge の役割解決を
        // Unity API 非依存の純関数に切り出したもの(EditMode テストで検証するため、Parse と同じ方針)。
        // CLI(`-ddrive-net`)で明示されていればそれを最優先し、未指定のときだけ Inspector の既定値
        // (DefaultNetBridge/DefaultNetStart)から導く。defaultBridgeIsNgo=false のときは常に Off
        // (DefaultNetStart の値に関わらず。既存の挙動を変えないための既定 = Loopback/Auto)。
        public static NetLaunchRole ResolveEffectiveRole(NetLaunchRole cliRole, bool defaultBridgeIsNgo, bool defaultStartIsManual)
        {
            if (cliRole != NetLaunchRole.Unspecified)
            {
                return cliRole;
            }

            if (!defaultBridgeIsNgo)
            {
                return NetLaunchRole.Off;
            }

            return defaultStartIsManual ? NetLaunchRole.Manual : NetLaunchRole.Host;
        }
    }
}
