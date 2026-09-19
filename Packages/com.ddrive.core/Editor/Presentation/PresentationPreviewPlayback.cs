using UnityEngine;

namespace DDrive.Editor.Presentation
{
    // [08_presentation.md] §4(5-4 追補、2026-09-14) — 統合プレビューの「▶ 再生」ボタンが取るべき挙動と、
    // シークスライダー追従・巻き戻し検出を純粋関数として切り出したもの。ユーザー報告
    // (「一時停止から再生するとシークバーで最初から再生になっている」)への対応。PresentationEditorWindow.
    // Preview.cs の Play()/OnEditorUpdate/SeekToTime から呼ばれるが、Unity オブジェクトの状態(ウィンドウ/
    // ScenePresentationPreviewDriver)に依存しないため EditMode テストで検証できる(PresentationTrackEditOps
    // と同じ設計)。
    public static class PresentationPreviewPlayback
    {
        public enum PlayAction
        {
            // 最初から再生し直す(DisposeSubscriptions → Play(_target) → SubscribeToCurrent)。
            StartFresh,

            // 一時停止を解除するだけ(SetPaused(false))。
            Resume,
        }

        // 「▶ 再生」ボタンが押されたときの挙動。
        //   停止中(previewIsPlaying=false)                    → StartFresh
        //   再生中で一時停止していない(再生中に連打された等)    → StartFresh(従来どおり最初から)
        //   再生中で一時停止中(windowPaused=true)              → Resume(バグ修正: 以前は常に StartFresh していた)
        public static PlayAction DecideOnPlay(bool previewIsPlaying, bool windowPaused)
            => previewIsPlaying && windowPaused ? PlayAction.Resume : PlayAction.StartFresh;

        // シークスライダー(0..1)に表示する値。再生中(一時停止中も含む。Handle が有効な間)は現在位置に
        // 追従し、停止中は 0 に戻す。呼び出し側はユーザーがスライダーをドラッグ中は呼ばない(上書きしない)。
        public static float ComputeSeekSliderValue(bool isPlaying, float normalizedTime)
            => isPlaying ? Mathf.Clamp01(normalizedTime) : 0f;

        // シーク先が現在位置より前(巻き戻し)かどうか。巻き戻しでは発火済みトラックが再発火しない
        // ([08] Runtime 実装メモ)ため、呼び出し側はこれが true の最初の 1 回だけログへ注意書きを出す。
        public static bool IsRewind(float previousElapsedSeconds, float targetElapsedSeconds, float epsilon = 1e-3f)
            => targetElapsedSeconds < previousElapsedSeconds - epsilon;
    }
}
