using System;
using System.Globalization;

namespace DDrive.Runtime.Net
{
    // [14_networking.md] §14(N-2、2026-09-22) — 開発用の手動接続 UI(NetManualConnectOverlay、
    // Runtime/Ngo/NetManualConnectOverlay.cs)が使う、Unity API に非依存の入力検証。IPv4 のドット表記のみを
    // 許可する(ホスト名は不可。ただし "localhost" だけ "127.0.0.1" に読み替える)。UI 側からもテスト
    // (Tests/Editor/NetManualConnectInputTests.cs)からも同じ純関数を呼ぶことで、OnGUI の毎フレーム経路には
    // 検証ロジックそのものを置かない([CLAUDE.md] §0-3「定常経路で LINQ・クロージャ・boxing 禁止」の趣旨に
    // 沿い、検証はボタン押下時にしか呼ばれないため実質無関係だが、ロジックを共有できるようにするため分離する)。
    public static class NetManualConnectInput
    {
        private const string LocalhostAlias = "localhost";
        private const string LocalhostAddress = "127.0.0.1";

        // Host 開始は Port だけが要る(接続先アドレスは既存の DefaultHostAddress/-ddrive-host のまま、
        // [14_networking.md] §14)。IP の妥当性を問わない Host 用に単独で公開する。
        public static bool TryParsePort(string port, out ushort portValue, out string error)
        {
            portValue = 0;
            error = null;

            if (string.IsNullOrWhiteSpace(port))
            {
                error = "Port を入力してください。";
                return false;
            }

            if (!int.TryParse(port.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                error = $"Port は数値で入力してください({port})。";
                return false;
            }

            if (parsed < 1 || parsed > 65535)
            {
                error = $"Port は 1〜65535 の範囲で入力してください({parsed})。";
                return false;
            }

            portValue = (ushort)parsed;
            return true;
        }

        // Client 接続は IP + Port の両方が要る。
        public static bool TryParse(string ip, string port, out string address, out ushort portValue, out string error)
        {
            address = null;

            if (!TryParsePort(port, out portValue, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(ip))
            {
                error = "IP アドレスを入力してください。";
                return false;
            }

            var trimmed = ip.Trim();
            if (string.Equals(trimmed, LocalhostAlias, StringComparison.OrdinalIgnoreCase))
            {
                address = LocalhostAddress;
                return true;
            }

            if (!IsIPv4DotNotation(trimmed))
            {
                error = $"IP アドレスは IPv4 のドット表記で入力してください(ホスト名不可。localhost のみ例外)({ip})。";
                return false;
            }

            address = trimmed;
            return true;
        }

        // System.Net.IPAddress.TryParse は IPv6 や短縮表記(例: "1234")も受け入れてしまうため、
        // 「0-255 の数値を . で 4 つ区切っただけの表記」だけを厳密に受け付ける専用の検証にする
        // (要件: ホスト名は許可しない、IPv4 ドット表記のみ)。
        private static bool IsIPv4DotNotation(string value)
        {
            var parts = value.Split('.');
            if (parts.Length != 4)
            {
                return false;
            }

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.Length == 0 || part.Length > 3)
                {
                    return false;
                }

                for (var j = 0; j < part.Length; j++)
                {
                    if (part[j] < '0' || part[j] > '9')
                    {
                        return false;
                    }
                }

                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var num) || num > 255)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
