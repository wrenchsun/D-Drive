using DDrive.Editor.CanvasTool;
using DDrive.Editor.Inspector;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Tests.Editor
{
    // [07_canvas_prefab.md] A-4 — 「Selectable を自動収集」(CanvasNavigationCollector)と、
    // CanvasData → CanvasEditorWindow の DataEditorRegistry 解決を検証する。
    public class CanvasEditorTests
    {
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("CollectorTestPrefab", typeof(RectTransform));

            var group = new GameObject("Group", typeof(RectTransform));
            group.transform.SetParent(_prefab.transform);

            var a = new GameObject("A", typeof(RectTransform));
            a.transform.SetParent(group.transform);
            a.AddComponent<Button>();

            var b = new GameObject("B", typeof(RectTransform));
            b.transform.SetParent(_prefab.transform);
            b.AddComponent<Button>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_prefab);

        [Test]
        public void Collect_ReturnsOnePerSelectable_WithRelativePaths()
        {
            var nodes = CanvasNavigationCollector.Collect(_prefab);

            Assert.AreEqual(2, nodes.Length);
            CollectionAssert.AreEquivalent(new[] { "Group/A", "B" }, System.Array.ConvertAll(nodes, n => n.Element));
        }

        [Test]
        public void Collect_NullPrefab_ReturnsEmpty()
        {
            Assert.AreEqual(0, CanvasNavigationCollector.Collect(null).Length);
        }

        [Test]
        public void CollectMerged_KeepsExistingEntries_AndAddsMissingOnes()
        {
            var existing = new[] { new NavNode { Element = "B", Up = "Group/A" } };

            var merged = CanvasNavigationCollector.CollectMerged(_prefab, existing);

            Assert.AreEqual(2, merged.Length);
            var bNode = System.Array.Find(merged, n => n.Element == "B");
            Assert.AreEqual("Group/A", bNode.Up, "既存の行は上書きされず保持されるはず");
        }

        [Test]
        public void CanvasData_ResolvesTo_CanvasEditorWindow()
        {
            var found = false;
            foreach (var entry in DataEditorRegistry.GetEntries(typeof(CanvasData)))
            {
                if (entry.WindowType == typeof(CanvasEditorWindow))
                {
                    found = true;
                    break;
                }
            }

            Assert.IsTrue(found, "CanvasData → CanvasEditorWindow が DataEditorRegistry に登録されていません");
        }
    }
}
