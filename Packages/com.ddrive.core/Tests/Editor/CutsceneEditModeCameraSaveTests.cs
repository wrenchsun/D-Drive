using DDrive.Editor.Cutscene;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;

namespace DDrive.Tests.Editor
{
    // docs/63 GE-R-01 / GE-R-02 — Edit Mode プレビューのカメラ書き込みが、保存時に元の姿勢へ戻ること・
    // Timeline の API が解決できないときは書かないことを、Timeline ウィンドウを開かずに確認する。
    public class CutsceneEditModeCameraSaveTests
    {
        private static readonly Vector3 OriginalPos = new Vector3(1f, 2f, 3f);
        private static readonly Vector3 PreviewPos = new Vector3(10f, 20f, 30f);

        private GameObject _camGo;
        private GameObject _directorGo;
        private GameObject[] _disabled;
        private Camera _cam;

        [SetUp]
        public void SetUp()
        {
            var list = new System.Collections.Generic.List<GameObject>();
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c.CompareTag("MainCamera"))
                {
                    list.Add(c.gameObject);
                    c.gameObject.SetActive(false);
                }
            }

            _disabled = list.ToArray();

            _camGo = new GameObject("CamSaveTestCamera", typeof(Camera)) { tag = "MainCamera" };
            _cam = _camGo.GetComponent<Camera>();
            _camGo.transform.SetPositionAndRotation(OriginalPos, Quaternion.identity);
            _cam.fieldOfView = 55f;

            _directorGo = new GameObject("CamSaveTestDirector", typeof(PlayableDirector));
            var holder = _directorGo.AddComponent<CutsceneCameraStateHolder>();
            holder.HasData = true;
            holder.LocalPos = PreviewPos;
            holder.LocalRot = Quaternion.identity;
            holder.Fov = 30f;
            CutsceneEditModePreviewProvider.PrepareContext(_directorGo);
        }

        [TearDown]
        public void TearDown()
        {
            CutsceneEditModePreviewProvider.IsInspectedOverrideForTests = null;
            CutsceneEditModeCameraWriter.ResetCapture();
            CutsceneEditModePreviewProvider.TearDownForTests();
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            foreach (var go in _disabled)
            {
                if (go != null) go.SetActive(true);
            }
        }

        [Test]
        public void Apply_WritesPreviewPose_AndResetCapture_RestoresOriginal()
        {
            CutsceneEditModeCameraWriter.Apply(_directorGo);
            Assert.AreEqual(PreviewPos, _camGo.transform.position);
            Assert.AreEqual(30f, _cam.fieldOfView, 0.001f);

            CutsceneEditModeCameraWriter.ResetCapture();
            Assert.AreEqual(OriginalPos, _camGo.transform.position);
            Assert.AreEqual(55f, _cam.fieldOfView, 0.001f);
        }

        [Test]
        public void SuspendForSave_RestoresOriginalPose_AndResumeRewrites()
        {
            CutsceneEditModePreviewProvider.IsInspectedOverrideForTests = d => d == _directorGo.GetComponent<PlayableDirector>();
            CutsceneEditModeCameraWriter.Apply(_directorGo);
            var scene = _camGo.scene;

            // 保存の直前: シーンに保存される姿勢は元の姿勢。
            CutsceneEditModePreviewProvider.SuspendCameraForSave(scene);
            Assert.AreEqual(OriginalPos, _camGo.transform.position, "保存されるカメラ姿勢にプレビューが残らない");
            Assert.AreEqual(55f, _cam.fieldOfView, 0.001f);

            // 保存の後: まだ Timeline ウィンドウが開いているので書き直す。
            CutsceneEditModePreviewProvider.ResumeCameraAfterSave(scene);
            Assert.AreEqual(PreviewPos, _camGo.transform.position);
            Assert.AreEqual(30f, _cam.fieldOfView, 0.001f);
        }

        [Test]
        public void ResumeAfterSave_WhenWindowClosed_KeepsOriginalPose()
        {
            CutsceneEditModePreviewProvider.IsInspectedOverrideForTests = d => false;
            CutsceneEditModeCameraWriter.Apply(_directorGo);
            var scene = _camGo.scene;

            CutsceneEditModePreviewProvider.SuspendCameraForSave(scene);
            CutsceneEditModePreviewProvider.ResumeCameraAfterSave(scene);

            Assert.AreEqual(OriginalPos, _camGo.transform.position);
            Assert.AreEqual(55f, _cam.fieldOfView, 0.001f);
        }

        [Test]
        public void IsInspectedBy_NullProperty_ReturnsFalse_AndNeverWrites()
        {
            Assert.IsFalse(CutsceneEditModePreviewProvider.IsInspectedBy(null, null, _directorGo.GetComponent<PlayableDirector>()));
        }

        [Test]
        public void RealReflectionPath_ResolvesTimelineApi_AndNoWindowMeansNotInspected()
        {
            Assert.IsTrue(CutsceneEditModePreviewProvider.IsTimelineWindowApiResolved,
                "TimelineEditor.inspectedDirector / masterDirector を reflection で解決できる(Timeline パッケージの版が変わると赤になる)");

            var director = _directorGo.GetComponent<PlayableDirector>();
            // 解決できていれば値を読めて例外にならない。プレビュー用 Director を開いていなければ false。
            Assert.DoesNotThrow(() => CutsceneEditModePreviewProvider.ReadInspectedDirectorForTests());
            Assert.DoesNotThrow(() => CutsceneEditModePreviewProvider.ReadMasterDirectorForTests());
            if (CutsceneEditModePreviewProvider.ReadInspectedDirectorForTests() != director
                && CutsceneEditModePreviewProvider.ReadMasterDirectorForTests() != director)
            {
                Assert.IsFalse(CutsceneEditModePreviewProvider.IsInspectedByTimelineWindow(director));
            }
        }
    }
}
