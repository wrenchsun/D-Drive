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

        // ── 2026-09-18 レビュー対応(41 テストの穴 3: タイムアウト時のイベント) ──

        // 既存の HostSide_Timeout_InRelease_Disconnects は bridge.DisconnectClientCallCount しか見ておらず、
        // ClientDisconnectedForMismatch イベント自体が発火することは検証していなかった(NetDebugOverlay/
        // 上位が購読する契約なので、呼び出しの有無とイベント発火は別に確認する必要がある)。
        [Test]
        public void HostSide_Timeout_InRelease_FiresClientDisconnectedForMismatchEvent()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, NetworkTime = 0d };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: false);
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            ulong? disconnectedId = null;
            string detail = null;
            var fireCount = 0;
            gate.ClientDisconnectedForMismatch += (id, d) => { disconnectedId = id; detail = d; fireCount++; };

            bridge.RaiseClientConnected(7);
            bridge.NetworkTime = 5.1d;
            LogAssert.ignoreFailingMessages = true;
            gate.Tick(bridge.NetworkTime);
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(1, fireCount, "タイムアウト経由でも ClientDisconnectedForMismatch が(1回だけ)発火する");
            Assert.AreEqual(7UL, disconnectedId);
            StringAssert.Contains("タイムアウト", detail, "イベント引数の detail にタイムアウトの理由が入っている");
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

        // ── 2026-09-17 レビュー対応 ──

        // P2-2: 接続後、ハッシュを送る前に切断/クラッシュした Client の保留期限を掃除する。
        // 修正前は 5 秒後に「居ない Client」をタイムアウト扱いにして、リリースビルドでは
        // Debug.LogError + DisconnectClient + ClientDisconnectedForMismatch まで発火していた。
        [Test]
        public void HostSide_ClientDisconnectedBeforeHash_DoesNotTimeoutLater()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, LocalClientId = 0, NetworkTime = 0d };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: false);
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            var mismatchNotified = false;
            gate.ClientDisconnectedForMismatch += (_, __) => mismatchNotified = true;

            bridge.RaiseClientConnected(7);
            // ハッシュを送らないまま切断(クラッシュ/回線断)。
            bridge.RaiseClientDisconnected(7, "connection lost");

            bridge.NetworkTime = 10d;
            gate.Tick(bridge.NetworkTime);

            Assert.AreEqual(0, bridge.DisconnectClientCallCount, "既に居ない Client を切断しようとしてはいけない");
            Assert.IsFalse(mismatchNotified);
            StringAssert.DoesNotContain("未受信", gate.LastStatusText, "LastStatusText(Overlay / NetCheck 表示)を誤って上書きしない");
        }

        // P2-2: Client 視点の「自分が切れた」通知は Host 側の保留台帳とは無関係(誤って掃除しない)。
        [Test]
        public void ClientSide_DisconnectedEvent_DoesNotThrow()
        {
            var bridge = new FakeNetBridge { IsServer = false, IsClient = true, LocalClientId = 42 };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            Assert.DoesNotThrow(() => bridge.RaiseClientDisconnected(42, "host stopped"));
        }

        [Test]
        public void Dispose_UnsubscribesFromClientDisconnected()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, LocalClientId = 0, NetworkTime = 0d };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: false);
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            bridge.RaiseClientConnected(7);
            gate.Dispose();

            // Dispose 後は購読していないので、切断通知を出しても保留台帳は掃除されない
            // (= Dispose 前後で購読/解除が対になっていることの確認。Tick も呼ばれない前提)。
            Assert.DoesNotThrow(() => bridge.RaiseClientDisconnected(7, "after dispose"));
        }

        // P2-3: Host のカタログ登録(Preload 込み)が timeoutSeconds より長くかかっても、先に接続して
        // 正しくハッシュを送った正規の Client を切断しない。期限は _registryReady 後から数え直す。
        [Test]
        public void HostSide_RegistryNotReady_DoesNotTimeout_AndRebasesDeadline()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, LocalClientId = 0, NetworkTime = 0d };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: false);

            // Host のカタログ登録はまだ終わっていない(SetLocalSummary 未呼び出し)。
            bridge.RaiseClientConnected(7);

            // 登録に 8 秒かかる間、Tick が回り続けてもタイムアウト扱いにしない。
            bridge.NetworkTime = 8d;
            gate.Tick(bridge.NetworkTime);
            Assert.AreEqual(0, bridge.DisconnectClientCallCount, "Host が判定できない間はタイムアウトさせない");

            // 登録完了。ここから期限を数え直すので、直後の Tick でも切断しない。
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));
            gate.Tick(bridge.NetworkTime);
            Assert.AreEqual(0, bridge.DisconnectClientCallCount, "登録完了時点から数え直す");

            // 数え直した期限内にハッシュが届けば正常解決する。
            bridge.NetworkTime = 10d;
            gate.Tick(bridge.NetworkTime);
            Assert.AreEqual(0, bridge.DisconnectClientCallCount);
            bridge.RequestBroadcastFromClient(7, new CatalogContentHashMsg { CombinedHash = 123UL, Catalogs = MakeCatalogs(123UL) }, NetChannel.ReliableOrdered);
            Assert.AreEqual("OK", gate.LastStatusText);

            // 数え直した期限を過ぎてから Tick しても、既に解決済みなので何も起きない。
            bridge.NetworkTime = 20d;
            gate.Tick(bridge.NetworkTime);
            Assert.AreEqual(0, bridge.DisconnectClientCallCount);
        }

        // P2-3: 未 ready 中に届いたハッシュ(_pendingBeforeReady)は、登録完了後に処理されて解決する。
        [Test]
        public void HostSide_HashReceivedBeforeReady_IsProcessedOnReady_WithoutTimeout()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, LocalClientId = 0, NetworkTime = 0d };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: false);

            bridge.RaiseClientConnected(7);
            bridge.RequestBroadcastFromClient(7, new CatalogContentHashMsg { CombinedHash = 123UL, Catalogs = MakeCatalogs(123UL) }, NetChannel.ReliableOrdered);

            // Host の登録完了が期限(5 秒)より遅い。
            bridge.NetworkTime = 8d;
            gate.Tick(bridge.NetworkTime);
            Assert.AreEqual(0, bridge.DisconnectClientCallCount);

            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            Assert.AreEqual("OK", gate.LastStatusText, "保留していたハッシュが登録完了後に処理される");
            Assert.AreEqual(0, bridge.DisconnectClientCallCount);

            gate.Tick(bridge.NetworkTime);
            Assert.AreEqual(0, bridge.DisconnectClientCallCount);
        }

        // P2-2 + P2-3: 未 ready 中に受信して保留したハッシュは、その Client が切断したら捨てる
        // (切断済み Client へ FlushPendingBeforeReady が SendTo しない)。
        [Test]
        public void HostSide_PendingBeforeReady_IsDroppedWhenClientDisconnects()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, LocalClientId = 0, NetworkTime = 0d };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);

            bridge.RaiseClientConnected(7);
            bridge.RequestBroadcastFromClient(7, new CatalogContentHashMsg { CombinedHash = 123UL, Catalogs = MakeCatalogs(123UL) }, NetChannel.ReliableOrdered);
            bridge.RaiseClientDisconnected(7, "connection lost");

            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            Assert.AreEqual(0, bridge.SendToCount, "切断済み Client へ結果を送らない");

            bridge.NetworkTime = 20d;
            gate.Tick(bridge.NetworkTime);
            Assert.AreEqual(0, bridge.DisconnectClientCallCount);
        }

        // ── 2026-09-18 レビュー対応(41 テストの穴 2: 保留のフラッシュ) ──

        // 既存の HostSide_HashReceivedBeforeReady_IsProcessedOnReady_WithoutTimeout は LastStatusText と
        // DisconnectClientCallCount しか見ておらず、「保留していたハッシュが SetLocalSummary で実際に
        // 処理されて Client へ結果が送られる(SendTo が呼ばれる)」こと自体は未検証だった。
        [Test]
        public void HostSide_HashReceivedBeforeReady_Flush_SendsMatchedResultToPendingClient()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, LocalClientId = 0, NetworkTime = 0d };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);

            bridge.RaiseClientConnected(7);
            bridge.RequestBroadcastFromClient(7, new CatalogContentHashMsg { CombinedHash = 123UL, Catalogs = MakeCatalogs(123UL) }, NetChannel.ReliableOrdered);

            Assert.AreEqual(0, bridge.SendToCount, "Host 未 ready の間は保留するだけで、まだ応答しない");

            gate.SetLocalSummary(123UL, MakeCatalogs(123UL));

            Assert.AreEqual(1, bridge.SendToCount, "SetLocalSummary(登録完了)時点で保留分がフラッシュされ、Client へ結果が送られる");
            Assert.AreEqual(7UL, bridge.LastSendToClientId);
            var result = (CatalogContentHashResultMsg)bridge.LastMessage;
            Assert.IsTrue(result.Matched);
        }

        // 不一致の場合もフラッシュ経路で正しく Descriptions 付きの結果が送られることを確認する
        // (ProcessHostSide の不一致分岐が FlushPendingBeforeReady 経由でも通ることの確認)。
        [Test]
        public void HostSide_MismatchedHashReceivedBeforeReady_Flush_SendsMismatchResult()
        {
            var bridge = new FakeNetBridge { IsServer = true, IsClient = true, LocalClientId = 0, NetworkTime = 0d };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);

            bridge.RaiseClientConnected(7);
            bridge.RequestBroadcastFromClient(7, new CatalogContentHashMsg { CombinedHash = 999UL, Catalogs = MakeCatalogs(999UL, 2) }, NetChannel.ReliableOrdered);

            LogAssert.ignoreFailingMessages = true;
            gate.SetLocalSummary(123UL, MakeCatalogs(123UL, 1));
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(1, bridge.SendToCount);
            var result = (CatalogContentHashResultMsg)bridge.LastMessage;
            Assert.IsFalse(result.Matched);
            Assert.IsNotEmpty(result.Descriptions);
            StringAssert.Contains("不一致", gate.LastStatusText);
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

            // 2026-09-18 レビュー対応(41 テストの穴 1) — Result は Host(senderId=0)からのみ受理する。
            // InjectReceive で「Host から」を明示的に模擬する(以前は SendTo(42, ...) が clientId=42 を
            // そのまま senderId として配送していたため、実質「自分自身から」という非現実的な模擬だった)。
            LogAssert.ignoreFailingMessages = true;
            bridge.InjectReceive(0UL, new CatalogContentHashResultMsg { Matched = false, Descriptions = new[] { "TestCatalog: entries local=1 remote=2" } });
            LogAssert.ignoreFailingMessages = false;

            StringAssert.Contains("不一致", gate.LastStatusText);
            StringAssert.Contains("TestCatalog", gate.LastStatusText);
        }

        [Test]
        public void ClientSide_ReceivesMatchedResult_SetsStatusOk()
        {
            var bridge = new FakeNetBridge { IsServer = false, IsClient = true, LocalClientId = 42 };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);

            bridge.InjectReceive(0UL, new CatalogContentHashResultMsg { Matched = true, Descriptions = System.Array.Empty<string>() });

            Assert.AreEqual("OK", gate.LastStatusText);
        }

        // ── 2026-09-18 レビュー対応(41 テストの穴 1: 偽造 Result) ──

        // OnReceiveResultMsg は senderId を見ていなかった。NgoNetBridge.Broadcast は Client 発でも
        // RequestBroadcastRpc 経由で「型登録済みなら」Host が中継してしまう(CatalogContentHashResultMsg も
        // Subscribe 済みのため型登録される)。中継時の senderId は真の送信元(攻撃者)になるため、改造
        // Client が Matched=true を騙って本物の Host 判定(不一致)を「OK」に上書きできてしまっていた。
        [Test]
        public void ClientSide_ForgedResultFromNonHostSender_IsIgnored()
        {
            var bridge = new FakeNetBridge { IsServer = false, IsClient = true, LocalClientId = 42 };
            var gate = new CatalogContentHashGate(bridge, timeoutSeconds: 5d, isDevelopmentOrEditor: true);

            // 前提: Host(senderId=0)からの本物の不一致通知を受け取っている。
            LogAssert.ignoreFailingMessages = true;
            bridge.InjectReceive(0UL, new CatalogContentHashResultMsg { Matched = false, Descriptions = new[] { "TestCatalog: entries local=1 remote=2" } });
            LogAssert.ignoreFailingMessages = false;
            StringAssert.Contains("不一致", gate.LastStatusText, "前提条件: Host からの本物の不一致通知");

            // 改造 Client(senderId=99。Host=0 でも自分自身でもない)が偽の Matched=true を送りつける。
            bridge.InjectReceive(99UL, new CatalogContentHashResultMsg { Matched = true, Descriptions = System.Array.Empty<string>() });

            StringAssert.Contains("不一致", gate.LastStatusText, "Host 以外からの Result は無視され、本物の不一致状態が保たれる");
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
