using System.Collections.Generic;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Net;
using NUnit.Framework;

namespace DDrive.Tests.Runtime
{
    // [11_tasks.md] 6-5 / [14_networking.md] §7 — 不一致/タイムアウト時の方針決定と差分説明は
    // Unity API・NetBridge に依存しない純関数として実装されている。ここではその純関数だけを検証する。
    public class CatalogContentHashPolicyTests
    {
        private static CatalogContentHasher.CatalogHashEntry Entry(string name, ulong hash, int count)
            => new() { CatalogName = name, Hash = hash, EntryCount = count };

        [Test]
        public void Decide_DevelopmentOrEditor_WarnsAndContinues()
        {
            Assert.AreEqual(CatalogContentHashPolicy.Outcome.WarnAndContinue, CatalogContentHashPolicy.Decide(isDevelopmentOrEditor: true));
        }

        [Test]
        public void Decide_ReleaseBuild_Disconnects()
        {
            Assert.AreEqual(CatalogContentHashPolicy.Outcome.Disconnect, CatalogContentHashPolicy.Decide(isDevelopmentOrEditor: false));
        }

        [Test]
        public void DescribeDifferences_HashMismatch_ReportsCatalogNameAndEntryCounts()
        {
            var local = new List<CatalogContentHasher.CatalogHashEntry> { Entry("MaterialCatalog", 111UL, 41) };
            var remote = new List<CatalogContentHasher.CatalogHashEntry> { Entry("MaterialCatalog", 222UL, 42) };

            var diffs = CatalogContentHashPolicy.DescribeDifferences(local, remote);

            Assert.AreEqual(1, diffs.Count);
            StringAssert.Contains("MaterialCatalog", diffs[0]);
            StringAssert.Contains("local=41", diffs[0]);
            StringAssert.Contains("remote=42", diffs[0]);
        }

        [Test]
        public void DescribeDifferences_MissingOnRemote_IsReported()
        {
            var local = new List<CatalogContentHasher.CatalogHashEntry> { Entry("AudioCatalog", 111UL, 10) };
            var remote = new List<CatalogContentHasher.CatalogHashEntry>();

            var diffs = CatalogContentHashPolicy.DescribeDifferences(local, remote);

            Assert.AreEqual(1, diffs.Count);
            StringAssert.Contains("AudioCatalog", diffs[0]);
            StringAssert.Contains("リモートに存在しません", diffs[0]);
        }

        [Test]
        public void DescribeDifferences_MissingOnLocal_IsReported()
        {
            var local = new List<CatalogContentHasher.CatalogHashEntry>();
            var remote = new List<CatalogContentHasher.CatalogHashEntry> { Entry("VfxCatalog", 111UL, 5) };

            var diffs = CatalogContentHashPolicy.DescribeDifferences(local, remote);

            Assert.AreEqual(1, diffs.Count);
            StringAssert.Contains("VfxCatalog", diffs[0]);
            StringAssert.Contains("ローカルに存在しません", diffs[0]);
        }

        [Test]
        public void DescribeDifferences_SameHash_ReportsNoDifference()
        {
            var local = new List<CatalogContentHasher.CatalogHashEntry> { Entry("Catalog", 111UL, 10) };
            var remote = new List<CatalogContentHasher.CatalogHashEntry> { Entry("Catalog", 111UL, 10) };

            var diffs = CatalogContentHashPolicy.DescribeDifferences(local, remote);

            Assert.AreEqual(0, diffs.Count);
        }
    }
}
