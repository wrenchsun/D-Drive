using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using NUnit.Framework;

namespace DDrive.Tests.Runtime
{
    public class GameLoopTests
    {
        private sealed class FakeManager : IAssetManager
        {
            public AssetType Type => AssetType.Se;
            public float LastDt;
            public int TickCount;
            public bool Paused;
            public StopReason? LastStopReason;
            public bool SceneUnloaded;

            public void Tick(float dt)
            {
                TickCount++;
                LastDt = dt;
            }

            public void OnPause(PauseChannel channel, bool paused) => Paused = paused;
            public void StopAll(StopReason reason) => LastStopReason = reason;
            public void OnSceneUnload() => SceneUnloaded = true;
        }

        [Test]
        public void Tick_DispatchesToAllRegisteredManagers()
        {
            var loop = new GameLoop();
            var a = new FakeManager();
            var b = new FakeManager();
            loop.Register(a);
            loop.Register(b);

            loop.Tick(0.5f);

            Assert.AreEqual(1, a.TickCount);
            Assert.AreEqual(0.5f, a.LastDt);
            Assert.AreEqual(1, b.TickCount);
        }

        [Test]
        public void Unregister_StopsReceivingTicks()
        {
            var loop = new GameLoop();
            var a = new FakeManager();
            loop.Register(a);
            loop.Unregister(a);

            loop.Tick(0.1f);

            Assert.AreEqual(0, a.TickCount);
        }

        [Test]
        public void BroadcastPause_And_StopAll_And_SceneUnload_ReachAllManagers()
        {
            var loop = new GameLoop();
            var a = new FakeManager();
            loop.Register(a);

            loop.BroadcastPause(PauseChannel.Gameplay, true);
            Assert.IsTrue(a.Paused);

            loop.StopAll(StopReason.GameOver);
            Assert.AreEqual(StopReason.GameOver, a.LastStopReason);

            loop.NotifySceneUnload();
            Assert.IsTrue(a.SceneUnloaded);
        }
    }
}
