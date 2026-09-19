using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Dependencies;
using DDrive.Editor.Preload;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] 5-7 — ScenePreloadGenerator(.asset の生成/更新)のテスト。
    // シーンのスナップショット手法は DependencyGraphServiceTests と同じ(アクティブシーンに一時オブジェクトを
    // 足して saveAsCopy で書き出すだけで、Test Runner のシーン数は変えない)。
    // 出力先は実プロジェクトの GameData/Preload ではなく一時フォルダを明示的に指定する
    // (実 GameData に書き込まない。CLAUDE.md §0-8 相当の配慮)。識別子は "ZzTest5007" のみ。
    public class ScenePreloadGeneratorTests
    {
        private const string TestRoot = "Packages/com.ddrive.core/Tests/Editor/TempPreloadGenGameData";
        private const string TestOutputRoot = TestRoot + "/Preload";

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

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder("Packages/com.ddrive.core/Tests/Editor", System.IO.Path.GetFileName(path));
            }
        }

        [Test]
        public void GenerateForScene_ProducesListWithResolvedDisplayName_AndUpdatesInPlaceOnRerun()
        {
            EnsureFolder(TestRoot);

            var target = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5007 Target", "Category", "ZzTest5007Target", gameDataRoot: TestRoot);
            var targetPath = AssetDatabase.GetAssetPath(target);
            Track(targetPath);

            var scenesBefore = SceneManager.sceneCount;
            var activeScene = SceneManager.GetActiveScene();

            var go = new GameObject("ZzTest5007SceneRoot");
            string scenePath = null;
            try
            {
                var emitter = go.AddComponent<SeEmitter>();
                var so = new SerializedObject(emitter);
                var seId = so.FindProperty("seId");
                seId.FindPropertyRelative("value").ulongValue = target.Id;
                seId.FindPropertyRelative("type").enumValueIndex = (int)AssetType.Se;
                so.ApplyModifiedProperties();

                scenePath = $"{TestRoot}/ZzTest5007Scene.unity";
                EditorSceneManager.SaveScene(activeScene, scenePath, saveAsCopy: true);
                Track(scenePath);

                Assert.AreEqual(scenesBefore, SceneManager.sceneCount);

                DependencyGraphService.UpdatePaths(new[] { targetPath, scenePath }, null);

                var list = ScenePreloadGenerator.GenerateForScene(scenePath, TestOutputRoot);

                Assert.IsNotNull(list);
                Assert.AreEqual("ZzTest5007Scene", list.SceneName);
                Assert.AreEqual(1, list.Entries.Count);
                Assert.AreEqual(target.Id, list.Entries[0].Id);
                Assert.AreEqual(AssetType.Se, list.Entries[0].Type);
                Assert.AreEqual("ZzTest5007 Target", list.Entries[0].DisplayName);

                var expectedAssetPath = $"{TestOutputRoot}/ZzTest5007Scene_PreloadList.asset";
                Assert.AreEqual(expectedAssetPath, AssetDatabase.GetAssetPath(list));

                // 変更が無いまま再集計しても、同じ .asset が上書きされるだけで増えないはず。
                var rerun = ScenePreloadGenerator.GenerateForScene(scenePath, TestOutputRoot);
                Assert.AreSame(list, rerun, "同じシーンの再集計は同一アセットを上書きするはず(新規作成しない)");
                Assert.AreEqual(1, rerun.Entries.Count);

                // 参照先(Data)を削除して再集計すると、シーン自身が持つエッジは残るため
                // エントリ自体は残るが、未解決(見つからない)扱いになるはず(ScenePreloadAggregator の仕様。
                // DependencyAssetResolver.Find が失敗するだけで、シーンの SerializedProperty はそのまま)。
                DependencyGraphService.UpdatePaths(null, new[] { targetPath });
                AssetDatabase.DeleteAsset(targetPath);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
                DependencyGraphService.UpdatePaths(new[] { scenePath }, null);

                var updated = ScenePreloadGenerator.GenerateForScene(scenePath, TestOutputRoot);

                Assert.AreSame(list, updated, "参照先が消えてもアセット自体は同一のままのはず");
                Assert.AreEqual(1, updated.Entries.Count);
                StringAssert.Contains("見つかりません", updated.Entries[0].DisplayName);

                var allAtPath = AssetDatabase.LoadAllAssetsAtPath(expectedAssetPath);
                Assert.AreEqual(1, allAtPath.Count(a => a is DDrive.Runtime.Loading.ScenePreloadList), "重複生成されていないはず");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
