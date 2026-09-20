using System;
using System.Collections.Generic;
using System.IO;
using DDrive.Editor.Migration;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using DDrive.Tests.Editor.Compat;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DDrive.Tests.Editor.Migration
{
    // [42_distribution.md] §4.3 / §6 P-7(2026-09-20) — DDriveMigrationRunner の計画・適用・Undo・
    // 二重適用防止を検証する。ダミーの IDataMigration はこのテストアセンブリ内だけに存在し、
    // DDriveMigrationRunner.DiscoverDataMigrations()(実運用の TypeCache 発見)には現れない
    // (CI.DiscoverValidators と同じ「DDrive.Tests.* は除外」規則)。そのため各テストは Plan/Apply へ
    // ダミー実装を明示的に渡す(発見に頼らない)。実運用の発見経路自体は
    // CI_MigrateCheck_DoesNotThrow_OnRealProject で最小限のスモーク確認をする。
    // 実 GameData・カタログ・Addressables には触れない(一時アセットと ScriptableObject.CreateInstance のみ)。
    public class DDriveMigrationRunnerTests
    {
        private const string TempDir = "Packages/com.ddrive.core/Tests/Editor/TempMigration";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
            {
                AssetDatabase.CreateFolder("Packages/com.ddrive.core/Tests/Editor", "TempMigration");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir))
            {
                using (VersionStampSuppression.Scope())
                {
                    AssetDatabase.DeleteAsset(TempDir);
                    AssetDatabase.SaveAssets();
                }
            }
        }

        // FromSchema=0 → ToSchema=1。Category が "MigrationTarget" の TestAssetData だけを対象にする。
        private sealed class BumpMigrationTargetCategoryOnly : IDataMigration
        {
            public string Id => "test-only-dummy-bump-target-category";
            public int FromSchema => 0;
            public int ToSchema => 1;

            public bool AppliesTo(AssetDataBase data) => data is TestAssetData t && t.Category == "MigrationTarget";

            public void Migrate(AssetDataBase data, MigrationContext context)
            {
                var t = (TestAssetData)data;
                t.ChangeNote = "migrated-by:" + Id;
                context.Note("migrated " + AssetDatabase.GetAssetPath(data));
            }
        }

        private TestAssetData CreateTestAsset(string fileName, string category)
        {
            var data = ScriptableObject.CreateInstance<TestAssetData>();
            data.Category = category;
            AssetDatabase.CreateAsset(data, $"{TempDir}/{fileName}.asset");
            return AssetDatabase.LoadAssetAtPath<TestAssetData>($"{TempDir}/{fileName}.asset");
        }

        [Test]
        public void Plan_SelectsOnlyMatchingAsset()
        {
            var target = CreateTestAsset("Target", "MigrationTarget");
            var other = CreateTestAsset("Other", "SomethingElse");
            var migration = new BumpMigrationTargetCategoryOnly();

            var plan = DDriveMigrationRunner.Plan(
                new List<IDataMigration> { migration },
                new List<AssetDataBase> { target, other },
                Array.Empty<IProjectMigration>(),
                settings: null);

            Assert.AreEqual(1, plan.DataMigrations.Count, "AppliesTo が true の Data だけが対象になる");
            Assert.AreSame(target, plan.DataMigrations[0].Asset);
            Assert.AreSame(migration, plan.DataMigrations[0].Migration);
        }

        [Test]
        public void Plan_DoesNotModifyAnyAsset_DryRunIsSafe()
        {
            var target = CreateTestAsset("Target", "MigrationTarget");
            var migration = new BumpMigrationTargetCategoryOnly();

            DDriveMigrationRunner.Plan(
                new List<IDataMigration> { migration },
                new List<AssetDataBase> { target },
                Array.Empty<IProjectMigration>(),
                settings: null);

            Assert.AreEqual(0, target.SchemaVersion, "ドライラン(Plan だけ)は実データを一切変更しない");
            Assert.IsNull(target.ChangeNote);
        }

        [Test]
        public void Apply_BumpsSchemaVersion_MigratesValue_AndIsNoOpOnSecondRun()
        {
            var target = CreateTestAsset("Target", "MigrationTarget");
            var other = CreateTestAsset("Other", "SomethingElse");
            var migration = new BumpMigrationTargetCategoryOnly();
            var migrations = new List<IDataMigration> { migration };
            var assets = new List<AssetDataBase> { target, other };

            var plan1 = DDriveMigrationRunner.Plan(migrations, assets, Array.Empty<IProjectMigration>(), settings: null);
            Assert.AreEqual(1, plan1.TotalCount);

            DDriveMigrationRunner.Apply(plan1, settings: null);

            Assert.AreEqual(1, target.SchemaVersion, "適用後は ToSchema になる");
            Assert.AreEqual("migrated-by:" + migration.Id, target.ChangeNote);
            Assert.AreEqual(0, other.SchemaVersion, "対象外の Data は触られない");
            Assert.IsNull(other.ChangeNote);

            // 2 回目は対象 0 件(SchemaVersion が ToSchema に達しているため AppliesTo を満たしても対象外)。
            var plan2 = DDriveMigrationRunner.Plan(migrations, assets, Array.Empty<IProjectMigration>(), settings: null);
            Assert.AreEqual(0, plan2.TotalCount, "SchemaVersion が既に ToSchema 以上のものは二重適用されない");
        }

        [Test]
        public void Apply_RecordsUndo_UndoRestoresSchemaVersionAndMigratedValue()
        {
            var target = CreateTestAsset("Target", "MigrationTarget");
            var migration = new BumpMigrationTargetCategoryOnly();
            var plan = DDriveMigrationRunner.Plan(
                new List<IDataMigration> { migration },
                new List<AssetDataBase> { target },
                Array.Empty<IProjectMigration>(),
                settings: null);

            Undo.IncrementCurrentGroup();
            DDriveMigrationRunner.Apply(plan, settings: null);

            Assert.AreEqual(1, target.SchemaVersion);
            Assert.IsNotNull(target.ChangeNote);

            Undo.PerformUndo();

            Assert.AreEqual(0, target.SchemaVersion, "Undo で SchemaVersion の書き換えも戻る");
            // Unity の Undo/シリアライズ往復で未設定の string フィールドは "" になる(null のままではない)。
            Assert.IsTrue(string.IsNullOrEmpty(target.ChangeNote), "Undo で Migrate() によるフィールドの書き換えも戻る");
        }

        [Test]
        public void Apply_WithEmptyPlan_DoesNothing()
        {
            var target = CreateTestAsset("Target", "NotAMatch");
            var emptyPlan = DDriveMigrationRunner.Plan(
                new List<IDataMigration> { new BumpMigrationTargetCategoryOnly() },
                new List<AssetDataBase> { target },
                Array.Empty<IProjectMigration>(),
                settings: null);

            Assert.AreEqual(0, emptyPlan.TotalCount);

            var context = DDriveMigrationRunner.Apply(emptyPlan, settings: null);

            Assert.AreEqual(0, target.SchemaVersion);
            Assert.AreEqual(0, context.Log.Count);
        }

        // [42_distribution.md] §6 P-7 AC「MigrateCheck が未適用ありで失敗扱い」— exit の代わりに
        // 戻り値/例外で検証する(CI.MigrateCheck は batch モードでしか Exit しないため、
        // 実際の判定ロジック(Plan().TotalCount > 0)を直接検証する。実運用相当の確認は
        // CI_MigrateCheck_DoesNotThrow_OnRealProject を参照)。
        [Test]
        public void Plan_TotalCount_ReflectsPendingState_UsedByMigrateCheck()
        {
            var target = CreateTestAsset("Target", "MigrationTarget");
            var migration = new BumpMigrationTargetCategoryOnly();
            var migrations = new List<IDataMigration> { migration };
            var assets = new List<AssetDataBase> { target };

            var beforeApply = DDriveMigrationRunner.Plan(migrations, assets, Array.Empty<IProjectMigration>(), null);
            Assert.Greater(beforeApply.TotalCount, 0, "未適用のマイグレーションがあれば MigrateCheck は失敗扱いにする");

            DDriveMigrationRunner.Apply(beforeApply, null);

            var afterApply = DDriveMigrationRunner.Plan(migrations, assets, Array.Empty<IProjectMigration>(), null);
            Assert.AreEqual(0, afterApply.TotalCount, "適用後は MigrateCheck が通る状態になる");
        }

        // CI.MigrateCheck 自体(実プロジェクトの TypeCache 発見を使う経路)が例外を投げないことの
        // スモークテスト。Application.isBatchMode は EditMode テスト実行中は false のため
        // EditorApplication.Exit は呼ばれない(テストプロセスを道連れに終了しない)。
        [Test]
        public void CI_MigrateCheck_DoesNotThrow_OnRealProject()
        {
            Assert.IsFalse(Application.isBatchMode, "このテストは Exit を避けるため非バッチモードで走ることを前提にする");
            Assert.DoesNotThrow(() => DDrive.Editor.CI.MigrateCheck());
        }

        // [42_distribution.md] §5.11-2 の旧版フィクスチャ(Fixtures/v1_0_0、SchemaVersion=0)を
        // 実際に Runner へ通し、既知の値(DisplayName/Category/Id)が壊れないことを確認する。
        // 実フィクスチャそのものは変更せず、一時コピーに対して適用する(コミット済みファイルを汚さない)。
        [Test]
        public void LegacyFixtures_RoundTripThroughRunner_PreservesKnownValues()
        {
            const string roundTripDir = TempDir + "/LegacyRoundTrip";
            if (!AssetDatabase.IsValidFolder(roundTripDir))
            {
                AssetDatabase.CreateFolder(TempDir, "LegacyRoundTrip");
            }

            var fixturesRoot = LegacyAssetFixtureTests.FixturesRoot;
            Assert.IsTrue(Directory.Exists(fixturesRoot), $"{fixturesRoot} が見つかりません。");

            var copies = new List<AssetDataBase>();
            foreach (var path in Directory.GetFiles(fixturesRoot, "*.asset"))
            {
                var normalized = path.Replace('\\', '/');
                var destPath = $"{roundTripDir}/{Path.GetFileName(normalized)}";
                Assert.IsTrue(AssetDatabase.CopyAsset(normalized, destPath), $"{normalized} のコピーに失敗しました。");
            }

            AssetDatabase.Refresh();

            foreach (var file in Directory.GetFiles(roundTripDir, "*.asset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(file.Replace('\\', '/'));
                Assert.IsNotNull(asset, file);
                Assert.AreEqual(0, asset.SchemaVersion, $"{file}: 旧版フィクスチャは SchemaVersion=0(1.0.0 以前の形式)のまま");
                copies.Add(asset);
            }

            Assert.Greater(copies.Count, 0, "コピーしたフィクスチャが 0 件でした。");

            var bumpAll = new BumpAllToOneMigration();
            var plan = DDriveMigrationRunner.Plan(
                new List<IDataMigration> { bumpAll },
                copies,
                Array.Empty<IProjectMigration>(),
                settings: null);

            Assert.AreEqual(copies.Count, plan.DataMigrations.Count, "AppliesTo=常に true のマイグレーションは全フィクスチャが対象になる");

            DDriveMigrationRunner.Apply(plan, settings: null);

            foreach (var asset in copies)
            {
                Assert.AreEqual(1, asset.SchemaVersion);
                StringAssert.StartsWith("CompatFixture_", asset.DisplayName, "往復後も DisplayName(既知の値)が保たれる");
                Assert.AreEqual("CompatFixture", asset.Category, "往復後も Category(既知の値)が保たれる");
                Assert.AreNotEqual(0UL, asset.Id, "往復後も Id(既知の値、非 0)が保たれる");
            }

            var planAfter = DDriveMigrationRunner.Plan(
                new List<IDataMigration> { bumpAll },
                copies,
                Array.Empty<IProjectMigration>(),
                settings: null);
            Assert.AreEqual(0, planAfter.TotalCount, "適用後は二度目の Plan で対象 0 件になる");
        }

        // 値は書き換えない(Migrate 内で SchemaVersion を触らない契約、Runner 側が書く)。
        // AppliesTo は常に true(型を問わずフィクスチャ全種別を対象にするため)。
        private sealed class BumpAllToOneMigration : IDataMigration
        {
            public string Id => "test-only-dummy-bump-all-to-1";
            public int FromSchema => 0;
            public int ToSchema => 1;
            public bool AppliesTo(AssetDataBase data) => true;
            public void Migrate(AssetDataBase data, MigrationContext context) { }
        }
    }
}
