using System.Collections;
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
    // [07_canvas_prefab.md] A-3(MS-1、2026-10-15) — Ui.OpenInto(UI スタックの外で RenderTexture に描く)。
    public class UiOpenIntoTests
    {
        private PoolService _pool;
        private UiTweenManager _tweens;
        private UiManager _manager;
        private GameObject _prefab;
        private RenderTexture _rt;

        [SetUp]
        public void SetUp()
        {
            var registry = new AssetRegistry(new FakeAssetLoader());
            _pool = new PoolService();
            _tweens = new UiTweenManager(registry);
            _manager = new UiManager(_pool, registry, new PauseService());
            _manager.SetTweenManager(_tweens);

            _prefab = MakePrefab("OffscreenPrefab", Color.red);
            _rt = new RenderTexture(64, 32, 16, RenderTextureFormat.ARGB32);
            _rt.Create();
        }

        [TearDown]
        public void TearDown()
        {
            _manager.StopAll(DDrive.Foundation.Manager.StopReason.Manual);
            _pool.Clear(PoolScope.Global);
            Object.DestroyImmediate(_prefab);
            if (_rt != null)
            {
                _rt.Release();
                Object.DestroyImmediate(_rt);
            }
        }

        private static GameObject MakePrefab(string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.AddComponent<Canvas>();
            go.AddComponent<CanvasGroup>();
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var image = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            image.transform.SetParent(go.transform, false);
            var rect = (RectTransform)image.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            image.GetComponent<Image>().color = color;
            return go;
        }

        private static CanvasData Data(ulong id, GameObject prefab, UiLayer layer = UiLayer.Menu)
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Id = id;
            data.Prefab = prefab;
            data.Layer = layer;
            return data;
        }

        private Color ReadCenter()
        {
            var previous = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            var c = tex.GetPixel(_rt.width / 2, _rt.height / 2);
            Object.DestroyImmediate(tex);
            return c;
        }

        [UnityTest]
        public IEnumerator OpenInto_RendersPrefabIntoRenderTexture()
        {
            RequiresGraphicsGuard.SkipIfNoGraphicsDevice();

            var handle = _manager.OpenDataInto(Data(1, _prefab), _rt);
            Assert.IsTrue(_manager.IsOpen(handle));

            yield return null;
            yield return null;

            var c = ReadCenter();
            Assert.Greater(c.a, 0f, "RT に α が残る(背景は透明)");
            Assert.Greater(c.r, 0.5f, "Prefab の Image の赤が載っている");
            Assert.Less(c.g, 0.5f);
        }

        [UnityTest]
        public IEnumerator OpenInto_IsIsolatedFromLayersAndHud()
        {
            RequiresGraphicsGuard.SkipIfNoGraphicsDevice();

            var hudPrefab = MakePrefab("HudPrefab", Color.green);
            try
            {
                var hud = _manager.OpenData(Data(2, hudPrefab, UiLayer.HUD));
                var off = _manager.OpenDataInto(Data(3, _prefab), _rt);

                var offGo = _manager.GetGameObject(off);
                var hudGo = _manager.GetGameObject(hud);
                Assert.IsFalse(IsUnderUiRoot(offGo), "Offscreen は画面の 5 レイヤーに居ない");
                Assert.IsTrue(IsUnderUiRoot(hudGo));
                Assert.Less(offGo.transform.position.y, -9000f, "遠方に置かれている");

                yield return null;
                yield return null;

                var c = ReadCenter();
                Assert.Greater(c.r, 0.5f, "RT には Offscreen の Canvas(赤)だけ");
                Assert.Less(c.g, 0.5f, "HUD(緑)は RT に写らない");
            }
            finally
            {
                Object.DestroyImmediate(hudPrefab);
            }
        }

        private static bool IsUnderUiRoot(GameObject go)
        {
            for (var t = go.transform; t != null; t = t.parent)
            {
                if (t.name == UiManager.RootName)
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void OpenInto_IsNotOnStack_AndCloseTopIgnoresIt()
        {
            var handle = _manager.OpenDataInto(Data(1, _prefab), _rt);

            Assert.AreEqual(0, _manager.StackCount);
            Assert.AreEqual(DDrive.Foundation.Handle.Handle<CanvasMarker>.Invalid, _manager.Top);
            Assert.IsFalse(_manager.CloseTop());
            Assert.IsTrue(_manager.IsOpen(handle));
            var group = _manager.GetComponent<CanvasGroup>(handle);
            Assert.IsTrue(group.interactable);
            Assert.IsNull(_manager.GetGameObject(handle).GetComponentInParent<GraphicRaycaster>(), "入力を受けない(GraphicRaycaster を付けない)");
        }

        [Test]
        public void PlayDisappear_ThenClose_DoesNotPlayDisappearTwice()
        {
            var data = Data(1, _prefab);
            data.ElementEffects = new[]
            {
                new ElementFx { ElementPath = "Fill", DisappearPreset = new UiPresetRef { Preset = UiPreset.FadeOut, Duration = 0.2f } },
            };

            var handle = _manager.OpenDataInto(data, _rt);
            _tweens.Tick(0.05f);
            _manager.Tick(0.05f);
            Assert.AreEqual(0, _tweens.ActiveCount);

            _manager.PlayDisappear(handle);
            Assert.AreEqual(1, _tweens.ActiveCount, "Disappear が 1 本走る");
            Assert.IsTrue(_manager.IsOpen(handle), "PlayDisappear は Canvas を閉じない");

            for (var i = 0; i < 8; i++)
            {
                _tweens.Tick(0.05f);
                _manager.Tick(0.05f);
            }

            Assert.AreEqual(0, _tweens.ActiveCount, "Disappear は終わった");
            Assert.IsTrue(_manager.IsOpen(handle));

            _manager.Close(handle);
            Assert.AreEqual(0, _tweens.ActiveCount, "Close は Disappear をもう一度再生しない");
            Assert.IsFalse(_manager.IsOpen(handle), "CloseTransition(なし)だけで閉じ切る");
        }

        [UnityTest]
        public IEnumerator Close_DestroysRootAndCamera_AndReturnsPrefabToPool()
        {
            var data = Data(1, _prefab);
            data.Flags.Pool.Kind = DDrive.Foundation.Data.PoolPolicyKind.Pooled;
            var handle = _manager.OpenDataInto(data, _rt);
            var go = _manager.GetGameObject(handle);
            var canvas = go.GetComponentInParent<Canvas>().rootCanvas;
            var cam = canvas.worldCamera;
            Assert.IsNotNull(cam);
            var offscreenRoot = canvas.transform.parent.gameObject;
            Assert.AreEqual(_rt, cam.targetTexture);

            _manager.Close(handle);
            yield return null;

            Assert.IsFalse(_manager.IsOpen(handle));
            Assert.IsTrue(offscreenRoot == null, "専用ルートは破棄される");
            Assert.IsTrue(cam == null, "Camera も破棄される");
            Assert.IsTrue(go != null, "Prefab はプールへ戻り破棄されない");
            Assert.IsFalse(go.activeSelf);

            var again = _manager.OpenDataInto(data, _rt);
            Assert.AreSame(go, _manager.GetGameObject(again), "プールから再貸出される");
        }

        [Test]
        public void OpenInto_NullTarget_ReturnsInvalidWithWarning()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("OpenInto"));
            var handle = _manager.OpenDataInto(Data(1, _prefab), null);
            Assert.AreEqual(DDrive.Foundation.Handle.Handle<CanvasMarker>.Invalid, handle);
            Assert.AreEqual(0, _manager.StackCount);
        }
    }
}
