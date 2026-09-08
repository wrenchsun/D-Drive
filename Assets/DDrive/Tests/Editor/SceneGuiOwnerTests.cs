using DDrive.Editor.Preview;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // SceneView 描画権の調停(複数エディタ同時表示の重なり対策)。
    public class SceneGuiOwnerTests
    {
        private EditorWindow _a;
        private EditorWindow _b;

        [SetUp]
        public void SetUp()
        {
            _a = ScriptableObject.CreateInstance<EditorWindow>();
            _b = ScriptableObject.CreateInstance<EditorWindow>();
            SceneGuiOwner.Release(SceneGuiOwner.Current);
        }

        [TearDown]
        public void TearDown()
        {
            SceneGuiOwner.Release(_a);
            SceneGuiOwner.Release(_b);
            Object.DestroyImmediate(_a);
            Object.DestroyImmediate(_b);
        }

        [Test]
        public void FirstAsker_BecomesOwner_WhenNobodyHoldsIt()
        {
            Assert.IsTrue(SceneGuiOwner.IsOwner(_a));
            Assert.IsFalse(SceneGuiOwner.IsOwner(_b), "既に A が持っているので B は描画権を持たない");
        }

        [Test]
        public void Claim_SwitchesOwner_AndReleaseFreesIt()
        {
            SceneGuiOwner.Claim(_a);
            SceneGuiOwner.Claim(_b);
            Assert.IsFalse(SceneGuiOwner.IsOwner(_a));
            Assert.IsTrue(SceneGuiOwner.IsOwner(_b));

            SceneGuiOwner.Release(_b);
            Assert.IsNull(SceneGuiOwner.Current);
            Assert.IsTrue(SceneGuiOwner.IsOwner(_a), "空いたら次に問い合わせたウィンドウが持つ");
        }

        [Test]
        public void Release_ByNonOwner_DoesNothing()
        {
            SceneGuiOwner.Claim(_a);
            SceneGuiOwner.Release(_b);
            Assert.AreSame(_a, SceneGuiOwner.Current);
        }
    }
}
