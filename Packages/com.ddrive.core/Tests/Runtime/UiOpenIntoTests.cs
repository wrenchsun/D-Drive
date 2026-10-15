using System.Collections;
using System.Collections.Generic;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Pool;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DDrive.Tests.Runtime
{
    // [07_canvas_prefab.md] A-3(MS-1、2026-10-15) — Ui.OpenInto(UI スタックの外で RenderTexture に描く)。
    // 2026-10-15 レビュー対応([1013] MS-R-01〜04・08・11)の検証を含む。
    public class UiOpenIntoTests
    {
        private PoolService _pool;
        private UiTweenManager _tweens;
        private UiManager _manager;
        private GameObject _prefab;
        private RenderTexture _rt;
        private readonly List<Object> _created = new();

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
            }
        }

        // 標準の Canvas Prefab(CanvasSetupService)と同じく、ルートに Canvas + GraphicRaycaster を持つ。
        // halfFill = Fill を左半分だけにする(右半分は何も描かれない = 背景の α を読める)。
        private static GameObject MakePrefab(string name, Color color, bool halfFill = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var root = (RectTransform)go.transform;
            root.anchorMin = Vector2.zero; // 標準の Canvas Prefab と同じく親(専用 Canvas)いっぱいに広げる
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            go.AddComponent<Canvas>();
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<CanvasGroup>();
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var image = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            image.transform.SetParent(go.transform, false);
            var rect = (RectTransform)image.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = halfFill ? new Vector2(0.5f, 1f) : Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            image.GetComponent<Image>().color = color;
            return go;
        }

        private CanvasData Data(ulong id, GameObject prefab, UiLayer layer = UiLayer.Menu)
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Id = id;
            data.Prefab = prefab;
            data.Layer = layer;
            _created.Add(data);
            return data;
        }

        private Color ReadPixel(float fx, float fy)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            var c = tex.GetPixel(Mathf.RoundToInt((_rt.width - 1) * fx), Mathf.RoundToInt((_rt.height - 1) * fy));
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

            var c = ReadPixel(0.5f, 0.5f);
            Assert.Greater(c.a, 0f, "RT に α が残る(背景は透明)");
            Assert.Greater(c.r, 0.5f, "Prefab の Image の赤が載っている");
            Assert.Less(c.g, 0.5f);
        }

        // MS-R-04: 描かれていない所は α = 0 のまま(Camera の背景 (0,0,0,0) が RT に残る)。
        [UnityTest]
        public IEnumerator OpenInto_LeavesTransparentBackground_WhereNothingIsDrawn()
        {
            RequiresGraphicsGuard.SkipIfNoGraphicsDevice();

            var half = MakePrefab("HalfPrefab", Color.red, halfFill: true);
            try
            {
                _manager.OpenDataInto(Data(1, half), _rt);
                yield return null;
                yield return null;

                var filled = ReadPixel(0.25f, 0.5f);
                var empty = ReadPixel(0.75f, 0.5f);
                Assert.Greater(filled.a, 0.5f, "左半分は描かれる");
                Assert.Greater(filled.r, 0.5f);
                Assert.AreEqual(0f, empty.a, 0.01f, "右半分(何も描かれない所)は α = 0 のまま");
            }
            finally
            {
                Object.DestroyImmediate(half);
            }
        }

        // MS-R-04: size を RT より小さくした経路(中間テクスチャになりやすい)でも、描画域は描かれ、外は α = 0。
        [UnityTest]
        public IEnumerator OpenInto_WithSmallerSize_DrawsOnlyInsideSize_AndKeepsAlpha()
        {
            RequiresGraphicsGuard.SkipIfNoGraphicsDevice();

            _manager.OpenDataInto(Data(1, _prefab), _rt, new Vector2Int(32, 16));
            yield return null;
            yield return null;

            var inside = ReadPixel(0.25f, 0.25f);
            var outside = ReadPixel(0.9f, 0.9f);
            Assert.Greater(inside.a, 0.5f, "size の内側は描かれる");
            Assert.Greater(inside.r, 0.5f);
            Assert.AreEqual(0f, outside.a, 0.01f, "size の外側は α = 0");
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

                var c = ReadPixel(0.5f, 0.5f);
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
            Assert.AreEqual(Handle<CanvasMarker>.Invalid, _manager.Top);
            Assert.IsFalse(_manager.CloseTop());
            Assert.IsTrue(_manager.IsOpen(handle));
        }

        // MS-R-01: 標準 Prefab(ルートに GraphicRaycaster)でも、画面の左下のクリックを受けない。
        [UnityTest]
        public IEnumerator OpenInto_DoesNotReceiveRaycasts_AtScreenBottomLeft()
        {
            // RT を実際に Create する(GraphicRaycaster の eventCamera 判定)ため、-nographics では Inconclusive にする。
            RequiresGraphicsGuard.SkipIfNoGraphicsDevice();

            var es = new GameObject("TestEventSystem", typeof(EventSystem));
            _created.Add(es);

            var handle = _manager.OpenDataInto(Data(1, _prefab), _rt);
            var go = _manager.GetGameObject(handle);
            var raycaster = go.GetComponent<GraphicRaycaster>();
            Assert.IsNotNull(raycaster, "試験用 Prefab は標準 Prefab と同じくルートに GraphicRaycaster を持つ");

            yield return null;

            var data = new PointerEventData(EventSystem.current) { position = new Vector2(10f, 10f) };
            var results = new List<RaycastResult>();
            raycaster.Raycast(data, results);
            Assert.AreEqual(0, results.Count, "Offscreen の Canvas は画面左下のクリックを受けない");

            var group = _manager.GetComponent<CanvasGroup>(handle);
            Assert.IsFalse(group.blocksRaycasts);
            Assert.IsFalse(group.interactable);
        }

        [Test]
        public void PlayDisappear_ThenClose_DoesNotPlayDisappearTwice()
        {
            var data = Data(1, _prefab);
            data.ElementEffects = new[]
            {
                new ElementFx
                {
                    ElementPath = "Fill",
                    AppearPreset = new UiPresetRef { Preset = UiPreset.FadeIn, Duration = 0.1f },
                    DisappearPreset = new UiPresetRef { Preset = UiPreset.FadeOut, Duration = 0.2f },
                },
            };

            var handle = _manager.OpenDataInto(data, _rt);
            _tweens.Tick(0.01f);
            _manager.Tick(0.01f);
            Assert.AreEqual(1, _tweens.ActiveCount, "Open の ElementFx.Appear が走る");

            for (var i = 0; i < 4; i++)
            {
                _tweens.Tick(0.05f);
                _manager.Tick(0.05f);
            }

            Assert.AreEqual(0, _tweens.ActiveCount, "Appear は終わった");

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

        // MS-R-08: Appear の途中で PlayDisappear しても、入力ゲート(IsOpening)が開かないままにならない。
        [Test]
        public void PlayDisappear_DuringAppear_ReleasesInputGate()
        {
            var data = Data(1, _prefab);
            data.ElementEffects = new[]
            {
                new ElementFx
                {
                    ElementPath = "Fill",
                    AppearPreset = new UiPresetRef { Preset = UiPreset.FadeIn, Duration = 1f },
                    DisappearPreset = new UiPresetRef { Preset = UiPreset.FadeOut, Duration = 0.1f },
                },
            };

            var handle = _manager.OpenData(data);
            _tweens.Tick(0.05f);
            _manager.Tick(0.05f);
            Assert.IsTrue(_manager.IsOpening(handle), "Appear 中は入力ゲートが閉じている");

            _manager.PlayDisappear(handle);
            Assert.IsFalse(_manager.IsOpening(handle), "Appear を打ち切ったので入力ゲートは開く");
            var group = _manager.GetComponent<CanvasGroup>(handle);
            Assert.IsTrue(group.interactable);
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

            // MS-R-01: Offscreen で塞いだ入力は、プールへ返すときに戻る(通常の Open で再貸出されても塞がれたままにならない)。
            var again = _manager.OpenData(data);
            Assert.AreSame(go, _manager.GetGameObject(again), "プールから再貸出される");
            var group = _manager.GetComponent<CanvasGroup>(again);
            Assert.IsTrue(group.blocksRaycasts);
            Assert.IsTrue(group.interactable);
        }

        // MS-R-02: 専用ルートの番号は「同時に開いている中での空き番号」。差し替えを繰り返しても Y が遠ざからない。
        [Test]
        public void OffscreenRootIndex_IsReused_AfterClose()
        {
            var first = _manager.OpenDataInto(Data(1, _prefab), _rt);
            var y0 = OffscreenRootY(first);
            Assert.AreEqual(-10000f, y0, 0.001f);

            var second = _manager.OpenDataInto(Data(2, _prefab), _rt);
            Assert.AreEqual(-10100f, OffscreenRootY(second), 0.001f, "同時に開いている 2 つ目は次の番号");

            _manager.Close(first);
            var third = _manager.OpenDataInto(Data(3, _prefab), _rt);
            Assert.AreEqual(y0, OffscreenRootY(third), 0.001f, "閉じた番号を再利用する");

            for (var i = 0; i < 20; i++)
            {
                _manager.Close(third);
                third = _manager.OpenDataInto(Data(4, _prefab), _rt);
            }

            Assert.AreEqual(y0, OffscreenRootY(third), 0.001f, "開閉を繰り返しても Y は遠ざからない");
        }

        private float OffscreenRootY(Handle<CanvasMarker> handle)
            => _manager.GetGameObject(handle).GetComponentInParent<Canvas>().rootCanvas.transform.parent.position.y;

        // MS-R-03: RT が Close より先に破棄されたら、Camera を止めて警告 1 回(画面に描かない)。
        [Test]
        public void OffscreenTarget_DestroyedBeforeClose_StopsCameraWithOneWarning()
        {
            var rt = new RenderTexture(32, 32, 0, RenderTextureFormat.ARGB32);
            rt.Create();
            var handle = _manager.OpenDataInto(Data(1, _prefab), rt);
            var cam = _manager.GetGameObject(handle).GetComponentInParent<Canvas>().rootCanvas.worldCamera;
            Assert.IsTrue(cam.enabled);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Releasing render texture that is set as Camera.targetTexture"));
            rt.Release();
            Object.DestroyImmediate(rt);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("RenderTexture が先に破棄"));
            _manager.Tick(0.016f);
            _manager.Tick(0.016f); // 2 回目は警告なし(LogAssert が未処理の警告で失敗しないことで確認)
            Assert.IsFalse(cam.enabled, "RT を失った Camera は止まる");

            _manager.Close(handle);
            Assert.IsFalse(_manager.IsOpen(handle));
        }

        [Test]
        public void OpenInto_NullTarget_ReturnsInvalidWithWarning()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("OpenInto"));
            var handle = _manager.OpenDataInto(Data(1, _prefab), null);
            Assert.AreEqual(Handle<CanvasMarker>.Invalid, handle);
            Assert.AreEqual(0, _manager.StackCount);
        }
    }
}
