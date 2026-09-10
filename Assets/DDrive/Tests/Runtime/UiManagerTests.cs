using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using CanvasIdT = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Ui.CanvasMarker>;

namespace DDrive.Tests.Runtime
{
    public class UiManagerTests
    {
        private PoolService _pool;
        private AssetRegistry _registry;
        private FakeAssetLoader _loader;
        private PauseService _pause;
        private UiManager _manager;
        private GameObject _prefab;
        private Selectable _selA;
        private Selectable _selB;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _loader = new FakeAssetLoader();
            _registry = new AssetRegistry(_loader);
            _pause = new PauseService();
            _manager = new UiManager(_pool, _registry, _pause);

            _prefab = new GameObject("CanvasTestPrefab", typeof(RectTransform));
            _prefab.AddComponent<Canvas>();
            _prefab.AddComponent<CanvasGroup>();

            var a = new GameObject("A", typeof(RectTransform));
            a.transform.SetParent(_prefab.transform);
            _selA = a.AddComponent<Button>();

            var b = new GameObject("B", typeof(RectTransform));
            b.transform.SetParent(_prefab.transform);
            _selB = b.AddComponent<Button>();
        }

        [TearDown]
        public void TearDown()
        {
            _manager.StopAll(DDrive.Foundation.Manager.StopReason.Manual);
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_prefab);
        }

        private CanvasData CreateCanvasData(ulong id)
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Id = id;
            data.Prefab = _prefab;
            data.CloseOnBack = true;
            return data;
        }

        private void RegisterAndCache(CanvasData data)
        {
            var address = "canvas/" + data.Id;
            _loader.Assets[address] = data;

            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = data.Id, Type = AssetType.Canvas, Address = address } });
            _registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            _registry.ResolveAsync<CanvasData>(data.Id).GetAwaiter().GetResult();
        }

        [Test]
        public void OpenData_PushesStack_AndParentsUnderLayerRoot()
        {
            var data = CreateCanvasData(1);
            data.Layer = UiLayer.Menu;

            var handle = _manager.OpenData(data);

            Assert.AreEqual(1, _manager.StackCount);
            Assert.AreEqual(handle, _manager.Top);
            var root = _manager.GetGameObject(handle);
            Assert.IsNotNull(root);
            Assert.AreEqual("Menu", root.transform.parent.name);
        }

        [Test]
        public void Close_PooledPolicy_PopsStack_AndReuseGivesSameGameObject()
        {
            var data = CreateCanvasData(1);
            data.Flags.Pool = PoolPolicy.Pooled(0, 8);

            var handle = _manager.OpenData(data);
            var root = _manager.GetGameObject(handle);

            _manager.Close(handle);

            Assert.IsFalse(_manager.IsOpen(handle));
            Assert.AreEqual(0, _manager.StackCount);

            var handle2 = _manager.OpenData(data);
            Assert.AreSame(root, _manager.GetGameObject(handle2));
        }

        [Test]
        public void Popup_BlocksCanvasBelow_AndRestoresOnClose()
        {
            var below = CreateCanvasData(1);
            var popup = CreateCanvasData(2);
            popup.Layer = UiLayer.Popup;
            popup.ModalBlocksInput = true;

            var belowHandle = _manager.OpenData(below);
            var popupHandle = _manager.OpenData(popup, modal: true);

            var belowGroup = _manager.GetComponent<CanvasGroup>(belowHandle);
            Assert.IsFalse(belowGroup.interactable);
            Assert.IsFalse(belowGroup.blocksRaycasts);

            _manager.Close(popupHandle);

            Assert.IsTrue(belowGroup.interactable);
            Assert.IsTrue(belowGroup.blocksRaycasts);
        }

        [Test]
        public void CloseTop_SkipsCloseOnBackFalse_AndClosesFirstTrueBelow()
        {
            var bottom = CreateCanvasData(1);
            bottom.CloseOnBack = true;
            var top = CreateCanvasData(2);
            top.CloseOnBack = false;

            var bottomHandle = _manager.OpenData(bottom);
            var topHandle = _manager.OpenData(top);

            var closed = _manager.CloseTop();

            Assert.IsTrue(closed);
            Assert.IsFalse(_manager.IsOpen(bottomHandle));
            Assert.IsTrue(_manager.IsOpen(topHandle));
        }

        [Test]
        public void CloseTop_ReturnsFalse_WhenNothingCloseable()
        {
            var data = CreateCanvasData(1);
            data.CloseOnBack = false;
            _manager.OpenData(data);

            Assert.IsFalse(_manager.CloseTop());
        }

        [Test]
        public void PauseGameWhileOpen_PushesAndPopsGameplayChannel()
        {
            var data = CreateCanvasData(1);
            data.PauseGameWhileOpen = true;

            var handle = _manager.OpenData(data);
            Assert.IsTrue(_pause.IsPaused(PauseChannel.Gameplay));

            _manager.Close(handle);
            Assert.IsFalse(_pause.IsPaused(PauseChannel.Gameplay));
        }

        [Test]
        public void Navigation_AppliesExplicitLinks()
        {
            var data = CreateCanvasData(1);
            data.Navigation = new[] { new NavNode { Element = "A", Up = "B" } };

            var handle = _manager.OpenData(data);
            var a = _manager.GetComponent<Selectable>(handle, "A");

            Assert.AreEqual(UnityEngine.UI.Navigation.Mode.Explicit, a.navigation.mode);
            Assert.AreEqual(_manager.GetComponent<Selectable>(handle, "B"), a.navigation.selectOnUp);
        }

        [Test]
        public void OnSignal_ReceivesSendSignal_AndDisposeUnsubscribes()
        {
            var received = new List<SignalArgs>();
            var sub = _manager.OnSignal("shop/buy", args => received.Add(args));

            var handle = Handle<CanvasMarker>.Invalid;
            _manager.SendSignal("shop/buy", handle, "Button");
            Assert.AreEqual(1, received.Count);
            Assert.AreEqual("shop/buy", received[0].Key);
            Assert.AreEqual("Button", received[0].ElementPath);

            sub.Dispose();
            _manager.SendSignal("shop/buy", handle);
            Assert.AreEqual(1, received.Count);
        }

        [Test]
        public void FadeTransition_CompletesAfterTickingDuration_AlphaReachesOne()
        {
            var data = CreateCanvasData(1);
            data.OpenTransition = new UiTransition { Kind = UiTransitionKind.Fade, Duration = 0.5f };

            var handle = _manager.OpenData(data);
            var group = _manager.GetComponent<CanvasGroup>(handle);
            Assert.AreEqual(0f, group.alpha, 0.001f);

            _manager.Tick(0.5f);

            Assert.AreEqual(1f, group.alpha, 0.001f);
        }

        [Test]
        public void OpenAsync_AwaitsTransition_AndCompletesAfterEnoughTicks()
        {
            var data = CreateCanvasData(1);
            data.OpenTransition = new UiTransition { Kind = UiTransitionKind.Fade, Duration = 0.5f };
            RegisterAndCache(data);

            var task = _manager.OpenAsync(new CanvasIdT(data.Id, AssetType.Canvas));
            var awaiter = task.GetAwaiter();
            Assert.IsFalse(awaiter.IsCompleted);

            _manager.Tick(0.5f);

            Assert.IsTrue(awaiter.IsCompleted);
            var handle = awaiter.GetResult();
            Assert.IsTrue(_manager.IsOpen(handle));
            var group = _manager.GetComponent<CanvasGroup>(handle);
            Assert.AreEqual(1f, group.alpha, 0.001f);
        }

        [Test]
        public void OpenAndClose_FireLifecycleEvents_ViaEventBus()
        {
            var fired = new List<EventTrigger>();
            _manager.Events.OnEventFired += (_, evt) => fired.Add(evt.Trigger);

            var data = CreateCanvasData(1);
            data.Events = new[]
            {
                new AssetEvent { Trigger = EventTrigger.OnSpawn },
                new AssetEvent { Trigger = EventTrigger.OnEnable },
                new AssetEvent { Trigger = EventTrigger.OnDisable },
                new AssetEvent { Trigger = EventTrigger.OnDestroy },
            };

            var handle = _manager.OpenData(data);
            Assert.Contains(EventTrigger.OnSpawn, fired);
            Assert.Contains(EventTrigger.OnEnable, fired);

            _manager.Close(handle);
            Assert.Contains(EventTrigger.OnDisable, fired);
            Assert.Contains(EventTrigger.OnDestroy, fired);
        }

        [Test]
        public void OpenData_UnregisteredId_OpensPlaceholder_WithoutThrowing()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*CanvasData.*"));
            Handle<CanvasMarker> handle = default;
            Assert.DoesNotThrow(() => handle = _manager.Open(new CanvasIdT(0xDEAD, AssetType.Canvas)));

            var root = _manager.GetGameObject(handle);
            Assert.IsNotNull(root);
            Assert.AreEqual("<Placeholder:CANVAS>", root.name);

            Assert.DoesNotThrow(() => _manager.Close(handle));
        }
    }

    public class CanvasDataValidatorTests
    {
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("ValidatorPrefab", typeof(RectTransform));
            var a = new GameObject("A", typeof(RectTransform));
            a.transform.SetParent(_prefab.transform);
            a.AddComponent<Button>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_prefab);

        private static List<ValidationResult> Run(CanvasData data)
        {
            var results = new List<ValidationResult>();
            foreach (var r in new CanvasDataValidator().Validate(data, new ValidationContext(new List<AssetDataBase> { data })))
            {
                results.Add(r);
            }

            return results;
        }

        [Test]
        public void MissingPrefab_ReportsError()
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            var results = Run(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Prefab")));
            Object.DestroyImmediate(data);
        }

        [Test]
        public void BadButtonPath_ReportsError()
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Prefab = _prefab;
            data.Buttons = new[] { new ButtonWire { ButtonPath = "NoSuchPath" } };

            var results = Run(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("ButtonPath")));
            Object.DestroyImmediate(data);
        }

        [Test]
        public void UnreachableSelectable_ReportsWarning()
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Prefab = _prefab;
            data.Navigation = new[] { new NavNode { Element = "A" } };

            var results = Run(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("到達できません")));
            Object.DestroyImmediate(data);
        }

        [Test]
        public void SendSignalWithoutKey_ReportsError()
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Prefab = _prefab;
            data.Buttons = new[] { new ButtonWire { ButtonPath = "A", Action = UiAction.SendSignal, SignalKey = "" } };

            var results = Run(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("SignalKey")));
            Object.DestroyImmediate(data);
        }
    }
}
