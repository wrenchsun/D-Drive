using System;
using System.Collections.Generic;
using DDrive.Editor.Settings;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Migration
{
    // [42_distribution.md] §4.3 / §6 P-7(2026-09-20) — スキーマ版マイグレーションの実行基盤。
    //
    // 流れ: 発見(TypeCache、テストアセンブリ除外) → 対象探索(実データ、Compat のフィクスチャは除外) →
    // 計画(Plan、読み取りのみ = ドライランに使える) → 適用(Apply、Undo + VersionStampSuppression 内で
    // 保存)。`IValidator`/`CI.DiscoverValidators` と同じ設計を踏襲する。
    //
    // [47_review_p_tickets_2026-09-20.md] P2-3(2026-09-20 修正) — 3 点の穴を直した:
    //   (1) 多段連鎖の取りこぼし: 計画時点の状態だけで判定していたため、0→1 の適用で初めて条件を満たす
    //       1→2 が計画に入らなかった。Apply 側で「1 つ適用するたびに残りを再評価する」ループにした
    //       (Plan は「何件くらい動くか」の見積もりに留め、正確性は Apply のループが担保する)。
    //   (2) `FromSchema` を見ていなかった: 条件を `FromSchema <= SchemaVersion < ToSchema` にした。
    //   (3) 例外: `AppliesTo`/`Migrate` を try/catch し、失敗したら警告 + そのアセットをスキップする
    //       (CLAUDE.md §0-4「例外で止めない」)。
    // Undo グループも 1 回の Apply で 1 つにまとめ、`MarkMigrationApplied` は `IProjectMigration` だけに限定した。
    //
    // [47_review_p_tickets_2026-09-20.md] P1-4/P1-5(2026-09-20 修正) — 保存フック(VersionStampProcessor)は
    // もう SchemaVersion を書かないため、「適用すべき IDataMigration が無い Data」の SchemaVersion は
    // 永久に古いまま残ってしまう。Apply の最後に「まだ Current 未満なら Current へ引き上げる」スキーマ版の
    // 刻印段を追加した(`MigrationPlan.SchemaStampOnly`)。これにより `SchemaVersionValidator` の Warning は
    // 「マイグレーション(適用)を実行すれば必ず解消する」という案内と一致する。
    public static class DDriveMigrationRunner
    {
        public readonly struct PlannedDataMigration
        {
            public readonly IDataMigration Migration;
            public readonly AssetDataBase Asset;

            public PlannedDataMigration(IDataMigration migration, AssetDataBase asset)
            {
                Migration = migration;
                Asset = asset;
            }
        }

        public sealed class MigrationPlan
        {
            public IReadOnlyList<PlannedDataMigration> DataMigrations { get; }
            public IReadOnlyList<IProjectMigration> ProjectMigrations { get; }

            // P1-4/P1-5 — 「適用すべき IDataMigration が無い(または単発の適用だけでは Current に届かない)」
            // が SchemaVersion < Current な Data。Apply はこれらを Current へ直接刻印する。
            public IReadOnlyList<AssetDataBase> SchemaStampOnly { get; }

            public int TotalCount => DataMigrations.Count + ProjectMigrations.Count + SchemaStampOnly.Count;

            // Apply 側の再評価ループが使う、From 順に並べた発見済みマイグレーション一覧(内部専用)。
            internal IReadOnlyList<IDataMigration> OrderedDataMigrations { get; }

            internal MigrationPlan(
                List<PlannedDataMigration> dataMigrations,
                List<IProjectMigration> projectMigrations,
                List<AssetDataBase> schemaStampOnly,
                List<IDataMigration> orderedDataMigrations)
            {
                DataMigrations = dataMigrations;
                ProjectMigrations = projectMigrations;
                SchemaStampOnly = schemaStampOnly;
                OrderedDataMigrations = orderedDataMigrations;
            }
        }

        // ── 発見(TypeCache。CI.DiscoverValidators と同じ理由でテストアセンブリの実装は除外する) ──

        public static IEnumerable<IDataMigration> DiscoverDataMigrations()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<IDataMigration>())
            {
                if (!IsInstantiableNonTestType(type))
                {
                    continue;
                }

                if (Activator.CreateInstance(type) is IDataMigration migration)
                {
                    yield return migration;
                }
            }
        }

        public static IEnumerable<IProjectMigration> DiscoverProjectMigrations()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<IProjectMigration>())
            {
                if (!IsInstantiableNonTestType(type))
                {
                    continue;
                }

                if (Activator.CreateInstance(type) is IProjectMigration migration)
                {
                    yield return migration;
                }
            }
        }

        private static bool IsInstantiableNonTestType(Type type)
        {
            if (type == null || type.IsAbstract || type.IsInterface)
            {
                return false;
            }

            // [11_tasks.md] P-3/CI.DiscoverValidators と同じ理由 — テストアセンブリ内のダミー実装
            // (DDriveMigrationRunnerTests 等)が実運用の発見・CI.MigrateCheck に混ざらないようにする。
            var assemblyName = type.Assembly.GetName().Name;
            if (assemblyName.StartsWith("DDrive.Tests", StringComparison.Ordinal))
            {
                return false;
            }

            return type.GetConstructor(Type.EmptyTypes) != null;
        }

        // ── 対象探索(実データ。CI.LoadAllAssetDataAssets と同じくスナップショットの旧版フィクスチャは除外) ──

        public static List<AssetDataBase> FindAllProjectAssets()
        {
            var result = new List<AssetDataBase>();
            var guids = AssetSearch.FindAssets("t:" + nameof(AssetDataBase));

            // [47_review_p_tickets_2026-09-20.md] P2-1(2026-09-20) — 互換性スナップショットの
            // 「旧版フィクスチャ」の除外は AssetSearch.FindAssets 自身が行う(唯一の検索口に集約)。
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(path);
                if (asset != null)
                {
                    result.Add(asset);
                }
            }

            return result;
        }

        // ── 計画(読み取りのみ。実データを一切変更しない = ドライラン用にそのまま使える) ──
        //
        // P2-3 — ここでの一致判定は単発(1 回分)の見積もりに留める。多段連鎖(0→1 で初めて 1→2 の
        // 条件を満たす等)の正確な解決は Apply 側の再評価ループが担当する(AppliesTo が Migrate 後の
        // フィールド状態に依存し得るため、Migrate を呼ばない Plan では正確に解決できない)。
        public static MigrationPlan Plan(
            IReadOnlyList<IDataMigration> dataMigrations,
            IReadOnlyList<AssetDataBase> assets,
            IReadOnlyList<IProjectMigration> projectMigrations,
            DDriveProjectSettings settings)
        {
            var orderedMigrations = new List<IDataMigration>(dataMigrations ?? Array.Empty<IDataMigration>());
            // [42_distribution.md] §4.3「From→To の順に適用」。
            orderedMigrations.Sort((a, b) => a.FromSchema.CompareTo(b.FromSchema));

            var dataPlan = new List<PlannedDataMigration>();
            var stampOnly = new List<AssetDataBase>();
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    if (asset == null)
                    {
                        continue;
                    }

                    var matchedAny = false;
                    foreach (var migration in orderedMigrations)
                    {
                        var applies = SafeAppliesTo(migration, asset);
                        // P2-3 — FromSchema を条件に含める(以前は ToSchema しか見ておらず、
                        // SchemaVersion==1 の Data に 0→2 のマイグレーションが適用され得た)。
                        if (migration.FromSchema <= asset.SchemaVersion && asset.SchemaVersion < migration.ToSchema && applies)
                        {
                            dataPlan.Add(new PlannedDataMigration(migration, asset));
                            matchedAny = true;
                        }
                    }

                    // P1-4/P1-5 — 適用すべきマイグレーションが 1 つも無くても、SchemaVersion が
                    // Current 未満なら「スキーマ版の刻印」だけの対象にする(Apply が Current まで引き上げる)。
                    if (!matchedAny && asset.SchemaVersion < DDriveSchema.Current)
                    {
                        stampOnly.Add(asset);
                    }
                }
            }

            var projectPlan = new List<IProjectMigration>();
            if (projectMigrations != null)
            {
                foreach (var migration in projectMigrations)
                {
                    if (migration == null)
                    {
                        continue;
                    }

                    if (settings == null || !settings.HasAppliedMigration(migration.Id))
                    {
                        projectPlan.Add(migration);
                    }
                }
            }

            return new MigrationPlan(dataPlan, projectPlan, stampOnly, orderedMigrations);
        }

        // 実プロジェクト(発見された全マイグレーション × 実データ)を対象にした計画。
        // ドライラン表示・CI.MigrateCheck・更新ツール(P-8)から呼ぶ想定。
        public static MigrationPlan PlanProject()
        {
            return Plan(
                new List<IDataMigration>(DiscoverDataMigrations()),
                FindAllProjectAssets(),
                new List<IProjectMigration>(DiscoverProjectMigrations()),
                DDriveProjectSettings.instance);
        }

        public static bool HasPendingMigrations() => PlanProject().TotalCount > 0;

        // ── 適用(Undo + VersionStampSuppression + DDriveAssetSave 経由の保存) ──

        public static MigrationContext Apply(MigrationPlan plan, DDriveProjectSettings settings)
        {
            var context = new MigrationContext(dryRun: false);
            if (plan == null || plan.TotalCount == 0)
            {
                return context;
            }

            // P2-3 — 67 件マイグレートしたら Ctrl+Z を 67 回、を避けるため 1 回の Apply を 1 つの
            // Undo グループにまとめる。
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("D-Drive Migration");
            var undoGroup = Undo.GetCurrentGroup();

            using (VersionStampSuppression.Scope())
            {
                // DataMigrations(Plan が単発一致で見積もったもの)と SchemaStampOnly の両方から、
                // 処理対象アセットの重複無し一覧を作る(1 アセットにつき 1 回だけ再評価ループを回す)。
                var assetsToProcess = new List<AssetDataBase>();
                var seen = new HashSet<AssetDataBase>();
                foreach (var entry in plan.DataMigrations)
                {
                    if (entry.Asset != null && seen.Add(entry.Asset))
                    {
                        assetsToProcess.Add(entry.Asset);
                    }
                }

                foreach (var asset in plan.SchemaStampOnly)
                {
                    if (asset != null && seen.Add(asset))
                    {
                        assetsToProcess.Add(asset);
                    }
                }

                foreach (var asset in assetsToProcess)
                {
                    Undo.RecordObject(asset, "D-Drive Migration");

                    try
                    {
                        ApplyToSingleAsset(asset, plan.OrderedDataMigrations, context);
                    }
                    catch (Exception e)
                    {
                        // P2-3 — 1 つの実装が投げても Apply 全体を落とさない(CLAUDE.md §0-4)。
                        Debug.LogWarning($"[DDrive][Migration] {AssetDatabase.GetAssetPath(asset)} のマイグレーションに失敗したためスキップしました: {e.Message}");
                        continue;
                    }

                    EditorUtility.SetDirty(asset);
                }

                foreach (var migration in plan.ProjectMigrations)
                {
                    try
                    {
                        migration.Migrate(context);
                        settings?.MarkMigrationApplied(migration.Id);
                        context.Note($"{migration.Id}: プロジェクト全体のマイグレーション");
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[DDrive][Migration] プロジェクトマイグレーション '{migration.Id}' に失敗したためスキップしました: {e.Message}");
                    }
                }

                DDriveAssetSave.SaveAllSuppressed();
            }

            Undo.CollapseUndoOperations(undoGroup);

            return context;
        }

        // 1 アセットぶんの「1 つ適用するたびに残りを再評価する」ループ(P2-3)。
        // AppliesTo が直前の Migrate() によるフィールド変更に依存する多段連鎖でも正しく解決できる。
        // 同じマイグレーションを 2 度適用しないよう Id で防御する(Migrate 中に例外的に SchemaVersion が
        // 進まない実装があっても無限ループにならない)。
        private static void ApplyToSingleAsset(
            AssetDataBase asset,
            IReadOnlyList<IDataMigration> orderedMigrations,
            MigrationContext context)
        {
            var appliedIds = new HashSet<string>(StringComparer.Ordinal);
            var progressed = true;

            while (progressed)
            {
                progressed = false;

                foreach (var migration in orderedMigrations)
                {
                    if (appliedIds.Contains(migration.Id))
                    {
                        continue;
                    }

                    if (migration.FromSchema > asset.SchemaVersion || asset.SchemaVersion >= migration.ToSchema)
                    {
                        continue;
                    }

                    if (!SafeAppliesTo(migration, asset))
                    {
                        continue;
                    }

                    migration.Migrate(asset, context);
                    asset.SchemaVersion = migration.ToSchema;
                    appliedIds.Add(migration.Id);
                    // IDataMigration の Id は「Data マイグレーションのみの二重適用防止」用の台帳
                    // (SchemaVersion 比較)で足りるため、IProjectMigration とは別に AppliedMigrationIds へは
                    // 記録しない(将来 Data/Project で同じ Id を使ったときの誤認を避ける、[47] P2-3)。
                    context.Note($"{migration.Id}: {AssetDatabase.GetAssetPath(asset)} (SchemaVersion {migration.FromSchema}->{migration.ToSchema})");
                    progressed = true;
                    break; // SchemaVersion が変わったので先頭から再評価する
                }
            }

            // P1-4/P1-5 — 適用対象のマイグレーションが尽きても Current に届いていなければ、
            // そのまま刻印する(「マイグレーション(適用)で必ず解消する」という Validator の案内を成立させる)。
            // Data 側の二重適用防止は SchemaVersion 比較だけで足りるため、settings.AppliedMigrationIds
            // (IProjectMigration 専用の台帳、[47] P2-3)には何も記録しない。
            if (asset.SchemaVersion < DDriveSchema.Current)
            {
                asset.SchemaVersion = DDriveSchema.Current;
            }
        }

        private static bool SafeAppliesTo(IDataMigration migration, AssetDataBase asset)
        {
            try
            {
                return migration.AppliesTo(asset);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DDrive][Migration] '{migration.Id}'.AppliesTo が例外を投げました({AssetDatabase.GetAssetPath(asset)}): {e.Message}");
                return false;
            }
        }

        public static MigrationContext ApplyToProject()
        {
            var plan = PlanProject();
            return Apply(plan, DDriveProjectSettings.instance);
        }
    }
}
