using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Preview;
using DDrive.Foundation.Data;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Tests.Editor
{
    // [07_canvas_prefab.md] A-4(4-3) — NavigationGraph(モデル)/ MoveFocusFrom(データ駆動フォールバック) /
    // UiButton.SimulateClick(パッド操作シミュレーションの決定ボタンが叩く実体)を検証する。
    // registry は CanvasEditorWindow と同じ EditorAnchorRegistry.Build()(Tests/Runtime の internal
    // FakeAssetLoader は別アセンブリのため使えない)。OpenData は Data を直接渡すため実質未使用。
    public class CanvasGraphTests
    {
        private GameObject _prefab;
        private PoolService _pool;
        private AssetRegistry _registry;
        private UiManager _manager;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _registry = EditorAnchorRegistry.Build();
            _manager = new UiManager(_pool, _registry, new PauseService());
        }

        [TearDown]
        public void TearDown()
        {
            _manager.StopAll(DDrive.Foundation.Manager.StopReason.Manual);
            _pool.Clear(PoolScope.Global);
            if (_prefab != null)
            {
                Object.DestroyImmediate(_prefab);
            }
        }

        private static GameObject CreateButton(Transform parent, string name, Vector2 anchoredPosition)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).anchoredPosition = anchoredPosition;
            return go;
        }

        private CanvasData CreateCanvasData(GameObject prefab, ulong id = 1)
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Id = id;
            data.Prefab = prefab;
            return data;
        }

        // ── Build ──

        [Test]
        public void Build_CollectsListedAndUnlistedNodes_WithPositions()
        {
            _prefab = new GameObject("GraphTestPrefab", typeof(RectTransform));
            ((RectTransform)_prefab.transform).sizeDelta = new Vector2(400, 400);
            CreateButton(_prefab.transform, "A", new Vector2(0, 100));
            CreateButton(_prefab.transform, "B", new Vector2(0, -100));

            var data = CreateCanvasData(_prefab);
            data.Navigation = new[] { new NavNode { Element = "A" } }; // B は登録されていない(IsListed=false)

            var graph = NavigationGraph.Build(data, _prefab);

            Assert.AreEqual(2, graph.Nodes.Count);
            var a = graph.FindNode("A");
            var b = graph.FindNode("B");
            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.IsTrue(a.IsListed);
            Assert.IsFalse(b.IsListed);
            Assert.IsTrue(a.HasComponent);
            // A は B より上(anchoredPosition.y が大きい)= 画面座標では上に来る(Y が小さい)はず。
            Assert.Less(a.Position.y, b.Position.y);
        }

        [Test]
        public void Build_NullPrefab_ReturnsEmptyGraph()
        {
            var graph = NavigationGraph.Build(null, null);
            Assert.AreEqual(0, graph.Nodes.Count);
            Assert.AreEqual(0, graph.Edges.Count);
        }

        // ── Unreachable / Validator との一致 ──

        [Test]
        public void Unreachable_AgreesWithCanvasDataValidator_OnThreeNodeSample()
        {
            _prefab = new GameObject("UnreachablePrefab", typeof(RectTransform));
            CreateButton(_prefab.transform, "A", Vector2.zero);
            CreateButton(_prefab.transform, "B", Vector2.zero);
            CreateButton(_prefab.transform, "C", Vector2.zero); // どこからもリンクされない

            var data = CreateCanvasData(_prefab);
            data.FirstSelected = "A";
            data.Navigation = new[] { new NavNode { Element = "A", Right = "B" } };

            var graph = NavigationGraph.Build(data, _prefab);
            var graphUnreachable = graph.Unreachable(data.FirstSelected);

            var validatorWarnings = new CanvasDataValidator()
                .Validate(data, new ValidationContext(new List<AssetDataBase> { data }))
                .Where(r => r.Message.Contains("到達できません"))
                .Select(r => r.Message)
                .ToList();

            CollectionAssert.AreEqual(new[] { "C" }, graphUnreachable);
            Assert.AreEqual(1, validatorWarnings.Count, "Validator も C だけを到達不能として警告するはず");
            StringAssert.Contains("'C'", validatorWarnings[0]);
        }

        // ── SetLink / ClearLink ──

        [Test]
        public void SetLink_AddsNavNode_WhenMissing()
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            Assert.IsNull(data.Navigation);

            NavigationGraph.SetLink(data, "B", NavDirection.Up, "A");

            Assert.AreEqual(1, data.Navigation.Length);
            Assert.AreEqual("B", data.Navigation[0].Element);
            Assert.AreEqual("A", data.Navigation[0].Up);
        }

        [Test]
        public void ClearLink_EmptiesDirection_ButKeepsNode()
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            NavigationGraph.SetLink(data, "B", NavDirection.Up, "A");

            NavigationGraph.ClearLink(data, "B", NavDirection.Up);

            Assert.AreEqual(1, data.Navigation.Length);
            Assert.AreEqual(string.Empty, data.Navigation[0].Up);
        }

        [Test]
        public void ClearAllLinks_EmptiesAllFourDirections()
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            NavigationGraph.SetLink(data, "B", NavDirection.Up, "A");
            NavigationGraph.SetLink(data, "B", NavDirection.Down, "C");

            NavigationGraph.ClearAllLinks(data, "B");

            var node = data.Navigation[0];
            Assert.AreEqual(string.Empty, node.Up);
            Assert.AreEqual(string.Empty, node.Down);
            Assert.AreEqual(string.Empty, node.Left);
            Assert.AreEqual(string.Empty, node.Right);
        }

        // ── MoveFocusFrom(UiManager, データ駆動) ──

        [Test]
        public void MoveFocusFrom_FollowsExplicitLink()
        {
            _prefab = new GameObject("MoveFocusPrefab", typeof(RectTransform));
            CreateButton(_prefab.transform, "A", new Vector2(0, 0));
            var cGo = new GameObject("C", typeof(RectTransform));
            cGo.transform.SetParent(_prefab.transform, false);

            var data = CreateCanvasData(_prefab);
            data.Navigation = new[] { new NavNode { Element = "A", Right = "C" } };

            var handle = _manager.OpenData(data);

            var moved = _manager.MoveFocusFrom(handle, "A", Vector2.right, out var next);

            Assert.IsTrue(moved);
            Assert.AreEqual("C", next);
        }

        [Test]
        public void MoveFocusFrom_FallsBackToNearestInDirection_WhenNoExplicitLink()
        {
            _prefab = new GameObject("MoveFocusFallbackPrefab", typeof(RectTransform));
            ((RectTransform)_prefab.transform).sizeDelta = new Vector2(400, 400);
            CreateButton(_prefab.transform, "A", new Vector2(0, 100));
            CreateButton(_prefab.transform, "B", new Vector2(0, -100)); // A の真下

            var data = CreateCanvasData(_prefab);
            // Navigation は空(Automatic) = 明示リンク無し。

            var handle = _manager.OpenData(data);

            var moved = _manager.MoveFocusFrom(handle, "A", Vector2.down, out var next);

            Assert.IsTrue(moved);
            Assert.AreEqual("B", next);
        }

        [Test]
        public void MoveFocusFrom_ReturnsFalse_WhenNothingInDirection()
        {
            _prefab = new GameObject("MoveFocusNoneprefab", typeof(RectTransform));
            CreateButton(_prefab.transform, "A", Vector2.zero);

            var data = CreateCanvasData(_prefab);
            var handle = _manager.OpenData(data);

            var moved = _manager.MoveFocusFrom(handle, "A", Vector2.up, out var next);

            Assert.IsFalse(moved);
            Assert.AreEqual("A", next);
        }

        // ── UiButton.SimulateClick(決定ボタンの実体) ──

        [Test]
        public void UiButton_SimulateClick_FiresOnClick()
        {
            _prefab = new GameObject("SubmitPrefab", typeof(RectTransform));
            var btnGo = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(UiButton));
            btnGo.transform.SetParent(_prefab.transform, false);

            var data = CreateCanvasData(_prefab);
            var handle = _manager.OpenData(data);

            var button = _manager.GetComponent<UiButton>(handle, "Btn");
            Assert.IsNotNull(button);

            var fired = false;
            button.OnClick += () => fired = true;

            button.SimulateClick();

            Assert.IsTrue(fired, "パッド操作シミュレーションの「決定」が呼ぶ UiButton.SimulateClick は OnClick を発火するはず");
        }
    }
}
