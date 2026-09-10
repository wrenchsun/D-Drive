using DDrive.Editor.Ui;
using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [18_ui_controls.md] B-6/B-7(4-17) — SliderEditor の純ロジック部分(SliderPresets.Apply /
    // SliderEditorMath のサンプリング)。EditorWindow の UI Toolkit 組み立て自体はここでは検証しない
    // (UiTweenEditorTests と同じ方針)。
    public class SliderEditorTests
    {
        private static UiSlider CreateSlider()
        {
            var go = new GameObject("Slider", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UiSlider));
            return go.GetComponent<UiSlider>();
        }

        // ── SliderPresets.Apply ──

        [Test]
        public void Apply_音量_SetsRangeStepResponseFollow()
        {
            var slider = CreateSlider();
            SliderPresets.Apply(slider, SliderPresets.SliderPreset.音量);

            Assert.AreEqual(0f, slider.Min);
            Assert.AreEqual(1f, slider.Max);
            Assert.AreEqual(0f, slider.Step);
            Assert.AreEqual(0, slider.Notches);
            Assert.AreEqual(ValueMode.Parametric, slider.Response.Mode);
            Assert.AreEqual(ValueMode.Constant, slider.FollowMotion.Mode);

            Object.DestroyImmediate(slider.gameObject);
        }

        [Test]
        public void Apply_感度_SetsWideRange_And_OutQuadResponse()
        {
            var slider = CreateSlider();
            SliderPresets.Apply(slider, SliderPresets.SliderPreset.感度);

            Assert.AreEqual(0.1f, slider.Min);
            Assert.AreEqual(5f, slider.Max);
            Assert.AreEqual(ValueMode.Parametric, slider.Response.Mode);

            Object.DestroyImmediate(slider.gameObject);
        }

        [Test]
        public void Apply_HPバー_SetsIntegerStepRange_And_FollowMotion()
        {
            var slider = CreateSlider();
            SliderPresets.Apply(slider, SliderPresets.SliderPreset.HPバー);

            Assert.AreEqual(0f, slider.Min);
            Assert.AreEqual(100f, slider.Max);
            Assert.AreEqual(1f, slider.Step);
            Assert.AreEqual(0, slider.Notches);
            Assert.AreEqual(ValueMode.Parametric, slider.FollowMotion.Mode);
            Assert.Greater(slider.FollowMotion.Time.Value, 0f);

            Object.DestroyImmediate(slider.gameObject);
        }

        [Test]
        public void Apply_スタミナ_SetsNotchesAndSnapThreshold()
        {
            var slider = CreateSlider();
            SliderPresets.Apply(slider, SliderPresets.SliderPreset.スタミナ);

            Assert.AreEqual(4, slider.Notches);
            Assert.AreEqual(0.03f, slider.SnapThreshold);
            Assert.AreEqual(1f, slider.Step);

            Object.DestroyImmediate(slider.gameObject);
        }

        [Test]
        public void Apply_キャラメイク_SetsWholeNumbers_And_TenNotches()
        {
            var slider = CreateSlider();
            SliderPresets.Apply(slider, SliderPresets.SliderPreset.キャラメイク);

            Assert.AreEqual(0f, slider.Min);
            Assert.AreEqual(10f, slider.Max);
            Assert.IsTrue(slider.WholeNumbers);
            Assert.AreEqual(10, slider.Notches);

            Object.DestroyImmediate(slider.gameObject);
        }

        [Test]
        public void Apply_None_DoesNothing()
        {
            var slider = CreateSlider();
            var beforeMax = slider.Max;

            SliderPresets.Apply(slider, SliderPresets.SliderPreset.None);

            Assert.AreEqual(beforeMax, slider.Max);
            Object.DestroyImmediate(slider.gameObject);
        }

        [Test]
        public void Apply_NullSlider_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => SliderPresets.Apply(null, SliderPresets.SliderPreset.音量));
        }

        // ── SliderEditorMath.SampleResponse ──

        [Test]
        public void SampleResponse_InQuad_IsMonotoneIncreasing_And_EndpointsAre0And1()
        {
            var slider = CreateSlider();
            slider.Response = new ValueDef
            {
                Mode = ValueMode.Parametric,
                Parametric = EaseDef.Named(DDrive.Foundation.Easing.Ease.InQuad),
                From = 0f,
                To = 1f,
            };

            var samples = SliderEditorMath.SampleResponse(slider, 64);

            Assert.AreEqual(64, samples.Length);
            Assert.That(samples[0], Is.EqualTo(0f).Within(0.001f));
            Assert.That(samples[samples.Length - 1], Is.EqualTo(1f).Within(0.001f));

            for (var i = 1; i < samples.Length; i++)
            {
                Assert.GreaterOrEqual(samples[i], samples[i - 1] - 1e-4f, $"index {i} は単調増加でない");
            }

            Object.DestroyImmediate(slider.gameObject);
        }

        [Test]
        public void SampleResponse_ConstantMode_IsLinear()
        {
            var slider = CreateSlider();
            // Response 未設定(既定 Mode=Constant)は線形として扱う([18] B-1)。
            var samples = SliderEditorMath.SampleResponse(slider, 5);

            Assert.That(samples[0], Is.EqualTo(0f).Within(0.001f));
            Assert.That(samples[2], Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(samples[4], Is.EqualTo(1f).Within(0.001f));

            Object.DestroyImmediate(slider.gameObject);
        }

        [Test]
        public void SampleResponse_NullSlider_ReturnsZeroedArray()
        {
            var samples = SliderEditorMath.SampleResponse(null, 8);
            Assert.AreEqual(8, samples.Length);
            foreach (var v in samples)
            {
                Assert.AreEqual(0f, v);
            }
        }

        // ── SliderEditorMath.NotchPositions ──

        [Test]
        public void NotchPositions_Notches4_ReturnsFiveEquallySpacedPositions()
        {
            var positions = SliderEditorMath.NotchPositions(4);

            Assert.AreEqual(new[] { 0f, 0.25f, 0.5f, 0.75f, 1f }, positions);
        }

        [Test]
        public void NotchPositions_ZeroOrNegative_ReturnsEmpty()
        {
            Assert.AreEqual(0, SliderEditorMath.NotchPositions(0).Length);
            Assert.AreEqual(0, SliderEditorMath.NotchPositions(-1).Length);
        }
    }
}
