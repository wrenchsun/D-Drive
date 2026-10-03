using System.Collections;
using System.Collections.Generic;
using DDrive.Foundation.Event;
using DDrive.Foundation.Values;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Viewing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace DDrive.Tests.Runtime
{
    // [51_tdrive_integration.md] §4.4(FC-3) — ViewCamera の解決順・隔離・Cutscene 所有中の姿勢。
    // 外部アセンブリから見た契約(E-18)は ExternalContract.Tests.Runtime 側。ここは D-Drive 内部の実 CutsceneManager
    // と合わせた確認と、プロバイダの例外隔離・破棄済み・登録順などの細目。
    public class ViewCameraTests
    {
        private readonly List<Object> _created = new();
        private readonly List<GameObject> _disabledMainCameras = new();
        private readonly List<IViewProvider> _registered = new();

        [SetUp]
        public void SetUp()
        {
            TestProvider.CallOrder.Clear();
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (cam.CompareTag("MainCamera"))
                {
                    _disabledMainCameras.Add(cam.gameObject);
                    cam.gameObject.SetActive(false);
                }
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var p in _registered)
            {
                ViewCamera.Unregister(p);
            }

            _registered.Clear();

            foreach (var o in _created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _created.Clear();

            foreach (var go in _disabledMainCameras)
            {
                if (go != null)
                {
                    go.SetActive(true);
                }
            }

            _disabledMainCameras.Clear();
            TestProvider.CallOrder.Clear();
        }

        private Camera MakeCamera(string name, float fov, bool main)
        {
            var go = new GameObject(name, typeof(Camera));
            _created.Add(go);
            if (main)
            {
                go.tag = "MainCamera";
            }

            var cam = go.GetComponent<Camera>();
            cam.fieldOfView = fov;
            return cam;
        }

        private TestProvider Register(string name, int priority, Camera cam, bool doThrow = false)
        {
            var p = new TestProvider { Name = name, Camera = cam, Throw = doThrow };
            _registered.Add(p);
            ViewCamera.Register(p, priority);
            return p;
        }

        [Test]
        public void SamePriority_QueriedInRegistrationOrder_ReRegisterUpdatesPriority()
        {
            var cam = MakeCamera("VcCam", 50f, false);
            var a = Register("a", 0, cam);
            var b = Register("b", 0, cam);
            var c = Register("c", 0, cam);

            // 全員が答えるので先頭だけ呼ばれる。登録順に a から。
            ViewCamera.TryGetCurrent(null, out _);
            CollectionAssert.AreEqual(new[] { "a" }, TestProvider.CallOrder);

            // a を優先度 -1 に再登録 → b が先頭。
            ViewCamera.Register(a, -1);
            TestProvider.CallOrder.Clear();
            ViewCamera.TryGetCurrent(null, out _);
            CollectionAssert.AreEqual(new[] { "b" }, TestProvider.CallOrder);

            // 全員が担当しないとき(Camera 無し)は優先度の降順 → b, c, a の順に全員問い合わせられる。
            b.Camera = null;
            c.Camera = null;
            a.Camera = null;
            TestProvider.CallOrder.Clear();
            ViewCamera.TryGetCurrent(null, out _);
            CollectionAssert.AreEqual(new[] { "b", "c", "a" }, TestProvider.CallOrder);
        }

        [Test]
        public void ThrowingProvider_IsIsolated_NextProviderAndFallbackStillWork()
        {
            var main = MakeCamera("VcMain", 70f, true);
            var cam = MakeCamera("VcCam", 20f, false);
            Register("thrower", 10, cam, doThrow: true);
            Register("ok", 0, cam);

            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("TestProvider boom"));
            Assert.IsTrue(ViewCamera.TryGetCurrent(null, out var pose));
            Assert.AreEqual(20f, pose.VerticalFovDegrees, "例外のプロバイダの次の ok が答える");
            CollectionAssert.AreEqual(new[] { "thrower", "ok" }, TestProvider.CallOrder);

            // 全プロバイダが例外 / 不担当なら Camera.main へ。
            foreach (var p in _registered)
            {
                ViewCamera.Unregister(p);
            }

            _registered.Clear();
            Register("thrower2", 0, cam, doThrow: true);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("TestProvider boom"));
            Assert.IsTrue(ViewCamera.TryGetCurrent(null, out var pose2));
            Assert.AreEqual(ViewSource.MainCamera, pose2.Source);
            Assert.AreSame(main, pose2.Camera);
        }

        [UnityTest]
        public IEnumerator DestroyedMonoBehaviourProvider_IsSkippedAndRemoved()
        {
            var main = MakeCamera("VcMain", 70f, true);
            var go = new GameObject("VcProviderGo");
            var behaviour = go.AddComponent<TestBehaviour>();
            ViewCamera.Register(behaviour, 5);
            _registered.Add(behaviour);

            Assert.IsTrue(ViewCamera.TryGetCurrent(null, out var pose));
            Assert.AreEqual(33f, pose.VerticalFovDegrees);
            Assert.AreEqual(ViewSource.Override, pose.Source);

            Object.Destroy(go);
            yield return null;

            Assert.IsTrue(ViewCamera.TryGetCurrent(null, out var pose2));
            Assert.AreEqual(ViewSource.MainCamera, pose2.Source, "破棄済みのプロバイダは飛ばされる(例外・警告なし)");
            Assert.AreSame(main, pose2.Camera);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ProviderThatRegistersDuringCallback_DoesNotThrow()
        {
            var cam = MakeCamera("VcCam", 20f, false);
            var reentrant = new ReentrantProvider(cam);
            _registered.Add(reentrant);
            ViewCamera.Register(reentrant, 0);

            Assert.DoesNotThrow(() => ViewCamera.TryGetCurrent(null, out _));
            _registered.Add(reentrant.Added);
        }

        [Test]
        public void OrthographicCamera_ReturnsFieldOfViewAsIs()
        {
            var cam = MakeCamera("VcOrtho", 42f, true);
            cam.orthographic = true;
            cam.orthographicSize = 7f;

            Assert.IsTrue(ViewCamera.TryGetCurrent(null, out var pose));
            Assert.AreEqual(42f, pose.VerticalFovDegrees, "正射影でも Camera.fieldOfView をそのまま返す");
        }

        // 実 CutsceneManager が Camera.main を駆動している間は Source = Cutscene で、姿勢が Camera.main と一致(ブレンド中も追従)。
        [UnityTest]
        public IEnumerator CutsceneOwnsMainCamera_SourceIsCutscene_FollowsBlend_ThenReturnsToMainCamera()
        {
            var camGo = new GameObject("VcCutCam", typeof(Camera)) { tag = "MainCamera" };
            _created.Add(camGo);
            var cam = camGo.GetComponent<Camera>();
            cam.fieldOfView = 60f;
            var reader = camGo.AddComponent<LateReader>(); // 実行順 1001(Applier の後)

            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _created.Add(timeline);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 1.0;
            var track = timeline.CreateTrack<CutsceneCameraTrack>(null, "Camera");
            var clip = track.CreateClip<CutsceneCameraClip>();
            clip.duration = 1.0;
            var camAsset = (CutsceneCameraClip)clip.asset;
            camAsset.PosX = AnimationCurve.Linear(0f, 0f, 1f, 10f);
            camAsset.FieldOfView = AnimationCurve.Constant(0f, 1f, 30f);
            camAsset.BlendIn = ValueDef.Constant01(1f);
            camAsset.BlendOut = ValueDef.Constant01(1f);
            camAsset.Focus = CameraFocusMode.Off;

            var data = ScriptableObject.CreateInstance<CutsceneData>();
            _created.Add(data);
            data.Timeline = timeline;
            data.Origin = CutsceneOrigin.World;

            var manager = new CutsceneManager(new AssetRegistry(new FakeAssetLoader()));
            var handle = manager.PlayData(data, new PlayContext());

            manager.Tick(0.4f);
            yield return null;
            Assert.IsTrue(reader.Found);
            Assert.AreEqual(ViewSource.Cutscene, reader.Pose.Source);
            Assert.AreEqual(4f, reader.Pose.Position.x, 0.05f);
            Assert.AreEqual(cam.transform.position, reader.Pose.Position);
            Assert.AreEqual(cam.fieldOfView, reader.Pose.VerticalFovDegrees, 1e-4f);
            var firstX = reader.Pose.Position.x;

            manager.Tick(0.3f);
            yield return null;
            Assert.AreEqual(ViewSource.Cutscene, reader.Pose.Source);
            Assert.Greater(reader.Pose.Position.x, firstX, "カットの進行に追従する");
            Assert.AreEqual(cam.transform.position, reader.Pose.Position);

            manager.Cancel(handle);
            yield return null;
            Assert.AreEqual(ViewSource.MainCamera, reader.Pose.Source, "カットシーンが終わったら MainCamera");
        }

        private sealed class ReentrantProvider : IViewProvider
        {
            private readonly Camera _camera;
            public TestProvider Added;

            public ReentrantProvider(Camera camera)
            {
                _camera = camera;
            }

            public bool TryGetView(Transform subject, out ViewPose pose)
            {
                if (Added == null)
                {
                    Added = new TestProvider { Name = "added", Camera = _camera };
                    ViewCamera.Register(Added, 100);
                    ViewCamera.Unregister(this);
                }

                pose = default;
                return false;
            }
        }
    }

    internal sealed class TestProvider : IViewProvider
    {
        public static readonly List<string> CallOrder = new();

        public string Name = "provider";
        public Camera Camera;
        public bool Throw;

        public bool TryGetView(Transform subject, out ViewPose pose)
        {
            CallOrder.Add(Name);
            if (Throw)
            {
                throw new System.InvalidOperationException("TestProvider boom");
            }

            if (Camera == null)
            {
                pose = default;
                return false;
            }

            var t = Camera.transform;
            pose = new ViewPose(t.position, t.rotation, Camera.fieldOfView, ViewSource.Override, Camera);
            return true;
        }
    }

    internal sealed class TestBehaviour : MonoBehaviour, IViewProvider
    {
        public bool TryGetView(Transform subject, out ViewPose pose)
        {
            pose = new ViewPose(Vector3.one, Quaternion.identity, 33f, ViewSource.Override, null);
            return true;
        }
    }

    // Applier(1000)より後(1001)の LateUpdate で ViewCamera を読む(契約どおりの呼び出し位置)。
    [DefaultExecutionOrder(DDriveCutsceneCameraApplier.ExecutionOrder + 1)]
    internal sealed class LateReader : MonoBehaviour
    {
        public bool Found;
        public ViewPose Pose;

        private void LateUpdate()
        {
            Found = ViewCamera.TryGetCurrent(null, out Pose);
        }
    }
}
