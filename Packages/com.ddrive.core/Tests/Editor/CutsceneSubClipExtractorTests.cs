using System.Linq;
using DDrive.Editor.Cutscene;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // 1017 TA-5(DD-15) — CutsceneSubClipExtractor(小物ごとのサブクリップ)の純ロジックテスト。
    public class CutsceneSubClipExtractorTests
    {
        private static AnimationClip BuildClip(params string[] paths)
        {
            var clip = new AnimationClip { frameRate = 24f };
            foreach (var path in paths)
            {
                clip.SetCurve(path, typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            }

            return clip;
        }

        [Test]
        public void ExtractForNode_CopiesNodeAndDescendants_WithPathsRebased()
        {
            var source = BuildClip("PRP_Mic", "PRP_Mic/Lid", "CAM_Main");
            AnimationClip sub = null;
            try
            {
                sub = CutsceneSubClipExtractor.ExtractForNode(source, "PRP_Mic");
                Assert.IsNotNull(sub);

                var paths = AnimationUtility.GetCurveBindings(sub).Select(b => b.path).Distinct().OrderBy(p => p).ToArray(); // Unity が x だけの SetCurve でも y/z を補う
                CollectionAssert.AreEqual(new[] { string.Empty, "Lid" }, paths);
                Assert.AreEqual(24f, sub.frameRate);
            }
            finally
            {
                if (sub != null)
                {
                    Object.DestroyImmediate(sub);
                }

                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ExtractForNode_DoesNotMatchByPrefixOnly()
        {
            var source = BuildClip("PRP_Micro", "PRP_Mic");
            AnimationClip sub = null;
            try
            {
                sub = CutsceneSubClipExtractor.ExtractForNode(source, "PRP_Mic");
                var paths = AnimationUtility.GetCurveBindings(sub).Select(b => b.path).Distinct().ToArray();
                CollectionAssert.AreEqual(new[] { string.Empty }, paths, "PRP_Micro は PRP_Mic のサブクリップに入らない");
            }
            finally
            {
                if (sub != null)
                {
                    Object.DestroyImmediate(sub);
                }

                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ExtractForNode_NoMatch_ReturnsNull()
        {
            var source = BuildClip("CAM_Main");
            try
            {
                Assert.IsNull(CutsceneSubClipExtractor.ExtractForNode(source, "PRP_Mic"));
                Assert.IsNull(CutsceneSubClipExtractor.ExtractForNode(source, string.Empty));
                Assert.IsNull(CutsceneSubClipExtractor.ExtractForNode(null, "PRP_Mic"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }
    }
}
