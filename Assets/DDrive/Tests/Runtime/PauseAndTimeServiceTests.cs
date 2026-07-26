using System.Collections.Generic;
using System.Text.RegularExpressions;
using DDrive.Foundation.Pause;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    public class PauseServiceTests
    {
        [Test]
        public void MultiplePush_OnlyNotifiesOnFirstAndLastPop()
        {
            var service = new PauseService();
            var events = new List<bool>();
            service.OnPauseChanged += (_, paused) => events.Add(paused);

            service.Push(PauseChannel.Gameplay);
            service.Push(PauseChannel.Gameplay);
            service.Push(PauseChannel.Gameplay);
            Assert.IsTrue(service.IsPaused(PauseChannel.Gameplay));
            CollectionAssert.AreEqual(new[] { true }, events);

            service.Pop(PauseChannel.Gameplay);
            service.Pop(PauseChannel.Gameplay);
            Assert.IsTrue(service.IsPaused(PauseChannel.Gameplay));
            CollectionAssert.AreEqual(new[] { true }, events);

            service.Pop(PauseChannel.Gameplay);
            Assert.IsFalse(service.IsPaused(PauseChannel.Gameplay));
            CollectionAssert.AreEqual(new[] { true, false }, events);
        }

        [Test]
        public void Channels_AreIndependent()
        {
            var service = new PauseService();
            service.Push(PauseChannel.UI);

            Assert.IsTrue(service.IsPaused(PauseChannel.UI));
            Assert.IsFalse(service.IsPaused(PauseChannel.Gameplay));
        }

        [Test]
        public void Pop_WithoutPush_LogsWarningAndDoesNotThrow()
        {
            var service = new PauseService();
            LogAssert.Expect(LogType.Warning, new Regex(".*"));
            Assert.DoesNotThrow(() => service.Pop(PauseChannel.Gameplay));
        }
    }

    public class AudioDuckServiceTests
    {
        [Test]
        public void Pop_RestoresPreviousValueOnStack()
        {
            var duck = new AudioDuckService();
            duck.Push(DuckChannel.Dialogue, -6f);
            duck.Push(DuckChannel.Dialogue, -12f);

            Assert.AreEqual(-12f, duck.CurrentDb(DuckChannel.Dialogue));

            duck.Pop(DuckChannel.Dialogue);
            Assert.AreEqual(-6f, duck.CurrentDb(DuckChannel.Dialogue));

            duck.Pop(DuckChannel.Dialogue);
            Assert.AreEqual(0f, duck.CurrentDb(DuckChannel.Dialogue));
        }

        [Test]
        public void Pop_WithoutPush_LogsWarningAndDoesNotThrow()
        {
            var duck = new AudioDuckService();
            LogAssert.Expect(LogType.Warning, new Regex(".*"));
            Assert.DoesNotThrow(() => duck.Pop(DuckChannel.Menu));
        }
    }

    public class TimeServiceTests
    {
        [Test]
        public void HitStop_ScalesDownThenRestoresAfterDuration()
        {
            var time = new TimeService();
            Assert.AreEqual(1f, time.TimeScale);

            time.HitStop(0.1f, 0f);
            Assert.AreEqual(0f, time.TimeScale);
            Assert.AreEqual(0f, time.ScaledDeltaTime(0.05f));

            time.Tick(0.05f);
            Assert.AreEqual(0f, time.TimeScale);

            time.Tick(0.06f);
            Assert.AreEqual(1f, time.TimeScale);
            Assert.AreEqual(0.02f, time.ScaledDeltaTime(0.02f));
        }
    }
}
