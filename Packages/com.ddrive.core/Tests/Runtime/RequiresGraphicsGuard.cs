using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDrive.Tests.Runtime
{
    // [49_p12_ms2026_install_2026-09-20.md] §7/§13-3(P-12 で発見) — `CutsceneTimelineTracksTests.
    // Applier_DetectsOverwrite_WhenLaterScriptWritesCameraInLateUpdate` は
    // `DDriveCutsceneCameraApplier` の検出2([26_timeline.md] §4.6.5、
    // `RenderPipelineManager.endCameraRendering` で「描画に使われた姿勢」と自分の書き込みを比較する)に
    // 依存しているが、`-batchmode -nographics` では実際のカメラ描画(SRP のレンダーループ)が発生しないため
    // このコールバックが一度も呼ばれず、警告も出ない。[48_p11_install_test_2026-09-20.md] §12.2 の
    // `TestFrameWait`(コルーチンの待ち方の切り替え)だけでは解消できないことを実測で確認した
    // (待ち方の問題ではなく、描画そのものが発生しないことが原因のため)。
    //
    // `Tests/Editor/RequiresGraphicsGuard.cs` と同じ判定
    // (`SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null`。`-nographics` で Null になることを
    // EditMode 側のフォローアップで実測済み)で、グラフィックデバイスが無い環境では Assume で
    // Inconclusive にする(Fail にしない。CLAUDE.md §0-4: デザイナーの作業を止めない)。
    public static class RequiresGraphicsGuard
    {
        public static void SkipIfNoGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assume.That(false, "グラフィックデバイスが無い環境(-nographics 等)のためスキップ(SRP のカメラ描画コールバックに依存する検出ロジックは、実際の描画が無いと一度も呼ばれない)");
            }
        }
    }
}
