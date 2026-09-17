using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Dependencies;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Tests.Editor
{
    // [削除の確認画面(2026-09-14、UE の Delete Assets / Replace References 相当)] — ReferenceReplaceService。
    // Data/Prefab の参照は書き換える、Scene の参照は書き換えず RemainingSceneUsages に残す(自動保存しない方針)。
    public class ReferenceReplaceServiceTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempRefReplaceGameData";

        private readonly List<string> _trackedPaths = new();

        [SetUp]
        public void SetUp()
        {
            DependencyGraphPostprocessor.Suppress = true;
            DependencyGraphService.ResetInMemoryCacheForTests();
        }

        [TearDown]
        public void TearDown()
        {
            if (_trackedPaths.Count > 0)
            {
                DependencyGraphService.UpdatePaths(null, _trackedPaths);
                _trackedPaths.Clear();
            }

            DependencyGraphService.ResetInMemoryCacheForTests();
            DependencyGraphPostprocessor.Suppress = false;

            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            }
        }

        private void Track(string path) => _trackedPaths.Add(path);

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "TempRefReplaceGameData");
            }
        }

        private static SeData CreateSe(string name)
            => (SeData)AssetCreationService.Create(typeof(SeData), AssetType.Se, name, "Category", name, gameDataRoot: TestRoot);

        private static void SetAnchorRef(SeData data, AssetType type, ulong id)
        {
            var so = new SerializedObject(data);
            var anchorId = so.FindProperty("AnchorId");
            anchorId.FindPropertyRelative("value").ulongValue = id;
            anchorId.FindPropertyRelative("type").enumValueIndex = (int)type;
            so.ApplyModifiedProperties();
            using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
        }

        private static ulong GetAnchorId(SeData data)
        {
            var so = new SerializedObject(data);
            return so.FindProperty("AnchorId").FindPropertyRelative("value").ulongValue;
        }

        [Test]
        public void Replace_DataReference_IsRewritten_AndUndoRevertsIt()
        {
            var oldTarget = CreateSe("ZzTestRefReplaceOld1");
            Track(AssetDatabase.GetAssetPath(oldTarget));

            var newTarget = CreateSe("ZzTestRefReplaceNew1");
            Track(AssetDatabase.GetAssetPath(newTarget));

            var referencer = CreateSe("ZzTestRefReplaceReferencer1");
            var referencerPath = AssetDatabase.GetAssetPath(referencer);
            Track(referencerPath);
            SetAnchorRef(referencer, AssetType.Se, oldTarget.Id);

            DependencyGraphService.UpdatePaths(new[] { referencerPath }, null);

            Undo.IncrementCurrentGroup();
            var result = ReferenceReplaceService.Replace(new List<ReplacementPlan>
            {
                new ReplacementPlan(AssetType.Se, oldTarget.Id, newTarget.Id),
            });

            Assert.AreEqual(1, result.ReplacedCount);
            CollectionAssert.Contains(result.ChangedDataPaths, referencerPath);
            Assert.AreEqual(newTarget.Id, GetAnchorId(referencer));
            Assert.IsFalse(DependencyGraphService.FindUsages(AssetType.Se, oldTarget.Id).Any(u => u.SourcePath == referencerPath));
            Assert.IsTrue(DependencyGraphService.FindUsages(AssetType.Se, newTarget.Id).Any(u => u.SourcePath == referencerPath));

            Undo.PerformUndo();
            Assert.AreEqual(oldTarget.Id, GetAnchorId(referencer), "Data の差し替えは Undo.RecordObject 経由なので Ctrl+Z で戻せるはず");
        }

        [Test]
        public void Replace_PrefabReference_IsRewritten()
        {
            EnsureFolder();

            var oldTarget = CreateSe("ZzTestRefReplaceOld2");
            Track(AssetDatabase.GetAssetPath(oldTarget));

            var newTarget = CreateSe("ZzTestRefReplaceNew2");
            Track(AssetDatabase.GetAssetPath(newTarget));

            var referencer = new GameObject("ZzTestRefReplacePrefabReferencer");
            string prefabPath;
            try
            {
                var emitter = referencer.AddComponent<SeEmitter>();
                var so = new SerializedObject(emitter);
                var seId = so.FindProperty("seId");
                seId.FindPropertyRelative("value").ulongValue = oldTarget.Id;
                seId.FindPropertyRelative("type").enumValueIndex = (int)AssetType.Se;
                so.ApplyModifiedProperties();

                prefabPath = $"{TestRoot}/ZzTestRefReplacePrefabReferencer.prefab";
                PrefabUtility.SaveAsPrefabAsset(referencer, prefabPath);
                Track(prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(referencer);
            }

            DependencyGraphService.UpdatePaths(new[] { prefabPath }, null);

            var result = ReferenceReplaceService.Replace(new List<ReplacementPlan>
            {
                new ReplacementPlan(AssetType.Se, oldTarget.Id, newTarget.Id),
            });

            Assert.AreEqual(1, result.ReplacedCount);
            CollectionAssert.Contains(result.ChangedPrefabPaths, prefabPath);

            var reloadedRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var reloadedEmitter = reloadedRoot.GetComponent<SeEmitter>();
            var reloadedSo = new SerializedObject(reloadedEmitter);
            var reloadedId = reloadedSo.FindProperty("seId").FindPropertyRelative("value").ulongValue;
            Assert.AreEqual(newTarget.Id, reloadedId);

            Assert.IsTrue(DependencyGraphService.FindUsages(AssetType.Se, newTarget.Id).Any(u => u.SourcePath == prefabPath));
        }

        [Test]
        public void Replace_SceneReference_IsNotRewritten_AndReportedAsRemaining()
        {
            EnsureFolder();

            var oldTarget = CreateSe("ZzTestRefReplaceOld3");
            Track(AssetDatabase.GetAssetPath(oldTarget));

            var newTarget = CreateSe("ZzTestRefReplaceNew3");
            Track(AssetDatabase.GetAssetPath(newTarget));

            var scenesBefore = SceneManager.sceneCount;
            var activeScene = SceneManager.GetActiveScene();

            var go = new GameObject("ZzTestRefReplaceSceneRoot");
            string scenePath;
            try
            {
                var emitter = go.AddComponent<SeEmitter>();
                var so = new SerializedObject(emitter);
                var seId = so.FindProperty("seId");
                seId.FindPropertyRelative("value").ulongValue = oldTarget.Id;
                seId.FindPropertyRelative("type").enumValueIndex = (int)AssetType.Se;
                so.ApplyModifiedProperties();

                scenePath = $"{TestRoot}/ZzTestRefReplaceScene.unity";
                EditorSceneManager.SaveScene(activeScene, scenePath, saveAsCopy: true);
                Track(scenePath);

                DependencyGraphService.UpdatePaths(new[] { scenePath }, null);
                Assert.AreEqual(scenesBefore, SceneManager.sceneCount);

                var result = ReferenceReplaceService.Replace(new List<ReplacementPlan>
                {
                    new ReplacementPlan(AssetType.Se, oldTarget.Id, newTarget.Id),
                });

                Assert.AreEqual(0, result.ReplacedCount);
                Assert.IsTrue(result.RemainingSceneUsages.Any(u => u.SourcePath == scenePath), "Scene 内の参照は書き換えず、手動で直す一覧に残るはず");
                Assert.IsTrue(DependencyGraphService.FindUsages(AssetType.Se, oldTarget.Id).Any(u => u.SourcePath == scenePath), "Scene 側のファイルは変更されていないはず");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
