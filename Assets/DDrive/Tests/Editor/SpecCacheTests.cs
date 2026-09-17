using System.Linq;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Spec;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;

namespace DDrive.Tests.Editor
{
    // 5-16 — SpecCache.GetUncreatedRows(filterType) と RecomputeDiff() のテスト。
    // ネットワークに出ない(CSV 文字列を直接パースして SpecCache に注入する)。
    // SpecCache は静的な共有状態のため、テスト前後で保存/復元して他テスト・自動同期の状態を壊さない。
    public class SpecCacheTests
    {
        private const string TestRoot = "Assets/DDrive/Tests/Editor/TempSpecCacheGameData";
        private const string AssetHeader = "種別,カテゴリ,識別子,表示名,状態,担当,仕様,備考\n";

        private SpecParseResult<SpecAssetRow> _prevAssetRows;
        private SpecParseResult<SpecTuningRow> _prevTuningRows;
        private SpecDiffResult _prevDiff;
        private string _prevWarning;
        private string _prevError;

        [SetUp]
        public void SetUp()
        {
            _prevAssetRows = SpecCache.LastAssetRows;
            _prevTuningRows = SpecCache.LastTuningRows;
            _prevDiff = SpecCache.LastDiff;
            _prevWarning = SpecCache.LastWarning;
            _prevError = SpecCache.LastError;
        }

        [TearDown]
        public void TearDown()
        {
            SpecCache.Set(_prevAssetRows, _prevTuningRows, _prevDiff, _prevWarning, _prevError);

            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            }
        }

        private static void SeedCache(string csv)
        {
            var parsed = SpecSheetParser.ParseAssetSheet(csv);
            var diff = SpecDiffService.ComputeDiff(parsed);
            SpecCache.Set(parsed, new SpecParseResult<SpecTuningRow>(), diff, null, null);
        }

        [Test]
        public void GetUncreatedRows_NoFilter_ReturnsAllNewRows()
        {
            SeedCache(AssetHeader
                + "Se,Player,ZzTest5016CacheSe,テストSE,未着手,,,\n"
                + "Vfx,Skill,ZzTest5016CacheVfx,テストVFX,未着手,,,\n");

            var rows = SpecCache.GetUncreatedRows();

            Assert.IsTrue(rows.Any(r => r.Identifier == "ZzTest5016CacheSe" && r.Type == AssetType.Se));
            Assert.IsTrue(rows.Any(r => r.Identifier == "ZzTest5016CacheVfx" && r.Type == AssetType.Vfx));
        }

        [Test]
        public void GetUncreatedRows_WithFilterType_OnlyReturnsThatType()
        {
            SeedCache(AssetHeader
                + "Se,Player,ZzTest5016CacheSeOnly,テストSE,未着手,,,\n"
                + "Vfx,Skill,ZzTest5016CacheVfxOnly,テストVFX,未着手,,,\n");

            var rows = SpecCache.GetUncreatedRows(AssetType.Se);

            Assert.IsTrue(rows.All(r => r.Type == AssetType.Se));
            Assert.IsTrue(rows.Any(r => r.Identifier == "ZzTest5016CacheSeOnly"));
            Assert.IsFalse(rows.Any(r => r.Identifier == "ZzTest5016CacheVfxOnly"));
        }

        [Test]
        public void GetUncreatedRows_NoDiff_ReturnsEmpty()
        {
            SpecCache.Set(null, null, null, null, null);

            Assert.IsEmpty(SpecCache.GetUncreatedRows());
        }

        [Test]
        public void RecomputeDiff_AfterAssetCreated_RowNoLongerUncreated()
        {
            const string identifier = "ZzTest5016CacheRecompute";
            SeedCache(AssetHeader + $"Se,Player,{identifier},テストSE,未着手,,,\n");
            Assert.IsTrue(SpecCache.GetUncreatedRows().Any(r => r.Identifier == identifier));

            AssetCreationService.Create(typeof(SeData), AssetType.Se, "テストSE", "Player", identifier, gameDataRoot: TestRoot);

            SpecCache.RecomputeDiff();

            Assert.IsFalse(SpecCache.GetUncreatedRows().Any(r => r.Identifier == identifier),
                "作成後は SpecCache.RecomputeDiff で「未作成」一覧から消えるはず");
        }

        [Test]
        public void RecomputeDiff_NoLastAssetRows_DoesNothing()
        {
            SpecCache.Set(null, null, null, null, null);

            Assert.DoesNotThrow(() => SpecCache.RecomputeDiff());
            Assert.IsFalse(SpecCache.HasData);
        }

        [Test]
        public void RecomputeDiff_DoesNotChangeLastFetchUtc()
        {
            SeedCache(AssetHeader + "Se,Player,ZzTest5016CacheFetchTime,テストSE,未着手,,,\n");
            var fetchTime = SpecCache.LastFetchUtc;

            SpecCache.RecomputeDiff();

            Assert.AreEqual(fetchTime, SpecCache.LastFetchUtc);
        }
    }
}
