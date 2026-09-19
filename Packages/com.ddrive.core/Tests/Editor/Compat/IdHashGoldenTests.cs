using DDrive.Editor.Codegen;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Compat
{
    // [42_distribution.md] §5.3 / §5.11-4(P-3、2026-09-20) — `AssetIdGenerator.StableHashFromGuid`
    // (64bit FNV-1a、GUID 文字列 → ulong)の算法を固定する。発効後にここが変わると、再生成で
    // 全 ID が変わり、既存 .asset・カタログ・ゲームコードの定数値・ネット越しの ID が一斉に不一致になる
    // ([42] §5.3)。
    //
    // 期待値は算法そのもの(FNV-1a、offset=14695981039346656037、prime=1099511628211、
    // hash==0 のときだけ 1 に補正)を独立に計算して求めたもの(実装のコピペではない)。
    public class IdHashGoldenTests
    {
        private const string Hint = "StableHashFromGuid の算法固定(MAJOR、[42] §5.3)。アルゴリズムを意図して変えた場合のみゴールデン値を更新してください。";

        [TestCase("00000000000000000000000000000000", 0x7c02c15d9cbe35a5UL)]
        [TestCase("0123456789abcdef0123456789abcdef", 0x1527c9731f0ff55UL)]
        [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0x4850acf661dfab85UL)]
        public void StableHashFromGuid_MatchesFixedGolden(string guid, ulong expected)
        {
            var actual = AssetIdGenerator.StableHashFromGuid(guid);
            Assert.AreEqual(expected, actual, $"{Hint}\nguid={guid} expected=0x{expected:X} actual=0x{actual:X}");
        }

        [Test]
        public void StableHashFromGuid_IsIdempotent()
        {
            const string guid = "0123456789abcdef0123456789abcdef";
            Assert.AreEqual(AssetIdGenerator.StableHashFromGuid(guid), AssetIdGenerator.StableHashFromGuid(guid));
        }
    }
}
