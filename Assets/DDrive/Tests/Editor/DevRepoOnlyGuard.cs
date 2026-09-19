using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [42_distribution.md] §2.1 Tests 行 / §2.3-6(P-4、2026-09-20) — 開発リポジトリの実データ
    // (Assets/SourceAssets の UnityChan FBX 等)が無いと実行できないテストの共通ガード。
    // `DDRIVE_DEV_REPO`(開発リポジトリの ProjectSettings でのみ Scripting Define Symbols に追加する。
    // 持ち込み先には無い)が定義されていなければ Fail ではなく Inconclusive にする
    // (持ち込み先で testables を有効にしてもテスト全体が赤くならないようにするため)。
    // 併せて `[Category("DevRepoOnly")]` を付けたテストから呼ぶ(CI で分離実行できるようにする土台)。
    internal static class DevRepoOnlyGuard
    {
        public static void SkipUnlessDevRepo()
        {
#if !DDRIVE_DEV_REPO
            Assume.That(false, "開発リポジトリの実データ(Assets/SourceAssets 等)が必要なためスキップ(DDRIVE_DEV_REPO 未定義)");
#endif
        }
    }
}
