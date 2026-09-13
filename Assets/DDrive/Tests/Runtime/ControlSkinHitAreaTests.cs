using System.Collections.Generic;
using System.Text.RegularExpressions;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DDrive.Tests.Runtime
{
    // 2026-09-14 — Skin の当たり判定(HitAreaExpand / AlphaHitThreshold / SliderSkinData.ExtraHitPadding)。
    public class ControlSkinHitAreaTests
    {
        private readonly List<Object> _cleanup = new();

        [SetUp]
        public void SetUp() => UiInteractable.ResetDoubleFireGuardForTests();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _cleanup)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _cleanup.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _cleanup.Add(o);
            return o;
        }

        private UiButton CreateButton(out Image image)
        {
            var go = Track(new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(UiButton)));
            image = go.GetComponent<Image>();
            var button = go.GetComponent<UiButton>();
            button.TargetGraphic = image;
            return button;
        }

        private Sprite CreateSprite(bool readable)
        {
            var texture = Track(new Texture2D(4, 4));
            var sprite = Track(Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect));
            if (!readable)
            {
                texture.Apply(false, true);
            }

            return sprite;
        }

        [Test]
        public void SetVisual_AppliesHitAreaExpand_AsNegativeRaycastPadding()
        {
            var button = CreateButton(out var image);
            var skin = Track(ScriptableObject.CreateInstance<ButtonSkinData>());
            skin.HitAreaExpand = new Vector4(10f, 20f, 30f, 40f);

            button.SetVisual(skin);

            Assert.AreEqual(new Vector4(-10f, -20f, -30f, -40f), image.raycastPadding);
        }

        [Test]
        public void SetVisual_AppliesAlphaHitThreshold_WhenSpriteReadable()
        {
            var button = CreateButton(out var image);
            image.sprite = CreateSprite(readable: true);
            var skin = Track(ScriptableObject.CreateInstance<ButtonSkinData>());
            skin.AlphaHitThreshold = 0.5f;

            button.SetVisual(skin);

            Assert.AreEqual(0.5f, image.alphaHitTestMinimumThreshold, 0.0001f);
        }

        [Test]
        public void SetVisual_SkipsAlphaHitThreshold_WithWarning_WhenSpriteNotReadable()
        {
            var button = CreateButton(out var image);
            image.sprite = CreateSprite(readable: false);
            var skin = Track(ScriptableObject.CreateInstance<ButtonSkinData>());
            skin.AlphaHitThreshold = 0.5f;

            LogAssert.Expect(LogType.Warning, new Regex("AlphaHitThreshold"));
            button.SetVisual(skin);

            Assert.AreEqual(0f, image.alphaHitTestMinimumThreshold);
        }

        // 回帰: Image.alphaHitTestMinimumThreshold の setter は画像が読めないと 0 でも例外を投げる。
        // 透明判定を使わない普通のボタン(読めない画像が大半)で、状態変化・無効化のたびに例外が出ないこと。
        [Test]
        public void UnreadableSprite_WithoutAlphaThreshold_DoesNotThrow()
        {
            var button = CreateButton(out var image);
            image.sprite = CreateSprite(readable: false);
            var skin = Track(ScriptableObject.CreateInstance<ButtonSkinData>());

            Assert.DoesNotThrow(() =>
            {
                button.SetVisual(skin);
                button.ForceStateForPreview(ControlState.Pressed);
                button.gameObject.SetActive(false);
                button.gameObject.SetActive(true);
            });
            Assert.AreEqual(0f, image.alphaHitTestMinimumThreshold);
        }

        [Test]
        public void SliderSkin_EffectiveHitAreaExpand_AddsExtraHitPadding()
        {
            var skin = Track(ScriptableObject.CreateInstance<SliderSkinData>());
            skin.HitAreaExpand = new Vector4(1f, 2f, 3f, 4f);
            skin.ExtraHitPadding = new Vector2(5f, 6f);

            Assert.AreEqual(new Vector4(6f, 8f, 8f, 10f), skin.EffectiveHitAreaExpand);
        }

        [Test]
        public void Validator_Warns_WhenAlphaThresholdOn_AndOverrideSpriteNotReadable()
        {
            var skin = Track(ScriptableObject.CreateInstance<ButtonSkinData>());
            skin.AlphaHitThreshold = 0.5f;
            skin.Pressed.OverrideSprite = CreateSprite(readable: false);

            var results = Validate(skin);

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("Pressed")));
        }

        [Test]
        public void Validator_NoHitAreaWarning_WhenAlphaThresholdZero()
        {
            var skin = Track(ScriptableObject.CreateInstance<ButtonSkinData>());
            skin.Pressed.OverrideSprite = CreateSprite(readable: false);

            var results = Validate(skin);

            Assert.IsFalse(results.Exists(r => r.Message.Contains("AlphaHitThreshold")));
        }

        private static List<ValidationResult> Validate(ButtonSkinData skin)
            => new(new ButtonSkinDataValidator().Validate(skin, new ValidationContext(new List<AssetDataBase> { skin })));
    }
}
