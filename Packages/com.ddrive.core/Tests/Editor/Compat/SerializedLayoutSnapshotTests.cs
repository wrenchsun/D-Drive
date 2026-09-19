using DDrive.Editor.Compat;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Compat
{
    // [42_distribution.md] §5.1 / §5.11-2(P-3、2026-09-20) — 全 AssetDataBase 派生型の
    // SerializedObject フィールド一覧(propertyPath : propertyType)を固定する。
    public class SerializedLayoutSnapshotTests
    {
        private const string Hint = "シリアライズ形式(.asset に書かれるフィールド)の削除・型変更は MAJOR([42] §5.1)。追加(MINOR)ならゴールデンを更新してください。";

        [Test]
        public void MatchesGolden()
        {
            var actual = SerializedLayoutSnapshotBuilder.Build();
            CompatGoldenAssert.AssertMatches(CompatSnapshotPaths.SerializedLayout, actual, Hint);
        }
    }
}
