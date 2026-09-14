using DDrive.Editor.Presentation;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [08_presentation.md] 5-4 追補(2026-09-14) — 統合プレビューの「▶ 再生」ボタンの挙動判定・シークスライダー
    // 追従・巻き戻し検出(PresentationPreviewPlayback)。ユーザー報告「一時停止から再生すると最初から再生に
    // なっている」への対応。すべて純粋関数(ウィンドウ/ScenePresentationPreviewDriver を起動せずテストできる)。
    public class PresentationPreviewPlaybackTests
    {
        // ── DecideOnPlay(「▶ 再生」を押したときの挙動) ──

        [Test]
        public void DecideOnPlay_Stopped_StartsFresh()
        {
            Assert.AreEqual(PresentationPreviewPlayback.PlayAction.StartFresh,
                PresentationPreviewPlayback.DecideOnPlay(previewIsPlaying: false, windowPaused: false));
        }

        [Test]
        public void DecideOnPlay_PlayingNotPaused_StartsFresh()
        {
            // 再生中に「▶ 再生」が連打された場合は従来どおり最初から(バグ修正の対象外)。
            Assert.AreEqual(PresentationPreviewPlayback.PlayAction.StartFresh,
                PresentationPreviewPlayback.DecideOnPlay(previewIsPlaying: true, windowPaused: false));
        }

        [Test]
        public void DecideOnPlay_PlayingAndPaused_Resumes()
        {
            // バグ修正: 以前は一時停止中でも常に StartFresh していた。
            Assert.AreEqual(PresentationPreviewPlayback.PlayAction.Resume,
                PresentationPreviewPlayback.DecideOnPlay(previewIsPlaying: true, windowPaused: true));
        }

        [Test]
        public void DecideOnPlay_NotPlayingButPausedFlagStale_StartsFresh()
        {
            // 通常起こらない組み合わせ(Stop() が _paused を戻す)だが、安全側(最初から)に倒れることを確認する。
            Assert.AreEqual(PresentationPreviewPlayback.PlayAction.StartFresh,
                PresentationPreviewPlayback.DecideOnPlay(previewIsPlaying: false, windowPaused: true));
        }

        // ── ComputeSeekSliderValue(シークスライダーの追従) ──

        [Test]
        public void ComputeSeekSliderValue_NotPlaying_ReturnsZero()
        {
            Assert.AreEqual(0f, PresentationPreviewPlayback.ComputeSeekSliderValue(false, 0.75f));
        }

        [Test]
        public void ComputeSeekSliderValue_Playing_ReturnsNormalizedTime()
        {
            Assert.AreEqual(0.42f, PresentationPreviewPlayback.ComputeSeekSliderValue(true, 0.42f), 0.001f);
        }

        [Test]
        public void ComputeSeekSliderValue_Playing_ClampsOutOfRangeInput()
        {
            // NormalizedTime は無効な Handle だと -1 を返す実装(ScenePresentationPreviewDriver)なので、
            // その場合でも 0..1 に収める。
            Assert.AreEqual(0f, PresentationPreviewPlayback.ComputeSeekSliderValue(true, -1f));
            Assert.AreEqual(1f, PresentationPreviewPlayback.ComputeSeekSliderValue(true, 1.5f));
        }

        // ── IsRewind(巻き戻し検出) ──

        [Test]
        public void IsRewind_TargetBeforePrevious_ReturnsTrue()
        {
            Assert.IsTrue(PresentationPreviewPlayback.IsRewind(2f, 0.5f));
        }

        [Test]
        public void IsRewind_TargetAtOrAfterPrevious_ReturnsFalse()
        {
            Assert.IsFalse(PresentationPreviewPlayback.IsRewind(2f, 2f));
            Assert.IsFalse(PresentationPreviewPlayback.IsRewind(2f, 2.5f));
        }

        [Test]
        public void IsRewind_WithinEpsilon_ReturnsFalse()
        {
            Assert.IsFalse(PresentationPreviewPlayback.IsRewind(2f, 1.9995f, epsilon: 1e-3f));
        }
    }
}
