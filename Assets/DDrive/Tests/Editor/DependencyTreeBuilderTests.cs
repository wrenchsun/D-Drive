using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Dependencies;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] 5-6 — 依存関係ツリー(FindReferencesIn の再帰展開・循環検出)。
    // SeData.AnchorId は AssetId<AnchorMarker> だが、SerializedProperty 経由なら任意の
    // (AssetType, ulong) を仕込める(DependencyGraphServiceTests と同じ手法。DependencyGraphCollector は
    // 総称引数を見ず value/type の中身しか見ないため、テストの都合で Se 同士を指しても問題ない)。
    public class DependencyTreeBuilderTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempTreeGameData";

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
        public void Build_ChainOfThree_ProducesNestedChildren()
        {
            var c = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5006TreeC", "Category", "ZzTest5006TreeC", gameDataRoot: TestRoot);
            var b = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5006TreeB", "Category", "ZzTest5006TreeB", gameDataRoot: TestRoot);
            var a = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5006TreeA", "Category", "ZzTest5006TreeA", gameDataRoot: TestRoot);

            SetAnchorRef(b, AssetType.Se, c.Id);
            SetAnchorRef(a, AssetType.Se, b.Id);

            var pathA = AssetDatabase.GetAssetPath(a);
            var pathB = AssetDatabase.GetAssetPath(b);
            var pathC = AssetDatabase.GetAssetPath(c);
            Track(pathA);
            Track(pathB);
            Track(pathC);

            DependencyGraphService.UpdatePaths(new[] { pathA, pathB, pathC }, null);

            var tree = DependencyTreeBuilder.Build(pathA, "A");

            Assert.AreEqual(1, tree.Children.Count);
            var childB = tree.Children[0];
            Assert.AreEqual(b.Id, childB.Id);
            Assert.IsFalse(childB.IsCycle);

            Assert.AreEqual(1, childB.Children.Count);
            var childC = childB.Children[0];
            Assert.AreEqual(c.Id, childC.Id);
            Assert.AreEqual(0, childC.Children.Count);
        }

        [Test]
        public void Build_Cycle_StopsAndMarksCycleNode()
        {
            var a = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5006CycleA", "Category", "ZzTest5006CycleA", gameDataRoot: TestRoot);
            var b = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5006CycleB", "Category", "ZzTest5006CycleB", gameDataRoot: TestRoot);

            SetAnchorRef(a, AssetType.Se, b.Id);
            SetAnchorRef(b, AssetType.Se, a.Id);

            var pathA = AssetDatabase.GetAssetPath(a);
            var pathB = AssetDatabase.GetAssetPath(b);
            Track(pathA);
            Track(pathB);

            DependencyGraphService.UpdatePaths(new[] { pathA, pathB }, null);

            var tree = DependencyTreeBuilder.Build(pathA, "A");

            Assert.AreEqual(1, tree.Children.Count);
            var childB = tree.Children[0];
            Assert.AreEqual(b.Id, childB.Id);
            Assert.IsFalse(childB.IsCycle);
            Assert.AreEqual(1, childB.Children.Count);

            var backToA = childB.Children[0];
            Assert.AreEqual(a.Id, backToA.Id);
            Assert.IsTrue(backToA.IsCycle, "A に戻る参照は循環として打ち切られるはず");
            Assert.AreEqual(0, backToA.Children.Count, "循環と判定した先は展開しないはず");
        }

        [Test]
        public void Build_UnresolvedTarget_IsMarkedUnresolved()
        {
            var a = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest5006UnresolvedA", "Category", "ZzTest5006UnresolvedA", gameDataRoot: TestRoot);
            SetAnchorRef(a, AssetType.Se, 0xDEADBEEFUL);

            var pathA = AssetDatabase.GetAssetPath(a);
            Track(pathA);
            DependencyGraphService.UpdatePaths(new[] { pathA }, null);

            var tree = DependencyTreeBuilder.Build(pathA, "A");

            Assert.AreEqual(1, tree.Children.Count);
            Assert.IsTrue(tree.Children[0].IsUnresolved);
        }
    }
}
