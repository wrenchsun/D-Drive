using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // [48_p11_install_test_2026-09-20.md] フォローアップ「-nographics/WaitForEndOfFrame 起因の 18 件」—
    // `new WaitForEndOfFrame()` は `-batchmode`(この開発リポジトリの `Tools/CI/run-ci.cmd`、および
    // 持ち込み先の CI/スモークが使う実行方式)では
    // `Exception: UnityTest yielded WaitForEndOfFrame, which is not evoked in batchmode.` で失敗する
    // (Unity 自体の既知の制約)。`Application.isBatchMode` のときは `yield return null`(次フレームの
    // Update 直前まで待つ。対象の LateUpdate〔例: DDriveCutsceneCameraApplier〕はこの時点で必ず実行済み)
    // に切り替えることで、非バッチ実行時の挙動(WaitForEndOfFrame でレンダリング完了まで待つ)は変えずに
    // バッチ実行でも Fail しないようにする。
    public static class TestFrameWait
    {
        // `yield return TestFrameWait.EndOfFrameOrNextUpdate();` の形で使う
        // (`YieldInstruction`/`null` のどちらも `yield return object` として扱えるため、戻り値の型は object)。
        public static object EndOfFrameOrNextUpdate() => Application.isBatchMode ? null : new WaitForEndOfFrame();
    }
}
