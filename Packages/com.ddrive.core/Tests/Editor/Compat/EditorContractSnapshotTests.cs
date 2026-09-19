using DDrive.Editor.Compat;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Compat
{
    // [42_distribution.md] §5.9 / §5.11-8(P-3、2026-09-20) — 「弱い互換面」(メニューパス・CI エントリ
    // ポイント・[DataEditor] 対応表)を固定する。破っても MAJOR にはしないが CHANGELOG は必須([42] §5.9)。
    public class EditorContractSnapshotTests
    {
        private const string Hint = "DDriveMenu のメニューパス・CI の -executeMethod エントリ・[DataEditor] 対応表の変更は CHANGELOG 必須([42] §5.9)。改名は旧名を 1 MINOR 残す。";

        [Test]
        public void MatchesGolden()
        {
            var actual = EditorContractSnapshotBuilder.Build();
            CompatGoldenAssert.AssertMatches(CompatSnapshotPaths.EditorContract, actual, Hint);
        }
    }
}
