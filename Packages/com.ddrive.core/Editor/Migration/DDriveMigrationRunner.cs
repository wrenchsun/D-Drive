using System;
using System.Collections.Generic;
using DDrive.Editor.Settings;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using UnityEditor;

namespace DDrive.Editor.Migration
{
    // [42_distribution.md] §4.3 / §6 P-7(2026-09-20) — スキーマ版マイグレーションの実行基盤。
    //
    // 流れ: 発見(TypeCache、テストアセンブリ除外) → 対象探索(実データ、Compat のフィクスチャは除外) →
    // 計画(Plan、読み取りのみ = ドライランに使える) → 適用(Apply、Undo + VersionStampSuppression 内で
    // 保存)。`IValidator`/`CI.DiscoverValidators` と同じ設計を踏襲する。
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
            public int TotalCount => DataMigrations.Count + ProjectMigrations.Count;

            internal MigrationPlan(List<PlannedDataMigration> dataMigrations, List<IProjectMigration> projectMigrations)
            {
                DataMigrations = dataMigrations;
                ProjectMigrations = projectMigrations;
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

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Compat/Fixtures/", StringComparison.Ordinal))
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(path);
                if (asset != null)
                {
                    result.Add(asset);
                }
            }

            return result;
        }

        // ── 計画(読み取りのみ。実データを一切変更しない = ドライラン用にそのまま使える) ──

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
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    if (asset == null)
                    {
                        continue;
                    }

                    foreach (var migration in orderedMigrations)
                    {
                        if (asset.SchemaVersion < migration.ToSchema && migration.AppliesTo(asset))
                        {
                            dataPlan.Add(new PlannedDataMigration(migration, asset));
                        }
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

            return new MigrationPlan(dataPlan, projectPlan);
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

            using (VersionStampSuppression.Scope())
            {
                foreach (var entry in plan.DataMigrations)
                {
                    Undo.RecordObject(entry.Asset, "D-Drive Migration: " + entry.Migration.Id);
                    entry.Migration.Migrate(entry.Asset, context);
                    entry.Asset.SchemaVersion = entry.Migration.ToSchema;
                    EditorUtility.SetDirty(entry.Asset);
                    settings?.MarkMigrationApplied(entry.Migration.Id);
                    context.Note($"{entry.Migration.Id}: {AssetDatabase.GetAssetPath(entry.Asset)} " +
                                 $"(SchemaVersion {entry.Migration.FromSchema}->{entry.Migration.ToSchema})");
                }

                foreach (var migration in plan.ProjectMigrations)
                {
                    migration.Migrate(context);
                    settings?.MarkMigrationApplied(migration.Id);
                    context.Note($"{migration.Id}: プロジェクト全体のマイグレーション");
                }

                DDriveAssetSave.SaveAllSuppressed();
            }

            return context;
        }

        public static MigrationContext ApplyToProject()
        {
            var plan = PlanProject();
            return Apply(plan, DDriveProjectSettings.instance);
        }
    }
}
