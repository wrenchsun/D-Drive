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

        // [07_canvas_prefab.md] 2026-09-12 追記 — ノードグラフのドラッグ位置/Reroute point は
        // CanvasData.NavigationNodeLayout/NavigationEdgeWaypoints へ永続化する(ユーザー要望による例外)。
        // ドラッグ操作自体はポインタイベントに依存するため、LoadLayout/Export の往復だけを検証する。
        [Test]
        public void NavigationGraphView_LoadLayout_ThenExport_RoundTripsNodePositionsAndWaypoints()
        {
            var view = new NavigationGraphView();

            var nodeLayout = new[] { new NavNodeLayout { Element = "Group/A", Position = new Vector2(10f, 20f) } };
            var waypoints = new[] { new NavEdgeWaypoint { Element = "Group/A", Direction = "Right", Points = new[] { new Vector2(50f, 60f) } } };

            view.LoadLayout(nodeLayout, waypoints);

            var exportedNodes = view.ExportNodeLayout();
            var exportedWaypoints = view.ExportEdgeWaypoints();

            Assert.AreEqual(1, exportedNodes.Length);
            Assert.AreEqual("Group/A", exportedNodes[0].Element);
            Assert.AreEqual(new Vector2(10f, 20f), exportedNodes[0].Position);

            Assert.AreEqual(1, exportedWaypoints.Length);
            Assert.AreEqual("Group/A", exportedWaypoints[0].Element);
            Assert.AreEqual("Right", exportedWaypoints[0].Direction);
            CollectionAssert.AreEqual(new[] { new Vector2(50f, 60f) }, exportedWaypoints[0].Points);
        }

        [Test]
        public void NavigationGraphView_LoadLayout_IgnoresUnknownDirectionString()
        {
            var view = new NavigationGraphView();

            view.LoadLayout(null, new[] { new NavEdgeWaypoint { Element = "A", Direction = "Diagonal", Points = new[] { Vector2.zero } } });

            Assert.AreEqual(0, view.ExportEdgeWaypoints().Length, "パースできない Direction 文字列は無視されるはず");
        }
    }
}
