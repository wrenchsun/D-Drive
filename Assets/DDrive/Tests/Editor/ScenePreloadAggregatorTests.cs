using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Dependencies;
using DDrive.Editor.Preload;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] 5-7 — ScenePreloadAggregator(依存グラフの再帰集計・循環打ち切り・未解決の扱い)のテスト。
    // DependencyTreeBuilderTests と同じ手法(SeData の AnchorId フィールドを SerializedProperty 経由で
    // 任意の (AssetType, ulong) に仕立てて疑似的な参照チェーンを作る)を流用する。識別子は "ZzTest5007" のみ。
    public class ScenePreloadAggregatorTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempPreloadGameData";

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
                AssetDatabase.SaveAssets();
            }
        }

        private void Track(string path) => _trackedPaths.Add(path);

        private static void SetAnchorRef(DDrive.Foundation.Data.AssetDataBase data, AssetType type, ulong id)
        {
            var so = new SerializedObject(data);
            var anchorId = so.FindProperty("AnchorId");
            anchorId.FindPropertyRelative("value").ulongValue = id;
            anchorId.FindPropertyRelative("type").enumValueIndex = (int)type;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void Aggregate_ChainOfThree_ReturnsReferencedIdsOnly_NotRoot()
        {
            var c = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5007ChainC", "Category", "ZzTest5007ChainC", gameDataRoot: TestRoot);
            var b = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5007ChainB", "Category", "ZzTest5007ChainB", gameDataRoot: TestRoot);
            var a = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5007ChainA", "Category", "ZzTest5007ChainA", gameDataRoot: TestRoot);

            SetAnchorRef(b, AssetType.Se, c.Id);
            SetAnchorRef(a, AssetType.Se, b.Id);

            var pathA = AssetDatabase.GetAssetPath(a);
            var pathB = AssetDatabase.GetAssetPath(b);
            var pathC = AssetDatabase.GetAssetPath(c);
            Track(pathA);
            Track(pathB);
            Track(pathC);

            DependencyGraphService.UpdatePaths(new[] { pathA, pathB, pathC }, null);

            var result = ScenePreloadAggregator.Aggregate(pathA);

            Assert.AreEqual(2, result.Count, "ルート自身(A)は含まず、辿り着いた B・C の 2 件のはず");
            Assert.IsTrue(result.Any(e => e.Id == b.Id && e.Type == AssetType.Se));
            Assert.IsTrue(result.Any(e => e.Id == c.Id && e.Type == AssetType.Se));
            CollectionAssert.IsOrdered(result.Select(e => e.Id).ToList(), "ID 昇順で安定した並びのはず");
        }

        [Test]
        public void Aggregate_Cycle_StopsRecursion_ButIncludesBothIds()
        {
            var a = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5007CycleA", "Category", "ZzTest5007CycleA", gameDataRoot: TestRoot);
            var b = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5007CycleB", "Category", "ZzTest5007CycleB", gameDataRoot: TestRoot);

            SetAnchorRef(a, AssetType.Se, b.Id);
            SetAnchorRef(b, AssetType.Se, a.Id);

            var pathA = AssetDatabase.GetAssetPath(a);
            var pathB = AssetDatabase.GetAssetPath(b);
            Track(pathA);
            Track(pathB);

            DependencyGraphService.UpdatePaths(new[] { pathA, pathB }, null);

            var result = ScenePreloadAggregator.Aggregate(pathA);

            // B は直接参照、A は B からの折り返し参照として拾われる。どちらも 1 回ずつだけ(循環で無限展開しない)。
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.Any(e => e.Id == a.Id));
            Assert.IsTrue(result.Any(e => e.Id == b.Id));
        }

        [Test]
        public void Aggregate_UnresolvedTarget_IsIncludedButNotExpanded()
        {
            var a = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5007UnresolvedA", "Category", "ZzTest5007UnresolvedA", gameDataRoot: TestRoot);
            SetAnchorRef(a, AssetType.Se, 0xDEADBEEFUL);

            var pathA = AssetDatabase.GetAssetPath(a);
            Track(pathA);
            DependencyGraphService.UpdatePaths(new[] { pathA }, null);

            var result = ScenePreloadAggregator.Aggregate(pathA);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(0xDEADBEEFUL, result[0].Id);
            StringAssert.Contains("見つかりません", result[0].DisplayName);
        }

        [Test]
        public void Aggregate_NoReferences_ReturnsEmpty()
        {
            var a = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5007Empty", "Category", "ZzTest5007Empty", gameDataRoot: TestRoot);
            var pathA = AssetDatabase.GetAssetPath(a);
            Track(pathA);
            DependencyGraphService.UpdatePaths(new[] { pathA }, null);

            var result = ScenePreloadAggregator.Aggregate(pathA);

            Assert.AreEqual(0, result.Count);
        }
    }
}
