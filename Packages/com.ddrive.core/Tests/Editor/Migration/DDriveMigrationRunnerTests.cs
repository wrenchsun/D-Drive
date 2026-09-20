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
using UnityEngine.TestTools;

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
        private const string TempDir = TestTempFolder.Root + "/TempMigration";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
            {
                TestTempFolder.CreateFolder("TempMigration");
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
            Assert.AreEqual(1, plan1.DataMigrations.Count, "target だけが実マイグレーションの対象");
            // [47] P1-4/P1-5 — other は AppliesTo を満たさないが、SchemaVersion(0) < Current なので
            // 「スキーマ版の刻印」だけの対象になる(TotalCount にはこちらも数える)。
            Assert.AreEqual(1, plan1.SchemaStampOnly.Count);
            Assert.AreEqual(2, plan1.TotalCount);

            DDriveMigrationRunner.Apply(plan1, settings: null);

            Assert.AreEqual(1, target.SchemaVersion, "適用後は ToSchema になる");
            Assert.AreEqual("migrated-by:" + migration.Id, target.ChangeNote);
            Assert.AreEqual(DDriveSchema.Current, other.SchemaVersion,
                "対象の実マイグレーションは無いが、スキーマ版の刻印段で Current まで引き上げられる([47] P1-4/P1-5)");
            Assert.IsNull(other.ChangeNote, "刻印段は値を書き換えない(SchemaVersion だけを進める)");

            // 2 回目は対象 0 件(SchemaVersion が ToSchema/Current に達しているため)。
            var plan2 = DDriveMigrationRunner.Plan(migrations, assets, Array.Empty<IProjectMigration>(), settings: null);
            Assert.AreEqual(0, plan2.TotalCount, "SchemaVersion が既に ToSchema/Current 以上のものは二重適用されない");
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

        // [47] P1-4/P1-5 — 「本当に何もしない」は SchemaVersion が既に Current の Data だけ
        // (StampNew と同じタイミングで付与される。新規作成直後を模擬する)。
        [Test]
        public void Apply_TrulyEmptyPlan_WhenAlreadyAtCurrentSchema_DoesNothing()
        {
            var target = CreateTestAsset("Target", "NotAMatch");
            VersionStampProcessor.StampNew(target);
            Assert.AreEqual(DDriveSchema.Current, target.SchemaVersion, "このテストの前提: StampNew 直後は Current");

            var emptyPlan = DDriveMigrationRunner.Plan(
                new List<IDataMigration> { new BumpMigrationTargetCategoryOnly() },
                new List<AssetDataBase> { target },
                Array.Empty<IProjectMigration>(),
                settings: null);

            Assert.AreEqual(0, emptyPlan.TotalCount);

            var context = DDriveMigrationRunner.Apply(emptyPlan, settings: null);

            Assert.AreEqual(DDriveSchema.Current, target.SchemaVersion);
            Assert.AreEqual(0, context.Log.Count);
        }

        // [47] P1-4/P1-5 の中心的なシナリオ: 対象の IDataMigration が 1 つも無くても
        // (migrations が空配列でも)、SchemaVersion < Current の Data は Apply で Current に刻印される。
        [Test]
        public void Apply_NoMigrationsAtAll_StillStampsSchemaVersionToCurrent()
        {
            var target = CreateTestAsset("Target", "AnyCategory");
            Assert.AreEqual(0, target.SchemaVersion);

            var plan = DDriveMigrationRunner.Plan(
                Array.Empty<IDataMigration>(),
                new List<AssetDataBase> { target },
                Array.Empty<IProjectMigration>(),
                settings: null);

            Assert.AreEqual(0, plan.DataMigrations.Count);
            Assert.AreEqual(1, plan.SchemaStampOnly.Count);
            Assert.AreEqual(1, plan.TotalCount, "マイグレーション実装が 0 件でも、刻印段が TotalCount に数える");

            DDriveMigrationRunner.Apply(plan, settings: null);

            Assert.AreEqual(DDriveSchema.Current, target.SchemaVersion);
        }

        // [47] P2-3 — 多段連鎖(0→1 の適用で初めて 1→2 の条件〔AppliesTo〕を満たすようになる Data)が
        // 1 回の Apply で完了することを固定する(以前は計画時点の状態だけで判定していたため、
        // 1→2 が計画に入らず 2 回 Apply しないと完了しなかった)。
        private sealed class StageOneMigration : IDataMigration
        {
            public string Id => "test-only-dummy-stage1-0-to-1";
            public int FromSchema => 0;
            public int ToSchema => 1;
            public bool AppliesTo(AssetDataBase data) => data is TestAssetData;
            public void Migrate(AssetDataBase data, MigrationContext context)
            {
                ((TestAssetData)data).ChangeNote = "stage1";
            }
        }

        private sealed class StageTwoMigration : IDataMigration
        {
            public string Id => "test-only-dummy-stage2-1-to-2";
            public int FromSchema => 1;
            public int ToSchema => 2;
            // stage1 が付けた ChangeNote に依存する(実際の多段連鎖でよくある「前段の出力を見て判断する」形)。
            public bool AppliesTo(AssetDataBase data) => data is TestAssetData t && t.ChangeNote == "stage1";
            public void Migrate(AssetDataBase data, MigrationContext context)
            {
                ((TestAssetData)data).ChangeNote = "stage1+stage2";
            }
        }

        [Test]
        public void Apply_MultiHopMigration_CompletesBothStagesInOneCall()
        {
            var target = CreateTestAsset("Target", "AnyCategory");
            var migrations = new List<IDataMigration> { new StageOneMigration(), new StageTwoMigration() };

            var plan = DDriveMigrationRunner.Plan(migrations, new List<AssetDataBase> { target }, Array.Empty<IProjectMigration>(), settings: null);
            // Plan は単発の見積もりのため、この時点では stage1 しか計画に入らない(stage2 の AppliesTo は
            // まだ ChangeNote が "stage1" になっていないため false)。
            Assert.AreEqual(1, plan.DataMigrations.Count);

            DDriveMigrationRunner.Apply(plan, settings: null);

            Assert.AreEqual(2, target.SchemaVersion, "Apply の再評価ループにより stage1→stage2 まで 1 回で進む");
            Assert.AreEqual("stage1+stage2", target.ChangeNote);
        }

        // [47] P2-3 — AppliesTo/Migrate の例外は警告 + そのアセットをスキップするだけで、
        // Apply 全体やほかのアセットの処理を止めない(CLAUDE.md §0-4)。
        private sealed class ThrowingMigration : IDataMigration
        {
            public string Id => "test-only-dummy-throwing";
            public int FromSchema => 0;
            public int ToSchema => 1;
            public bool AppliesTo(AssetDataBase data) => data is TestAssetData t && t.Category == "ThrowOnMe";
            public void Migrate(AssetDataBase data, MigrationContext context) => throw new InvalidOperationException("boom");
        }

        [Test]
        public void Apply_WhenMigrationThrows_SkipsThatAssetButContinuesWithOthers()
        {
            var broken = CreateTestAsset("Broken", "ThrowOnMe");
            var healthy = CreateTestAsset("Healthy", "AnyCategory");
            var migrations = new List<IDataMigration> { new ThrowingMigration() };

            var plan = DDriveMigrationRunner.Plan(migrations, new List<AssetDataBase> { broken, healthy }, Array.Empty<IProjectMigration>(), settings: null);

            LogAssert.ignoreFailingMessages = true;
            Assert.DoesNotThrow(() => DDriveMigrationRunner.Apply(plan, settings: null));
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(0, broken.SchemaVersion, "例外を投げたアセットはスキップされ、SchemaVersion も変わらない");
            Assert.AreEqual(DDriveSchema.Current, healthy.SchemaVersion, "他のアセットの処理は継続する(刻印段まで完了する)");
        }

        // [47] P2-3 — 67 件マイグレートしても Undo は 1 回で済む(1 回の Apply = 1 つの Undo グループ)。
        [Test]
        public void Apply_CollapsesIntoSingleUndoGroup_ForMultipleAssets()
        {
            var a = CreateTestAsset("A", "AnyCategory");
            var b = CreateTestAsset("B", "AnyCategory");

            var plan = DDriveMigrationRunner.Plan(
                Array.Empty<IDataMigration>(),
                new List<AssetDataBase> { a, b },
                Array.Empty<IProjectMigration>(),
                settings: null);

            Undo.IncrementCurrentGroup();
            var groupBefore = Undo.GetCurrentGroup();
            DDriveMigrationRunner.Apply(plan, settings: null);

            Assert.AreEqual(DDriveSchema.Current, a.SchemaVersion);
            Assert.AreEqual(DDriveSchema.Current, b.SchemaVersion);

            Undo.PerformUndo();

            Assert.AreEqual(0, a.SchemaVersion, "1 回の Undo で両方のアセットが戻る(1 グループにまとまっている)");
            Assert.AreEqual(0, b.SchemaVersion);
            Assert.GreaterOrEqual(groupBefore, 0);
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
        // [47] P1-4/P1-5(2026-09-20 修正) — この開発リポジトリの実 GameData は「スキーマ版の刻印」が
        // まだ適用されていない(SchemaVersion=0 のまま)ため、通常は Pending=true で Error ログが出る。
        // それ自体は仕様どおりの挙動なので、ログの有無ではなく「例外を投げない」ことだけを確認する。
        [Test]
        public void CI_MigrateCheck_DoesNotThrow_OnRealProject()
        {
            Assert.IsFalse(Application.isBatchMode, "このテストは Exit を避けるため非バッチモードで走ることを前提にする");
            LogAssert.ignoreFailingMessages = true;
            try
            {
                Assert.DoesNotThrow(() => DDrive.Editor.CI.MigrateCheck());
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
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
