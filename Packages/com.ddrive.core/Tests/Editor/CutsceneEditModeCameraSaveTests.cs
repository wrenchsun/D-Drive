using DDrive.Editor.Cutscene;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using DDrive.Runtime.CameraShake;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

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
            CutsceneEditModePreviewProvider.ResetTimelineApiStateForTests();
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
        public void IsInspectedBy_NullProperty_ReturnsFalse()
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

        // ---- 実際のシーン保存(sceneSaving / sceneSaved の購読を通す)。追加シーンだけを一時パスへ保存し、作業中のシーンには触れない ----

        private const string TempScenePath = "Assets/__GeSaveTest.unity";

        // 作業中のシーン(カメラはここに作る)を「コピーとして」一時パスへ保存する。シーン自体の保存状態・パスは変わらない
        // (未保存の無題シーンのときは追加シーンを作れないため、既存の保存系テストと同じ saveAsCopy を使う)。
        private string SaveCameraSceneAndReadText(out Scene scene)
        {
            scene = _camGo.scene;
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, TempScenePath, saveAsCopy: true));
            return System.IO.File.ReadAllText(TempScenePath);
        }

        private void CleanupTempScene(Scene scene)
        {
            AssetDatabase.DeleteAsset(TempScenePath);
        }

        [Test]
        public void RealSave_DoesNotStorePreviewPose_AndRewritesAfter()
        {
            CutsceneEditModePreviewProvider.IsInspectedOverrideForTests = d => true;
            CutsceneEditModeCameraWriter.Apply(_directorGo);
            Scene scene = default;
            try
            {
                var text = SaveCameraSceneAndReadText(out scene);
                StringAssert.Contains("m_LocalPosition: {x: 1, y: 2, z: 3}", text);
                StringAssert.DoesNotContain("m_LocalPosition: {x: 10, y: 20, z: 30}", text);
                Assert.AreEqual(PreviewPos, _camGo.transform.position, "保存の後に書き直される");
            }
            finally
            {
                CleanupTempScene(scene);
            }
        }

        [Test]
        public void RealSave_AfterShakeStarted_DoesNotStorePreviewPose()
        {
            // docs/66 GH-R-01: Shake を鳴らした後の保存でも、Shake ドライバの復元が Cutscene の姿勢を書き戻さない。
            CutsceneEditModePreviewProvider.IsInspectedOverrideForTests = d => true;
            CutsceneEditModeCameraWriter.Apply(_directorGo);
            var shake = CutsceneEditModePreviewProvider.EnsureAndGetManagers().ShakeDriver;
            var data = ScriptableObject.CreateInstance<CameraShakeData>();
            Scene scene = default;
            try
            {
                shake.Play(data);
                shake.Tick(0.05f);
                var text = SaveCameraSceneAndReadText(out scene);
                StringAssert.Contains("m_LocalPosition: {x: 1, y: 2, z: 3}", text);
                StringAssert.DoesNotContain("m_LocalPosition: {x: 10, y: 20, z: 30}", text);
            }
            finally
            {
                Object.DestroyImmediate(data);
                CleanupTempScene(scene);
            }
        }

        [Test]
        public void TearDown_AfterShakeStarted_RestoresOriginalPose()
        {
            CutsceneEditModeCameraWriter.Apply(_directorGo);
            var shake = CutsceneEditModePreviewProvider.EnsureAndGetManagers().ShakeDriver;
            var data = ScriptableObject.CreateInstance<CameraShakeData>();
            try
            {
                shake.Play(data);
                shake.Tick(0.05f);
                CutsceneEditModePreviewProvider.TearDownForTests();
                Assert.AreEqual(OriginalPos, _camGo.transform.position, "後始末の後にカットシーンの姿勢が残らない");
                Assert.AreEqual(55f, _cam.fieldOfView, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void SaveOfAnotherScene_DoesNotTouchCamera()
        {
            CutsceneEditModePreviewProvider.IsInspectedOverrideForTests = d => true;
            CutsceneEditModeCameraWriter.Apply(_directorGo);
            // カメラのあるシーンとは別のシーンの保存(default = どのシーンでもない)
            CutsceneEditModePreviewProvider.SuspendCameraForSave(default(Scene));
            Assert.AreEqual(PreviewPos, _camGo.transform.position, "カメラのあるシーンではない保存では戻さない");
        }

        [Test]
        public void SaveFailed_NextApplyRecovers()
        {
            // sceneSaved が来ない(保存の失敗)場合: 次の Apply が書き直す。
            CutsceneEditModeCameraWriter.Apply(_directorGo);
            CutsceneEditModePreviewProvider.SuspendCameraForSave(_camGo.scene);
            Assert.AreEqual(OriginalPos, _camGo.transform.position);

            CutsceneEditModeCameraWriter.Apply(_directorGo);
            Assert.AreEqual(PreviewPos, _camGo.transform.position);

            // その後に別の保存が来ても、また元の姿勢へ戻せる(印が下りている)。
            CutsceneEditModePreviewProvider.SuspendCameraForSave(_camGo.scene);
            Assert.AreEqual(OriginalPos, _camGo.transform.position);
        }

        [Test]
        public void MainCameraChangedMidway_RestoresPreviousCamera()
        {
            // docs/66 GH-R-02
            CutsceneEditModeCameraWriter.Apply(_directorGo);
            Assert.AreEqual(PreviewPos, _camGo.transform.position);

            var second = new GameObject("CamSaveTestCamera2", typeof(Camera));
            try
            {
                _camGo.tag = "Untagged";
                second.tag = "MainCamera";
                second.transform.position = new Vector3(7f, 7f, 7f);

                CutsceneEditModeCameraWriter.Apply(_directorGo);

                Assert.AreEqual(OriginalPos, _camGo.transform.position, "前のカメラはカットシーンの姿勢のまま残らない");
                Assert.AreEqual(PreviewPos, second.transform.position);
            }
            finally
            {
                CutsceneEditModeCameraWriter.ResetCapture();
                Object.DestroyImmediate(second);
            }
        }

        private static int ThrowingCount;

        public static object ThrowingProperty
        {
            get
            {
                ThrowingCount++;
                throw new System.InvalidOperationException("boom");
            }
        }

        [Test]
        public void UnreadableTimelineApi_ReturnsFalse_WarnsOnce_AndStopsReading()
        {
            var director = _directorGo.GetComponent<PlayableDirector>();
            var prop = typeof(CutsceneEditModeCameraSaveTests).GetProperty(nameof(ThrowingProperty));
            ThrowingCount = 0;
            CutsceneEditModePreviewProvider.ResetTimelineApiStateForTests();

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Timeline ウィンドウの状態を取得できない.*InvalidOperationException: boom"));
            Assert.IsFalse(CutsceneEditModePreviewProvider.IsInspectedByOrWarn(prop, null, director));
            Assert.IsFalse(CutsceneEditModePreviewProvider.IsInspectedByOrWarn(prop, null, director), "2 回目も false で、警告は出ない");
            Assert.AreEqual(1, ThrowingCount, "一度読めなかったら以降は読まない");

            // プロパティが無い場合も、警告済みなら出ない。
            Assert.IsFalse(CutsceneEditModePreviewProvider.IsInspectedByOrWarn(null, null, director));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
