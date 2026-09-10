using System.Text.RegularExpressions;
using DDrive.Foundation.Handle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    public class InstanceStoreTests
    {
        private struct TestMarker
        {
        }

        private sealed class TestInstance
        {
            public string Name;
        }

        [Test]
        public void Add_ThenTryGet_ReturnsSameInstance()
        {
            var store = new InstanceStore<TestMarker, TestInstance>();
            var instance = new TestInstance { Name = "a" };

            var handle = store.Add(instance);

            Assert.IsTrue(store.TryGet(handle, out var got));
            Assert.AreSame(instance, got);
        }

        [Test]
        public void Remove_ThenTryGet_ReturnsFalseWithoutThrowingAndRecordsInvalidAccess()
        {
            var store = new InstanceStore<TestMarker, TestInstance>();
            var handle = store.Add(new TestInstance());
            store.Remove(handle);

            var invalidAccessIndex = -2;
            store.OnInvalidAccess += index => invalidAccessIndex = index;

            LogAssert.Expect(LogType.Warning, new Regex(".*"));
            bool result = false;
            Assert.DoesNotThrow(() => result = store.TryGet(handle, out _));

            Assert.IsFalse(result);
            Assert.AreEqual(1, store.InvalidAccessCount);
            Assert.AreEqual(handle.Index, invalidAccessIndex);
        }

        [Test]
        public void Remove_ThenReuseSlot_OldHandleStaysInvalid()
        {
            var store = new InstanceStore<TestMarker, TestInstance>();
            var oldInstance = new TestInstance { Name = "old" };
            var oldHandle = store.Add(oldInstance);
            store.Remove(oldHandle);

            var newInstance = new TestInstance { Name = "new" };
            var newHandle = store.Add(newInstance);

            Assert.AreEqual(oldHandle.Index, newHandle.Index);
            Assert.AreNotEqual(oldHandle.Generation, newHandle.Generation);

            LogAssert.Expect(LogType.Warning, new Regex(".*"));
            Assert.IsFalse(store.TryGet(oldHandle, out _));

            Assert.IsTrue(store.TryGet(newHandle, out var got));
            Assert.AreSame(newInstance, got);
        }

        [Test]
        public void IsValidSilent_OnRemovedHandle_DoesNotWarnOrCount()
        {
            var store = new InstanceStore<TestMarker, TestInstance>();
            var handle = store.Add(new TestInstance());
            Assert.IsTrue(store.IsValidSilent(handle));

            store.Remove(handle);
            Assert.IsFalse(store.IsValidSilent(handle));
            Assert.AreEqual(0, store.InvalidAccessCount, "問い合わせは不正アクセス扱いにしない");

            store.Remove(handle); // 二重 Remove も警告なし
            Assert.AreEqual(0, store.InvalidAccessCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Invalid_IsNotConfusedWithAllocatedHandle()
        {
            var store = new InstanceStore<TestMarker, TestInstance>();
            Assert.IsFalse(store.IsValid(Handle<TestMarker>.Invalid));
            Assert.AreEqual(0, store.InvalidAccessCount);
        }
    }
}
