using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Tests.Runtime
{
    // CR-1(2026-11-11) — Canvas を持つ Prefab のルートは、ルート Canvas だった間に Unity が画面サイズで駆動した
    // RectTransform の値(倍率・アンカー・位置)を、入れ子 Canvas になったあとに残さない。
    public class UiCanvasRootRectTests
    {
        private PoolService _pool;
        private UiManager _manager;
        private RenderTexture _rt;
        private readonly System.Collections.Generic.List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            var registry = new AssetRegistry(new FakeAssetLoader());
            _pool = new PoolService();
            _manager = new UiManager(_pool, registry, new PauseService());
        }

        [TearDown]
        public void TearDown()
        {
            _manager.StopAll(DDrive.Foundation.Manager.StopReason.Manual);
            _pool.Clear(PoolScope.Global);
            for (var i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
            if (_rt != null)
            {
                _rt.Release();
                Object.DestroyImmediate(_rt);
                _rt = null;
            }
        }

        // 「ルート Canvas として画面 960x540・基準 1920x1080 で駆動された」値を再現した Prefab。
        private GameObject MakeDrivenCanvasPrefab(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.AddComponent<Canvas>();
            go.AddComponent<CanvasGroup>();
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.anchoredPosition = new Vector2(480f, 270f);
            rect.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            _created.Add(go);
            return go;
        }

        private CanvasData Data(ulong id, GameObject prefab)
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Id = id;
            data.Prefab = prefab;
            data.Layer = UiLayer.Menu;
            _created.Add(data);
            return data;
        }

        private static void AssertFullStretchUnitScale(RectTransform rect)
        {
            Assert.AreEqual(Vector2.zero, rect.anchorMin);
            Assert.AreEqual(Vector2.one, rect.anchorMax);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), rect.pivot);
            Assert.AreEqual(Vector2.zero, rect.offsetMin);
            Assert.AreEqual(Vector2.zero, rect.offsetMax);
            Assert.AreEqual(Vector3.one, rect.localScale);
        }

        [Test]
        public void Open_CanvasRoot_IsNormalizedToFullStretchUnitScale()
        {
            var prefab = MakeDrivenCanvasPrefab("Driven");
            var handle = _manager.OpenData(Data(1, prefab));

            var root = _manager.GetGameObject(handle);
            Assert.IsNotNull(root);
            AssertFullStretchUnitScale((RectTransform)root.transform);
        }

        [Test]
        public void ReopenAfterClose_KeepsUnitScale()
        {
            var prefab = MakeDrivenCanvasPrefab("DrivenReopen");
            var data = Data(1, prefab);

            var first = _manager.OpenData(data);
            _manager.Close(first);
            var second = _manager.OpenData(data);

            var root = _manager.GetGameObject(second);
            Assert.IsNotNull(root);
            AssertFullStretchUnitScale((RectTransform)root.transform);
        }

        [Test]
        public void Open_RootWithoutCanvas_KeepsPrefabPlacement()
        {
            var go = new GameObject("NoCanvas", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(300f, 200f);
            rect.anchoredPosition = Vector2.zero;
            _created.Add(go);

            var handle = _manager.OpenData(Data(1, go));

            var opened = (RectTransform)_manager.GetGameObject(handle).transform;
            Assert.AreEqual(new Vector2(0.5f, 0.5f), opened.anchorMin);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), opened.anchorMax);
            Assert.AreEqual(new Vector2(300f, 200f), opened.sizeDelta);
        }

        [Test]
        public void OpenInto_CanvasRoot_IsNormalizedToFullStretchUnitScale()
        {
            RequiresGraphicsGuard.SkipIfNoGraphicsDevice();

            _rt = new RenderTexture(64, 32, 16, RenderTextureFormat.ARGB32);
            _rt.Create();
            var prefab = MakeDrivenCanvasPrefab("DrivenInto");
            var handle = _manager.OpenDataInto(Data(1, prefab), _rt);

            var root = _manager.GetGameObject(handle);
            Assert.IsNotNull(root);
            AssertFullStretchUnitScale((RectTransform)root.transform);
        }
    }
}
