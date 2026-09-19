using DDrive.Editor.Compat;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Compat
{
    // [42_distribution.md] §5.2 / §5.11-3(P-3、2026-09-20) — シリアライズされ得る全 enum(AssetType 含む)の
    // 「名前=値」を固定する。末尾追加は許可(MINOR)、削除・値変更・並べ替えは fail(MAJOR)。
    public class SerializedEnumSnapshotTests
    {
        private const string Hint = "enum の値の削除・変更・並べ替えは既存 .asset の種別を破壊する(MAJOR、[42] §5.2)。末尾追加(MINOR)ならゴールデンを更新してください。";

        [Test]
        public void MatchesGolden()
        {
            var actual = SerializedEnumSnapshotBuilder.Build();
            CompatGoldenAssert.AssertMatches(CompatSnapshotPaths.Enums, actual, Hint);
        }

        [Test]
        public void AssetType_IsIncluded()
        {
            var actual = SerializedEnumSnapshotBuilder.Build();
            StringAssert.Contains("DDrive.Foundation.Identity.AssetType.Se=1", actual);
        }
    }
}
