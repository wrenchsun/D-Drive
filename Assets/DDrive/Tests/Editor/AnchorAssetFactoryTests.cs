using DDrive.Editor.Anchor;
using DDrive.Editor.Preview;
using DDrive.Foundation.Data;
using DDrive.Runtime.Anchoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [21_anchor_spec.md] §3.9 — 既存ヒエラルキー / AnchorRig / 埋め込み AnchorDef からの AnchorData 生成。
    public class AnchorAssetFactoryTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempAnchorGameData";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                DDrive.Editor.AssetBrowser.AddressablesSync.RemoveEntriesUnder(TestRoot); // 作成時に登録された Addressables エントリを外す
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); } // Addressables 設定の dirty を後続テストに持ち越さない
            }
        }

        [Test]
        public void ToIdentifier_StripsPrefixAndSymbols()
        {
            Assert.AreEqual("RightHand", AnchorAssetFactory.ToIdentifier("Anchor_RightHand"));
            Assert.AreEqual("MuzzleFx", AnchorAssetFactory.ToIdentifier("muzzle-fx"));
            Assert.AreEqual("Anchor", AnchorAssetFactory.ToIdentifier("日本語"));
        }

        [Test]
        public void CreateFromTransform_UsesParentAsBasisAndLocalPose()
        {
            var bone = new GameObject("Bone_Hand");
            bone.transform.position = new Vector3(1f, 2f, 3f);
            var marker = new GameObject("FxPoint");
            marker.transform.SetParent(bone.transform);
            marker.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            marker.transform.localEulerAngles = new Vector3(0f, 90f, 0f);

            try
            {
                var asset = AnchorAssetFactory.CreateFromTransform(marker.transform, "Test", gameDataRoot: TestRoot);

                Assert.IsNotNull(asset);
                Assert.AreNotEqual(0UL, asset.Id);
                Assert.AreEqual(AnchorSpace.NamedObject, asset.Space);
                Assert.AreEqual("Bone_Hand", asset.Path);
                Assert.Less(Vector3.Distance(new Vector3(0f, 0.5f, 0f), asset.LocalOffset), 1e-4f);
                Assert.Less(Mathf.DeltaAngle(90f, asset.LocalEuler.y), 1e-3f);
                StringAssert.EndsWith("Anchor/Test/ANC_Test_FxPoint.asset", AssetDatabase.GetAssetPath(asset));
            }
            finally
            {
                Object.DestroyImmediate(bone);
            }
        }

        [Test]
        public void CreateFromRig_LinksParentChain_AndCopiesRandom()
        {
            var rig = new GameObject("AnchorRig");
            rig.AddComponent<AnchorRig>();
            var a = new GameObject("Anchor_A");
            var pointA = a.AddComponent<AnchorPoint>();
            pointA.PositionJitterRadius = 0.3f;
            a.transform.SetParent(rig.transform);
            a.transform.localPosition = new Vector3(1f, 0f, 0f);
            var b = new GameObject("Anchor_B");
            b.AddComponent<AnchorPoint>();
            b.transform.SetParent(a.transform);
            b.transform.localPosition = new Vector3(0f, 0f, 2f);

            try
            {
                var created = AnchorAssetFactory.CreateFromRig(rig, gameDataRoot: TestRoot);

                Assert.AreEqual(2, created.Count);
                var assetA = created[0];
                var assetB = created[1];
                Assert.AreEqual("AnchorRig", assetA.Path, "ルートは AnchorRig 名を基準にする");
                Assert.AreEqual(AnchorSpace.NamedObject, assetA.Space);
                Assert.AreEqual(0.3f, assetA.PositionJitterRadius);
                Assert.IsFalse(assetA.Parent.IsValid);
                Assert.AreEqual(assetA.Id, assetB.Parent.Value, "子は親 AnchorPoint のアセットを Parent にする");
                Assert.Less(Vector3.Distance(new Vector3(0f, 0f, 2f), assetB.LocalOffset), 1e-4f);

                // 連鎖を合成すると Rig 基準で (1,0,2) になる。
                var chain = AnchorChainEditor.CollectRootToTarget(assetB);
                Assert.AreEqual(2, chain.Count);
                var composed = AnchorChainEditor.ComposeUpTo(chain, 1);
                Assert.Less(Vector3.Distance(new Vector3(1f, 0f, 2f), composed.LocalOffset), 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(rig);
            }
        }

        [Test]
        public void CreateFromDef_CopiesEmbeddedValues()
        {
            var def = new AnchorDef
            {
                Space = AnchorSpace.BoneName,
                Path = "Head",
                LocalOffset = new Vector3(0f, 0.2f, 0f),
                FollowRotation = true,
                LocalScale = Vector3.zero,
            };

            var asset = AnchorAssetFactory.CreateFromDef(def, "テスト用", "Test", "HeadFx", TestRoot);

            Assert.IsNotNull(asset);
            Assert.AreEqual(AnchorSpace.BoneName, asset.Space);
            Assert.AreEqual("Head", asset.Path);
            Assert.AreEqual(new Vector3(0f, 0.2f, 0f), asset.LocalOffset);
            Assert.AreEqual(Vector3.one, asset.LocalScale, "(0,0,0) は 1 に正規化される");
            Assert.IsTrue(asset.FollowRotation);
        }

        [Test]
        public void EditorRegistry_ResolvesCreatedAsset()
        {
            var asset = AnchorAssetFactory.CreateFromDef(AnchorDef.WorldDefault, "R", "Test", "RegistryProbe", TestRoot);
            var registry = EditorAnchorRegistry.Build();

            Assert.IsTrue(registry.TryResolveSync<AnchorData>(asset.Id, out var resolved));
            Assert.AreSame(asset, resolved);
        }

        [Test]
        public void ToChildLocal_RoundTripsThroughCompose()
        {
            var root = ScriptableObject.CreateInstance<AnchorData>();
            root.LocalOffset = new Vector3(1f, 0f, 0f);
            root.LocalEuler = new Vector3(0f, 90f, 0f);
            root.LocalScale = new Vector3(2f, 2f, 2f);
            var child = ScriptableObject.CreateInstance<AnchorData>();
            child.LocalOffset = new Vector3(0f, 0f, 1f);
            child.LocalEuler = new Vector3(0f, 45f, 0f);

            var chain = new[] { root, child };
            var parentDef = AnchorChainEditor.ComposeUpTo(chain, 0);
            var composed = AnchorChainEditor.ComposeUpTo(chain, 1);

            var back = AnchorChainEditor.ToChildLocalOffset(parentDef, composed.LocalOffset);
            Assert.Less(Vector3.Distance(child.LocalOffset, back), 1e-4f);
            var euler = AnchorChainEditor.ToChildLocalEuler(parentDef, composed.LocalEuler);
            Assert.Less(Mathf.DeltaAngle(45f, euler.y), 1e-3f);
        }
    }
}
