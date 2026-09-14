using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Registry;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Runtime
{
    // [11_tasks.md] 6-5 / [14_networking.md] §7 — ContentHash の決定性・順序非依存・変更検出を検証する。
    // 実 GameData/カタログには触れない(ScriptableObject.CreateInstance のみ)。
    public class CatalogContentHasherTests
    {
        private static readonly List<AssetCatalog> _createdCatalogs = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var c in _createdCatalogs)
            {
                if (c != null)
                {
                    Object.DestroyImmediate(c);
                }
            }

            _createdCatalogs.Clear();
        }

        private static CatalogEntry MakeEntry(ulong id, AssetType type, string address, NetMode net = NetMode.Local)
        {
            var flags = default(AssetFlags);
            flags.Net = net;
            return new CatalogEntry { Id = id, Type = type, Address = address, Flags = flags };
        }

        private static AssetCatalog MakeCatalog(string name, List<CatalogEntry> entries)
        {
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.name = name;
            catalog.SetEntries(entries);
            _createdCatalogs.Add(catalog);
            return catalog;
        }

        [Test]
        public void HashEntries_SameInput_IsDeterministic()
        {
            var entries = new List<CatalogEntry>
            {
                MakeEntry(1, AssetType.Se, "SE_Foo"),
                MakeEntry(2, AssetType.Vfx, "VFX_Bar", NetMode.Cosmetic),
            };

            var a = CatalogContentHasher.HashEntries(entries);
            var b = CatalogContentHasher.HashEntries(entries);

            Assert.AreEqual(a, b);
        }

        [Test]
        public void HashEntries_OrderIndependent()
        {
            var forward = new List<CatalogEntry>
            {
                MakeEntry(1, AssetType.Se, "SE_Foo"),
                MakeEntry(2, AssetType.Vfx, "VFX_Bar", NetMode.Cosmetic),
                MakeEntry(3, AssetType.Prefab, "PFB_Baz", NetMode.Simulated),
            };

            var reversed = new List<CatalogEntry> { forward[2], forward[0], forward[1] };

            Assert.AreEqual(CatalogContentHasher.HashEntries(forward), CatalogContentHasher.HashEntries(reversed));
        }

        [Test]
        public void HashEntries_ChangingOneEntry_ChangesResult()
        {
            var baseline = new List<CatalogEntry>
            {
                MakeEntry(1, AssetType.Se, "SE_Foo"),
                MakeEntry(2, AssetType.Vfx, "VFX_Bar", NetMode.Cosmetic),
            };

            var changedAddress = new List<CatalogEntry> { baseline[0], MakeEntry(2, AssetType.Vfx, "VFX_Bar_Changed", NetMode.Cosmetic) };
            var changedNetMode = new List<CatalogEntry> { baseline[0], MakeEntry(2, AssetType.Vfx, "VFX_Bar", NetMode.Simulated) };
            var changedId = new List<CatalogEntry> { baseline[0], MakeEntry(99, AssetType.Vfx, "VFX_Bar", NetMode.Cosmetic) };

            var original = CatalogContentHasher.HashEntries(baseline);
            Assert.AreNotEqual(original, CatalogContentHasher.HashEntries(changedAddress));
            Assert.AreNotEqual(original, CatalogContentHasher.HashEntries(changedNetMode));
            Assert.AreNotEqual(original, CatalogContentHasher.HashEntries(changedId));
        }

        [Test]
        public void HashEntries_EmptyList_IsZero()
        {
            Assert.AreEqual(0UL, CatalogContentHasher.HashEntries(new List<CatalogEntry>()));
            Assert.AreEqual(0UL, CatalogContentHasher.HashEntries(null));
        }

        [Test]
        public void CombineCatalogHashes_OrderIndependent_AndMatchesFlattenedSingleCatalog()
        {
            var catalogA = MakeCatalog("CatalogA", new List<CatalogEntry> { MakeEntry(1, AssetType.Se, "SE_Foo") });
            var catalogB = MakeCatalog("CatalogB", new List<CatalogEntry> { MakeEntry(2, AssetType.Vfx, "VFX_Bar", NetMode.Cosmetic) });

            var hashA = CatalogContentHasher.HashCatalog(catalogA);
            var hashB = CatalogContentHasher.HashCatalog(catalogB);

            var combinedAB = CatalogContentHasher.CombineCatalogHashes(new List<CatalogContentHasher.CatalogHashEntry> { hashA, hashB });
            var combinedBA = CatalogContentHasher.CombineCatalogHashes(new List<CatalogContentHasher.CatalogHashEntry> { hashB, hashA });
            Assert.AreEqual(combinedAB, combinedBA);

            // 2 カタログに分けて合成しても、1 つに flatten して計算した場合と同じ値になる(XOR の結合性)。
            var flattened = CatalogContentHasher.HashEntries(new List<CatalogEntry>
            {
                MakeEntry(1, AssetType.Se, "SE_Foo"),
                MakeEntry(2, AssetType.Vfx, "VFX_Bar", NetMode.Cosmetic),
            });
            Assert.AreEqual(flattened, combinedAB);
        }

        [Test]
        public void HashCatalog_ReportsNameAndEntryCount()
        {
            var catalog = MakeCatalog("MyCatalog", new List<CatalogEntry>
            {
                MakeEntry(1, AssetType.Se, "SE_Foo"),
                MakeEntry(2, AssetType.Vfx, "VFX_Bar", NetMode.Cosmetic),
            });

            var result = CatalogContentHasher.HashCatalog(catalog);

            Assert.AreEqual("MyCatalog", result.CatalogName);
            Assert.AreEqual(2, result.EntryCount);
        }
    }
}
