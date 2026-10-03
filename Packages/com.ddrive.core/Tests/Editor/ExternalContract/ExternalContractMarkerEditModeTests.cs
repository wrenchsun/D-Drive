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
            // private メソッドをリフレクションで呼ぶ(docs/51 §4.5-5)。改名で黙って落ちないよう、理由を出して失敗させる(FC-R-21)。
            Assert.IsNotNull(OnEditorUpdate, "CutsceneEditModePreviewProvider.OnEditorUpdate(private static)が見つかりません。改名された場合はこのテストの MethodInfo を直してください");
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

        private TimelineAsset ReplaceTimeline(bool markerTrack, params double[] times)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 10.0;
            UnityEngine.Timeline.TrackAsset track;
            if (markerTrack)
            {
                timeline.CreateMarkerTrack();
                track = timeline.markerTrack;
            }
            else
            {
                track = timeline.CreateTrack<ExternalProbeTrack>(null, "ExtMarkers");
            }

            foreach (var t in times)
            {
                track.CreateMarker<ExternalFireMarker>(t);
            }

            _director.playableAsset = timeline;
            return timeline;
        }

        // E-20(FC-R-03): 先頭(0 秒)からのプレビュー再生では、ちょうど 0 秒のマーカーも発火する。二重発火しない。
        [Test]
        public void E20_EditPreview_PlayFromStart_FiresMarkerAtZero_Once()
        {
            var timeline = ReplaceTimeline(false, 0.0, 1.0);
            try
            {
                _director.time = 0.0;
                _director.Play();
                Update();
                Assert.AreEqual(1, ExternalFireMarker.Calls.Count, "先頭からの再生では 0 秒のマーカーも発火する");
                Assert.AreEqual(0.0, ExternalFireMarker.Calls[0].MarkerTime, 1e-6);

                Update();
                Assert.AreEqual(1, ExternalFireMarker.Calls.Count, "二重発火しない");

                _director.time = 1.2;
                Update();
                Assert.AreEqual(2, ExternalFireMarker.Calls.Count);
            }
            finally
            {
                _director.playableAsset = null;
                Object.DestroyImmediate(timeline);
            }
        }

        // E-20(FC-R-03): スクラブ(非再生)では発火しない。途中から再生を始めたとき、開始位置ちょうどのマーカーも無音。
        [Test]
        public void E20_EditPreview_ScrubToMarkerTime_ThenPlay_IsSilentAtStartPosition()
        {
            var timeline = ReplaceTimeline(false, 0.0, 1.0, 2.0);
            try
            {
                _director.Pause();
                _director.time = 0.0;
                Update();
                _director.time = 1.0;
                Update();
                Assert.AreEqual(0, ExternalFireMarker.Calls.Count, "スクラブは 0 秒も 1.0 秒も無音");

                _director.Play();
                Update();
                Assert.AreEqual(0, ExternalFireMarker.Calls.Count, "途中から再生: 開始位置ちょうどの 1.0 も無音");

                _director.time = 2.2;
                Update();
                Assert.AreEqual(1, ExternalFireMarker.Calls.Count);
                Assert.AreEqual(2.0, ExternalFireMarker.Calls[0].MarkerTime, 1e-6);
            }
            finally
            {
                _director.playableAsset = null;
                Object.DestroyImmediate(timeline);
            }
        }

        // E-20(FX-R-04): 「先頭からの再生か」は更新の間隔や再生開始後の最初の進みの大きさに依存しない(再生を始める直前の位置で決まる)。
        // 停止中に 0 にいて再生を始めたなら、最初の更新で director.time が大きく進んでいても(エディタの引っ掛かり等)先頭からの再生。
        [Test]
        public void E20_EditPreview_PlayFromZero_IsFromStart_EvenIfFirstUpdateAdvancesALot()
        {
            var timeline = ReplaceTimeline(false, 0.0, 0.3, 2.0);
            try
            {
                _director.Pause();
                _director.time = 0.0;
                Update(); // 停止中の位置 = 0 を記録
                Update();
                Assert.AreEqual(0, ExternalFireMarker.Calls.Count, "停止中は無音");

                _director.Play();
                _director.time = 0.5; // 最初の更新までに大きく進んだ
                Update();

                Assert.AreEqual(2, ExternalFireMarker.Calls.Count, "先頭からの再生: 0 秒と 0.3 秒(開始から過ぎた分)が発火する");
                Assert.AreEqual(0.0, ExternalFireMarker.Calls[0].MarkerTime, 1e-6);
                Assert.AreEqual(0.3, ExternalFireMarker.Calls[1].MarkerTime, 1e-6);
            }
            finally
            {
                _director.playableAsset = null;
                Object.DestroyImmediate(timeline);
            }
        }

        // E-20(FX-R-04): 0.05 秒付近にスクラブしてから再生した場合は途中からの再生。0 秒のマーカーも、
        // 0.1 秒以内の位置にあるマーカー(0.04 秒)も無音。再生開始後の最初の進みが小さくても関係ない。
        [Test]
        public void E20_EditPreview_ScrubNearZero_ThenPlay_IsFromMiddle_Silent()
        {
            var timeline = ReplaceTimeline(false, 0.0, 0.04, 1.0);
            try
            {
                _director.Pause();
                _director.time = 0.0;
                Update();
                _director.time = 0.05; // 0.1 秒以内へスクラブ
                Update();
                Assert.AreEqual(0, ExternalFireMarker.Calls.Count, "スクラブは無音");

                _director.Play();
                _director.time = 0.06;
                Update();
                Assert.AreEqual(0, ExternalFireMarker.Calls.Count, "途中からの再生: 開始位置までの 0 秒・0.04 秒は無音");

                _director.time = 1.1;
                Update();
                Assert.AreEqual(1, ExternalFireMarker.Calls.Count);
                Assert.AreEqual(1.0, ExternalFireMarker.Calls[0].MarkerTime, 1e-6);
            }
            finally
            {
                _director.playableAsset = null;
                Object.DestroyImmediate(timeline);
            }
        }

        // E-20(FC-R-04): Timeline 上端のマーカー領域(markerTrack)に置いた外部マーカーも Edit Mode のプレビューで発火する。
        [Test]
        public void E20_EditPreview_MarkerOnTimelineMarkerTrack_Fires()
        {
            var timeline = ReplaceTimeline(true, 1.0);
            try
            {
                _director.time = 0.0;
                _director.Play();
                Update();
                _director.time = 1.2;
                Update();
                Assert.AreEqual(1, ExternalFireMarker.Calls.Count);
                Assert.AreEqual(1.0, ExternalFireMarker.Calls[0].MarkerTime, 1e-6);
            }
            finally
            {
                _director.playableAsset = null;
                Object.DestroyImmediate(timeline);
            }
        }
    }
}
