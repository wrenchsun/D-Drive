using System.Collections.Generic;
using DDrive.Foundation.Event;
using DDrive.Foundation.Manager;
using NUnit.Framework;

namespace DDrive.Tests.Runtime
{
    public class EventBusTests
    {
        [Test]
        public void Fire_OnSpawn_InvokesMatchingEventOnly()
        {
            var bus = new EventBus();
            var ctx = new InstanceContext(0, 1);
            var fired = new List<EventTrigger>();
            bus.OnEventFired += (_, evt) => fired.Add(evt.Trigger);

            bus.Begin(ctx, new[]
            {
                new AssetEvent { Trigger = EventTrigger.OnSpawn },
                new AssetEvent { Trigger = EventTrigger.OnDestroy },
            });

            bus.Fire(ctx, EventTrigger.OnSpawn);

            CollectionAssert.AreEqual(new[] { EventTrigger.OnSpawn }, fired);
        }

        [Test]
        public void Fire_Custom_OnlyMatchesSameKey()
        {
            var bus = new EventBus();
            var ctx = new InstanceContext(0, 1);
            var fireCount = 0;
            bus.OnEventFired += (_, _) => fireCount++;

            bus.Begin(ctx, new[]
            {
                new AssetEvent { Trigger = EventTrigger.Custom, CustomKey = "hit" },
            });

            bus.Fire(ctx, EventTrigger.Custom, "miss");
            Assert.AreEqual(0, fireCount);

            bus.Fire(ctx, EventTrigger.Custom, "hit");
            Assert.AreEqual(1, fireCount);
        }

        [Test]
        public void Tick_TimeTrigger_FiresOnceWhenThresholdCrossed()
        {
            var bus = new EventBus();
            var ctx = new InstanceContext(0, 1);
            var fireCount = 0;
            bus.OnEventFired += (_, _) => fireCount++;

            bus.Begin(ctx, new[]
            {
                new AssetEvent { Trigger = EventTrigger.Time, Time = 1.0f },
            });

            bus.Tick(ctx, 0.5f); // elapsed 0.5
            Assert.AreEqual(0, fireCount);

            bus.Tick(ctx, 0.5f); // elapsed 1.0 -> crosses threshold
            Assert.AreEqual(1, fireCount);

            bus.Tick(ctx, 1.0f); // already fired, must not fire again
            Assert.AreEqual(1, fireCount);
        }

        [Test]
        public void Tick_FrameTrigger_FiresOnceOnTargetFrame()
        {
            var bus = new EventBus();
            var ctx = new InstanceContext(0, 1);
            var fireCount = 0;
            bus.OnEventFired += (_, _) => fireCount++;

            bus.Begin(ctx, new[]
            {
                new AssetEvent { Trigger = EventTrigger.Frame, Time = 3f },
            });

            bus.Tick(ctx, 0.016f); // frame 1
            bus.Tick(ctx, 0.016f); // frame 2
            Assert.AreEqual(0, fireCount);

            bus.Tick(ctx, 0.016f); // frame 3 -> fires
            Assert.AreEqual(1, fireCount);

            bus.Tick(ctx, 0.016f); // frame 4, must not refire
            Assert.AreEqual(1, fireCount);
        }

        [Test]
        public void End_RemovesSession_SubsequentFireIsNoOp()
        {
            var bus = new EventBus();
            var ctx = new InstanceContext(0, 1);
            var fireCount = 0;
            bus.OnEventFired += (_, _) => fireCount++;

            bus.Begin(ctx, new[] { new AssetEvent { Trigger = EventTrigger.OnSpawn } });
            bus.End(ctx);
            bus.Fire(ctx, EventTrigger.OnSpawn);

            Assert.AreEqual(0, fireCount);
        }
    }
}
