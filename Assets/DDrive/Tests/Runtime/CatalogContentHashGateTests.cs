using DDrive.Foundation.Net;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Net;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    // [11_tasks.md] 6-5 / [14_networking.md] §7 — カタログ ContentHash の接続時照合(Host 権威)。
    // 既存の FakeNetBridge(Tests/Runtime/FakeNetBridge.cs、4-13/6-0 で追加)を使い、実 NGO には触れない。
    // 実 GameData・カタログ・Addressables には一切書き込まない(ScriptableObject.CreateInstance も使わず、
    // CatalogContentHasher.CatalogHashEntry を直接組み立てる)。
    public class CatalogContentHashGateTests
    {
        private static CatalogContentHasher.CatalogHashEntry[] MakeCatalogs(ulong hash, int entryCount = 1)
            => new[] { new CatalogContentHasher.CatalogHashEntry { CatalogName = "TestCatalog", Hash = hash, EntryCount = entryCount } };

        // ── Host 側 ──

        [Test]
        public void HostSide_MatchingHash_SendsMatchedResult_AndDoesNotDisconnect()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, LocalClientId = 0 };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            bridge.RaiseClientConnected(5);
            var accepted = bridge.RequestBroadcastFromClient(5, new CatalogContentHashMsg { CombinedHash = 123UL, Catalogs = MakeCatalogs(123UL) }, NetChannel.ReliableOrdered);

            Assert.IsTrue(accepted);
            Assert.AreEqual(0, bridge.DisconnectClientCallCount);
            Assert.AreEqual(1, bridge.SendToCount);
            Assert.AreEqual(5UL, bridge.LastSendToClientId);
            var result = (CatalogContentHashResultMsg)bridge.LastMessage;
            Assert.IsTrue(result.Matched);
            Assert.AreEqual("OK", gate.LastStatusText);
        }

        [Test]
        public void HostSide_MismatchInDevelopment_WarnsAndContinues()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL, 10));

            bridge.RaiseClientConnected(5);
            LogAssert.ignoreFailingMessages = true;
            bridge.RequestBroadcastFromClient(5, new CatalogContentHashMsg { CombinedHash = 999UL, Catalogs = MakeCatalogs(999UL, 11) }, NetChannel.ReliableOrdered);
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(0, bridge.DisconnectClientCallCount, "開発ビルド/エディタでは切断しない");
            Assert.AreEqual(1, bridge.SendToCount, "不一致でも Client へ結果を通知する(双方に警告)");
            var result = (CatalogContentHashResultMsg)bridge.LastMessage;
            Assert.IsFalse(result.Matched);
            Assert.IsNotEmpty(result.Descriptions);
            StringAssert.Contains("TestCatalog", result.Descriptions[0]);
            StringAssert.Contains("不一致", gate.LastStatusText);
        }

        [Test]
        public void HostSide_MismatchInRelease_DisconnectsClient_AndSendsNoFurtherMessage()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: false);
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            ulong? disconnectedId = null;
            gate.ClientDisconnectedForMismatch += (id, _) => disconnectedId = id;

            bridge.RaiseClientConnected(5);
            LogAssert.ignoreFailingMessages = true;
            bridge.RequestBroadcastFromClient(5, new CatalogContentHashMsg { CombinedHash = 999UL, Catalogs = MakeCatalogs(999UL) }, NetChannel.ReliableOrdered);
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(1, bridge.DisconnectClientCallCount, "リリースビルドでは切断する");
            Assert.AreEqual(5UL, bridge.LastDisconnectedClientId);
            Assert.AreEqual(5UL, disconnectedId);
            Assert.AreEqual(0, bridge.SendToCount, "切断済みの Client へ追加のメッセージは送らない");
        }

        [Test]
        public void HostSide_Timeout_InDevelopment_WarnsWithoutDisconnect()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, NetworkTime = 0d };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            bridge.RaiseClientConnected(7);
            // まだハッシュを送っていない状態でタイムアウト期限を過ぎる(偽装/遅延クライアントの模擬)。
            bridge.NetworkTime = 5.1d;
            LogAssert.ignoreFailingMessages = true;
            gate.Tick(bridge.NetworkTime);
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(0, bridge.DisconnectClientCallCount);
            StringAssert.Contains("未受信", gate.LastStatusText);
        }

        [Test]
        public void HostSide_Timeout_InRelease_Disconnects()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, NetworkTime = 0d };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: false);
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            bridge.RaiseClientConnected(7);
            bridge.NetworkTime = 5.1d;
            LogAssert.ignoreFailingMessages = true;
            gate.Tick(bridge.NetworkTime);
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(1, bridge.DisconnectClientCallCount);
            Assert.AreEqual(7UL, bridge.LastDisconnectedClientId);
        }

        [Test]
        public void HostSide_BeforeTimeoutDeadline_DoesNotDisconnect()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, NetworkTime = 0d };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: false);
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            bridge.RaiseClientConnected(7);
            bridge.NetworkTime = 2d;
            gate.Tick(bridge.NetworkTime);

            Assert.AreEqual(0, bridge.DisconnectClientCallCount);
        }

        [Test]
        public void HostSide_ResolvedBeforeDeadline_DoesNotTimeoutLater()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, NetworkTime = 0d };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: false);
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            bridge.RaiseClientConnected(7);
            bridge.RequestBroadcastFromClient(7, new CatalogContentHashMsg { CombinedHash = 123UL, Catalogs = MakeCatalogs(123UL) }, NetChannel.ReliableOrdered);

            bridge.NetworkTime = 10d; // 元の期限をはるかに過ぎても、既に解決済みなのでタイムアウトしない。
            gate.Tick(bridge.NetworkTime);

            Assert.AreEqual(0, bridge.DisconnectClientCallCount);
        }

        [Test]
        public void HostSide_SelfConnectEvent_DoesNotCreatePendingDeadline_AndNeverTimesOut()
        {
            // 2026-09-15 修正(6-7 の自動テストで発覚した実バグ) — NGO の OnClientConnectedCallback は
            // Host 自身の自己接続(StartHost)でも発火する(clientId == Host の LocalClientId)。修正前は
            // この自己分にも保留期限を登録してしまい、Host は自分にハッシュを送らない(TrySendOwnHash が
            // IsServer を弾く)ため、実クライアントの有無に関わらず必ずタイムアウトして LastStatusText が
            // "OK" → "ContentHash 未受信" に戻っていた(実機・run-netcheck.cmd の全シナリオで再現)。
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, LocalClientId = 0, NetworkTime = 0d };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            // Host 自身の自己接続イベント(clientId == LocalClientId)。
            bridge.RaiseClientConnected(0);

            // 実クライアント(id=5)が接続し、ハッシュも一致して即座に解決する。
            bridge.RaiseClientConnected(5);
            bridge.RequestBroadcastFromClient(5, new CatalogContentHashMsg { CombinedHash = 123UL, Catalogs = MakeCatalogs(123UL) }, NetChannel.ReliableOrdered);
            Assert.AreEqual("OK", gate.LastStatusText);

            // タイムアウト期限をはるかに過ぎても、自己分の保留エントリが残っていなければ何も起きない
            // (修正前はここで自己分がタイムアウトし、LastStatusText が "ContentHash 未受信" に戻っていた)。
            bridge.NetworkTime = 10d;
            gate.Tick(bridge.NetworkTime);

            Assert.AreEqual("OK", gate.LastStatusText);
            Assert.AreEqual(0, bridge.DisconnectClientCallCount);
        }

        [Test]
        public void HostSide_SelfConnectEvent_AloneWithNoRealClient_NeverTimesOut()
        {
            // 実クライアントが 1 人も居ない(Loopback/シングルプレイ相当)場合でも、自己接続イベントだけで
            // タイムアウト扱いにならないことを確認する。
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, LocalClientId = 0, NetworkTime = 0d };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            bridge.RaiseClientConnected(0);

            bridge.NetworkTime = 10d;
            gate.Tick(bridge.NetworkTime);

            Assert.AreEqual(0, bridge.DisconnectClientCallCount);
            StringAssert.DoesNotContain("未受信", gate.LastStatusText);
        }

        // ── Client 側 ──

        [Test]
        public void ClientSide_SendsOwnHash_OnlyAfterConnectedAndRegistryReady()
        {
            var bridge = new FakeNetBridge { IsServer = false, IsClient = true, LocalClientId = 42 };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);

            // カタログ登録が先に終わっても、接続前は送らない。
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));
            Assert.AreEqual(0, bridge.BroadcastCount);

            bridge.RaiseClientConnected(42);
            Assert.AreEqual(1, bridge.BroadcastCount);
            var sent = (CatalogContentHashMsg)bridge.LastMessage;
            Assert.AreEqual(123UL, sent.CombinedHash);

            // 二重送信しない(再接続通知等で複数回呼ばれても 1 回だけ)。
            bridge.RaiseClientConnected(42);
            Assert.AreEqual(1, bridge.BroadcastCount);
        }

        [Test]
        public void ClientSide_ConnectedBeforeRegistryReady_SendsOnceReady()
        {
            var bridge = new FakeNetBridge { IsServer = false, IsClient = true, LocalClientId = 42 };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);

            bridge.RaiseClientConnected(42);
            Assert.AreEqual(0, bridge.BroadcastCount, "登録前は送らない");

            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));
            Assert.AreEqual(1, bridge.BroadcastCount, "登録完了時点で送る");
        }

        [Test]
        public void ClientSide_ReceivesMismatchResult_UpdatesStatusAndLogs()
        {
            var bridge = new FakeNetBridge { IsServer = false, IsClient = true, LocalClientId = 42 };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);

            LogAssert.ignoreFailingMessages = true;
            bridge.SendTo(42, new CatalogContentHashResultMsg { Matched = false, Descriptions = new[] { "TestCatalog: entries local=1 remote=2" } }, NetChannel.ReliableOrdered);
            LogAssert.ignoreFailingMessages = false;

            StringAssert.Contains("不一致", gate.LastStatusText);
            StringAssert.Contains("TestCatalog", gate.LastStatusText);
        }

        [Test]
        public void ClientSide_ReceivesMatchedResult_SetsStatusOk()
        {
            var bridge = new FakeNetBridge { IsServer = false, IsClient = true, LocalClientId = 42 };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);

            bridge.SendTo(42, new CatalogContentHashResultMsg { Matched = true, Descriptions = System.Array.Empty<string>() }, NetChannel.ReliableOrdered);

            Assert.AreEqual("OK", gate.LastStatusText);
        }

        [Test]
        public void Dispose_UnsubscribesFromClientConnected()
        {
            var bridge = new FakeNetBridge { IsServer = false, IsClient = true, LocalClientId = 42 };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            gate.Dispose();
            bridge.RaiseClientConnected(42);

            Assert.AreEqual(0, bridge.BroadcastCount, "Dispose 後は ClientConnected を購読していない");
        }
    }
}
