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

        // E-20(FY-R-05): 途中(2.0 秒)から再生を始めたとき、再生を始める位置(2.0)ちょうどは無音、最初の更新までに進んだ区間
        // (2.0 より後・最初の更新の位置以内)のマーカーは発火する。
        [Test]
        public void E20_EditPreview_ScrubThenPlay_FiresMarkersPassedBeforeTheFirstUpdate()
        {
            var timeline = ReplaceTimeline(false, 1.0, 2.0, 2.01, 2.5);
            try
            {
                _director.Pause();
                _director.time = 0.0;
                Update();
                _director.time = 2.0;
                Update();
                Assert.AreEqual(0, ExternalFireMarker.Calls.Count, "スクラブは無音");

                _director.Play();
                _director.time = 2.03; // 最初の更新までに少し進んだ
                Update();

                Assert.AreEqual(1, ExternalFireMarker.Calls.Count, "開始位置(2.0)ちょうどまでは無音、その後の 2.01 は発火");
                Assert.AreEqual(2.01, ExternalFireMarker.Calls[0].MarkerTime, 1e-6);

                Update();
                Assert.AreEqual(1, ExternalFireMarker.Calls.Count, "二重発火しない");

                _director.time = 2.6;
                Update();
                Assert.AreEqual(2, ExternalFireMarker.Calls.Count);
                Assert.AreEqual(2.5, ExternalFireMarker.Calls[1].MarkerTime, 1e-6);
            }
            finally
            {
                _director.playableAsset = null;
                Object.DestroyImmediate(timeline);
            }
        }

        // E-20(FY-R-05): 一時停止からの再開も同じ(再開の最初の更新までに進んだ区間のマーカーを飛ばさない)。
        [Test]
        public void E20_EditPreview_ResumeFromPause_FiresMarkersPassedBeforeTheFirstUpdate()
        {
            var timeline = ReplaceTimeline(false, 1.0, 1.51, 3.0);
            try
            {
                _director.time = 0.0;
                _director.Play();
                Update();
                _director.time = 1.5;
                Update();
                Assert.AreEqual(1, ExternalFireMarker.Calls.Count, "1.0 秒は再生中に発火");

                _director.Pause();
                Update(); // 停止中の位置 1.5 を記録
                _director.Play();
                _director.time = 1.52; // 再開の最初の更新までに進んだ
                Update();

                Assert.AreEqual(2, ExternalFireMarker.Calls.Count, "再開直後に跨いだ 1.51 も発火する");
                Assert.AreEqual(1.51, ExternalFireMarker.Calls[1].MarkerTime, 1e-6);
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

        // 実際の経路(「▶ Timeline ウィンドウで開く」)の Director で発火する。CutsceneEditModeDirectorSetup.EnsureDirector が作る
        // プレビュー用 Director は HideFlags.DontSave で、Object.FindObjectsByType では見つからない。検索だけに頼ると
        // プレビューが何も駆動しない(マーカーが 1 つも発火しない)ため、PrepareContext で用意した Context を直接駆動する。
        [Test]
        public void E20_EditPreview_DontSaveDirectorFromSetup_Fires()
        {
            var cutscene = ScriptableObject.CreateInstance<CutsceneData>();
            cutscene.Timeline = _timeline;
            PlayableDirector director = null;
            try
            {
                director = CutsceneEditModeDirectorSetup.EnsureDirector(cutscene);
                Assert.IsNotNull(director);
                Assert.AreNotEqual(HideFlags.None, director.gameObject.hideFlags & HideFlags.DontSave,
                    "プレビュー用 Director は DontSave で作られる前提(変わったらこのテストの意味を見直す)");
                Assert.AreEqual(0, Object.FindObjectsByType<CutsceneDirectorContext>(FindObjectsSortMode.None).Length - 1,
                    "DontSave の Director は検索に出ない(出るのは SetUp の Director の 1 件だけ)");

                // SetUp の Director は止めたままにして、プレビュー用 Director だけを再生する。
                _director.Pause();
                director.extrapolationMode = DirectorWrapMode.Hold;
                CutsceneEditModePreviewProvider.PrepareContext(director.gameObject);

                director.time = 0.0;
                director.Play();
                Update();
                Assert.AreEqual(0, ExternalFireMarker.Calls.Count);

                director.time = 1.2;
                Update();
                Assert.AreEqual(1, ExternalFireMarker.Calls.Count, "DontSave の Director でも、時刻を跨いだマーカーが発火する");
                Assert.IsTrue(ExternalFireMarker.Calls[0].IsEditPreview);
                Assert.AreEqual(1.0, ExternalFireMarker.Calls[0].MarkerTime, 1e-6);

                var context = director.GetComponent<CutsceneDirectorContext>();
                Assert.IsNotNull(context.ManagerRefs, "クリップ(SE / VFX / UI)に渡す Manager の参照が入っている");
                Assert.IsTrue(context.FireEnabled, "再生中は FireEnabled が立つ");
            }
            finally
            {
                CutsceneEditModeDirectorSetup.TearDown();
                Object.DestroyImmediate(cutscene);
            }
        }
    }
}
