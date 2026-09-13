using DDrive.Editor.Preview;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Tests.Editor
{
    // 2026-09-14 — 確認用プレビューの一括後片付け(EditorPreviewSweeper)。対象は「名前が [D-Drive] で始まる
    // DontSave のルート」だけで、デザイナーが置く本物のオブジェクト([D-Drive] Runtime 等)には触れないこと。
    public class EditorPreviewSweeperTests
    {
        [Test]
        public void IsPreviewRoot_OnlyDDrivePrefixed_DontSave_Roots()
        {
            var preview = new GameObject("[D-Drive] Sweeper Test Preview") { hideFlags = HideFlags.DontSave };
            var real = new GameObject("[D-Drive] Sweeper Test Real");
            var other = new GameObject("Sweeper Test Other") { hideFlags = HideFlags.DontSave };
            var child = new GameObject("[D-Drive] Sweeper Test Child") { hideFlags = HideFlags.DontSave };
            child.transform.SetParent(preview.transform, false);

            try
            {
                Assert.IsTrue(EditorPreviewSweeper.IsPreviewRoot(preview));
                Assert.IsFalse(EditorPreviewSweeper.IsPreviewRoot(real), "DontSave でない本物のオブジェクトは対象外");
                Assert.IsFalse(EditorPreviewSweeper.IsPreviewRoot(other), "[D-Drive] で始まらないものは対象外");
                Assert.IsFalse(EditorPreviewSweeper.IsPreviewRoot(child), "ルートでないものは対象外(親ごと消える)");
            }
            finally
            {
                Object.DestroyImmediate(preview);
                Object.DestroyImmediate(real);
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void DestroyInScene_RemovesPreviewRoots_KeepsRealObjects()
        {
            var preview = new GameObject("[D-Drive] Sweeper Test Preview") { hideFlags = HideFlags.DontSave };
            var real = new GameObject("[D-Drive] Sweeper Test Real");

            try
            {
                var removed = EditorPreviewSweeper.DestroyInScene(SceneManager.GetActiveScene());

                Assert.GreaterOrEqual(removed, 1);
                Assert.IsTrue(preview == null, "プレビューのルートは消える");
                Assert.IsTrue(real != null, "本物のオブジェクトは残る");
            }
            finally
            {
                if (preview != null)
                {
                    Object.DestroyImmediate(preview);
                }

                Object.DestroyImmediate(real);
            }
        }
    }
}
