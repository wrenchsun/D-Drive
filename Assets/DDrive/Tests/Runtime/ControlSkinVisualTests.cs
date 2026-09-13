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
    // 2026-09-14 — Skin の状態ごとのスプライトアニメ / スクロール、画像の復元、スライダーのパーツ反映。
    public class ControlSkinVisualTests
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

        private Sprite MakeSprite(string name)
        {
            var texture = Track(new Texture2D(4, 4));
            var sprite = Track(Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect));
            sprite.name = name;
            return sprite;
        }

        private UiButton MakeButton(out Image image)
        {
            var go = Track(new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(UiButton)));
            image = go.GetComponent<Image>();
            var button = go.GetComponent<UiButton>();
            button.TargetGraphic = image;
            return button;
        }

        [Test]
        public void SpriteAnim_AdvancesFrames_AndLoops()
        {
            var button = MakeButton(out var image);
            var s0 = MakeSprite("s0");
            var s1 = MakeSprite("s1");
            var s2 = MakeSprite("s2");
            var skin = Track(ScriptableObject.CreateInstance<ButtonSkinData>());
            skin.Normal.AnimFrames = new[] { s0, s1, s2 };
            skin.Normal.AnimFps = 10f;
            skin.Normal.AnimLoop = true;

            button.SetVisual(skin);
            Assert.AreSame(s0, image.sprite);

            button.TickVisuals(0.1f);
            Assert.AreSame(s1, image.sprite);
            button.TickVisuals(0.1f);
            Assert.AreSame(s2, image.sprite);
            button.TickVisuals(0.1f);
            Assert.AreSame(s0, image.sprite, "ループで最初のコマに戻る");
            Assert.IsTrue(button.HasVisualAnimation);
        }

        [Test]
        public void SpriteAnim_NoLoop_StopsAtLastFrame()
        {
            var button = MakeButton(out var image);
            var s0 = MakeSprite("s0");
            var s1 = MakeSprite("s1");
            var skin = Track(ScriptableObject.CreateInstance<ButtonSkinData>());
            skin.Normal.AnimFrames = new[] { s0, s1 };
            skin.Normal.AnimFps = 10f;
            skin.Normal.AnimLoop = false;

            button.SetVisual(skin);
            button.TickVisuals(1f);

            Assert.AreSame(s1, image.sprite);
        }

        [Test]
        public void StateWithoutSprite_RestoresOriginalSprite()
        {
            var button = MakeButton(out var image);
            var original = MakeSprite("original");
            var hover = MakeSprite("hover");
            image.sprite = original;
            var skin = Track(ScriptableObject.CreateInstance<ButtonSkinData>());
            skin.Hover.OverrideSprite = hover;

            button.SetVisual(skin);
            button.ForceStateForPreview(ControlState.Hover);
            Assert.AreSame(hover, image.sprite);

            button.ForceStateForPreview(ControlState.Normal);
            Assert.AreSame(original, image.sprite, "画像を持たない状態に戻ったら元の画像に戻る");
        }

        [Test]
        public void Scroll_UsesSharedMaterialPerSpeed_AndRestores()
        {
            var a = MakeButton(out var imageA);
            var b = MakeButton(out var imageB);
            var template = Track(new Material(Shader.Find("UI/Default")));
            var skin = Track(ScriptableObject.CreateInstance<ButtonSkinData>());
            skin.ScrollMaterial = template;
            skin.Hover.ScrollSpeed = new Vector2(0.5f, 0f);

            a.SetVisual(skin);
            b.SetVisual(skin);
            a.ForceStateForPreview(ControlState.Hover);
            b.ForceStateForPreview(ControlState.Hover);

            Assert.AreNotSame(template, imageA.material);
            Assert.AreEqual(new Vector4(0.5f, 0f, 0f, 0f), imageA.material.GetVector("_ScrollSpeed"));
            Assert.AreSame(imageA.material, imageB.material, "同じ速度はマテリアルを共有する");

            a.ForceStateForPreview(ControlState.Normal);
            Assert.AreSame(imageA.defaultMaterial, imageA.material, "スクロールしない状態では元のマテリアルに戻る");
        }

        [Test]
        public void Scroll_WithoutMaterial_WarnsAndKeepsDefault()
        {
            var button = MakeButton(out var image);
            var skin = Track(ScriptableObject.CreateInstance<ButtonSkinData>());
            skin.Normal.ScrollSpeed = new Vector2(1f, 0f);

            LogAssert.Expect(LogType.Warning, new Regex("Scroll Material"));
            button.SetVisual(skin);

            Assert.AreSame(image.defaultMaterial, image.material);
        }

        [Test]
        public void SliderParts_ApplySprite_AndSkipUnsetTintAndScale()
        {
            var go = Track(new GameObject("Slider", typeof(RectTransform), typeof(Image), typeof(UiSlider)));
            var slider = go.GetComponent<UiSlider>();
            slider.TargetGraphic = go.GetComponent<Image>();
            slider.TrackRect = (RectTransform)go.transform;

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(go.transform, false);
            slider.FillRect = (RectTransform)fillGo.transform;
            var fillImage = fillGo.GetComponent<Image>();
            fillImage.color = Color.green;

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(go.transform, false);
            slider.HandleRect = (RectTransform)handleGo.transform;
            var handleImage = handleGo.GetComponent<Image>();

            var fillSprite = MakeSprite("fill");
            var handleSprite = MakeSprite("handle");
            var skin = Track(ScriptableObject.CreateInstance<SliderSkinData>());
            skin.Fill.OverrideSprite = fillSprite;        // Tint / Scale は未設定(既定値)のまま
            skin.Handle.OverrideSprite = handleSprite;
            skin.Handle.Tint = Color.red;

            slider.SetVisual(skin);

            Assert.AreSame(fillSprite, fillImage.sprite);
            Assert.AreEqual(Color.green, fillImage.color, "Tint 未設定のパーツは色を変えない(透明にしない)");
            Assert.AreEqual(Vector3.one, slider.FillRect.localScale, "Scale 未設定のパーツは拡大率を変えない(潰さない)");
            Assert.AreSame(handleSprite, handleImage.sprite);
            Assert.AreEqual(Color.red, handleImage.color);
        }

        [Test]
        public void Validator_Warns_ScrollWithoutMaterial_AndEmptyFrame()
        {
            var skin = Track(ScriptableObject.CreateInstance<ButtonSkinData>());
            skin.Hover.ScrollSpeed = new Vector2(1f, 0f);
            skin.Pressed.AnimFrames = new Sprite[] { null };

            var results = new List<ValidationResult>(new ButtonSkinDataValidator().Validate(skin, new ValidationContext(new List<AssetDataBase> { skin })));

            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("Scroll Material")));
            Assert.IsTrue(results.Exists(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("空のコマ")));
        }
    }
}
