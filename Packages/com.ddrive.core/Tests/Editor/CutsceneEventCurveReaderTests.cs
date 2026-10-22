using System.Collections.Generic;
using System.Linq;
using DDrive.Editor.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Tests.Editor
{
    // 1017 TA-3(DD-2 + DD-9) — ddEvent の立ち上がり検出(純ロジック)と Events (FBX) トラックの作り直し。
    public class CutsceneEventCurveReaderTests
    {
        private const float Fps = 30f;

        // 整数のステップキー(Constant 接線)。values[i] はフレーム i の値。
        private static AnimationCurve StepCurve(params float[] values)
        {
            var curve = new AnimationCurve();
            for (var i = 0; i < values.Length; i++)
            {
                curve.AddKey(new Keyframe(i / Fps, values[i], 0f, 0f));
            }

            for (var i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
            }

            return curve;
        }

        [Test]
        public void FindRisingEdges_SingleRise()
        {
            var edges = CutsceneEventCurveReader.FindRisingEdges(StepCurve(0, 0, 0, 1, 1, 1), Fps);
            Assert.AreEqual(1, edges.Count);
            Assert.AreEqual(3 / Fps, edges[0], 1e-4f);
        }

        [Test]
        public void FindRisingEdges_StartsHigh_FirstFrameCounts()
        {
            var edges = CutsceneEventCurveReader.FindRisingEdges(StepCurve(1, 1, 0, 0), Fps);
            Assert.AreEqual(1, edges.Count);
            Assert.AreEqual(0f, edges[0], 1e-4f, "先頭キーが既に 1 なら先頭フレームがイベント(DD-9)");
        }

        [Test]
        public void FindRisingEdges_TwoPulses()
        {
            var edges = CutsceneEventCurveReader.FindRisingEdges(StepCurve(0, 1, 1, 0, 0, 1, 0), Fps);
            Assert.AreEqual(2, edges.Count);
            Assert.AreEqual(1 / Fps, edges[0], 1e-4f);
            Assert.AreEqual(5 / Fps, edges[1], 1e-4f);
        }

        [Test]
        public void FindRisingEdges_AllZero_OrEmpty_ReturnsNone()
        {
            Assert.AreEqual(0, CutsceneEventCurveReader.FindRisingEdges(StepCurve(0, 0, 0), Fps).Count);
            Assert.AreEqual(0, CutsceneEventCurveReader.FindRisingEdges(new AnimationCurve(), Fps).Count);
            Assert.AreEqual(0, CutsceneEventCurveReader.FindRisingEdges(null, Fps).Count);
        }

        // ── CutsceneShotParser.IsEventLocator ──

        [TestCase("EVT_Hit", true, "Hit")]
        [TestCase("ns:EVT_Hit", true, "Hit")]
        [TestCase("EVT_Hit1", true, "Hit1")]
        [TestCase("EVT_", false, null)]
        [TestCase("PRP_Mic", false, null)]
        [TestCase("evt_hit", false, null)]
        [TestCase("", false, null)]
        public void IsEventLocator_Table(string name, bool expected, string expectedKey)
        {
            Assert.AreEqual(expected, CutsceneShotParser.IsEventLocator(name, out var key));
            Assert.AreEqual(expectedKey, key);
        }

        // ── Events (FBX) トラック ──

        private static AnimationClip BuildEventClip(string path, params float[] values)
        {
            var clip = new AnimationClip { frameRate = Fps };
            // ユーザープロパティ(型は Transform に見立てる。読み取りは型を見ない)。
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "ddEvent"), StepCurve(values));
            return clip;
        }

        [Test]
        public void BuildEventMarkers_RebuildsOwnTrack_AndLeavesOtherSignalTracks()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var profile = ScriptableObject.CreateInstance<CutsceneImportProfile>();
            var clip = BuildEventClip("EVT_Hit", 0, 0, 1, 1, 0);
            var nodes = new List<CutsceneImportService.CutsceneEventNode> { new() { Path = "EVT_Hit", Key = "Hit" } };
            try
            {
                var manual = timeline.CreateTrack<CutsceneSignalTrack>(null, "Manual");
                manual.CreateMarker<CutsceneSignalNotification>(1.0).Key = "cutscene/manual";

                CutsceneImportService.BuildEventMarkers(timeline, clip, nodes, profile, null);
                CutsceneImportService.BuildEventMarkers(timeline, clip, nodes, profile, null); // 再取り込み

                var events = timeline.GetRootTracks().OfType<CutsceneSignalTrack>().Single(t => t.name == CutsceneImportService.EventTrackName);
                var markers = events.GetMarkers().OfType<CutsceneSignalNotification>().ToArray();
                Assert.AreEqual(1, markers.Length, "再取り込みでマーカーが重複しない");
                Assert.AreEqual("cutscene/Hit", markers[0].Key);
                Assert.AreEqual(2 / Fps, markers[0].time, 1e-4);

                Assert.AreEqual(1, manual.GetMarkers().Count(), "手で置いたマーカー(別トラック)は残る");
            }
            finally
            {
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void BuildEventMarkers_NoCurve_LogsInfoAndCreatesNoTrack()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var profile = ScriptableObject.CreateInstance<CutsceneImportProfile>();
            var clip = new AnimationClip { frameRate = Fps };
            var nodes = new List<CutsceneImportService.CutsceneEventNode> { new() { Path = "EVT_Hit", Key = "Hit" } };
            var logs = new List<string>();
            try
            {
                CutsceneImportService.BuildEventMarkers(timeline, clip, nodes, profile, logs.Add);
                Assert.AreEqual(1, logs.Count);
                StringAssert.Contains("EVT_Hit", logs[0]);
                Assert.IsFalse(timeline.GetRootTracks().Any());
            }
            finally
            {
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(clip);
            }
        }
    }
}
