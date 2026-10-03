using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DDrive.Tests.Runtime
{
    // [07_canvas_prefab.md] A-3 追記(2026-10-03、Canvas の埋め込み) — 親 CanvasData の EmbeddedCanvases に登録した子 CanvasData の
    // ElementEffects / Buttons / Sliders が、親を Open したとき子のルート基準で効くこと(優先順位・入れ子の入れ子・循環・
    // 未解決・プール再利用・従来の単独 Open が変わらないこと)を検証する。UiManager.Tick と UiTweenManager.Tick を
    // 手動で交互に叩く(ElementFxTests と同じ流儀)。
    public class EmbeddedCanvasTests
    {
        private const ulong ParentId = 1;
        private const ulong ChildId = 2;
        private const ulong GrandId = 3;

        private PoolService _pool;
        private AssetRegistry _registry;
        private FakeAssetLoader _loader;
        private UiTweenManager _tweens;
        private UiManager _manager;
        private OptionStore _options;
        private GameObject _prefab; // 親: Title / OptionRoot(Panel, BtnX, Sld, Inner(Deep))
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            UiInteractable.ResetDoubleFireGuardForTests();
            _pool = new PoolService();
            _loader = new FakeAssetLoader();
            _registry = new AssetRegistry(_loader);
            _tweens = new UiTweenManager(_registry);
            _manager = new UiManager(_pool, _registry, new PauseService());
            _manager.SetTweenManager(_tweens);
            _options = new OptionStore();
            _manager.SetOptionStore(_options);

            _prefab = new GameObject("EmbedParent", typeof(RectTransform));
            _prefab.AddComponent<Canvas>();
            _prefab.AddComponent<CanvasGroup>();
            Child(_prefab.transform, "Title", typeof(Image));
            var option = Child(_prefab.transform, "OptionRoot");
            Child(option, "Panel", typeof(Image));
            var button = Child(option, "BtnX", typeof(Image), typeof(UiButton)).GetComponent<UiButton>();
            button.DoubleClickSec = 0f;
            button.BlockDoubleFire = false;
            Child(option, "Sld", typeof(Image), typeof(UiSlider));
            var inner = Child(option, "Inner");
            Child(inner, "Deep", typeof(Image));
        }

        [TearDown]
        public void TearDown()
        {
            _manager.StopAll(DDrive.Foundation.Manager.StopReason.Manual);
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_prefab);
            foreach (var o in _created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }
        }

        private static Transform Child(Transform parent, string name, params System.Type[] components)
        {
            var types = new System.Type[components.Length + 1];
            types[0] = typeof(RectTransform);
            System.Array.Copy(components, 0, types, 1, components.Length);
            var go = new GameObject(name, types);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private CanvasData MakeData(ulong id, string name, GameObject prefab = null)
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Id = id;
            data.DisplayName = name;
            data.Prefab = prefab;
            _created.Add(data);
            return data;
        }

        private void Register(CanvasData data)
        {
            var address = "canvas/" + data.Id;
            _loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = data.Id, Type = AssetType.Canvas, Address = address } });
            _created.Add(catalog);
            _registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            _registry.ResolveAsync<CanvasData>(data.Id).GetAwaiter().GetResult();
        }

        private static AssetId<CanvasMarker> IdOf(ulong id) => new(id, AssetType.Canvas);

        private static ElementFx Fx(string path, UiPreset appear = UiPreset.None, float appearSec = 0.1f, UiPreset idle = UiPreset.None, UiPreset disappear = UiPreset.None)
            => new()
            {
                ElementPath = path,
                AppearPreset = new UiPresetRef { Preset = appear, Duration = appearSec },
                IdlePreset = new UiPresetRef { Preset = idle, Duration = 1f },
                DisappearPreset = new UiPresetRef { Preset = disappear, Duration = 0.1f },
            };

        private CanvasData MakeParent(params EmbeddedCanvas[] embeds)
        {
            var parent = MakeData(ParentId, "Hud", _prefab);
            parent.EmbeddedCanvases = embeds;
            return parent;
        }

        private void TickBoth(float dt, int steps = 1)
        {
            for (var i = 0; i < steps; i++)
            {
                _tweens.Tick(dt);
                _manager.Tick(dt);
            }
        }

        private static void Click(UiButton button)
        {
            button.Press();
            button.Release(true);
        }

        // ── ElementFx ──

        [Test]
        public void ParentOpen_AppliesChildElementFx_RelativeToChildRoot()
        {
            var child = MakeData(ChildId, "Option");
            child.ElementEffects = new[] { Fx("Panel", UiPreset.FadeIn, 0.1f, UiPreset.Pulse, UiPreset.FadeOut) };
            Register(child);
            var parent = MakeParent(new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) });

            var handle = _manager.OpenData(parent);
            var group = _manager.GetComponent<CanvasGroup>(handle);
            Assert.IsFalse(group.interactable, "子の Appear が終わるまでは親も入力ゲート中のはず");
            Assert.IsTrue(_manager.IsOpening(handle));

            TickBoth(0.05f, 4);
            Assert.IsTrue(group.interactable, "子の Appear 完了後は入力を受け付けるはず");
            Assert.Greater(_tweens.ActiveCount, 0, "Appear 完了後は子の Idle(Pulse)が動いているはず");

            _manager.Close(handle);
            Assert.IsTrue(_manager.IsClosing(handle));
            TickBoth(0.05f, 6);
            Assert.IsFalse(_manager.IsOpen(handle), "子の Disappear が終わったら閉じ切るはず");
            Assert.AreEqual(0, _tweens.ActiveCount, "Close 後に Idle が残らないはず");
        }

        [Test]
        public void ParentWithNoEffects_StillGetsChildEffects()
        {
            var child = MakeData(ChildId, "Option");
            child.ElementEffects = new[] { Fx("Panel", UiPreset.FadeIn, 5f) };
            Register(child);
            var parent = MakeParent(new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) });
            Assert.IsNull(parent.ElementEffects);

            var handle = _manager.OpenData(parent);
            TickBoth(0.05f, 2);

            Assert.IsTrue(_manager.IsOpening(handle), "親に ElementEffects が無くても子の Appear(5 秒)が走っているはず");
        }

        // ── ボタン / スライダー配線 ──

        [Test]
        public void ChildButtonWire_SendSignal_ElementPathIsChildRooted_AndEmbeddedRootPathSaysWhere()
        {
            var child = MakeData(ChildId, "Option");
            child.Buttons = new[] { new ButtonWire { ButtonPath = "BtnX", Trigger = WireTrigger.Click, Action = UiAction.SendSignal, SignalKey = "child/click" } };
            Register(child);
            var parent = MakeParent(new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) });

            var received = new List<SignalArgs>();
            _manager.OnSignal("child/click", args => received.Add(args));

            var handle = _manager.OpenData(parent);
            Click(_manager.GetComponent<UiButton>(handle, "OptionRoot/BtnX"));

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(handle, received[0].Canvas, "シグナルの Canvas は親(開いている Canvas)のハンドル");
            Assert.AreEqual("BtnX", received[0].ElementPath, "ElementPath は子のルート基準(その子を単独で開いたときと同じ値)");
            Assert.AreEqual("OptionRoot", received[0].EmbeddedRootPath, "埋め込みの位置は EmbeddedRootPath(Open した Canvas のルートから見たパス)");
        }

        [Test]
        public void SignalArgs_ParentOwnWire_And_StandaloneChild_HaveEmptyEmbeddedRootPath()
        {
            var childPrefab = new GameObject("ChildStandaloneSig", typeof(RectTransform));
            _created.Add(childPrefab);
            childPrefab.AddComponent<Canvas>();
            childPrefab.AddComponent<CanvasGroup>();
            var btn = Child(childPrefab.transform, "BtnX", typeof(Image), typeof(UiButton)).GetComponent<UiButton>();
            btn.DoubleClickSec = 0f;
            btn.BlockDoubleFire = false;
            var child = MakeData(ChildId, "Option", childPrefab);
            var childWire = new ButtonWire { ButtonPath = "BtnX", Trigger = WireTrigger.Click, Action = UiAction.SendSignal, SignalKey = "same/sig" };
            child.Buttons = new[] { childWire };
            Register(child);

            var received = new List<SignalArgs>();
            _manager.OnSignal("same/sig", a => received.Add(a));

            // 単独で Open: ElementPath = "BtnX"、EmbeddedRootPath = 空。
            var standalone = _manager.OpenData(child);
            Click(_manager.GetComponent<UiButton>(standalone, "BtnX"));
            Assert.AreEqual(1, received.Count);
            Assert.AreEqual("BtnX", received[0].ElementPath);
            Assert.AreEqual(string.Empty, received[0].EmbeddedRootPath);
            _manager.Close(standalone);
            received.Clear();

            // 親自身の配線も EmbeddedRootPath は空。
            var parent = MakeParent();
            parent.Buttons = new[] { new ButtonWire { ButtonPath = "OptionRoot/BtnX", Trigger = WireTrigger.Click, Action = UiAction.SendSignal, SignalKey = "same/sig" } };
            var handle = _manager.OpenData(parent);
            Click(_manager.GetComponent<UiButton>(handle, "OptionRoot/BtnX"));
            Assert.AreEqual(1, received.Count);
            Assert.AreEqual("OptionRoot/BtnX", received[0].ElementPath, "親自身の配線は親ルート基準(従来どおり)");
            Assert.AreEqual(string.Empty, received[0].EmbeddedRootPath);
        }

        [Test]
        public void NestedNested_SignalCarriesGrandchildRootedPath_AndConcatenatedEmbeddedRoot()
        {
            _prefab.transform.Find("OptionRoot/Inner/Deep").gameObject.AddComponent<UiButton>();
            var deepButton = _prefab.transform.Find("OptionRoot/Inner/Deep").GetComponent<UiButton>();
            deepButton.DoubleClickSec = 0f;
            deepButton.BlockDoubleFire = false;

            var grand = MakeData(GrandId, "Deep");
            grand.Buttons = new[] { new ButtonWire { ButtonPath = "Deep", Trigger = WireTrigger.Click, Action = UiAction.SendSignal, SignalKey = "grand/sig" } };
            Register(grand);
            var child = MakeData(ChildId, "Option");
            child.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "Inner", Canvas = IdOf(GrandId) } };
            child.Buttons = new[] { new ButtonWire { ButtonPath = "BtnX", Trigger = WireTrigger.Click, Action = UiAction.SendSignal, SignalKey = "child/sig2" } };
            Register(child);
            var parent = MakeParent(new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) });

            var grandSignals = new List<SignalArgs>();
            var childSignals = new List<SignalArgs>();
            _manager.OnSignal("grand/sig", a => grandSignals.Add(a));
            _manager.OnSignal("child/sig2", a => childSignals.Add(a));

            var handle = _manager.OpenData(parent);
            Click(_manager.GetComponent<UiButton>(handle, "OptionRoot/Inner/Deep"));
            Click(_manager.GetComponent<UiButton>(handle, "OptionRoot/BtnX"));

            Assert.AreEqual(1, grandSignals.Count);
            Assert.AreEqual("Deep", grandSignals[0].ElementPath, "孫のルート基準(孫を単独で開いたときと同じ)");
            Assert.AreEqual("OptionRoot/Inner", grandSignals[0].EmbeddedRootPath, "最外のルートからの連結パス");
            Assert.AreEqual(handle, grandSignals[0].Canvas);
            Assert.AreEqual(1, childSignals.Count);
            Assert.AreEqual("BtnX", childSignals[0].ElementPath);
            Assert.AreEqual("OptionRoot", childSignals[0].EmbeddedRootPath);
        }

        [Test]
        public void ChildButtonWire_CloseSelf_ClosesTheParent()
        {
            var child = MakeData(ChildId, "Option");
            child.Buttons = new[] { new ButtonWire { ButtonPath = "BtnX", Trigger = WireTrigger.Click, Action = UiAction.CloseSelf } };
            Register(child);
            var parent = MakeParent(new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) });

            var handle = _manager.OpenData(parent);
            Click(_manager.GetComponent<UiButton>(handle, "OptionRoot/BtnX"));

            Assert.IsFalse(_manager.IsOpen(handle), "埋め込み時の CloseSelf は埋め込み先の親を閉じる");
        }

        [Test]
        public void ChildSliderWire_SetOption_InitializesAndWritesBack()
        {
            var child = MakeData(ChildId, "Option");
            child.Sliders = new[] { new SliderWire { ElementPath = "Sld", Trigger = SliderTrigger.Commit, Action = UiAction.SetOption, Option = OptionKey.SeVolume } };
            Register(child);
            var parent = MakeParent(new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) });
            _options.Set(OptionKey.SeVolume, 0.7f);

            var handle = _manager.OpenData(parent);
            var slider = _manager.GetComponent<UiSlider>(handle, "OptionRoot/Sld");
            Assert.AreEqual(0.7f, slider.Value, 0.001f, "子の SliderWire が Open 時に OptionStore の値で初期化する");

            slider.BeginDragAt(0.25f);
            slider.EndDrag();
            Assert.AreEqual(0.25f, _options.Get(OptionKey.SeVolume), 0.001f);
        }

        [Test]
        public void ChildSliderWire_SendSignal_CarriesValueAndChildRootedPath()
        {
            var child = MakeData(ChildId, "Option");
            child.Sliders = new[] { new SliderWire { ElementPath = "Sld", Trigger = SliderTrigger.Commit, Action = UiAction.SendSignal, SignalKey = "child/slider" } };
            Register(child);
            var parent = MakeParent(new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) });
            var received = new List<SignalArgs>();
            _manager.OnSignal("child/slider", a => received.Add(a));

            var handle = _manager.OpenData(parent);
            var slider = _manager.GetComponent<UiSlider>(handle, "OptionRoot/Sld");
            slider.BeginDragAt(0.6f);
            slider.EndDrag();

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(0.6f, received[0].Value, 0.001f);
            Assert.AreEqual("Sld", received[0].ElementPath, "ElementPath は子のルート基準");
            Assert.AreEqual("OptionRoot", received[0].EmbeddedRootPath);
        }

        // ── 優先順位(親の行が勝つ) ──

        [Test]
        public void ParentElementFxRow_Wins_OverChildRowForTheSameElement()
        {
            var child = MakeData(ChildId, "Option");
            child.ElementEffects = new[] { Fx("Panel", UiPreset.FadeIn, 5f) }; // 子: 5 秒
            Register(child);
            var parent = MakeParent(new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) });
            parent.ElementEffects = new[] { Fx("OptionRoot/Panel", UiPreset.FadeIn, 0.05f) }; // 親の上書き: 0.05 秒

            var handle = _manager.OpenData(parent);
            TickBoth(0.05f, 6); // 0.3 秒

            Assert.IsFalse(_manager.IsOpening(handle), "親の行(0.05 秒)が優先され、子の行(5 秒)は適用されないはず");
        }

        [Test]
        public void ParentButtonWire_Wins_OverChildWireForTheSameButton()
        {
            var child = MakeData(ChildId, "Option");
            child.Buttons = new[] { new ButtonWire { ButtonPath = "BtnX", Trigger = WireTrigger.Click, Action = UiAction.SendSignal, SignalKey = "child/sig" } };
            Register(child);
            var parent = MakeParent(new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) });
            parent.Buttons = new[] { new ButtonWire { ButtonPath = "OptionRoot/BtnX", Trigger = WireTrigger.Click, Action = UiAction.SendSignal, SignalKey = "parent/sig" } };

            var childSignals = 0;
            var parentSignals = 0;
            _manager.OnSignal("child/sig", _ => childSignals++);
            _manager.OnSignal("parent/sig", _ => parentSignals++);

            var handle = _manager.OpenData(parent);
            Click(_manager.GetComponent<UiButton>(handle, "OptionRoot/BtnX"));

            Assert.AreEqual(1, parentSignals);
            Assert.AreEqual(0, childSignals, "親が同じボタンを配線していれば子の配線は適用されない");
        }

        [Test]
        public void ParentButtonWire_OnlyWinsForTheSameTrigger_ChildLongPressStillApplies()
        {
            var button = _prefab.transform.Find("OptionRoot/BtnX").GetComponent<UiButton>();
            button.LongPressSec = 0.2f;
            var child = MakeData(ChildId, "Option");
            child.Buttons = new[]
            {
                new ButtonWire { ButtonPath = "BtnX", Trigger = WireTrigger.Click, Action = UiAction.SendSignal, SignalKey = "child/click" },
                new ButtonWire { ButtonPath = "BtnX", Trigger = WireTrigger.LongPress, Action = UiAction.SendSignal, SignalKey = "child/long" },
            };
            Register(child);
            var parent = MakeParent(new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) });
            parent.Buttons = new[] { new ButtonWire { ButtonPath = "OptionRoot/BtnX", Trigger = WireTrigger.Click, Action = UiAction.SendSignal, SignalKey = "parent/click" } };

            var counts = new Dictionary<string, int>();
            foreach (var key in new[] { "child/click", "child/long", "parent/click" })
            {
                var k = key;
                counts[k] = 0;
                _manager.OnSignal(k, _ => counts[k]++);
            }

            var handle = _manager.OpenData(parent);
            var ui = _manager.GetComponent<UiButton>(handle, "OptionRoot/BtnX");
            Click(ui);
            Assert.AreEqual(1, counts["parent/click"]);
            Assert.AreEqual(0, counts["child/click"], "親が Click を配線している: 子の Click は適用されない");

            ui.Press();
            ui.Advance(0.25f); // LongPress
            ui.Release(true);
            Assert.AreEqual(1, counts["child/long"], "親が配線していない LongPress は子の配線が使われる(担当は(要素, トリガー)単位)");
        }

        [Test]
        public void OverlappingEmbedRegistrations_ApplyEachElementOnlyOnce()
        {
            // Hud が OptionRoot(Option)と OptionRoot/Inner(Volume)の両方を登録し、Option 自身も Inner(Volume)を埋め込んでいる。
            _prefab.transform.Find("OptionRoot/Inner/Deep").gameObject.AddComponent<UiButton>();
            var deepButton = _prefab.transform.Find("OptionRoot/Inner/Deep").GetComponent<UiButton>();
            deepButton.DoubleClickSec = 0f;
            deepButton.BlockDoubleFire = false;

            var volume = MakeData(GrandId, "Volume");
            volume.Buttons = new[] { new ButtonWire { ButtonPath = "Deep", Trigger = WireTrigger.Click, Action = UiAction.SendSignal, SignalKey = "vol/sig" } };
            Register(volume);
            var option = MakeData(ChildId, "Option");
            option.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "Inner", Canvas = IdOf(GrandId) } };
            Register(option);
            var parent = MakeParent(
                new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) },
                new EmbeddedCanvas { RootPath = "OptionRoot/Inner", Canvas = IdOf(GrandId) });

            var received = new List<SignalArgs>();
            _manager.OnSignal("vol/sig", a => received.Add(a));

            var handle = _manager.OpenData(parent);
            Click(_manager.GetComponent<UiButton>(handle, "OptionRoot/Inner/Deep"));

            Assert.AreEqual(1, received.Count, "重なった登録でも、同じ要素の配線は 1 回だけ(ボタン 1 回で 2 回発火しない)");
            Assert.AreEqual("OptionRoot/Inner", received[0].EmbeddedRootPath, "内側の登録(RootPath が深い方)が先に担当する");
        }

        [Test]
        public void OverlappingEmbedRegistrations_ApplyEachElementFxOnlyOnce()
        {
            // OptionRoot(A)と OptionRoot/Inner(B)の重なる登録で、同じ要素(OptionRoot/Inner/Deep)に A(5 秒)と B(0.05 秒)の行がある。
            // 内側の登録 B が先に担当するので、A の行は適用されない(両方適用されると A の 5 秒の Appear が残る)。
            var a = MakeData(ChildId, "A");
            a.ElementEffects = new[] { Fx("Inner/Deep", UiPreset.FadeIn, 5f) };
            Register(a);
            var b = MakeData(GrandId, "B");
            b.ElementEffects = new[] { Fx("Deep", UiPreset.FadeIn, 0.05f) };
            Register(b);
            var parent = MakeParent(
                new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) },
                new EmbeddedCanvas { RootPath = "OptionRoot/Inner", Canvas = IdOf(GrandId) });

            var handle = _manager.OpenData(parent);
            TickBoth(0.05f, 6); // 0.3 秒

            Assert.IsFalse(_manager.IsOpening(handle), "同じ要素の演出は 1 本だけ(内側の登録 B の 0.05 秒)。A の 5 秒は適用されない");
        }

        [Test]
        public void ParentRowForAnotherElement_DoesNotSuppressChildRow()
        {
            var child = MakeData(ChildId, "Option");
            child.ElementEffects = new[] { Fx("Panel", UiPreset.FadeIn, 5f) };
            Register(child);
            var parent = MakeParent(new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) });
            parent.ElementEffects = new[] { Fx("Title", UiPreset.FadeIn, 0.05f) };

            var handle = _manager.OpenData(parent);
            TickBoth(0.05f, 4);

            Assert.IsTrue(_manager.IsOpening(handle), "別の要素の行は子の行を妨げない(子の Panel の Appear が続く)");
        }

        // ── 入れ子の入れ子 ──

        [Test]
        public void NestedEmbedding_AppliesGrandchildEffects_AndOuterRowsWin()
        {
            var grand = MakeData(GrandId, "Deep");
            grand.ElementEffects = new[] { Fx("Deep", UiPreset.FadeIn, 5f) };
            Register(grand);
            var child = MakeData(ChildId, "Option");
            child.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "Inner", Canvas = IdOf(GrandId) } };
            Register(child);
            var parent = MakeParent(new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) });

            var handle = _manager.OpenData(parent);
            TickBoth(0.05f, 4);
            Assert.IsTrue(_manager.IsOpening(handle), "孫の Appear(5 秒)が親の Open で効くはず");
            _manager.StopAll(DDrive.Foundation.Manager.StopReason.Manual);

            // 子(中間)の行が孫の同じ要素を上書きする。
            child.ElementEffects = new[] { Fx("Inner/Deep", UiPreset.FadeIn, 0.05f) };
            var handle2 = _manager.OpenData(parent);
            TickBoth(0.05f, 4);
            Assert.IsFalse(_manager.IsOpening(handle2), "中間の子の行(0.05 秒)が孫の行(5 秒)に勝つはず");
        }

        // ── 不備: 警告 + 継続 ──

        [Test]
        public void Cycle_WarnsOnce_AndStillAppliesTheRest()
        {
            var child = MakeData(ChildId, "Option");
            child.ElementEffects = new[] { Fx("Panel", UiPreset.FadeIn, 5f) };
            child.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "Inner", Canvas = IdOf(ParentId) } }; // 親を埋め込む(循環)
            Register(child);
            var parent = MakeParent(new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) });
            Register(parent);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*循環.*"));
            Handle<CanvasMarker> handle = default;
            Assert.DoesNotThrow(() => handle = _manager.OpenData(parent));
            TickBoth(0.05f, 2);
            Assert.IsTrue(_manager.IsOpening(handle), "循環の打ち切り後も、循環していない子の ElementFx は効く");
            _manager.StopAll(DDrive.Foundation.Manager.StopReason.Manual);

            // 同じ設定の 2 回目の Open では警告が出ない(1 回だけ)。
            Assert.DoesNotThrow(() => _manager.OpenData(parent));
        }

        [Test]
        public void MissingRootPath_UnresolvedChild_AndMissingElement_WarnAndContinue()
        {
            var child = MakeData(ChildId, "Option");
            child.ElementEffects = new[] { Fx("NoSuchPanel", UiPreset.FadeIn, 5f), Fx("Panel", UiPreset.FadeIn, 5f) };
            Register(child);
            var parent = MakeParent(
                new EmbeddedCanvas { RootPath = "NoSuchRoot", Canvas = IdOf(ChildId) },
                new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(999) },
                new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) });
            parent.ElementEffects = new[] { Fx("Title", UiPreset.FadeIn, 0.05f) };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*RootPath 'NoSuchRoot'.*"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*未設定、または読み込まれていません.*"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*ElementFx.*NoSuchPanel.*"));
            Handle<CanvasMarker> handle = default;
            Assert.DoesNotThrow(() => handle = _manager.OpenData(parent));
            TickBoth(0.05f, 2);
            Assert.IsTrue(_manager.IsOpening(handle), "見つかる行(子の Panel 5 秒)は不備のある行があっても適用される");
            Assert.DoesNotThrow(() => _manager.Close(handle));
        }

        // ── 従来どおり ──

        [Test]
        public void ChildOpenedStandalone_BehavesAsBefore()
        {
            var childPrefab = new GameObject("ChildStandalone", typeof(RectTransform));
            _created.Add(childPrefab);
            childPrefab.AddComponent<Canvas>();
            childPrefab.AddComponent<CanvasGroup>();
            Child(childPrefab.transform, "Panel", typeof(Image));
            var child = MakeData(ChildId, "Option", childPrefab);
            child.ElementEffects = new[] { Fx("Panel", UiPreset.FadeIn, 0.1f) };

            var handle = _manager.OpenData(child);
            Assert.IsTrue(_manager.IsOpening(handle));
            TickBoth(0.05f, 4);
            Assert.IsFalse(_manager.IsOpening(handle), "子を単独で Open する従来の挙動(子ルート基準)は変わらない");
        }

        [Test]
        public void EmptyEmbeddedCanvases_BehavesAsBefore()
        {
            var parent = MakeParent(); // 空配列
            parent.ElementEffects = new[] { Fx("Title", UiPreset.FadeIn, 0.1f) };
            var handle = _manager.OpenData(parent);
            TickBoth(0.05f, 4);
            Assert.IsFalse(_manager.IsOpening(handle));

            var parentNull = MakeData(10, "NoEmbeds", _prefab); // null(旧データ)
            Assert.IsNull(parentNull.EmbeddedCanvases);
            Assert.DoesNotThrow(() => _manager.OpenData(parentNull));
        }

        [Test]
        public void PooledReopen_DoesNotKeepOldWiringOrState()
        {
            var child = MakeData(ChildId, "Option");
            child.ElementEffects = new[] { Fx("Panel", UiPreset.FadeIn, 0.1f, UiPreset.Pulse) };
            child.Buttons = new[] { new ButtonWire { ButtonPath = "BtnX", Trigger = WireTrigger.Click, Action = UiAction.SendSignal, SignalKey = "pool/click" } };
            Register(child);
            var parent = MakeParent(new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(ChildId) });
            parent.Flags.Pool = PoolPolicy.Pooled(0, 8);

            var clicks = 0;
            _manager.OnSignal("pool/click", _ => clicks++);

            var first = _manager.OpenData(parent);
            var rootGo = _manager.GetGameObject(first);
            TickBoth(0.05f, 4);
            _manager.Close(first);
            TickBoth(0.05f, 6);
            Assert.IsFalse(_manager.IsOpen(first));
            Assert.AreEqual(0, _tweens.ActiveCount, "Close で Idle が全て止まる");

            var second = _manager.OpenData(parent);
            Assert.AreSame(rootGo, _manager.GetGameObject(second), "プールから同じ実体が戻る");
            Assert.IsTrue(_manager.IsOpening(second), "再 Open でも子の Appear がやり直される(入力ゲートが閉じる)");
            TickBoth(0.05f, 4);
            Assert.IsFalse(_manager.IsOpening(second));

            Click(_manager.GetComponent<UiButton>(second, "OptionRoot/BtnX"));
            Assert.AreEqual(1, clicks, "配線は 1 回分だけ(前回の購読が残って二重に発火しない)");
        }
    }
}
