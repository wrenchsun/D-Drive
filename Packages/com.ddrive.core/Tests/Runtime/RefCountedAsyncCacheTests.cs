using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using DDrive.Foundation.Loader;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    public class RefCountedAsyncCacheTests
    {
        [Test]
        public void Acquire_DedupesConcurrentLoadsForSameKey()
        {
            var factoryCalls = 0;
            var cache = new RefCountedAsyncCache<string, int>(
                _ =>
                {
                    factoryCalls++;
                    return UniTask.FromResult(42);
                },
                (_, _) => { });

            cache.Acquire("a");
            cache.Acquire("a");

            Assert.AreEqual(1, factoryCalls);
            Assert.AreEqual(2, cache.RefCountOf("a"));
        }

        [UnityTest]
        public IEnumerator Release_OnlyDisposesWhenRefCountReachesZero()
        {
            var disposedKeys = new List<string>();
            var cache = new RefCountedAsyncCache<string, int>(
                _ => UniTask.FromResult(1),
                (key, _) => disposedKeys.Add(key));

            cache.Acquire("a");
            cache.Acquire("a");
            yield return null;

            cache.Release("a");
            yield return null;
            CollectionAssert.IsEmpty(disposedKeys);
            Assert.AreEqual(1, cache.RefCountOf("a"));

            cache.Release("a");
            yield return null;
            CollectionAssert.AreEqual(new[] { "a" }, disposedKeys);
            Assert.AreEqual(0, cache.RefCountOf("a"));
        }

        [Test]
        public void Release_WithoutAcquire_LogsWarningAndDoesNotThrow()
        {
            var cache = new RefCountedAsyncCache<string, int>(_ => UniTask.FromResult(1), (_, _) => { });
            LogAssert.Expect(LogType.Warning, new Regex(".*"));
            Assert.DoesNotThrow(() => cache.Release("missing"));
        }

        [Test]
        public void Acquire_DifferentKeys_AreIndependent()
        {
            var calls = new List<string>();
            var cache = new RefCountedAsyncCache<string, int>(
                key =>
                {
                    calls.Add(key);
                    return UniTask.FromResult(1);
                },
                (_, _) => { });

            cache.Acquire("a");
            cache.Acquire("b");

            CollectionAssert.AreEquivalent(new[] { "a", "b" }, calls);
            Assert.AreEqual(2, cache.Count);
        }
    }
}
