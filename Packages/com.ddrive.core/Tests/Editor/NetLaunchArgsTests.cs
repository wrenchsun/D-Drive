using DDrive.Runtime.Net;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] 6-0(B) — コマンドライン引数パーサ(NetLaunchArgs)の EditMode テスト。
    // Unity API に依存しない純関数のため EditMode(高速)で検証する。
    public class NetLaunchArgsTests
    {
        [Test]
        public void Parse_NoArgs_ReturnsUnspecified()
        {
            var result = NetLaunchArgs.Parse(new string[0]);
            Assert.AreEqual(NetLaunchRole.Unspecified, result.Role);
            Assert.IsNull(result.Host);
            Assert.IsNull(result.Port);
            Assert.IsNull(result.SimLatencyMs);
            Assert.IsNull(result.SimLossPercent);
            Assert.IsNull(result.AutoTestName);
            Assert.IsNull(result.AutoTestSeconds);
        }

        [Test]
        public void Parse_Null_ReturnsUnspecified_DoesNotThrow()
        {
            var result = NetLaunchArgs.Parse(null);
            Assert.AreEqual(NetLaunchRole.Unspecified, result.Role);
        }

        [TestCase("host", NetLaunchRole.Host)]
        [TestCase("client", NetLaunchRole.Client)]
        [TestCase("off", NetLaunchRole.Off)]
        [TestCase("manual", NetLaunchRole.Manual)]
        [TestCase("HOST", NetLaunchRole.Host)]
        [TestCase("MANUAL", NetLaunchRole.Manual)]
        [TestCase("garbage", NetLaunchRole.Unspecified)]
        public void Parse_NetFlag_ParsesRole(string value, NetLaunchRole expected)
        {
            var result = NetLaunchArgs.Parse(new[] { "-ddrive-net", value });
            Assert.AreEqual(expected, result.Role);
        }

        [Test]
        public void Parse_HostAndPort()
        {
            var result = NetLaunchArgs.Parse(new[] { "-ddrive-host", "192.168.137.1", "-ddrive-port", "7777" });
            Assert.AreEqual("192.168.137.1", result.Host);
            Assert.AreEqual(7777, result.Port);
        }

        [Test]
        public void Parse_SimLatencyAndLoss()
        {
            var result = NetLaunchArgs.Parse(new[] { "-ddrive-sim-latency", "200", "-ddrive-sim-loss", "5.5" });
            Assert.AreEqual(200, result.SimLatencyMs);
            Assert.AreEqual(5.5f, result.SimLossPercent.Value, 0.001f);
        }

        [Test]
        public void Parse_AutoTestName()
        {
            var result = NetLaunchArgs.Parse(new[] { "-ddrive-autotest", "smoke" });
            Assert.AreEqual("smoke", result.AutoTestName);
        }

        // [11_tasks.md] 6-7 — run-netcheck.cmd がシナリオごとに実行時間を明示するための引数。
        [Test]
        public void Parse_AutoTestSeconds()
        {
            var result = NetLaunchArgs.Parse(new[] { "-ddrive-autotest-seconds", "30" });
            Assert.AreEqual(30f, result.AutoTestSeconds.Value, 0.001f);
        }

        [Test]
        public void Parse_AutoTestSeconds_InvalidValue_LeavesNull()
        {
            var result = NetLaunchArgs.Parse(new[] { "-ddrive-autotest-seconds", "not-a-number" });
            Assert.IsNull(result.AutoTestSeconds);
        }

        // [14_networking.md] §16(N-3、2026-09-22) — Host 1 + Client 3 の接続確認用フラグ。
        [Test]
        public void Parse_ExpectClients()
        {
            var result = NetLaunchArgs.Parse(new[] { "-ddrive-expect-clients", "3" });
            Assert.AreEqual(3, result.ExpectedClientCount);
        }

        [Test]
        public void Parse_ExpectClients_InvalidValue_LeavesNull()
        {
            var result = NetLaunchArgs.Parse(new[] { "-ddrive-expect-clients", "not-a-number" });
            Assert.IsNull(result.ExpectedClientCount);
        }

        [Test]
        public void Parse_ExpectClients_Unspecified_IsNull()
        {
            var result = NetLaunchArgs.Parse(new string[0]);
            Assert.IsNull(result.ExpectedClientCount);
        }

        [Test]
        public void Parse_AllFlagsTogether_InAnyOrder()
        {
            var args = new[]
            {
                "-ddrive-net", "client",
                "-ddrive-host", "127.0.0.1",
                "-ddrive-port", "7778",
                "-ddrive-sim-latency", "50",
                "-ddrive-sim-loss", "1",
                "-ddrive-autotest", "netcheck",
                "-ddrive-autotest-seconds", "30",
                "-ddrive-expect-clients", "3",
                "-ddrive-migrate", "successor",
                "-ddrive-migrate-host", "192.168.1.5",
                "-ddrive-migrate-port", "7779",
            };

            var result = NetLaunchArgs.Parse(args);
            Assert.AreEqual(NetLaunchRole.Client, result.Role);
            Assert.AreEqual("127.0.0.1", result.Host);
            Assert.AreEqual(7778, result.Port);
            Assert.AreEqual(50, result.SimLatencyMs);
            Assert.AreEqual(1f, result.SimLossPercent.Value, 0.001f);
            Assert.AreEqual("netcheck", result.AutoTestName);
            Assert.AreEqual(30f, result.AutoTestSeconds.Value, 0.001f);
            Assert.AreEqual(3, result.ExpectedClientCount);
            Assert.AreEqual(NetMigrationRole.Successor, result.MigrationRole);
            Assert.AreEqual("192.168.1.5", result.MigrationHost);
            Assert.AreEqual(7779, result.MigrationPort);
        }

        // [14_networking.md] §18/N-6(2026-09-24) — Host 引き継ぎ(ホストマイグレーション)の自動確認用引数。

        [TestCase("successor", NetMigrationRole.Successor)]
        [TestCase("follower", NetMigrationRole.Follower)]
        [TestCase("SUCCESSOR", NetMigrationRole.Successor)]
        [TestCase("garbage", NetMigrationRole.None)]
        public void Parse_MigrateFlag_ParsesRole(string value, NetMigrationRole expected)
        {
            var result = NetLaunchArgs.Parse(new[] { "-ddrive-migrate", value });
            Assert.AreEqual(expected, result.MigrationRole);
        }

        [Test]
        public void Parse_MigrateFlag_Unspecified_IsNone()
        {
            var result = NetLaunchArgs.Parse(new string[0]);
            Assert.AreEqual(NetMigrationRole.None, result.MigrationRole);
            Assert.IsNull(result.MigrationHost);
            Assert.IsNull(result.MigrationPort);
        }

        [Test]
        public void Parse_MigrateHostAndPort()
        {
            var result = NetLaunchArgs.Parse(new[] { "-ddrive-migrate-host", "10.0.0.2", "-ddrive-migrate-port", "8888" });
            Assert.AreEqual("10.0.0.2", result.MigrationHost);
            Assert.AreEqual(8888, result.MigrationPort);
        }

        [Test]
        public void Parse_MigratePort_InvalidValue_LeavesNull()
        {
            var result = NetLaunchArgs.Parse(new[] { "-ddrive-migrate-port", "not-a-number" });
            Assert.IsNull(result.MigrationPort);
        }

        [Test]
        public void Parse_FlagWithoutValue_AtEndOfArgs_DoesNotThrow_AndLeavesValueNull()
        {
            var result = NetLaunchArgs.Parse(new[] { "-ddrive-host" });
            Assert.IsNull(result.Host);
        }

        [Test]
        public void Parse_FlagImmediatelyFollowedByAnotherFlag_DoesNotConsumeItAsValue()
        {
            // -ddrive-host の直後が別のフラグ(値ではない)ときは、値なしとして扱い、後続フラグは
            // 通常どおり解釈される(NextValue の "-ddrive-" プレフィックス判定)。
            var result = NetLaunchArgs.Parse(new[] { "-ddrive-host", "-ddrive-net", "host" });
            Assert.IsNull(result.Host);
            Assert.AreEqual(NetLaunchRole.Host, result.Role);
        }

        [Test]
        public void Parse_UnknownFlags_AreIgnored()
        {
            var result = NetLaunchArgs.Parse(new[] { "-someOtherUnityFlag", "value", "-ddrive-net", "off" });
            Assert.AreEqual(NetLaunchRole.Off, result.Role);
        }

        // [14_networking.md] N-1(2026-09-22) — DDriveRuntimeBootstrap.ResolveNetBridge の役割解決
        // (NetLaunchArgs.ResolveEffectiveRole)。既定(defaultBridgeIsNgo=false, defaultStartIsManual=false)
        // では常に Off を返すこと(既存の挙動を変えない)を含めて検証する。

        [Test]
        public void ResolveEffectiveRole_CliRoleSpecified_AlwaysWinsOverDefaults()
        {
            Assert.AreEqual(NetLaunchRole.Client, NetLaunchArgs.ResolveEffectiveRole(NetLaunchRole.Client, defaultBridgeIsNgo: false, defaultStartIsManual: false));
            Assert.AreEqual(NetLaunchRole.Host, NetLaunchArgs.ResolveEffectiveRole(NetLaunchRole.Host, defaultBridgeIsNgo: true, defaultStartIsManual: true));
            Assert.AreEqual(NetLaunchRole.Off, NetLaunchArgs.ResolveEffectiveRole(NetLaunchRole.Off, defaultBridgeIsNgo: true, defaultStartIsManual: false));
            Assert.AreEqual(NetLaunchRole.Manual, NetLaunchArgs.ResolveEffectiveRole(NetLaunchRole.Manual, defaultBridgeIsNgo: false, defaultStartIsManual: false));
        }

        [Test]
        public void ResolveEffectiveRole_Unspecified_DefaultBridgeLoopback_ReturnsOff_RegardlessOfNetStart()
        {
            // 既存の既定値(DefaultNetBridge=Loopback)のときは DefaultNetStart の値に関わらず Off
            // (= LocalLoopbackBridge)のまま。既存挙動を変えないための回帰テスト。
            Assert.AreEqual(NetLaunchRole.Off, NetLaunchArgs.ResolveEffectiveRole(NetLaunchRole.Unspecified, defaultBridgeIsNgo: false, defaultStartIsManual: false));
            Assert.AreEqual(NetLaunchRole.Off, NetLaunchArgs.ResolveEffectiveRole(NetLaunchRole.Unspecified, defaultBridgeIsNgo: false, defaultStartIsManual: true));
        }

        [Test]
        public void ResolveEffectiveRole_Unspecified_DefaultBridgeNgo_AutoStart_ReturnsHost()
        {
            Assert.AreEqual(NetLaunchRole.Host, NetLaunchArgs.ResolveEffectiveRole(NetLaunchRole.Unspecified, defaultBridgeIsNgo: true, defaultStartIsManual: false));
        }

        [Test]
        public void ResolveEffectiveRole_Unspecified_DefaultBridgeNgo_ManualStart_ReturnsManual()
        {
            Assert.AreEqual(NetLaunchRole.Manual, NetLaunchArgs.ResolveEffectiveRole(NetLaunchRole.Unspecified, defaultBridgeIsNgo: true, defaultStartIsManual: true));
        }
    }
}
