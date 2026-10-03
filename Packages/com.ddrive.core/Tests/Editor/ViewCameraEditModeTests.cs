using DDrive.Runtime.Viewing;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [51_tdrive_integration.md] §4.4(FC-3) — Edit Mode(Timeline ウィンドウのスクラブ中は CutsceneEditModeCameraWriter が
    // Camera.main に直接書く)でも ViewCamera は Camera.main の現在の姿勢を ViewSource.MainCamera で返す。
    public class ViewCameraEditModeTests
    {
        private GameObject _go;
        private GameObject[] _disabled;

        [SetUp]
        public void SetUp()
        {
            var list = new System.Collections.Generic.List<GameObject>();
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (cam.CompareTag("MainCamera"))
                {
                    list.Add(cam.gameObject);
                    cam.gameObject.SetActive(false);
                }
            }

            _disabled = list.ToArray();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }

            foreach (var go in _disabled)
            {
                if (go != null)
                {
                    go.SetActive(true);
                }
            }
        }

        [Test]
        public void EditMode_ReturnsCameraMainPose_AsMainCamera_AndFollowsDirectWrites()
        {
            Assert.IsFalse(ViewCamera.TryGetCurrent(null, out _), "カメラ無しは false");

            _go = new GameObject("VcEditCam", typeof(Camera)) { tag = "MainCamera" };
            var cam = _go.GetComponent<Camera>();
            cam.fieldOfView = 55f;

            // スクラブ中のライターが直接書くのと同じ操作。
            _go.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 20f, 30f));
            cam.fieldOfView = 35f;

            Assert.IsTrue(ViewCamera.TryGetCurrent(null, out var pose));
            Assert.AreEqual(ViewSource.MainCamera, pose.Source);
            Assert.AreEqual(_go.transform.position, pose.Position);
            Assert.AreEqual(_go.transform.rotation, pose.Rotation);
            Assert.AreEqual(35f, pose.VerticalFovDegrees);
        }
    }
}
