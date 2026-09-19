using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Runtime.Audio;
using NUnit.Framework;

namespace DDrive.Tests.Runtime
{
    public class DuckEventBridgeTests
    {
        [Test]
        public void OnEnableDuckEvent_PushesDuck()
        {
            var bus = new EventBus();
            var duck = new AudioDuckService();
            using var bridge = new DuckEventBridge(bus, duck);

            var ctx = new InstanceContext(0, 1);
            bus.Begin(ctx, new[]
            {
                new AssetEvent
                {
                    Trigger = EventTrigger.OnEnable,
                    Action = EventAction.Duck,
                    CustomKey = nameof(DuckChannel.Dialogue),
                    Param = ParamValue.Of(-12f),
                },
            });

            bus.Fire(ctx, EventTrigger.OnEnable);

            Assert.AreEqual(-12f, duck.CurrentDb(DuckChannel.Dialogue));
        }

        [Test]
        public void OnDisableDuckEvent_PopsMatchingChannel()
        {
            var bus = new EventBus();
            var duck = new AudioDuckService();
            using var bridge = new DuckEventBridge(bus, duck);
            duck.Push(DuckChannel.Dialogue, -6f);

            var ctx = new InstanceContext(0, 1);
            bus.Begin(ctx, new[]
            {
                new AssetEvent
                {
                    Trigger = EventTrigger.OnDisable,
                    Action = EventAction.Duck,
                    CustomKey = nameof(DuckChannel.Dialogue),
                },
            });

            bus.Fire(ctx, EventTrigger.OnDisable);

            Assert.AreEqual(0f, duck.CurrentDb(DuckChannel.Dialogue));
        }

        [Test]
        public void NonDuckAction_IsIgnored()
        {
            var bus = new EventBus();
            var duck = new AudioDuckService();
            using var bridge = new DuckEventBridge(bus, duck);

            var ctx = new InstanceContext(0, 1);
            bus.Begin(ctx, new[]
            {
                new AssetEvent { Trigger = EventTrigger.OnEnable, Action = EventAction.PlayAsset },
            });

            bus.Fire(ctx, EventTrigger.OnEnable);

            Assert.AreEqual(0f, duck.CurrentDb(DuckChannel.Dialogue));
            Assert.AreEqual(0f, duck.CurrentDb(DuckChannel.Menu));
        }

        [Test]
        public void Dispose_UnsubscribesFromEventBus()
        {
            var bus = new EventBus();
            var duck = new AudioDuckService();
            var bridge = new DuckEventBridge(bus, duck);
            bridge.Dispose();

            var ctx = new InstanceContext(0, 1);
            bus.Begin(ctx, new[]
            {
                new AssetEvent
                {
                    Trigger = EventTrigger.OnEnable,
                    Action = EventAction.Duck,
                    CustomKey = nameof(DuckChannel.Dialogue),
                    Param = ParamValue.Of(-12f),
                },
            });

            bus.Fire(ctx, EventTrigger.OnEnable);

            Assert.AreEqual(0f, duck.CurrentDb(DuckChannel.Dialogue));
        }
    }
}
