#if DDRIVE_NGO
using System.Collections;
using System.Reflection;
using DDrive.Foundation.Net;
using DDrive.Runtime.Net;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime.Ngo
{
    // [14_networking.md] §19「D-2 の検証方針」/ §20(DD-2、M-3f、2026-09-27) — Host+Client のインプロセス
    // 構成は NGO の制約(1 プロセスに NetworkManager を 1 つしか持てない)で PlayMode でも組めないため、
    // ここでは Host 単体の NetworkManager.Shutdown() → 同一プロセスでの再 StartHost() に絞って、常駐
    // GameObject 上の in-scene NetworkObject(NgoNetBridge)が再 Spawn/再接続できることを検証する。
    //
    // 実行時に new GameObject() で作った NetworkObject は、NGO の「in-scene 配置」判定
    // (NetworkObject.CheckForInScenePlaced が GlobalObjectIdHash からシーンアセットとの対応を見て決める。
    // com.unity.netcode.gameobjects@.../Runtime/Core/NetworkObject.cs 参照)を自然には満たさない
    // (InScenePlaced=false のまま)。これだと NetworkSpawnManager.DespawnAndDestroyNetworkObjects が
    // Shutdown 時に GameObject ごと破棄してしまい(shouldDestroy = !InScenePlaced)、「常駐 GameObject」の
    // 検証にならない。そのため reflection で InScenePlaced=true を明示的に立て、本番のシーン配置状態を
    // 模している(セッターが internal なため。RuntimeBootstrapTests 等、既存テストの reflection 利用と同じ
    // 考え方)。InScenePlaced=true にしておけば、NetworkSpawnManager.ServerSpawnSceneObjectsOnStartSweep
    // (StartHost/StartServer のたびに呼ばれる)が対象として拾い、Shutdown でも GameObject を破棄しない。
    public class NgoNetBridgeRestartTests
    {
        private const ushort PortForBasicRestart = 7797;
        private const ushort PortForResetSessionStateRestart = 7798;

        private struct RestartProbeMsg : INetMessage
        {
            public int Value;
        }

        private GameObject _nmGo;
        private GameObject _bridgeGo;
        private NetworkManager _nm;
        private NgoNetBridge _bridge;
        private NetworkObject _bridgeNetObj;

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            if (_nm != null && _nm.IsListening)
            {
                _nm.Shutdown();
            }

            if (_bridgeGo != null)
            {
                Object.DestroyImmediate(_bridgeGo);
            }

            if (_nmGo != null)
            {
                Object.DestroyImmediate(_nmGo);
            }
        }

        // [docs/14_networking.md] §12 — NetworkManager 自身の Awake()/OnEnable() が済む前に
        // StartHost()/StartClient() を呼ぶと NullReferenceException になる実バグが過去にあったため、
        // 生成後 1 フレーム待ってから開始する。NetworkManager と NetworkObject は同じ GameObject に
        // 置けない(NGO が警告し機能しない)ため、NgoNetBridge 用に別 GameObject を用意する(同じ理由の
        // 既存コメントが NgoBridgeFactoryInstaller 周辺の docs にもある)。
        private IEnumerator SetUpHostAsync()
        {
            _nmGo = new GameObject("NgoNetBridgeRestartTests_NetworkManager");
            _nm = _nmGo.AddComponent<NetworkManager>();
            _nm.NetworkConfig = new NetworkConfig { EnableSceneManagement = false };
            var transport = _nmGo.AddComponent<UnityTransport>();
            _nm.NetworkConfig.NetworkTransport = transport;

            _bridgeGo = new GameObject("NgoNetBridgeRestartTests_Bridge");
            _bridgeNetObj = _bridgeGo.AddComponent<NetworkObject>();
            _bridge = _bridgeGo.AddComponent<NgoNetBridge>();
            MarkAsInScenePlaced(_bridgeNetObj);

            yield return null; // Awake/OnEnable を済ませる
        }

        private static void MarkAsInScenePlaced(NetworkObject netObj)
        {
            var prop = typeof(NetworkObject).GetProperty("InScenePlaced", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(prop, "NetworkObject.InScenePlaced プロパティの名前が変わっていないか確認する");
            prop.SetValue(netObj, true);
        }

        private IEnumerator WaitForShutdownComplete(float timeoutSeconds = 5f)
        {
            var start = Time.realtimeSinceStartup;
            while (_nm.ShutdownInProgress)
            {
                if (Time.realtimeSinceStartup - start > timeoutSeconds)
                {
                    Assert.Fail("NetworkManager.Shutdown() が時間内に完了しませんでした(ShutdownInProgress が true のまま)。");
                }

                yield return null;
            }
        }

        private bool StartHostOn(ushort port)
        {
            Assert.IsTrue(
                NgoTransportConfigurator.TryConfigure(_nm, "127.0.0.1", port, null, null, listenAddress: "127.0.0.1"),
                "NgoTransportConfigurator.TryConfigure に失敗しました");
            return _nm.StartHost();
        }

        [UnityTest]
        public IEnumerator StopThenStartHost_ReSpawnsInSceneBridge_AndReconnects()
        {
            // NGO 自体が接続/切断まわりで多数の Log/LogWarning を出すため(既存の RuntimeBootstrapTests と
            // 同じ方針で)無視する。意味のある検証は Assert 側で行う。
            LogAssert.ignoreFailingMessages = true;

            yield return SetUpHostAsync();

            Assert.IsTrue(StartHostOn(PortForBasicRestart), "1 回目の StartHost() が失敗しました");
            yield return null;
            yield return null;
            yield return null;

            Assert.IsTrue(_bridgeNetObj.IsSpawned, "1 回目の StartHost 後、in-scene 配置の NgoNetBridge が自動 Spawn されること");
            Assert.IsTrue(_bridge.IsConnected, "1 回目の StartHost 後、IsConnected が true であること");
            Assert.AreEqual(NetworkManager.ServerClientId, _bridge.LocalClientId, "Host の LocalClientId は ServerClientId と一致する");

            _nm.Shutdown();
            yield return WaitForShutdownComplete();

            Assert.IsFalse(_bridgeNetObj.IsSpawned, "Shutdown 後、NgoNetBridge は Despawn されていること");
            Assert.IsNotNull(_bridgeGo, "InScenePlaced=true のため、Shutdown で GameObject 自体は破棄されないこと(常駐 GameObject の前提)");

            // ── 2 回目の StartHost(再 Start) ──
            Assert.IsTrue(StartHostOn(PortForBasicRestart), "2 回目の StartHost() が失敗しました");
            yield return null;

            // [14_networking.md] §19 — NGO が自動で再 Spawn したかどうかを、明示 Spawn の保険を通す前の
            // 「素の」状態で判別する。
            var autoRespawned = _bridgeNetObj.IsSpawned;
            if (!autoRespawned)
            {
                // NgoBridgeFactoryInstaller.DoManualStartHost と同じ保険(§19)。
                _bridgeNetObj.Spawn();
                Debug.LogWarning("[Test] NgoNetBridgeRestartTests: 2 回目の StartHost で自動 Spawn されなかったため明示的に Spawn しました(保険が発火、docs/14_networking.md §19 参照)。");
            }

            Debug.Log($"[Test] NgoNetBridgeRestartTests: NGO が自動で再 Spawn したか(素の挙動) = {autoRespawned}");

            yield return null;
            yield return null;

            Assert.IsTrue(_bridgeNetObj.IsSpawned, "2 回目の StartHost 後、NgoNetBridge が Spawn 済みであること(自動 or 保険)");
            Assert.IsTrue(_bridge.IsConnected, "2 回目の StartHost 後、IsConnected が true であること");
            Assert.AreEqual(NetworkManager.ServerClientId, _bridge.LocalClientId, "再接続後も Host の LocalClientId は ServerClientId と一致する");

            // Broadcast が例外や警告無しに機能すること(Host 自身も SendTo.ClientsAndHost の RPC で受信する)。
            var received = false;
            using (_bridge.Subscribe<RestartProbeMsg>((senderId, msg) => received = msg.Value == 123))
            {
                _bridge.Broadcast(new RestartProbeMsg { Value = 123 }, NetChannel.ReliableOrdered);
                yield return null;
                yield return null;
            }

            Assert.IsTrue(received, "再 Start 後も Broadcast が Host 自身に届くこと");
        }

        [UnityTest]
        public IEnumerator StopThenStartHost_WithResetSessionStateBeforeRestart_AlsoReconnects()
        {
            LogAssert.ignoreFailingMessages = true;

            yield return SetUpHostAsync();

            Assert.IsTrue(StartHostOn(PortForResetSessionStateRestart), "1 回目の StartHost() が失敗しました");
            yield return null;
            yield return null;
            yield return null;

            Assert.IsTrue(_bridgeNetObj.IsSpawned);
            Assert.IsTrue(_bridge.IsConnected);

            // NgoBridgeFactoryInstaller.DoManualStop と同じ順序(Shutdown → ResetSessionState、N-5)。
            _nm.Shutdown();
            _bridge.ResetSessionState();
            yield return WaitForShutdownComplete();

            Assert.IsFalse(_bridgeNetObj.IsSpawned);

            Assert.IsTrue(StartHostOn(PortForResetSessionStateRestart), "2 回目の StartHost() が失敗しました(ResetSessionState 経路)");
            yield return null;

            var autoRespawned = _bridgeNetObj.IsSpawned;
            if (!autoRespawned)
            {
                _bridgeNetObj.Spawn();
                Debug.LogWarning("[Test] NgoNetBridgeRestartTests: ResetSessionState 経路でも自動 Spawn されなかったため明示的に Spawn しました。");
            }

            Debug.Log($"[Test] NgoNetBridgeRestartTests(ResetSessionState 経路): NGO が自動で再 Spawn したか(素の挙動) = {autoRespawned}");

            yield return null;
            yield return null;

            Assert.IsTrue(_bridgeNetObj.IsSpawned);
            Assert.IsTrue(_bridge.IsConnected);
            Assert.AreEqual(NetworkManager.ServerClientId, _bridge.LocalClientId);
            Assert.AreEqual(0, _bridge.ReceivedMessageCount, "ResetSessionState 後は受信カウントが 0 にリセットされていること");
        }
    }
}
#endif // DDRIVE_NGO
