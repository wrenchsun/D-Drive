using System;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // [14_networking.md] §12 / [11_tasks.md] 6-0(A/B) — NetworkManager.NetworkConfig.NetworkTransport の
    // IP/Port とネットワークシミュレータ(遅延/損失)を設定する。
    //
    // UnityTransport クラス自体は "Unity.Netcode.Runtime"(DDrive.Runtime.asmdef が既に参照済み)に同梱
    // されているが、そのメソッド(SetConnectionData 等)のオーバーロードの一部が引数に取る
    // `Unity.Networking.Transport.NetworkEndpoint` は別アセンブリ(com.unity.transport、未参照)にあるため、
    // 型を直接 using して呼ぶと C# コンパイラがオーバーロード解決のために未参照アセンブリの読み込みを要求し
    // コンパイルエラーになる(実際に確認した。CS0012)。asmdef 変更は要判断のため 6-0 では見送り、
    // リフレクションで名前解決して呼ぶことでこれを回避する(型/メソッドが見つからない場合は警告 1 回だけ
    // 出して no-op で継続する。[CLAUDE.md] TL;DR 4「例外で止めない」)。
    // 要判断: DDrive.Runtime.asmdef に Unity.Networking.Transport を正式参照として追加すれば型安全に呼べる。
    public static class NgoTransportConfigurator
    {
        private static bool _warnedMissingTransport;
        private static bool _warnedMissingSetConnectionData;
        private static bool _warnedMissingSimulator;

        // Host/Client 開始前に呼ぶ。IP/Port を設定し、指定があればシミュレータ(遅延/損失)も設定する。
        public static bool TryConfigure(NetworkManager nm, string host, ushort port, int? simLatencyMs, float? simLossPercent)
        {
            if (nm == null || nm.NetworkConfig == null || nm.NetworkConfig.NetworkTransport == null)
            {
                WarnOnce(ref _warnedMissingTransport, "[Net] NgoTransportConfigurator: NetworkManager.NetworkConfig.NetworkTransport が未設定のため IP/Port を設定できません(シーンに UnityTransport を追加した NetworkManager を置いてください)。");
                return false;
            }

            var transport = nm.NetworkConfig.NetworkTransport;
            var type = transport.GetType();

            // SetConnectionData(string, ushort, string) オーバーロードだけを名前・引数個数で狙う
            // (NetworkEndpoint を取る別オーバーロードは無視される。実行時確認済み: UnityTransport 2.13.2 相当)。
            MethodInfo setConnectionData = null;
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "SetConnectionData")
                {
                    continue;
                }

                var p = m.GetParameters();
                if (p.Length == 3 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(ushort) && p[2].ParameterType == typeof(string))
                {
                    setConnectionData = m;
                    break;
                }
            }

            if (setConnectionData == null)
            {
                WarnOnce(ref _warnedMissingSetConnectionData, $"[Net] NgoTransportConfigurator: {type.FullName}.SetConnectionData(string,ushort,string) が見つからないため IP/Port を設定できませんでした。");
            }
            else
            {
                try
                {
                    setConnectionData.Invoke(transport, new object[] { host, port, null });
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Net] NgoTransportConfigurator.SetConnectionData 呼び出しに失敗しました: {e.Message}");
                }
            }

            if (simLatencyMs.HasValue || simLossPercent.HasValue)
            {
                ApplySimulatorParams(transport, type, simLatencyMs ?? 0, simLossPercent ?? 0f);
            }

            return true;
        }

        private static void ApplySimulatorParams(object transport, Type type, int simLatencyMs, float simLossPercent)
        {
            // UnityTransport.SetDebugSimulatorParameters(int packetDelay, int packetJitter, int dropRate)。
            // dropRate は 0-100 のドロップ率(%)そのもの(実行時にリフレクションで確認済み)。
            var method = type.GetMethod("SetDebugSimulatorParameters", BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                WarnOnce(ref _warnedMissingSimulator, $"[Net] NgoTransportConfigurator: {type.FullName} にシミュレータ設定 API が見つからないため -ddrive-sim-latency/-ddrive-sim-loss は無視されました。");
                return;
            }

            var parameters = method.GetParameters();
            var args = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var pType = parameters[i].ParameterType;
                object value = 0;
                var nameLower = parameters[i].Name.ToLowerInvariant();
                if (nameLower.Contains("delay"))
                {
                    value = simLatencyMs;
                }
                else if (nameLower.Contains("drop") || nameLower.Contains("loss"))
                {
                    value = Mathf.Clamp(Mathf.RoundToInt(simLossPercent), 0, 100);
                }

                args[i] = Convert.ChangeType(value, pType);
            }

            try
            {
                method.Invoke(transport, args);
                Debug.Log($"[Net] NgoTransportConfigurator: シミュレータ設定を適用しました(latency={simLatencyMs}ms, loss={simLossPercent}%)。");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Net] NgoTransportConfigurator.SetDebugSimulatorParameters 呼び出しに失敗しました: {e.Message}");
            }
        }

        // RTT 表示用(NetDebugOverlay)。NetworkTransport 基底クラス(Unity.Netcode.Runtime 内、パラメータは
        // ulong のみで NetworkEndpoint 等の未参照型を経由しない)の API なので直接呼べる。
        public static double? TryGetRoundTripTimeMs(NetworkManager nm, ulong clientId)
        {
            var transport = nm?.NetworkConfig?.NetworkTransport;
            if (transport == null)
            {
                return null;
            }

            try
            {
                return (double)transport.GetCurrentRtt(clientId);
            }
            catch
            {
                return null;
            }
        }

        private static void WarnOnce(ref bool flag, string message)
        {
            if (flag)
            {
                return;
            }

            flag = true;
            Debug.LogWarning(message);
        }
    }
}
