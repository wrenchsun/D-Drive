using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Tests.Runtime
{
    // [15_ui_interaction.md] Part A / [18_ui_controls.md] Part A — UiButton(4-6)。
    // EventSystem 無しで Press()/Release()/Hover()/Focus()/Advance() を直接呼んで駆動する
    // (Update() は本体では Time.unscaledDeltaTime を渡すだけの薄いラッパー)。
    public class UiButtonTests
    {
        private static GameObject CreateButton(out UiButton button, out Image image)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(UiButton));
            image = go.GetComponent<Image>();
            button = go.GetComponent<UiButton>();
            button.TargetGraphic = image;
            // DoubleClick/BlockDoubleFire は各テストの意図を明確にするため既定を切り、必要なテストだけ有効化する。
            button.DoubleClickSec = 0f;
            button.CooldownSec = 0f;
            button.BlockDoubleFire = false;
            return go;
        }

        [SetUp]
        public void SetUp() => UiInteractable.ResetDoubleFireGuardForTests();

        [Test]
        public void Click_FiresOnce_OnPressAndRelease()
        {
            var go = CreateButton(out var button, out _);
            var count = 0;
            button.OnClick += () => count++;

            button.Press();
            button.Release(true);

            Assert.AreEqual(1, count);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Cooldown_SuppressesSecondClick_ThenAllowsAfterElapsed()
        {
            var go = CreateButton(out var button, out _);
            button.CooldownSec = 0.2f;
            var count = 0;
            button.OnClick += () => count++;

            button.Press();
            button.Release(true);
            Assert.AreEqual(1, count);

            button.Press();
            button.Release(true);
            Assert.AreEqual(1, count, "Cooldown 中は発火しない");

            button.Advance(0.25f);
            button.Press();
            button.Release(true);
            Assert.AreEqual(2, count);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void BlockDoubleFire_SuppressesSecondButton_SameFrame()
        {
            var goA = CreateButton(out var a, out _);
            var goB = CreateButton(out var b, out _);
            a.BlockDoubleFire = true;
            b.BlockDoubleFire = true;

            var countA = 0;
            var countB = 0;
            a.OnClick += () => countA++;
            b.OnClick += () => countB++;

            a.Press();
            a.Release(true);
            b.Press();
            b.Release(true);

            Assert.AreEqual(1, countA);
            Assert.AreEqual(0, countB);

            Object.DestroyImmediate(goA);
            Object.DestroyImmediate(goB);
        }

        [Test]
        public void LongPress_FiresAtLongPressSec_AndSuppressesClick()
        {
            var go = CreateButton(out var button, out _);
            button.LongPressSec = 0.5f;
            var clickCount = 0;
            var longPressCount = 0;
            button.OnClick += () => clickCount++;
            button.OnLongPress += () => longPressCount++;

            button.Press();
            button.Advance(0.5f);
            button.Release(true);

            Assert.AreEqual(1, longPressCount);
            Assert.AreEqual(0, clickCount);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Repeat_FiresMultipleTimes_WhileHeld()
        {
            var go = CreateButton(out var button, out _);
            button.LongPressSec = 0.5f;
            button.RepeatIntervalSec = 0.1f;
            var repeatCount = 0;
            button.OnRepeat += () => repeatCount++;

            button.Press();
            button.Advance(0.5f); // LongPress 発火
            button.Advance(0.1f);
            button.Advance(0.1f);
            button.Advance(0.1f);
            button.Release(false);

            Assert.AreEqual(3, repeatCount);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void DoubleClick_Fires_AndSuppressesSecondSingleClick()
        {
            var go = CreateButton(out var button, out _);
            button.DoubleClickSec = 0.3f;
            var clickCount = 0;
            var doubleCount = 0;
            button.OnClick += () => clickCount++;
            button.OnDoubleClick += () => doubleCount++;

            button.Press();
            button.Release(true);
            button.Advance(0.1f);
            button.Press();
            button.Release(true);

            Assert.AreEqual(1, doubleCount);
            Assert.AreEqual(0, clickCount);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SingleClick_FiresAfterDoubleClickWindow_WhenNotDoubled()
        {
            var go = CreateButton(out var button, out _);
            button.DoubleClickSec = 0.3f;
            var clickCount = 0;
            button.OnClick += () => clickCount++;

            button.Press();
            button.Release(true);
            button.Advance(0.35f);

            Assert.AreEqual(1, clickCount);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void DisabledPress_RaisesOnDenied_AndNoClick()
        {
            var go = CreateButton(out var button, out _);
            button.Interactable = false;
            var denied = 0;
            var click = 0;
            button.OnDenied += () => denied++;
            button.OnClick += () => click++;

            button.Press();
            button.Release(true);

            Assert.AreEqual(1, denied);
            Assert.AreEqual(0, click);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void LockedPress_RaisesOnDenied_AndNoClick_AndKeepsReasonKey()
        {
            var go = CreateButton(out var button, out _);
            button.SetLocked(true, "reason/test");
            var denied = 0;
            button.OnDenied += () => denied++;

            button.Press();
            button.Release(true);

            Assert.AreEqual(1, denied);
            Assert.AreEqual("reason/test", button.LockReasonKey);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void StateTransitions_HoverPressHover_AndLockedOverrides()
        {
            var go = CreateButton(out var button, out _);

            button.Hover(true);
            Assert.AreEqual(ControlState.Hover, button.State);

            button.Press();
            Assert.AreEqual(ControlState.Pressed, button.State);

            button.Release(true);
            Assert.AreEqual(ControlState.Hover, button.State);

            button.Hover(false);
            Assert.AreEqual(ControlState.Normal, button.State);

            button.Hover(true);
            button.SetLocked(true);
            Assert.AreEqual(ControlState.Locked, button.State);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void WaitClickAsync_CompletesOnSimulateClick()
        {
            var go = CreateButton(out var button, out _);
            var task = button.WaitClickAsync(System.Threading.CancellationToken.None);
            var awaiter = task.GetAwaiter();
            Assert.IsFalse(awaiter.IsCompleted);

            button.SimulateClick();

            Assert.IsTrue(awaiter.IsCompleted);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void SetVisual_AppliesTintAndScale_ToGraphicAndRectTransform()
        {
            var go = CreateButton(out var button, out var image);
            var skin = ScriptableObject.CreateInstance<ButtonSkinData>();
            skin.Normal.Tint = Color.red;
            skin.Normal.Scale = DDrive.Foundation.Values.ValueDef.Constant01(0.8f);

            button.SetVisual(skin);

            Assert.AreEqual(Color.red, image.color);
            Assert.AreEqual(0.8f, ((RectTransform)button.transform).localScale.x, 0.001f);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(skin);
        }
    }
}
