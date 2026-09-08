using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    public class AssetReorganizerTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempReorgRoot";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.DeleteAsset(TestRoot);
            }
        }

        [Test]
        public void Reorganize_CategoryChanged_MovesRenamesAndUpdatesCatalog()
        {
            var asset = AssetCreationService.Create(
                typeof(SeData), AssetType.Se, "表示名", category: "", identifier: "Slash",
                gameDataRoot: TestRoot);
            StringAssert.EndsWith("Audio/SE/SE_Slash.asset", AssetDatabase.GetAssetPath(asset));

            // 人がカテゴリだけ変更 → ツールが配置と名前を追従させる
            asset.Category = "Player";
            EditorUtility.SetDirty(asset);

            var result = AssetReorganizer.Reorganize(TestRoot);

            Assert.AreEqual(1, result.Scanned);
            Assert.AreEqual(1, result.Moved);
            Assert.AreEqual(1, result.Renamed);
            Assert.AreEqual(0, result.Skipped);

            var newPath = AssetDatabase.GetAssetPath(asset);
            StringAssert.EndsWith("Audio/SE/Player/SE_Player_Slash.asset", newPath);

            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>($"{TestRoot}/Catalogs/AudioCatalog.asset");
            var entry = catalog.Entries.First(e => e.Id == asset.Id);
            Assert.AreEqual("SE_Player_Slash", entry.Address, "リネームに合わせて Address が更新される");
        }

        [Test]
        public void Reorganize_AlreadyInPlace_DoesNothing()
        {
            var asset = AssetCreationService.Create(
                typeof(SeData), AssetType.Se, "表示名", category: "Player", identifier: "Slash",
                gameDataRoot: TestRoot);
            var pathBefore = AssetDatabase.GetAssetPath(asset);

            var result = AssetReorganizer.Reorganize(TestRoot);

            Assert.AreEqual(1, result.Scanned);
            Assert.AreEqual(0, result.Moved);
            Assert.AreEqual(0, result.Renamed);
            Assert.AreEqual(pathBefore, AssetDatabase.GetAssetPath(asset));
        }

        [Test]
        public void Reorganize_IdIsStableAcrossMove()
        {
            var asset = AssetCreationService.Create(
                typeof(SeData), AssetType.Se, "表示名", category: "", identifier: "Stable",
                gameDataRoot: TestRoot);
            var idBefore = asset.Id;

            asset.Category = "Enemy/Attack";
            EditorUtility.SetDirty(asset);
            AssetReorganizer.Reorganize(TestRoot);

            StringAssert.EndsWith("Audio/SE/Enemy/Attack/SE_Attack_Stable.asset", AssetDatabase.GetAssetPath(asset));
            Assert.AreEqual(idBefore, asset.Id, "移動・リネームしても ID(GUID 由来)は変わらない");
        }

        [Test]
        public void EnsureSeEmitterPrefab_CreatesIdempotently()
        {
            var path = $"{TestRoot}/Prefabs/Audio/SeEmitter.prefab";

            var first = DefaultPrefabs.EnsureSeEmitterPrefab(path);
            Assert.IsNotNull(first);
            Assert.IsNotNull(first.GetComponent<SeEmitter>(), "SeEmitter コンポーネントが付いている");

            var second = DefaultPrefabs.EnsureSeEmitterPrefab(path);
            Assert.AreSame(first, second, "2回目は既存プレハブを返す(上書きしない)");
        }

        [Test]
        public void CreateAnchorRigPrefab_CreatesRigWithAnchorPoint_EachCallIsUnique()
        {
            var folder = $"{TestRoot}/Prefabs/Anchors";

            var first = DefaultPrefabs.CreateAnchorRigPrefab(folder);
            Assert.IsNotNull(first.GetComponent<DDrive.Runtime.Anchoring.AnchorRig>(), "ルートに AnchorRig が付いている");
            Assert.GreaterOrEqual(first.GetComponentsInChildren<DDrive.Runtime.Anchoring.AnchorPoint>(true).Length, 1,
                "子に AnchorPoint の雛形が1つ以上ある");

            var second = DefaultPrefabs.CreateAnchorRigPrefab(folder);
            Assert.AreNotEqual(
                UnityEditor.AssetDatabase.GetAssetPath(first),
                UnityEditor.AssetDatabase.GetAssetPath(second),
                "AnchorRig は用途ごとに複数作る前提のため、クリックごとに別プレハブが生成される");
        }
    }
}
