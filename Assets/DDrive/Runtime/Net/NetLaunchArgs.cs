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
                default:
                    return NetLaunchRole.Unspecified;
            }
        }
    }
}
