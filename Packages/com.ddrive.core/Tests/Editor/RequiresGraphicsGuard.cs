using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDrive.Tests.Editor
{
    // [48_p11_install_test_2026-09-20.md] フォローアップ「-nographics/WaitForEndOfFrame 起因の 18 件」—
    // `-batchmode -nographics`(この開発リポジトリの `Tools/CI/run-ci.cmd`、および持ち込み先の CI/スモークが
    // 使う実行方式)ではグラフィックデバイスが `GraphicsDeviceType.Null` になり、`EditorWindow.GetWindow<T>()`
    // での実ウィンドウ生成や `RenderTexture.Create`(サムネイル・アイコン生成)が失敗する
    // (`No graphic device is available` / `RenderTexture.Create failed`)。これは D-Drive 固有のバグでも
    // 持ち込み先固有の問題でもなく、Unity バッチ実行の既知の制約(グラフィックが無い環境では GUI/描画系の
    // Editor API が使えない)。`DevRepoOnlyGuard` と同じ流儀で、対象テストの先頭でこのガードを呼び、
    // `[Category("RequiresGraphics")]` を付けておくことで CI で分離実行できるようにする
    // (現状はグラフィックデバイスが無ければ Fail ではなく Inconclusive にするだけで、CI 設定自体は変えない)。
    internal static class RequiresGraphicsGuard
    {
        public static void SkipIfNoGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assume.That(false, "グラフィックデバイスが無い環境(-nographics 等)のためスキップ(EditorWindow/RenderTexture 系の Editor API はグラフィックデバイスを要求する)");
            }
        }
    }
}
