using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Registry;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Compat
{
    // [42_distribution.md] §5.6 / §5.11-5(P-3、2026-09-20) — `CatalogContentHasher` の算法(64bit FNV-1a、
    // Id → Type → Address → Flags.Net の順に mix、Entry 間は XOR)を固定する。発効後に対象フィールドや
    // 算法を変えると Host/Client の版が同じでも不一致になり得る([42] §5.6、禁止)。
    //
    // 期待値は独立に計算したもの(実装のコピペではない。Packages/com.ddrive.core/Tests/Editor/Compat/
    // CatalogContentHasherGoldenTests.cs の docstring に検算コードは残さないが、Python で
    // 同アルゴリズムを再実装して求めた)。
    public class CatalogContentHasherGoldenTests
    {
        private static CatalogEntry MakeEntry(ulong id, AssetType type, string address, NetMode net)
        {
            var flags = default(AssetFlags);
            flags.Net = net;
            return new CatalogEntry { Id = id, Type = type, Address = address, Flags = flags };
        }

        [Test]
        public void HashEntry_MatchesFixedGolden()
        {
            var e1 = MakeEntry(1, AssetType.Se, "SE_Test_Golden", NetMode.Cosmetic);
            var e2 = MakeEntry(2, AssetType.Vfx, "VFX_Test_Golden", NetMode.Local);

            Assert.AreEqual(0x719def9c298df55fUL, CatalogContentHasher.HashEntry(e1), "HashEntry(SE) の算法が変わりました([42] §5.6、MAJOR)。");
            Assert.AreEqual(0xbe1bb4877fe21b1bUL, CatalogContentHasher.HashEntry(e2), "HashEntry(VFX) の算法が変わりました([42] §5.6、MAJOR)。");
        }

        [Test]
        public void HashEntries_MatchesFixedGolden_AndIsOrderIndependent()
        {
            var e1 = MakeEntry(1, AssetType.Se, "SE_Test_Golden", NetMode.Cosmetic);
            var e2 = MakeEntry(2, AssetType.Vfx, "VFX_Test_Golden", NetMode.Local);

            var forward = CatalogContentHasher.HashEntries(new List<CatalogEntry> { e1, e2 });
            var reversed = CatalogContentHasher.HashEntries(new List<CatalogEntry> { e2, e1 });

            Assert.AreEqual(0xcf865b1b566fee44UL, forward, "HashEntries の合成結果が変わりました([42] §5.6、MAJOR)。");
            Assert.AreEqual(forward, reversed, "Entry の列挙順に依存してはいけません(XOR 合成、[42] §1.1)。");
        }
    }
}
