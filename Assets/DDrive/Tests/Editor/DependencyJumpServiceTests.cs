using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Dependencies;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] 5-6 — 使用箇所検索一覧のダブルクリックジャンプ(Data/Prefab は選択+Ping)。
    // Scene 分岐(EditorSceneManager.OpenScene)はアクティブシーンを差し替える副作用があり、共有の
    // Test Runner セッションを不安定にし得るため自動テストの対象にしない(要判断: docs/28 参照。手動検証のみ)。
    public class DependencyJumpServiceTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempJumpGameData";

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "TempJumpGameData");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                AssetDatabase.SaveAssets();
            }
        }

        [Test]
        public void RevealAt_DataAsset_SelectsIt()
        {
            var data = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5006 Jump Data", "Category", "ZzTest5006JumpData", gameDataRoot: TestRoot);
            var path = AssetDatabase.GetAssetPath(data);

            Selection.activeObject = null;
            DependencyJumpService.RevealAt(path, string.Empty);

            Assert.AreEqual(data, Selection.activeObject);
        }

        [Test]
        public void RevealAt_PrefabRoot_EmptyObjectPath_SelectsRoot()
        {
            EnsureFolder();

            var root = new GameObject("ZzTest5006PrefabRoot");
            string prefabPath;
            try
            {
                prefabPath = $"{TestRoot}/ZzTest5006Prefab.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            Selection.activeObject = null;
            DependencyJumpService.RevealAt(prefabPath, string.Empty);

            var selected = Selection.activeGameObject;
            Assert.IsNotNull(selected);
            // PrefabUtility.SaveAsPrefabAsset はルートの名前をファイル名に合わせてリネームする(Unity の仕様)。
            Assert.AreEqual(System.IO.Path.GetFileNameWithoutExtension(prefabPath), selected.name);
        }

        [Test]
        public void RevealAt_PrefabChild_SelectsNestedChild()
        {
            EnsureFolder();

            var root = new GameObject("ZzTest5006Root");
            string prefabPath;
            try
            {
                var child = new GameObject("ZzTest5006Child");
                child.transform.SetParent(root.transform);

                prefabPath = $"{TestRoot}/ZzTest5006ChildPrefab.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            Selection.activeObject = null;
            DependencyJumpService.RevealAt(prefabPath, "ZzTest5006Child");

            var selected = Selection.activeGameObject;
            Assert.IsNotNull(selected);
            Assert.AreEqual("ZzTest5006Child", selected.name);
        }

        [Test]
        public void RevealAt_PrefabWithMissingObjectPath_FallsBackToRoot_WithoutThrowing()
        {
            EnsureFolder();

            var root = new GameObject("ZzTest5006FallbackRoot");
            string prefabPath;
            try
            {
                prefabPath = $"{TestRoot}/ZzTest5006Fallback.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            Selection.activeObject = null;
            Assert.DoesNotThrow(() => DependencyJumpService.RevealAt(prefabPath, "DoesNotExist"));

            var selected = Selection.activeGameObject;
            Assert.IsNotNull(selected);
            Assert.AreEqual(System.IO.Path.GetFileNameWithoutExtension(prefabPath), selected.name);
        }
    }
}
