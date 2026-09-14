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
        [TestCase("HOST", NetLaunchRole.Host)]
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
            };

            var result = NetLaunchArgs.Parse(args);
            Assert.AreEqual(NetLaunchRole.Client, result.Role);
            Assert.AreEqual("127.0.0.1", result.Host);
            Assert.AreEqual(7778, result.Port);
            Assert.AreEqual(50, result.SimLatencyMs);
            Assert.AreEqual(1f, result.SimLossPercent.Value, 0.001f);
            Assert.AreEqual("netcheck", result.AutoTestName);
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
    }
}
