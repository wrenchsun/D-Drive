using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DDrive.Tests.Runtime
{
    // [07_canvas_prefab.md] 2026-10-12(C-1) — UiManager のレイヤールート Canvas の CanvasScaler。
    public class UiCanvasScalerTests
    {
        private PoolService _pool;
        private UiManager _manager;
        private GameObject _plain;
        private GameObject _hd;
        private GameObject _qhd;

        [SetUp]
        public void SetUp()
        {
            _pool = new PoolService();
            _manager = new UiManager(_pool, new AssetRegistry(new FakeAssetLoader()), new PauseService());
            _plain = MakePrefab("Plain", null);
            _hd = MakePrefab("Hd", s =>
            {
                s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                s.referenceResolution = new Vector2(1920, 1080);
                s.matchWidthOrHeight = 0.25f;
            });
            _qhd = MakePrefab("Qhd", s =>
            {
                s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                s.referenceResolution = new Vector2(2560, 1440);
                s.matchWidthOrHeight = 0.25f;
            });
        }

        [TearDown]
        public void TearDown()
        {
            _manager.StopAll(DDrive.Foundation.Manager.StopReason.Manual);
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_plain);
            Object.DestroyImmediate(_hd);
            Object.DestroyImmediate(_qhd);
        }

        private static GameObject MakePrefab(string name, System.Action<CanvasScaler> setup)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.AddComponent<Canvas>();
            go.AddComponent<CanvasGroup>();
            if (setup != null)
            {
                setup(go.AddComponent<CanvasScaler>());
            }

            return go;
        }

        private static CanvasData Data(ulong id, GameObject prefab, UiLayer layer)
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Id = id;
            data.Prefab = prefab;
            data.Layer = layer;
            return data;
        }

        private static CanvasScaler LayerScaler(GameObject opened)
            => opened.transform.parent.GetComponent<CanvasScaler>();

        [Test]
        public void LayerRoot_HasCanvasScaler_WithDefaults()
        {
            var handle = _manager.OpenData(Data(1, _plain, UiLayer.Menu));
            var scaler = LayerScaler(_manager.GetGameObject(handle));

            Assert.IsNotNull(scaler);
            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
            Assert.AreEqual(UiCanvasScalerDefaults.ReferenceResolution, scaler.referenceResolution);
            Assert.AreEqual(UiCanvasScalerDefaults.Match, scaler.matchWidthOrHeight, 0.0001f);
        }

        [Test]
        public void Open_CopiesPrefabCanvasScalerSettings()
        {
            var handle = _manager.OpenData(Data(1, _hd, UiLayer.HUD));
            var scaler = LayerScaler(_manager.GetGameObject(handle));

            Assert.AreEqual(new Vector2(1920, 1080), scaler.referenceResolution);
            Assert.AreEqual(0.25f, scaler.matchWidthOrHeight, 0.0001f);
        }

        [Test]
        public void Mismatch_KeepsFirstSettings_AndWarnsOnce()
        {
            _manager.OpenData(Data(1, _hd, UiLayer.Menu));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("CanvasScaler"));
            var second = _manager.OpenData(Data(2, _qhd, UiLayer.Menu));
            var third = _manager.OpenData(Data(3, _qhd, UiLayer.Menu)); // 2 回目は警告なし(LogAssert が未処理の警告で失敗する)

            var scaler = LayerScaler(_manager.GetGameObject(second));
            Assert.AreEqual(new Vector2(1920, 1080), scaler.referenceResolution);
            Assert.IsNotNull(_manager.GetGameObject(third));
        }

        [Test]
        public void ReopenAfterAllClosed_AdoptsNewPrefabSettings()
        {
            var first = _manager.OpenData(Data(1, _hd, UiLayer.Menu));
            _manager.Close(first);
            _manager.StopAll(DDrive.Foundation.Manager.StopReason.Manual);

            var second = _manager.OpenData(Data(2, _qhd, UiLayer.Menu));
            Assert.AreEqual(new Vector2(2560, 1440), LayerScaler(_manager.GetGameObject(second)).referenceResolution);
        }
    }
}
