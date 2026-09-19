using DDrive.Foundation.Net;
using NUnit.Framework;

namespace DDrive.Tests.Runtime
{
    public class SeedRandomTests
    {
        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            var a = new SeedRandom(123);
            var b = new SeedRandom(123);

            for (var i = 0; i < 10; i++)
            {
                Assert.AreEqual(a.NextUInt(), b.NextUInt());
            }
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentFirstValue()
        {
            var a = new SeedRandom(1);
            var b = new SeedRandom(2);

            Assert.AreNotEqual(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void NextFloat01_StaysWithinUnitRange()
        {
            var rng = new SeedRandom(999);
            for (var i = 0; i < 1000; i++)
            {
                var v = rng.NextFloat01();
                Assert.GreaterOrEqual(v, 0f);
                Assert.Less(v, 1f);
            }
        }

        [Test]
        public void NextInt_StaysWithinBounds()
        {
            var rng = new SeedRandom(555);
            for (var i = 0; i < 1000; i++)
            {
                var v = rng.NextInt(5);
                Assert.GreaterOrEqual(v, 0);
                Assert.Less(v, 5);
            }
        }

        [Test]
        public void ZeroSeed_DoesNotDegenerateToAllZeros()
        {
            var rng = new SeedRandom(0);
            Assert.AreNotEqual(0u, rng.NextUInt());
        }
    }
}
