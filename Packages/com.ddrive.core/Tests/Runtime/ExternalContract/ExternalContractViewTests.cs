using System.Collections;
using System.Collections.Generic;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using DDrive.Runtime.Viewing;
using ExternalPackage.Fake;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ExternalContract.Tests
{
    // [docs/42 §5.14] 外部拡張の契約(視点)。E-18(FC-3、docs/51 §4.4)。
    // 外部アセンブリ(ExternalPackage.Fake)が D-Drive の公開 API だけで IViewProvider を実装・登録・解除でき、
    // ViewCamera が Unity のワールド値を無変換で返すことを固定する。
    public class ExternalContractViewTests
    {
        private readonly List<Object> _cleanup = new();
        private readonly List<GameObject> _disabledMainCameras = new();
        private readonly List<IViewProvider> _registered = new();

        [SetUp]
        public void SetUp()
        {
            ExternalViewProvider.CallOrder.Clear();

            // 他のテストの MainCamera が残っていても Camera.main を決定的にするため、一時的に無効化する。
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

            foreach (var o in _cleanup)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _cleanup.Clear();

            foreach (var go in _disabledMainCameras)
            {
                if (go != null)
                {
                    go.SetActive(true);
                }
            }

            _disabledMainCameras.Clear();
            ExternalViewProvider.CallOrder.Clear();
        }

        private T Own<T>(T o) where T : Object
        {
            _cleanup.Add(o);
            return o;
        }

        private Camera MakeCamera(string name, Vector3 pos, Quaternion rot, float fov, bool main)
        {
            var go = Own(new GameObject(name, typeof(Camera)));
            if (main)
            {
                go.tag = "MainCamera";
            }

            go.transform.SetPositionAndRotation(pos, rot);
            var cam = go.GetComponent<Camera>();
            cam.fieldOfView = fov;
            return cam;
        }

        private ExternalViewProvider Register(string name, int priority, Transform subject, Camera cam)
        {
            var p = new ExternalViewProvider { Name = name, Subject = subject, Camera = cam };
            _registered.Add(p);
            ViewCamera.Register(p, priority);
            return p;
        }

        [Test]
        public void E18_NoCamera_ReturnsFalse_WithoutWarning()
        {
            var ok = ViewCamera.TryGetCurrent(null, out var pose);

            Assert.IsFalse(ok);
            Assert.AreEqual(ViewSource.None, pose.Source);
            Assert.IsNull(pose.Camera);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void E18_MainCamera_ReturnsWorldValuesUnchanged()
        {
            var rot = Quaternion.Euler(12f, 34f, 5f);
            var cam = MakeCamera("E18Main", new Vector3(1.5f, 2.5f, -3.5f), rot, 47f, true);

            Assert.IsTrue(ViewCamera.TryGetCurrent(null, out var pose));

            Assert.AreEqual(ViewSource.MainCamera, pose.Source);
            Assert.AreSame(cam, pose.Camera);
            Assert.AreEqual(cam.transform.position, pose.Position);
            Assert.AreEqual(cam.transform.rotation, pose.Rotation);
            Assert.AreEqual(cam.fieldOfView, pose.VerticalFovDegrees);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void E18_ExternalProvider_ResolvesSplitScreenBySubject_PriorityAndUnregister()
        {
            MakeCamera("E18Main", Vector3.zero, Quaternion.identity, 60f, true);
            var cam1 = MakeCamera("E18P1Cam", new Vector3(10f, 0f, 0f), Quaternion.identity, 40f, false);
            var cam2 = MakeCamera("E18P2Cam", new Vector3(-10f, 0f, 0f), Quaternion.identity, 50f, false);
            var p1 = Own(new GameObject("E18Player1")).transform;
            var p2 = Own(new GameObject("E18Player2")).transform;
            var other = Own(new GameObject("E18Other")).transform;

            var prov1 = Register("p1", 0, p1, cam1);
            var prov2 = Register("p2", 0, p2, cam2);

            Assert.IsTrue(ViewCamera.TryGetCurrent(p1, out var pose1));
            Assert.AreEqual(ViewSource.Override, pose1.Source);
            Assert.AreEqual(cam1.transform.position, pose1.Position);
            Assert.AreEqual(40f, pose1.VerticalFovDegrees);

            Assert.IsTrue(ViewCamera.TryGetCurrent(p2, out var pose2));
            Assert.AreEqual(cam2.transform.position, pose2.Position);

            // どのプロバイダも担当しない subject は Camera.main へ。
            Assert.IsTrue(ViewCamera.TryGetCurrent(other, out var poseOther));
            Assert.AreEqual(ViewSource.MainCamera, poseOther.Source);

            // 優先度: 高いほうが先に問い合わせられる(両方が担当する subject = null)。
            ViewCamera.Unregister(prov1);
            ViewCamera.Unregister(prov2);
            ExternalViewProvider.CallOrder.Clear();
            var low = Register("low", 0, null, cam1);
            var high = Register("high", 5, null, cam2);
            Assert.IsTrue(ViewCamera.TryGetCurrent(null, out var poseHigh));
            Assert.AreEqual(50f, poseHigh.VerticalFovDegrees, "優先度の高い high(cam2)が先に答える");
            Assert.AreEqual("high", ExternalViewProvider.CallOrder[0]);

            // Unregister で外れる。
            ViewCamera.Unregister(high);
            Assert.IsTrue(ViewCamera.TryGetCurrent(null, out var poseLow));
            Assert.AreEqual(40f, poseLow.VerticalFovDegrees);
            ViewCamera.Unregister(low);
            Assert.IsTrue(ViewCamera.TryGetCurrent(null, out var poseMain));
            Assert.AreEqual(ViewSource.MainCamera, poseMain.Source);
        }

        [Test]
        public void E18_TryGetCurrent_AllocatesNothing()
        {
            MakeCamera("E18Main", Vector3.up, Quaternion.identity, 60f, true);
            var cam = MakeCamera("E18Alloc", Vector3.zero, Quaternion.identity, 30f, false);
            var subject = Own(new GameObject("E18Subject")).transform;
            var other = Own(new GameObject("E18Other")).transform;
            Register("alloc", 0, subject, cam);

            // 暖機(JIT・初回の Camera.main キャッシュ)
            ViewCamera.TryGetCurrent(subject, out _);
            ViewCamera.TryGetCurrent(other, out _);

            var before = System.GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
            {
                ViewCamera.TryGetCurrent(subject, out _); // プロバイダが答える
                ViewCamera.TryGetCurrent(other, out _);   // Camera.main へ進む
            }

            var allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, "TryGetCurrent は定常経路で割り当てない");
        }

        [UnityTest]
        public IEnumerator E18_CutsceneOwnsCamera_ReturnsCutSource_FromLaterLateUpdate()
        {
            var cam = MakeCamera("E18Cut", Vector3.zero, Quaternion.identity, 60f, true);
            var reader = cam.gameObject.AddComponent<ExternalLateViewReader>();
            var applier = DDriveCutsceneCameraApplier.EnsureOn(cam);

            // カットシーンが無いときは MainCamera。
            yield return null;
            Assert.IsTrue(reader.Found);
            Assert.AreEqual(ViewSource.MainCamera, reader.Pose.Source);
            Assert.IsFalse(applier.IsDriving);

            var req = new CutsceneCameraWriteRequest
            {
                WorldPos = new Vector3(4f, 5f, 6f),
                WorldRot = Quaternion.Euler(0f, 90f, 0f),
                Fov = 25f,
                Weight = 1f,
                Focus = CameraFocusMode.Off,
            };
            Assert.IsTrue(applier.Submit(in req, "E18"));
            yield return null; // この LateUpdate で Applier(1000)→ Reader(1001)の順に走る

            Assert.IsTrue(applier.IsDriving);
            Assert.IsTrue(reader.Found);
            Assert.AreEqual(ViewSource.Cutscene, reader.Pose.Source);
            Assert.AreEqual(cam.transform.position, reader.Pose.Position);
            Assert.AreEqual(cam.transform.rotation, reader.Pose.Rotation);
            Assert.AreEqual(cam.fieldOfView, reader.Pose.VerticalFovDegrees, 1e-4f);
            Assert.AreEqual(4f, reader.Pose.Position.x, 1e-4f, "そのフレームのカット姿勢が返る(実行順 1000 より後の LateUpdate)");
            Assert.AreEqual(25f, reader.Pose.VerticalFovDegrees, 1e-4f);

            // 所有権が終わったら MainCamera に戻る。
            applier.Restore();
            yield return null;
            Assert.AreEqual(ViewSource.MainCamera, reader.Pose.Source);
        }
    }
}
