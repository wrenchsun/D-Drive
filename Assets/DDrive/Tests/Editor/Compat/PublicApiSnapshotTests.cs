using DDrive.Editor.Compat;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Compat
{
    // [42_distribution.md] §5.4 / §5.11-1(P-3、2026-09-20) — DDrive.Foundation / DDrive.Runtime の
    // 公開 API(型・メンバー・シグネチャ・[Obsolete])を固定する。
    //
    // 判定: 削除・シグネチャ変更・改名は fail(MAJOR)。追加(型・メンバー・オーバーロード・既定引数)は
    // MINOR として許可し、ゴールデンを更新して通す([12_review.md] §3「互換性」節)。
    public class PublicApiSnapshotTests
    {
        private const string Hint = "公開 API(DDrive.Foundation/DDrive.Runtime の public)の削除・改名・シグネチャ変更は MAJOR([42] §5.4)。追加(MINOR)ならゴールデンを更新してください。";

        [Test]
        public void Foundation_MatchesGolden()
        {
            var actual = PublicApiSnapshotBuilder.Build("DDrive.Foundation");
            CompatGoldenAssert.AssertMatches(DDrive.Editor.Compat.CompatSnapshotPaths.PublicApiFoundation, actual, Hint);
        }

        [Test]
        public void Runtime_MatchesGolden()
        {
            var actual = PublicApiSnapshotBuilder.Build("DDrive.Runtime");
            CompatGoldenAssert.AssertMatches(DDrive.Editor.Compat.CompatSnapshotPaths.PublicApiRuntime, actual, Hint);
        }
    }
}
