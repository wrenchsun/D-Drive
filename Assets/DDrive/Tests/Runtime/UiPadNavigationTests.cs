using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DDrive.Tests.Runtime
{
    // [07_canvas_prefab.md] NavNode / [18_ui_controls.md] A-1・A-3(2026-09-12) —
    // EventSystem の Navigate(IMoveHandler.OnMove)で UiButton / UiSlider 間のフォーカスが移ること。
    // 1: NavNode の明示リンク、2: リンクが無ければ開いている Canvas 内で方向の最寄り、
    // 3: UiSlider は 4 方向とも値の操作として消費し、端到達 + EscapeOnLimit のときだけ抜ける。
    public class UiPadNavigationTests
    {
        private PoolService _pool;
        private UiManager _manager;
        private GameObject _prefab;
        private GameObject _eventSystem;
        private UiButton _a;
        private UiButton _b;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _manager = new UiManager(_pool, new AssetRegistry(new FakeAssetLoader()), new PauseService());
            Ui.Bind(_manager);

            _eventSystem = new GameObject("EventSystem", typeof(EventSystem));

            _prefab = new GameObject("PadNavPrefab", typeof(RectTransform));
            _prefab.AddComponent<Canvas>();
            _a = MakeButton("A", new Vector2(0f, 100f));
            _b = MakeButton("B", new Vector2(0f, 0f));
            UiInteractable.ResetDoubleFireGuardForTests();
        }

        [TearDown]
        public void TearDown()
        {
            _manager.StopAll(DDrive.Foundation.Manager.StopReason.Manual);
            Ui.Bind(null);
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_prefab);
            Object.DestroyImmediate(_eventSystem);
        }

        private UiButton MakeButton(string name, Vector2 anchoredPosition)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_prefab.transform, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(100f, 30f);
            rt.anchoredPosition = anchoredPosition;
            return go.AddComponent<UiButton>();
        }

        private CanvasData CreateCanvasData(params NavNode[] navigation)
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Id = 1;
            data.Prefab = _prefab;
            data.Navigation = navigation;
            return data;
        }

        private static AxisEventData Move(MoveDirection dir) => new(EventSystem.current) { moveDir = dir };

        [Test]
        public void OnMove_FollowsExplicitNavNodeLink()
        {
            var handle = _manager.OpenData(CreateCanvasData(new NavNode { Element = "A", Down = "B" }));
            var a = _manager.GetComponent<UiButton>(handle, "A");
            var b = _manager.GetComponent<UiButton>(handle, "B");
            EventSystem.current.SetSelectedGameObject(a.gameObject);

            var evt = Move(MoveDirection.Down);
            a.OnMove(evt);

            Assert.AreEqual(b.gameObject, EventSystem.current.currentSelectedGameObject);
            Assert.IsTrue(evt.used, "移動できたイベントは消費される");
        }

        [Test]
        public void OnMove_WithoutNavNode_FallsBackToNearestInDirection()
        {
            var handle = _manager.OpenData(CreateCanvasData());
            var a = _manager.GetComponent<UiButton>(handle, "A");
            var b = _manager.GetComponent<UiButton>(handle, "B");
            EventSystem.current.SetSelectedGameObject(a.gameObject);

            a.OnMove(Move(MoveDirection.Down)); // B は A の真下
            Assert.AreEqual(b.gameObject, EventSystem.current.currentSelectedGameObject);

            var up = Move(MoveDirection.Up);
            b.OnMove(up);
            Assert.AreEqual(a.gameObject, EventSystem.current.currentSelectedGameObject);

            var left = Move(MoveDirection.Left); // 左には何も無い → 動かない・消費しない
            a.OnMove(left);
            Assert.AreEqual(a.gameObject, EventSystem.current.currentSelectedGameObject);
            Assert.IsFalse(left.used);
        }

        [Test]
        public void Slider_OnMove_AlongAxis_EscapesOnlyAtLimitWithEscapeOnLimit()
        {
            var sliderGo = new GameObject("S", typeof(RectTransform));
            sliderGo.transform.SetParent(_prefab.transform, false);
            ((RectTransform)sliderGo.transform).anchoredPosition = new Vector2(-200f, 0f);
            var prefabSlider = sliderGo.AddComponent<UiSlider>();
            prefabSlider.Min = 0f;
            prefabSlider.Max = 1f;
            prefabSlider.PadStepAmount = 0.5f;

            var handle = _manager.OpenData(CreateCanvasData(new NavNode { Element = "S", Right = "B", Up = "A" }));
            var s = _manager.GetComponent<UiSlider>(handle, "S");
            var b = _manager.GetComponent<UiButton>(handle, "B");
            var a = _manager.GetComponent<UiButton>(handle, "A");
            s.SetValueSilent(1f);
            EventSystem.current.SetSelectedGameObject(s.gameObject);

            s.EscapeOnLimit = false;
            s.OnMove(Move(MoveDirection.Right)); // 端でも抜けない
            Assert.AreEqual(s.gameObject, EventSystem.current.currentSelectedGameObject);

            s.EscapeOnLimit = true;
            s.OnMove(Move(MoveDirection.Right)); // 端 + EscapeOnLimit → 右隣へ
            Assert.AreEqual(b.gameObject, EventSystem.current.currentSelectedGameObject);

            EventSystem.current.SetSelectedGameObject(s.gameObject);
            s.OnMove(Move(MoveDirection.Left)); // 端ではない → 値が動き、フォーカスは残る
            Assert.AreEqual(0.5f, s.Value, 1e-4f);
            Assert.AreEqual(s.gameObject, EventSystem.current.currentSelectedGameObject);

            s.OnMove(Move(MoveDirection.Up)); // Up も値の操作(+)として消費される(SignFor の既存仕様) → 端に戻り、フォーカスは残る
            Assert.AreEqual(1f, s.Value, 1e-4f);
            Assert.AreEqual(s.gameObject, EventSystem.current.currentSelectedGameObject);

            s.OnMove(Move(MoveDirection.Up)); // 端 + EscapeOnLimit → NavNode.Up の A へ
            Assert.AreEqual(a.gameObject, EventSystem.current.currentSelectedGameObject);
        }

        [Test]
        public void OnMove_WhenUiUnbound_DoesNothing()
        {
            Ui.Bind(null);
            var handle = _manager.OpenData(CreateCanvasData(new NavNode { Element = "A", Down = "B" }));
            var a = _manager.GetComponent<UiButton>(handle, "A");
            EventSystem.current.SetSelectedGameObject(a.gameObject);

            var evt = Move(MoveDirection.Down);
            a.OnMove(evt);

            Assert.AreEqual(a.gameObject, EventSystem.current.currentSelectedGameObject);
            Assert.IsFalse(evt.used);
        }
    }
}
