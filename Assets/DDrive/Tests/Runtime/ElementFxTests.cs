using System.Collections.Generic;
using DDrive.Foundation.Data;
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

namespace DDrive.Tests.Runtime
{
    // [15_ui_interaction.md] B-4 — チケット 4-9(ElementFx: Appear/Idle/Disappear + スタッガー + Close 完了待ち)+
    // 4-7 残り(レイヤー既定 Skin/SE)。UiManager.Tick と UiTweenManager.Tick を手動で交互に叩く同期テスト
    // ([12_review.md] §3 に合わせ、両 Manager の Tick は決定的な dt だけを受け取る)。
    public class ElementFxTests
    {
        private PoolService _pool;
        private AssetRegistry _registry;
        private FakeAssetLoader _loader;
        private PauseService _pause;
        private UiTweenManager _tweens;
        private UiManager _manager;
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _loader = new FakeAssetLoader();
            _registry = new AssetRegistry(_loader);
            _pause = new PauseService();
            _tweens = new UiTweenManager(_registry);
            _manager = new UiManager(_pool, _registry, _pause);
            _manager.SetTweenManager(_tweens);

            _prefab = new GameObject("ElementFxPrefab", typeof(RectTransform));
            _prefab.AddComponent<Canvas>();
            _prefab.AddComponent<CanvasGroup>();
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
            return data;
        }

        private RectTransform AddElement(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_prefab.transform, false);
            return (RectTransform)go.transform;
        }

        private UiButton AddButton(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(UiButton));
            go.transform.SetParent(_prefab.transform, false);
            return go.GetComponent<UiButton>();
        }

        private void TickBoth(float dt, int steps = 1)
        {
            for (var i = 0; i < steps; i++)
            {
                _tweens.Tick(dt);
                _manager.Tick(dt);
            }
        }

        // ── Appear ──

        [Test]
        public void AppearPreset_PlaysOnOpen_AndGatesInteractableUntilComplete()
        {
            AddElement("Icon");
            var data = CreateCanvasData(1);
            data.ElementEffects = new[]
            {
                new ElementFx { ElementPath = "Icon", AppearPreset = new UiPresetRef { Preset = UiPreset.FadeIn, Duration = 0.2f } },
            };

            var handle = _manager.OpenData(data);
            var group = _manager.GetComponent<CanvasGroup>(handle);
            Assert.IsFalse(group.interactable, "Appear 完了までは interactable=false のはず");
            Assert.IsTrue(_manager.IsOpening(handle));

            TickBoth(0.05f, 10); // 合計 0.5s(0.2s の Appear は十分完了する)

            Assert.IsTrue(group.interactable, "Appear 完了後は interactable=true に戻るはず");
            Assert.IsFalse(_manager.IsOpening(handle));
        }

        [Test]
        public void AppearDelay_StaggersTwoElements_SecondStartsLater()
        {
            AddElement("First");
            AddElement("Second");
            var data = CreateCanvasData(1);
            data.ElementEffects = new[]
            {
                new ElementFx { ElementPath = "First", AppearPreset = new UiPresetRef { Preset = UiPreset.FadeIn, Duration = 0.1f }, AppearDelay = 0f },
                new ElementFx { ElementPath = "Second", AppearPreset = new UiPresetRef { Preset = UiPreset.FadeIn, Duration = 0.1f }, AppearDelay = 0.3f },
            };

            var handle = _manager.OpenData(data);

            TickBoth(0.05f, 3); // 0.15s 経過: First は完了しているはずだが Second はまだ Delay 中(0.3s 未満)
            // Alpha トラックは実際に再生が始まって初めて CanvasGroup を自動追加するため、GetComponent は Tick の後に呼ぶ。
            var firstGroup = FindTransform(handle, "First").GetComponent<CanvasGroup>();
            Assert.AreEqual(1f, firstGroup.alpha, 0.01f, "First は Delay 0 なのでもう完了しているはず");
            Assert.IsNull(FindTransform(handle, "Second").GetComponent<CanvasGroup>(), "Second は AppearDelay=0.3 でまだ始まっていない(CanvasGroup 未追加)はず");
            Assert.IsTrue(_manager.IsOpening(handle), "Second がまだ完了していないので入力はブロックされたまま");

            TickBoth(0.05f, 5); // 追加 0.25s(合計 0.4s): Second の Delay(0.3s)を超えて再生・完了する
            var secondGroup = FindTransform(handle, "Second").GetComponent<CanvasGroup>();
            Assert.AreEqual(1f, secondGroup.alpha, 0.01f);
            Assert.IsFalse(_manager.IsOpening(handle));
        }

        // ── Idle ──

        [Test]
        public void Idle_StartsAfterAppearCompletes_AndStopsOnClose()
        {
            AddElement("Icon");
            var data = CreateCanvasData(1);
            data.ElementEffects = new[]
            {
                new ElementFx
                {
                    ElementPath = "Icon",
                    AppearPreset = new UiPresetRef { Preset = UiPreset.FadeIn, Duration = 0.1f },
                    IdlePreset = new UiPresetRef { Preset = UiPreset.Pulse, Duration = 1f },
                },
            };

            var handle = _manager.OpenData(data);
            Assert.AreEqual(0, _tweens.ActiveCount, "Appear が始まる前は Idle は動いていないはず(まだ Tick していない)");

            TickBoth(0.05f, 4); // Appear(0.1s)完了直後まで進める
            Assert.Greater(_tweens.ActiveCount, 0, "Appear 完了後は Idle(Pulse, PingPong)が動いているはず");

            _manager.Close(handle);
            TickBoth(0.05f, 6);

            Assert.IsFalse(_manager.IsOpen(handle), "Disappear 未設定なら Close は素早く完了するはず");
        }

        // ── Close の完了待ち ──

        [Test]
        public void Close_WaitsForDisappear_ThenFinalizes()
        {
            AddElement("Icon");
            var data = CreateCanvasData(1);
            data.ElementEffects = new[]
            {
                new ElementFx { ElementPath = "Icon", DisappearPreset = new UiPresetRef { Preset = UiPreset.FadeOut, Duration = 0.3f } },
            };

            var handle = _manager.OpenData(data);
            TickBoth(0.01f, 1); // Appear 無し(即完了)を確定させる

            _manager.Close(handle);
            Assert.IsTrue(_manager.IsOpen(handle), "Disappear が終わるまでは Close 未完了(スタックに残る)のはず");
            Assert.IsTrue(_manager.IsClosing(handle));

            TickBoth(0.05f, 3); // 0.15s: まだ 0.3s の Disappear が終わっていない
            Assert.IsTrue(_manager.IsOpen(handle), "Disappear の途中ではまだ閉じ切っていないはず");

            TickBoth(0.05f, 5); // 追加 0.25s(合計 0.4s): Disappear 完了
            Assert.IsFalse(_manager.IsOpen(handle));
        }

        // ── StopAll ──

        [Test]
        public void StopAll_ClosesImmediately_EvenWithPendingElementFx()
        {
            AddElement("Icon");
            var data = CreateCanvasData(1);
            data.ElementEffects = new[]
            {
                new ElementFx
                {
                    ElementPath = "Icon",
                    AppearPreset = new UiPresetRef { Preset = UiPreset.FadeIn, Duration = 10f },
                    DisappearPreset = new UiPresetRef { Preset = UiPreset.FadeOut, Duration = 10f },
                },
            };

            var handle = _manager.OpenData(data);
            TickBoth(0.05f, 1);

            Assert.DoesNotThrow(() => _manager.StopAll(DDrive.Foundation.Manager.StopReason.Manual));
            Assert.IsFalse(_manager.IsOpen(handle));
        }

        // ── 欠損パス ──

        [Test]
        public void MissingElementPath_WarnsOnce_AndDoesNotThrow()
        {
            var data = CreateCanvasData(1);
            data.ElementEffects = new[]
            {
                new ElementFx { ElementPath = "NoSuchElement", AppearPreset = new UiPresetRef { Preset = UiPreset.FadeIn } },
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*ElementFx.*NoSuchElement.*"));
            Handle<CanvasMarker> handle = default;
            Assert.DoesNotThrow(() => handle = _manager.OpenData(data));
            Assert.DoesNotThrow(() => TickBoth(0.05f, 2));
            Assert.DoesNotThrow(() => _manager.Close(handle));
            TickBoth(0.05f, 2);
        }

        // ── 4-7 残り: レイヤー既定 Skin/SE ──

        [Test]
        public void LayerDefaultSkin_AppliesToButtonWithoutSkinId()
        {
            var button = AddButton("Btn");
            Assert.IsFalse(button.SkinId.IsValid);

            var skin = ScriptableObject.CreateInstance<ButtonSkinData>();
            skin.Normal = StateVisual.Default;
            skin.Normal.Tint = Color.red;

            var settings = ScriptableObject.CreateInstance<UiLayerSettings>();
            settings.Layers = new[]
            {
                new UiLayerDefaultEntry { Layer = UiLayer.HUD, DefaultButtonSkin = new AssetId<ControlSkinMarker>(555UL, AssetType.ControlSkin) },
            };

            const string address = "skin/555";
            _loader.Assets[address] = skin;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = 555UL, Type = AssetType.ControlSkin, Address = address } });
            _registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            _registry.ResolveAsync<ControlSkinData>(555UL).GetAwaiter().GetResult();

            _manager.SetLayerSettings(settings);
            var data = CreateCanvasData(1);
            data.Layer = UiLayer.HUD;

            var handle = _manager.OpenData(data);
            var resolvedButton = _manager.GetComponent<UiButton>(handle, "Btn");

            Assert.AreEqual(Color.red, resolvedButton.TargetGraphic.color, "レイヤー既定 Skin(Normal.Tint=赤)が当たっているはず");

            Object.DestroyImmediate(skin);
            Object.DestroyImmediate(settings);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void LayerDefaultClickSe_FallbackPath_DoesNotThrow_WhenAudioUnbound()
        {
            UiInteractable.ResetDoubleFireGuardForTests();
            var button = AddButton("Btn2");
            button.DoubleClickSec = 0f;
            button.BlockDoubleFire = false;

            var settings = ScriptableObject.CreateInstance<UiLayerSettings>();
            settings.Layers = new[]
            {
                new UiLayerDefaultEntry { Layer = UiLayer.HUD, DefaultClickSe = new AssetId<DDrive.Runtime.Audio.SeMarker>(777UL, AssetType.Se) },
            };
            _manager.SetLayerSettings(settings);

            var data = CreateCanvasData(1);
            data.Layer = UiLayer.HUD;
            var handle = _manager.OpenData(data);
            var resolvedButton = _manager.GetComponent<UiButton>(handle, "Btn2");

            Assert.DoesNotThrow(() =>
            {
                resolvedButton.Press();
                resolvedButton.Release(true);
            }, "Audio が Bind されていなくても DefaultClickSe フォールバック経路は例外を投げないはず");

            Object.DestroyImmediate(settings);
        }

        private Transform FindTransform(Handle<CanvasMarker> handle, string path)
            => _manager.GetGameObject(handle).transform.Find(path);
    }

    public class ElementFxValidatorTests
    {
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("ElementFxValidatorPrefab", typeof(RectTransform));
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(_prefab.transform);
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
        public void UnresolvedElementPath_ReportsError()
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Prefab = _prefab;
            data.ElementEffects = new[] { new ElementFx { ElementPath = "NoSuchPath" } };

            var results = Run(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("ElementPath")));
            Object.DestroyImmediate(data);
        }

        [Test]
        public void NegativeAppearDelay_ReportsError()
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Prefab = _prefab;
            data.ElementEffects = new[] { new ElementFx { ElementPath = "Icon", AppearDelay = -0.1f } };

            var results = Run(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("AppearDelay")));
            Object.DestroyImmediate(data);
        }

        [Test]
        public void DuplicateElementPath_ReportsWarning()
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Prefab = _prefab;
            data.ElementEffects = new[]
            {
                new ElementFx { ElementPath = "Icon" },
                new ElementFx { ElementPath = "Icon" },
            };

            var results = Run(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("重複")));
            Object.DestroyImmediate(data);
        }

        [Test]
        public void NoAppear_ButHasDisappear_ReportsInfo()
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Prefab = _prefab;
            data.ElementEffects = new[]
            {
                new ElementFx { ElementPath = "Icon", DisappearPreset = new UiPresetRef { Preset = UiPreset.FadeOut } },
            };

            var results = Run(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Info && r.Message.Contains("出現なし")));
            Object.DestroyImmediate(data);
        }

        [Test]
        public void IdlePreset_NotLoopPreset_ReportsWarning()
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Prefab = _prefab;
            data.ElementEffects = new[]
            {
                new ElementFx { ElementPath = "Icon", IdlePreset = new UiPresetRef { Preset = UiPreset.FadeIn } },
            };

            var results = Run(data);
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("常時系")));
            Object.DestroyImmediate(data);
        }
    }
}
