using System.Collections.Generic;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Tests.Editor
{
    // [26_timeline.md] §4.4(Edit Mode プレビュー、2026-09-19) — `CutsceneMarkerCursor<T>` は
    // `CutsceneManager.CollectMarkers`/`AdvanceMarkers`(6-10b)と同じ「跨いだら発火、Seek は無音で
    // スキップ」パターンを、Edit Mode の監視役(`CutsceneEditModePreviewProvider`)と共用するために
    // 切り出したユーティリティ。Timeline ウィンドウを起動せずに、TimelineAsset + Marker の生成だけで
    // ロジックを検証できる(ウィンドウを起動しない EditMode テスト)。
    public class CutsceneMarkerCursorTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _created.Clear();
        }

        private TimelineAsset CreateTimeline()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _created.Add(timeline);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 2.0;
            return timeline;
        }

        [Test]
        public void Collect_OnlyMatchesRequestedMarkerType_AndSortsByTime()
        {
            var timeline = CreateTimeline();
            var signalTrack = timeline.CreateTrack<CutsceneSignalTrack>(null, "Signal");
            var m2 = signalTrack.CreateMarker<CutsceneSignalNotification>(0.5);
            m2.Key = "second";
            var m1 = signalTrack.CreateMarker<CutsceneSignalNotification>(0.1);
            m1.Key = "first";

            // 別トラック/別マーカー型は混ざらないこと。
            var eventTrack = timeline.CreateTrack<CutsceneEventTrack>(null, "Event");
            eventTrack.CreateMarker<CutsceneEventNotification>(0.2);

            var cursor = new CutsceneMarkerCursor<CutsceneSignalNotification>();
            cursor.Collect(timeline);

            Assert.AreEqual(2, cursor.Count);

            var order = new List<string>();
            cursor.Advance(2.0, fire: true, marker => order.Add(marker.Key));
            CollectionAssert.AreEqual(new[] { "first", "second" }, order);
        }

        [Test]
        public void Advance_FiresOnlyWhenCrossed_AndDoesNotRefireOnSameElapsed()
        {
            var timeline = CreateTimeline();
            var track = timeline.CreateTrack<CutsceneSignalTrack>(null, "Signal");
            var marker = track.CreateMarker<CutsceneSignalNotification>(0.5);
            marker.Key = "hit";

            var cursor = new CutsceneMarkerCursor<CutsceneSignalNotification>();
            cursor.Collect(timeline);

            var fired = new List<string>();
            cursor.Advance(0.3, fire: true, m => fired.Add(m.Key));
            CollectionAssert.IsEmpty(fired, "0.5 秒より前では発火しない");

            cursor.Advance(0.6, fire: true, m => fired.Add(m.Key));
            CollectionAssert.AreEqual(new[] { "hit" }, fired);

            cursor.Advance(1.0, fire: true, m => fired.Add(m.Key));
            CollectionAssert.AreEqual(new[] { "hit" }, fired, "跨いだ後は同じマーカーを再発火しない");
        }

        [Test]
        public void Advance_FireFalse_AdvancesCursorSilently()
        {
            var timeline = CreateTimeline();
            var track = timeline.CreateTrack<CutsceneSignalTrack>(null, "Signal");
            var marker = track.CreateMarker<CutsceneSignalNotification>(0.5);
            marker.Key = "hit";

            var cursor = new CutsceneMarkerCursor<CutsceneSignalNotification>();
            cursor.Collect(timeline);

            var fired = new List<string>();
            // fire=false(スクラブ/巻き戻し相当): カーソルは進むが action は呼ばれない。
            cursor.Advance(1.0, fire: false, m => fired.Add(m.Key));
            CollectionAssert.IsEmpty(fired);
            Assert.AreEqual(1, cursor.Cursor);

            // 同じ点をもう一度 fire=true で通っても、既にカーソルが進んでいるので再発火しない
            // ([26] §4.4「スクラブで同じ点を何度も通っても連打しない」)。
            cursor.Advance(1.0, fire: true, m => fired.Add(m.Key));
            CollectionAssert.IsEmpty(fired);
        }

        [Test]
        public void ResetCursor_AllowsRefireAfterRewind()
        {
            var timeline = CreateTimeline();
            var track = timeline.CreateTrack<CutsceneSignalTrack>(null, "Signal");
            var marker = track.CreateMarker<CutsceneSignalNotification>(0.5);
            marker.Key = "hit";

            var cursor = new CutsceneMarkerCursor<CutsceneSignalNotification>();
            cursor.Collect(timeline);

            var fired = new List<string>();
            cursor.Advance(1.0, fire: true, m => fired.Add(m.Key));
            CollectionAssert.AreEqual(new[] { "hit" }, fired);

            // 巻き戻し(スクラブで戻した)を検出した側(CutsceneEditModePreviewProvider)が
            // ResetCursor() + 無音 Advance で「現在地点より前は発火済み」の状態を作り直す。
            cursor.ResetCursor();
            cursor.Advance(0.0, fire: false, null);
            fired.Clear();

            cursor.Advance(1.0, fire: true, m => fired.Add(m.Key));
            CollectionAssert.AreEqual(new[] { "hit" }, fired, "巻き戻し後に再び前進すれば再発火する");
        }
    }
}
