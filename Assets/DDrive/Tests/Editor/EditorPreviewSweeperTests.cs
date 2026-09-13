using DDrive.Editor.Preview;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Tests.Editor
{
    // 2026-09-14 — 確認用プレビューの一括後片付け(EditorPreviewSweeper)。対象は「名前が [D-Drive] で始まる
    // DontSave のルート」だけで、デザイナーが置く本物のオブジェクト([D-Drive] Runtime 等)には触れないこと。
    //
    // (レビュー対応 2026-09-14) 以前は DestroyInScene(GetActiveScene()) を呼んでいて、テストを走らせるとデザイナーが
    // 開いているシーンのプレビュー(他のエディタのものも)まで消していた。シーンを消す系のテストは専用のプレビューシーン
    // (EditorSceneManager.NewPreviewScene。開いているシーンとは別で、保存もされない)の中で行う。
    public class EditorPreviewSweeperTests
    {
        private Scene _scene;

        [SetUp]
        public void SetUp() => _scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown()
        {
            if (_scene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(_scene);
            }
        }

        private GameObject CreateInTestScene(string name, bool dontSave)
        {
            var go = new GameObject(name);
            if (dontSave)
            {
                go.hideFlags = HideFlags.DontSave;
            }

            SceneManager.MoveGameObjectToScene(go, _scene);
            return go;
        }

        [Test]
        public void IsPreviewRoot_OnlyDDrivePrefixed_DontSave_Roots()
        {
            var preview = CreateInTestScene("[D-Drive] Sweeper Test Preview", dontSave: true);
            var real = CreateInTestScene("[D-Drive] Sweeper Test Real", dontSave: false);
            var other = CreateInTestScene("Sweeper Test Other", dontSave: true);
            var child = new GameObject("[D-Drive] Sweeper Test Child") { hideFlags = HideFlags.DontSave };
            child.transform.SetParent(preview.transform, false);

            Assert.IsTrue(EditorPreviewSweeper.IsPreviewRoot(preview));
            Assert.IsFalse(EditorPreviewSweeper.IsPreviewRoot(real), "DontSave でない本物のオブジェクトは対象外");
            Assert.IsFalse(EditorPreviewSweeper.IsPreviewRoot(other), "[D-Drive] で始まらないものは対象外");
            Assert.IsFalse(EditorPreviewSweeper.IsPreviewRoot(child), "ルートでないものは対象外(親ごと消える)");
        }

        [Test]
        public void DestroyInScene_RemovesPreviewRoots_KeepsRealObjects()
        {
            var preview = CreateInTestScene("[D-Drive] Sweeper Test Preview", dontSave: true);
            var real = CreateInTestScene("[D-Drive] Sweeper Test Real", dontSave: false);

            var removed = EditorPreviewSweeper.DestroyInScene(_scene);

            Assert.AreEqual(1, removed);
            Assert.IsTrue(preview == null, "プレビューのルートは消える");
            Assert.IsTrue(real != null, "本物のオブジェクトは残る");
        }

        // (レビュー対応 2026-09-14) DestroyOrphans は「どのシーンにも属さない」ものだけを消す。シーンに入っている
        // プレビューは(シーンを閉じるときに DestroyInScene で消えるので)ここでは触らない。
        [Test]
        public void DestroyOrphans_KeepsPreviewRootsThatBelongToAScene()
        {
            var preview = CreateInTestScene("[D-Drive] Sweeper Test Preview", dontSave: true);

            EditorPreviewSweeper.DestroyOrphans();

            Assert.IsTrue(preview != null, "シーンに属しているプレビューは孤児ではない");
        }

        // (レビュー対応 2026-09-14) EditorPreviewRoots.DestroyAll(name) は名前だけで照合していた。同じ名前でも
        // DontSave でない本物のオブジェクトは消さないこと。名前はテスト固有にして、開いているシーンの他のものに触れない。
        [Test]
        public void EditorPreviewRoots_DestroyAll_KeepsRealObjectWithSameName()
        {
            const string name = "[D-Drive] Sweeper Test DestroyAll 7f3c";
            var preview = EditorPreviewRoots.CreateRoot(name);
            var real = new GameObject(name);

            try
            {
                var removed = EditorPreviewRoots.DestroyAll(name);

                Assert.AreEqual(1, removed);
                Assert.IsTrue(preview == null, "プレビューのルートは消える");
                Assert.IsTrue(real != null, "同じ名前でも本物のオブジェクトは残る");
            }
            finally
            {
                if (preview != null)
                {
                    Object.DestroyImmediate(preview);
                }

                if (real != null)
                {
                    Object.DestroyImmediate(real);
                }
            }
        }

        // (レビュー対応 2026-09-14) HideFlags は子に引き継がれないため、プレビュー配下の子も DontSave にすること。
        [Test]
        public void EditorPreviewRoots_ChildrenAreDontSave()
        {
            var root = EditorPreviewRoots.CreateOverlayCanvas("[D-Drive] Sweeper Test Canvas 7f3c");
            try
            {
                var viaHelper = EditorPreviewRoots.CreateChild(root.transform, "ViaHelper", typeof(RectTransform));
                var raw = new GameObject("Raw", typeof(RectTransform));
                raw.transform.SetParent(viaHelper.transform, false);

                Assert.AreNotEqual((HideFlags)0, viaHelper.hideFlags & HideFlags.DontSave, "CreateChild は DontSave で作る");
                Assert.AreEqual((HideFlags)0, raw.hideFlags & HideFlags.DontSave, "前提: 素の new GameObject は親の HideFlags を引き継がない");

                EditorPreviewRoots.MarkDontSaveRecursive(root);

                Assert.AreNotEqual((HideFlags)0, raw.hideFlags & HideFlags.DontSave, "MarkDontSaveRecursive で孫まで DontSave になる");
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, root.GetComponent<Canvas>().renderMode);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
