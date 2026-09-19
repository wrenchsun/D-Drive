using DDrive.Editor.Cutscene;
using DDrive.Runtime.Cutscene;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [26_timeline.md] §5.1(6-10c)「逃げ道: 1 FBX に複数ショット」— CutsceneFrameRangeTrimmer のテスト。
    public class CutsceneFrameRangeTrimmerTests
    {
        [Test]
        public void ShouldTrim_DefaultRange_IsFalse()
        {
            Assert.IsFalse(CutsceneFrameRangeTrimmer.ShouldTrim(new FrameRange { Start = 0, End = 0 }));
        }

        [Test]
        public void ShouldTrim_NonDefaultRange_IsTrue()
        {
            Assert.IsTrue(CutsceneFrameRangeTrimmer.ShouldTrim(new FrameRange { Start = 10, End = 20 }));
        }

        [Test]
        public void TrimClip_DefaultRange_ReturnsSameInstance()
        {
            var clip = new AnimationClip();
            try
            {
                var result = CutsceneFrameRangeTrimmer.TrimClip(clip, new FrameRange(), 30f);
                Assert.AreSame(clip, result);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void TrimCurve_KeepsKeysWithinRange_AndRebasesTime()
        {
            var curve = new AnimationCurve();
            curve.AddKey(0f, 0f);
            curve.AddKey(1f, 10f);
            curve.AddKey(2f, 20f);
            curve.AddKey(3f, 30f);

            // frame 30 == 1 秒, frame 60 == 2 秒(fps=30 想定)なので [1,2] 秒だけを残す。
            var trimmed = CutsceneFrameRangeTrimmer.TrimCurve(curve, 1f, 2f);

            Assert.AreEqual(2, trimmed.length);
            Assert.AreEqual(0f, trimmed.keys[0].time, 0.0001f);
            Assert.AreEqual(10f, trimmed.keys[0].value, 0.0001f);
            Assert.AreEqual(1f, trimmed.keys[1].time, 0.0001f);
            Assert.AreEqual(20f, trimmed.keys[1].value, 0.0001f);
        }

        [Test]
        public void TrimCurve_NoKeysInRange_FallsBackToSingleEvaluatedKey()
        {
            var curve = AnimationCurve.Constant(0f, 5f, 42f);

            var trimmed = CutsceneFrameRangeTrimmer.TrimCurve(curve, 100f, 200f);

            Assert.AreEqual(1, trimmed.length);
            Assert.AreEqual(42f, trimmed.keys[0].value, 0.0001f);
        }

        [Test]
        public void TrimClip_ProducesShorterClipWithRebasedCurve()
        {
            var clip = new AnimationClip();
            clip.SetCurve(string.Empty, typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 3f, 30f));
            try
            {
                // fps=30 → frame 30..60 = 1.0s..2.0s。End=59(含む) → 60 フレーム目(2.0s)未満まで。
                var trimmed = CutsceneFrameRangeTrimmer.TrimClip(clip, new FrameRange { Start = 30, End = 59 }, 30f);
                try
                {
                    var curve = UnityEditor.AnimationUtility.GetEditorCurve(
                        trimmed, new UnityEditor.EditorCurveBinding { path = string.Empty, type = typeof(Transform), propertyName = "m_LocalPosition.x" });

                    Assert.IsNotNull(curve);
                    Assert.AreEqual(10f, curve.Evaluate(0f), 0.01f); // 元の t=1.0 (value=10) が新 t=0 になる
                }
                finally
                {
                    Object.DestroyImmediate(trimmed);
                }
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }
    }
}
