using DDrive.Editor.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [08_presentation.md] 5-4 追補(2026-09-14) — タイムラインのズーム/パン/目盛り間隔選択(PresentationTimelineZoom)。
    // すべて純粋関数(Unity オブジェクトの状態に依存しない)。ユーザー報告「シークバーでどこにいるか分からない。
    // 拡縮が欲しい」への対応。
    public class PresentationTimelineZoomTests
    {
        // ── 目盛り間隔の自動選択 ──

        [Test]
        public void ChooseTickStep_WideRange_NarrowWidth_PicksCoarsestStep()
        {
            // 60 秒を 300px に収めようとすると、1 目盛りは 1 秒でも 5px しかない → それでも最も粗い候補(1s)を使う。
            var step = PresentationTimelineZoom.ChooseTickStep(60f, 300f);
            Assert.AreEqual(1f, step, 0.0001f);
        }

        [Test]
        public void ChooseTickStep_NarrowRange_WideWidth_PicksFinestStep()
        {
            // 0.5 秒を 900px に収める → 1/60s 刻みでも 1 目盛り 30px 確保できる → 最も細かい候補。
            var step = PresentationTimelineZoom.ChooseTickStep(0.5f, 900f);
            Assert.AreEqual(1f / 60f, step, 0.0001f);
        }

        [Test]
        public void ChooseTickStep_MidRange_PicksMidStep()
        {
            // 5 秒を 600px(120px/s) → 0.1s 刻みで 12px/tick(>=6px) だが 1/60s だと 2px/tick(<6px) なので 0.1s を選ぶ。
            var step = PresentationTimelineZoom.ChooseTickStep(5f, 600f);
            Assert.AreEqual(0.1f, step, 0.0001f);
        }

        [Test]
        public void ChooseTickStep_ZeroOrNegativeInput_ReturnsCoarsestStep_NoThrow()
        {
            Assert.AreEqual(1f, PresentationTimelineZoom.ChooseTickStep(0f, 500f));
            Assert.AreEqual(1f, PresentationTimelineZoom.ChooseTickStep(5f, 0f));
        }

        [Test]
        public void LabelStride_WidePxPerTick_ReturnsOne()
        {
            Assert.AreEqual(1, PresentationTimelineZoom.LabelStride(60f));
        }

        [Test]
        public void LabelStride_NarrowPxPerTick_ReturnsGreaterThanOne()
        {
            var stride = PresentationTimelineZoom.LabelStride(5f);
            Assert.Greater(stride, 1);
        }

        // ── 時刻 ⇔ X 座標 ──

        [Test]
        public void TimeToX_And_XToTime_RoundTrip()
        {
            var bar = new Rect(10f, 0f, 200f, 6f);
            const float rangeStart = 1f;
            const float rangeEnd = 3f;

            var x = PresentationTimelineZoom.TimeToX(bar, rangeStart, rangeEnd, 2f); // 中央
            Assert.AreEqual(bar.x + bar.width * 0.5f, x, 0.01f);

            var t = PresentationTimelineZoom.XToTime(bar, rangeStart, rangeEnd, x);
            Assert.AreEqual(2f, t, 0.01f);
        }

        [Test]
        public void TimeToX_OutOfRange_Clamps()
        {
            var bar = new Rect(0f, 0f, 100f, 6f);
            Assert.AreEqual(bar.x, PresentationTimelineZoom.TimeToX(bar, 0f, 1f, -5f), 0.01f);
            Assert.AreEqual(bar.xMax, PresentationTimelineZoom.TimeToX(bar, 0f, 1f, 5f), 0.01f);
        }

        // ── 表示範囲の操作 ──

        [Test]
        public void Fit_ReturnsFullDuration_ClampedToMinVisibleRange()
        {
            Assert.AreEqual((0f, 5f), PresentationTimelineZoom.Fit(5f));
            Assert.AreEqual((0f, PresentationTimelineZoom.MinVisibleRange), PresentationTimelineZoom.Fit(0f));
        }

        [Test]
        public void ClampRange_WidthWithinBounds_KeptStable()
        {
            var (start, end) = PresentationTimelineZoom.ClampRange(1f, 2f, 10f);
            Assert.AreEqual(1f, start, 0.001f);
            Assert.AreEqual(2f, end, 0.001f);
        }

        [Test]
        public void ClampRange_TooNarrow_ClampedToMinVisibleRange()
        {
            var (start, end) = PresentationTimelineZoom.ClampRange(1f, 1.01f, 10f);
            Assert.AreEqual(PresentationTimelineZoom.MinVisibleRange, end - start, 0.001f);
        }

        [Test]
        public void ClampRange_StartBeforeZero_ClampedToZero()
        {
            var (start, end) = PresentationTimelineZoom.ClampRange(-1f, 1f, 10f);
            Assert.AreEqual(0f, start, 0.001f);
            Assert.AreEqual(2f, end, 0.001f);
        }

        [Test]
        public void ClampRange_EndPastTotal_ShiftedBack()
        {
            var (start, end) = PresentationTimelineZoom.ClampRange(9f, 12f, 10f);
            Assert.AreEqual(10f, end, 0.001f);
            Assert.AreEqual(7f, start, 0.001f); // 幅(3)を保ったまま右端を 10 に揃える
        }

        [Test]
        public void ZoomAroundPivot_KeepsPivotAtSameRelativePosition()
        {
            // [0,10] を中心(5)基準に 2 倍ズーム → 幅 5、pivot(5)は依然中央。
            var (start, end) = PresentationTimelineZoom.ZoomAroundPivot(0f, 10f, 5f, 2f, 20f);
            Assert.AreEqual(5f, end - start, 0.001f);
            Assert.AreEqual(5f, (start + end) * 0.5f, 0.001f);
        }

        [Test]
        public void ZoomAroundPivot_ZoomIn_NarrowsWidth_ZoomOut_WidensWidth()
        {
            var (_, endIn) = PresentationTimelineZoom.ZoomAroundPivot(0f, 10f, 0f, 2f, 20f);
            Assert.Less(endIn, 10f);

            var (startOut, endOut) = PresentationTimelineZoom.ZoomAroundPivot(4f, 6f, 5f, 0.5f, 20f);
            Assert.Greater(endOut - startOut, 2f);
        }

        [Test]
        public void ZoomAroundPivot_NeverNarrowerThanMinVisibleRange()
        {
            var (start, end) = PresentationTimelineZoom.ZoomAroundPivot(0f, 1f, 0.5f, 1000f, 100f);
            Assert.GreaterOrEqual(end - start, PresentationTimelineZoom.MinVisibleRange - 0.0001f);
        }

        [Test]
        public void ZoomAroundPivot_InvalidFactor_NoThrow_ReturnsClampedRange()
        {
            Assert.DoesNotThrow(() => PresentationTimelineZoom.ZoomAroundPivot(0f, 10f, 5f, 0f, 20f));
            Assert.DoesNotThrow(() => PresentationTimelineZoom.ZoomAroundPivot(0f, 10f, 5f, float.NaN, 20f));
        }

        [Test]
        public void WithZoomFactor_And_ZoomFactor_AreApproximateInverses()
        {
            const float displayDuration = 20f;
            var (start, end) = PresentationTimelineZoom.WithZoomFactor(0f, displayDuration, 10f, 4f, displayDuration);

            var factor = PresentationTimelineZoom.ZoomFactor(start, end, displayDuration);
            Assert.AreEqual(4f, factor, 0.01f);
        }

        [Test]
        public void Pan_ShiftsRange_ClampedAtBounds()
        {
            var (start, end) = PresentationTimelineZoom.Pan(0f, 2f, 1f, 10f);
            Assert.AreEqual(1f, start, 0.001f);
            Assert.AreEqual(3f, end, 0.001f);

            // 右端を超えて動かそうとしても総尺の外には出ない。
            var (clampedStart, clampedEnd) = PresentationTimelineZoom.Pan(8f, 10f, 5f, 10f);
            Assert.AreEqual(10f, clampedEnd, 0.001f);
            Assert.AreEqual(8f, clampedStart, 0.001f);
        }

        [Test]
        public void FollowPlayhead_HeadWithinMargins_NoScroll()
        {
            var (start, end) = PresentationTimelineZoom.FollowPlayhead(0f, 10f, 5f, 100f);
            Assert.AreEqual(0f, start, 0.001f);
            Assert.AreEqual(10f, end, 0.001f);
        }

        [Test]
        public void FollowPlayhead_HeadNearRightEdge_ScrollsForward()
        {
            var (start, end) = PresentationTimelineZoom.FollowPlayhead(0f, 10f, 9.9f, 100f, 0.1f);
            Assert.Greater(start, 0f, "右端に迫ったら前方へスクロールする");
            Assert.AreEqual(10f, end - start, 0.001f, "幅は変わらない");
        }

        [Test]
        public void FollowPlayhead_HeadNearLeftEdge_ScrollsBackward()
        {
            var (start, end) = PresentationTimelineZoom.FollowPlayhead(5f, 15f, 5.05f, 100f, 0.1f);
            Assert.Less(start, 5f, "左端に迫ったら後方へスクロールする");
            Assert.AreEqual(10f, end - start, 0.001f);
        }
    }
}
