using DDrive.Editor.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [26_timeline.md] §4.6.2/§5.2/§7.3(6-10c) — CutsceneCameraCurveExtractor のテスト。
    // 実 FBX を使わず、コードで組んだ AnimationClip + GameObject(Camera 付き)で検証する
    // (6-10c の方針: 「純ロジックの部分はコードで作った AnimationClip/GameObject でテストする」)。
    public class CutsceneCameraCurveExtractorTests
    {
        [Test]
        public void Extract_PositionRotationFov_AtRoot_FillsCurves()
        {
            var clip = new AnimationClip();
            clip.SetCurve(string.Empty, typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 5f));
            clip.SetCurve(string.Empty, typeof(Transform), "m_LocalPosition.y", AnimationCurve.Constant(0f, 1f, 2f));
            clip.SetCurve(string.Empty, typeof(Transform), "m_LocalPosition.z", AnimationCurve.Constant(0f, 1f, -3f));
            clip.SetCurve(string.Empty, typeof(Transform), "m_LocalRotation.x", AnimationCurve.Constant(0f, 1f, 0f));
            clip.SetCurve(string.Empty, typeof(Transform), "m_LocalRotation.y", AnimationCurve.Constant(0f, 1f, 0f));
            clip.SetCurve(string.Empty, typeof(Transform), "m_LocalRotation.z", AnimationCurve.Constant(0f, 1f, 0f));
            clip.SetCurve(string.Empty, typeof(Transform), "m_LocalRotation.w", AnimationCurve.Constant(0f, 1f, 1f));
            clip.SetCurve(string.Empty, typeof(Camera), "field of view", AnimationCurve.Linear(0f, 40f, 1f, 60f));

            var target = ScriptableObject.CreateInstance<CutsceneCameraClip>();
            try
            {
                var found = CutsceneCameraCurveExtractor.Extract(clip, string.Empty, target);

                Assert.IsTrue(found);
                Assert.AreEqual(5f, target.PosX.Evaluate(1f), 0.001f);
                Assert.AreEqual(2f, target.PosY.Evaluate(0.5f), 0.001f);
                Assert.AreEqual(-3f, target.PosZ.Evaluate(0.5f), 0.001f);
                Assert.AreEqual(1f, target.RotW.Evaluate(0.5f), 0.001f);
                Assert.AreEqual(60f, target.FieldOfView.Evaluate(1f), 0.001f);

                // 見つからなかったチャンネルは空カーブ(§4.6.4「取れなければ書かない」)。
                Assert.AreEqual(0, target.FocusDistance.length);
                Assert.AreEqual(0, target.Aperture.length);
                Assert.AreEqual(0, target.FocalLengthMm.length);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void Extract_CameraUnderChildNode_UsesRelativePath()
        {
            var clip = new AnimationClip();
            clip.SetCurve("CamNode", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 7f));
            // 無関係なパス(別ノード)のカーブが混ざっても影響しないことも確認する。
            clip.SetCurve("Other", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 999f));

            var target = ScriptableObject.CreateInstance<CutsceneCameraClip>();
            try
            {
                var found = CutsceneCameraCurveExtractor.Extract(clip, "CamNode", target);

                Assert.IsTrue(found);
                Assert.AreEqual(7f, target.PosX.Evaluate(0f), 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void Extract_NoMatchingCurves_ReturnsFalseWithDefaults()
        {
            var clip = new AnimationClip();
            var target = ScriptableObject.CreateInstance<CutsceneCameraClip>();
            try
            {
                var found = CutsceneCameraCurveExtractor.Extract(clip, string.Empty, target);

                Assert.IsFalse(found);
                Assert.AreEqual(0, target.PosX.length);
                Assert.AreEqual(1f, target.RotW.Evaluate(0f), 0.001f); // identity にフォールバック
                Assert.AreEqual(60f, target.FieldOfView.Evaluate(0f), 0.001f); // 既定 60 度にフォールバック
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void Extract_RotationXyzOnly_UnityAutoFillsW_SoValuesArePreserved()
        {
            // AnimationClip.SetCurve は x/y/z の 1 つでも四元数の回転カーブを設定すると、
            // Unity が他のチャンネル(w 含む)を自動で補完する(実機確認済み: 6-10c テストでの発見)。
            // そのため「w だけ無い」状態は実際には起きず、hasRotation はここでは true になり
            // x/y/z の値がそのまま保持される(空カーブにリセットされない)ことを確認する。
            var clip = new AnimationClip();
            clip.SetCurve(string.Empty, typeof(Transform), "m_LocalRotation.x", AnimationCurve.Constant(0f, 1f, 0.5f));
            clip.SetCurve(string.Empty, typeof(Transform), "m_LocalRotation.y", AnimationCurve.Constant(0f, 1f, 0.5f));
            clip.SetCurve(string.Empty, typeof(Transform), "m_LocalRotation.z", AnimationCurve.Constant(0f, 1f, 0.5f));

            var target = ScriptableObject.CreateInstance<CutsceneCameraClip>();
            try
            {
                CutsceneCameraCurveExtractor.Extract(clip, string.Empty, target);

                Assert.Greater(target.RotX.length, 0);
                Assert.AreEqual(0.5f, target.RotX.Evaluate(0f), 0.001f);
                Assert.AreEqual(0.5f, target.RotY.Evaluate(0f), 0.001f);
                Assert.AreEqual(0.5f, target.RotZ.Evaluate(0f), 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(clip);
            }
        }
    }
}
