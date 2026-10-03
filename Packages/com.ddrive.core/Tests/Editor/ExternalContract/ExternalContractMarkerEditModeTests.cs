using System.Reflection;
using DDrive.Editor.Cutscene;
using DDrive.Runtime.Cutscene;
using ExternalPackage.Fake;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ExternalContract.Tests.Editor
{
    // [docs/42 §5.14] 外部拡張の契約(マーカー、Edit Mode)。E-20(FC-4、docs/51 §4.5)。
    // Edit Mode のプレビュー(CutsceneEditModePreviewProvider)が、Play Mode と同じ規則で
    // 外部の ICutsceneMarker を呼ぶ: IsEditPreview = true・スクラブ(非再生)では呼ばない・巻き戻しは無音。
    // プレビューの更新(EditorApplication.update に登録された private な OnEditorUpdate)を 1 回ずつ手で進める。
    public class ExternalContractMarkerEditModeTests
    {
        private static readonly MethodInfo OnEditorUpdate = typeof(CutsceneEditModePreviewProvider)
            .GetMethod("OnEditorUpdate", BindingFlags.NonPublic | BindingFlags.Static);

        private GameObject _go;
        private TimelineAsset _timeline;
        private PlayableDirector _director;

        [SetUp]
        public void SetUp()
        {
            ExternalFireMarker.ClearCalls();
            _timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            _timeline.fixedDuration = 10.0;
            var track = _timeline.CreateTrack<ExternalProbeTrack>(null, "ExtMarkers");
            track.CreateMarker<ExternalFireMarker>(1.0);
            track.CreateMarker<ExternalProbeMarker>(1.5);
            track.CreateMarker<ExternalFireMarker>(3.0);

            _go = new GameObject("ExternalContractMarkerEditMode");
            _director = _go.AddComponent<PlayableDirector>();
            _director.playOnAwake = false;
            _director.extrapolationMode = DirectorWrapMode.Hold;
            _director.playableAsset = _timeline;
            CutsceneEditModePreviewProvider.PrepareContext(_go);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }

            if (_timeline != null)
            {
                Object.DestroyImmediate(_timeline);
            }

            CutsceneEditModePreviewProvider.TearDownForTests();
            ExternalFireMarker.ClearCalls();
        }

        private void Update() => OnEditorUpdate.Invoke(null, null);

        // E-20: 再生中に時刻を跨ぐと 1 回だけ Fire(IsEditPreview = true・Director あり・Handle は無効)。
        [Test]
        public void E20_EditPreview_Playing_FiresOnceWithIsEditPreview()
        {
            _director.time = 0.0;
            _director.Play();
            Update();
            Assert.AreEqual(0, ExternalFireMarker.Calls.Count);

            _director.time = 1.2;
            Update();
            Assert.AreEqual(1, ExternalFireMarker.Calls.Count);
            var call = ExternalFireMarker.Calls[0];
            Assert.IsTrue(call.IsEditPreview);
            Assert.IsTrue(call.HasDirector);
            Assert.IsFalse(call.HasHandle, "Edit Mode のプレビューにカットシーンのハンドルは無い");
            Assert.AreEqual(1.0, call.MarkerTime, 1e-6);
            Assert.AreEqual(1.2, call.Elapsed, 1e-6);

            Update();
            Assert.AreEqual(1, ExternalFireMarker.Calls.Count, "同じマーカーは連打しない");
        }

        // E-20: スクラブ(非再生)では呼ばない。スクラブしたあとの再生開始でも、通り過ぎたマーカーは呼ばない。
        [Test]
        public void E20_EditPreview_Scrub_IsSilent_AndPlayStartDoesNotRefire()
        {
            _director.Pause();
            _director.time = 0.0;
            Update();

            _director.time = 3.5;
            Update();
            Assert.AreEqual(0, ExternalFireMarker.Calls.Count, "スクラブ(非再生)は無音");

            _director.Play();
            Update();
            Assert.AreEqual(0, ExternalFireMarker.Calls.Count, "再生開始の立ち上がりで通過済みを誤発火しない");

            _director.time = 3.6;
            Update();
            Assert.AreEqual(0, ExternalFireMarker.Calls.Count, "通過済みの 1.0 / 3.0 は呼ばれない");
        }

        // E-20: 再生中に巻き戻すと無音で追いつかせる(巻き戻し自体では呼ばない)。
        [Test]
        public void E20_EditPreview_Rewind_IsSilent()
        {
            _director.time = 0.0;
            _director.Play();
            Update();

            _director.time = 3.5;
            Update();
            Assert.AreEqual(2, ExternalFireMarker.Calls.Count);

            _director.time = 0.5;
            Update();
            Assert.AreEqual(2, ExternalFireMarker.Calls.Count, "巻き戻しは無音");
        }
    }
}
