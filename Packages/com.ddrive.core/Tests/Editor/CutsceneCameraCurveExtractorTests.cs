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

        // ── ApplyCutSteps(1017 TA-4) ──

        private const float Frame = 1f / 30f;

        // 毎フレームのキー(線形補間)。cutFrame 以降は x が jump だけ増える。
        private static CutsceneCameraClip BuildPerFrameCamera(int frames, int cutFrame, float jump, float yawDegAfterCut = 0f)
        {
            var target = ScriptableObject.CreateInstance<CutsceneCameraClip>();
            target.PosX = new AnimationCurve();
            target.PosY = new AnimationCurve();
            target.PosZ = new AnimationCurve();
            target.RotX = new AnimationCurve();
            target.RotY = new AnimationCurve();
            target.RotZ = new AnimationCurve();
            target.RotW = new AnimationCurve();
            target.FieldOfView = new AnimationCurve();
            for (var f = 0; f < frames; f++)
            {
                var t = f * Frame;
                var after = f >= cutFrame;
                var q = Quaternion.Euler(0f, after ? yawDegAfterCut : 0f, 0f);
                target.PosX.AddKey(new Keyframe(t, 0.01f * f + (after ? jump : 0f)));
                target.PosY.AddKey(new Keyframe(t, 0f));
                target.PosZ.AddKey(new Keyframe(t, 0f));
                target.RotX.AddKey(new Keyframe(t, q.x));
                target.RotY.AddKey(new Keyframe(t, q.y));
                target.RotZ.AddKey(new Keyframe(t, q.z));
                target.RotW.AddKey(new Keyframe(t, q.w));
                target.FieldOfView.AddKey(new Keyframe(t, after ? 40f : 60f));
            }

            return target;
        }

        [Test]
        public void ApplyCutSteps_PositionJump_MakesStepBetweenKeys()
        {
            var target = BuildPerFrameCamera(20, 10, 5f);
            try
            {
                var cuts = CutsceneCameraCurveExtractor.ApplyCutSteps(target, 1f, 60f);
                Assert.AreEqual(1, cuts);

                var mid = 9.5f * Frame;
                Assert.AreEqual(target.PosX.Evaluate(9 * Frame), target.PosX.Evaluate(mid), 1e-4f, "カットの間は前のカメラの位置に留まる");
                Assert.AreEqual(target.FieldOfView.Evaluate(9 * Frame), target.FieldOfView.Evaluate(mid), 1e-4f, "画角のカーブも同じ位置でステップ");
                Assert.AreEqual(40f, target.FieldOfView.Evaluate(10 * Frame), 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ApplyCutSteps_RotationJump_MakesStep()
        {
            var target = BuildPerFrameCamera(20, 10, 0f, 90f);
            try
            {
                Assert.AreEqual(1, CutsceneCameraCurveExtractor.ApplyCutSteps(target, 1f, 60f));
                Assert.AreEqual(target.RotY.Evaluate(9 * Frame), target.RotY.Evaluate(9.5f * Frame), 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ApplyCutSteps_ThresholdZero_DoesNothing()
        {
            var target = BuildPerFrameCamera(20, 10, 5f, 90f);
            try
            {
                var before = target.PosX.Evaluate(9.5f * Frame);
                Assert.AreEqual(0, CutsceneCameraCurveExtractor.ApplyCutSteps(target, 0f, 0f));
                Assert.AreEqual(before, target.PosX.Evaluate(9.5f * Frame), 1e-6f);
                Assert.AreNotEqual(target.PosX.Evaluate(9 * Frame), target.PosX.Evaluate(9.5f * Frame), "補間のまま(中間の値が出る)");
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ApplyCutSteps_SlowCameraMove_IsUntouched()
        {
            var target = BuildPerFrameCamera(20, 100, 0f);
            try
            {
                Assert.AreEqual(0, CutsceneCameraCurveExtractor.ApplyCutSteps(target, 1f, 60f));
                Assert.AreNotEqual(target.PosX.Evaluate(9 * Frame), target.PosX.Evaluate(9.5f * Frame));
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
