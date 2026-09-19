using DDrive.Foundation.Net;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    public class LocalLoopbackBridgeTests
    {
        private readonly struct PingMessage : INetMessage
        {
            public readonly int Value;
            public PingMessage(int value) => Value = value;
        }

        [Test]
        public void Broadcast_InvokesHandlerWithSenderIdZero()
        {
            var bridge = new LocalLoopbackBridge();
            ulong? receivedSender = null;
            var receivedValue = 0;

            bridge.Subscribe<PingMessage>((sender, msg) =>
            {
                receivedSender = sender;
                receivedValue = msg.Value;
            });

            bridge.Broadcast(new PingMessage(42), NetChannel.Unreliable);

            Assert.AreEqual(0UL, receivedSender);
            Assert.AreEqual(42, receivedValue);
        }

        [Test]
        public void SendTo_InvokesHandlerWithGivenClientId()
        {
            var bridge = new LocalLoopbackBridge();
            ulong receivedSender = 0;

            bridge.Subscribe<PingMessage>((sender, _) => receivedSender = sender);
            bridge.SendTo(7, new PingMessage(1), NetChannel.ReliableOrdered);

            Assert.AreEqual(7UL, receivedSender);
        }

        [Test]
        public void Dispose_UnsubscribesHandler()
        {
            var bridge = new LocalLoopbackBridge();
            var callCount = 0;

            var subscription = bridge.Subscribe<PingMessage>((_, _) => callCount++);
            bridge.Broadcast(new PingMessage(1), NetChannel.Unreliable);
            subscription.Dispose();
            bridge.Broadcast(new PingMessage(2), NetChannel.Unreliable);

            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void ResolveNetObject_ReturnsRegisteredTransform_OrNullWhenUnknown()
        {
            var bridge = new LocalLoopbackBridge();
            var go = new GameObject("NetObj");

            Assert.IsNull(bridge.ResolveNetObject(1));

            bridge.RegisterNetObject(1, go.transform);
            Assert.AreSame(go.transform, bridge.ResolveNetObject(1));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Tick_AccumulatesNetworkTime()
        {
            var bridge = new LocalLoopbackBridge();
            Assert.AreEqual(0d, bridge.NetworkTime);

            bridge.Tick(0.5d);
            bridge.Tick(0.25d);

            Assert.AreEqual(0.75d, bridge.NetworkTime, 1e-9);
        }
    }
}
