using DDrive.Runtime.Net;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [14_networking.md] §14(N-2、2026-09-22) — 手動接続 UI の入力検証(NetManualConnectInput)。
    // Unity API に依存しない純関数のため EditMode(高速)で検証する。
    public class NetManualConnectInputTests
    {
        // ── TryParsePort ──

        [Test]
        public void TryParsePort_Empty_Fails()
        {
            Assert.IsFalse(NetManualConnectInput.TryParsePort(string.Empty, out var port, out var error));
            Assert.AreEqual(0, port);
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParsePort_Whitespace_Fails()
        {
            Assert.IsFalse(NetManualConnectInput.TryParsePort("   ", out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParsePort_Null_Fails()
        {
            Assert.IsFalse(NetManualConnectInput.TryParsePort(null, out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParsePort_NonNumeric_Fails()
        {
            Assert.IsFalse(NetManualConnectInput.TryParsePort("abc", out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParsePort_Zero_Fails()
        {
            Assert.IsFalse(NetManualConnectInput.TryParsePort("0", out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParsePort_Negative_Fails()
        {
            Assert.IsFalse(NetManualConnectInput.TryParsePort("-1", out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParsePort_65536_Fails()
        {
            Assert.IsFalse(NetManualConnectInput.TryParsePort("65536", out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParsePort_65535_Succeeds()
        {
            Assert.IsTrue(NetManualConnectInput.TryParsePort("65535", out var port, out var error));
            Assert.AreEqual((ushort)65535, port);
            Assert.IsNull(error);
        }

        [Test]
        public void TryParsePort_1_Succeeds()
        {
            Assert.IsTrue(NetManualConnectInput.TryParsePort("1", out var port, out _));
            Assert.AreEqual((ushort)1, port);
        }

        [Test]
        public void TryParsePort_Normal_Succeeds()
        {
            Assert.IsTrue(NetManualConnectInput.TryParsePort("7777", out var port, out var error));
            Assert.AreEqual((ushort)7777, port);
            Assert.IsNull(error);
        }

        [Test]
        public void TryParsePort_WithSurroundingWhitespace_Succeeds()
        {
            Assert.IsTrue(NetManualConnectInput.TryParsePort("  7777  ", out var port, out _));
            Assert.AreEqual((ushort)7777, port);
        }

        // ── TryParse(ip, port, ...) ──

        [Test]
        public void TryParse_EmptyIp_Fails()
        {
            Assert.IsFalse(NetManualConnectInput.TryParse(string.Empty, "7777", out var address, out _, out var error));
            Assert.IsNull(address);
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParse_WhitespaceIp_Fails()
        {
            Assert.IsFalse(NetManualConnectInput.TryParse("   ", "7777", out _, out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParse_NullIp_Fails()
        {
            Assert.IsFalse(NetManualConnectInput.TryParse(null, "7777", out _, out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParse_InvalidPort_FailsBeforeCheckingIp()
        {
            // Port が不正なら IP を見るまでもなく失敗する(呼び出し側は Port のエラーを表示すればよい)。
            Assert.IsFalse(NetManualConnectInput.TryParse(string.Empty, "0", out _, out var port, out var error));
            Assert.AreEqual(0, port);
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParse_HostName_IsRejected()
        {
            // ホスト名は許可しない(要件どおり。DNS 解決に依存させないため)。
            Assert.IsFalse(NetManualConnectInput.TryParse("example.com", "7777", out var address, out _, out var error));
            Assert.IsNull(address);
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParse_IPv6_IsRejected()
        {
            Assert.IsFalse(NetManualConnectInput.TryParse("::1", "7777", out _, out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParse_TooFewOctets_IsRejected()
        {
            Assert.IsFalse(NetManualConnectInput.TryParse("192.168.1", "7777", out _, out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParse_TooManyOctets_IsRejected()
        {
            Assert.IsFalse(NetManualConnectInput.TryParse("192.168.1.1.1", "7777", out _, out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParse_OctetOver255_IsRejected()
        {
            Assert.IsFalse(NetManualConnectInput.TryParse("192.168.1.256", "7777", out _, out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParse_NonDigitOctet_IsRejected()
        {
            Assert.IsFalse(NetManualConnectInput.TryParse("192.168.1.abc", "7777", out _, out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryParse_Localhost_ResolvesTo127001()
        {
            Assert.IsTrue(NetManualConnectInput.TryParse("localhost", "7777", out var address, out var port, out var error));
            Assert.AreEqual("127.0.0.1", address);
            Assert.AreEqual((ushort)7777, port);
            Assert.IsNull(error);
        }

        [Test]
        public void TryParse_LocalhostCaseInsensitive_ResolvesTo127001()
        {
            Assert.IsTrue(NetManualConnectInput.TryParse("LOCALHOST", "7777", out var address, out _, out _));
            Assert.AreEqual("127.0.0.1", address);
        }

        [Test]
        public void TryParse_ValidIPv4_Succeeds()
        {
            Assert.IsTrue(NetManualConnectInput.TryParse("192.168.137.1", "7777", out var address, out var port, out var error));
            Assert.AreEqual("192.168.137.1", address);
            Assert.AreEqual((ushort)7777, port);
            Assert.IsNull(error);
        }

        [Test]
        public void TryParse_ValidIPv4_WithSurroundingWhitespace_Succeeds()
        {
            Assert.IsTrue(NetManualConnectInput.TryParse("  10.0.0.5  ", "7777", out var address, out _, out _));
            Assert.AreEqual("10.0.0.5", address);
        }

        [Test]
        public void TryParse_AllZeros_Succeeds()
        {
            Assert.IsTrue(NetManualConnectInput.TryParse("0.0.0.0", "7777", out var address, out _, out _));
            Assert.AreEqual("0.0.0.0", address);
        }

        [Test]
        public void TryParse_AllMax_Succeeds()
        {
            Assert.IsTrue(NetManualConnectInput.TryParse("255.255.255.255", "7777", out var address, out _, out _));
            Assert.AreEqual("255.255.255.255", address);
        }
    }
}
