using DDrive.Editor.Menu;
using DDrive.Editor.Settings;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Migration
{
    // [42_distribution.md] §4.3/§6 P-7(2026-09-20) — マイグレーションのドライラン・適用の最小 UI。
    // P-8(更新ツールウィンドウ)に統合される前提の暫定メニュー(ログはコンソールへ出すだけ)。
    public static class MigrationMenu
    {
        [MenuItem(DDriveMenu.Update + "マイグレーション(ドライラン)")]
        public static void DryRunMenuItem()
        {
            var plan = DDriveMigrationRunner.PlanProject();
            if (plan.TotalCount == 0)
            {
                Debug.Log("[DDrive][Migration] 未適用のマイグレーションはありません。");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append($"[DDrive][Migration] ドライラン: 対象 {plan.TotalCount} 件(Data {plan.DataMigrations.Count} 件 / プロジェクト {plan.ProjectMigrations.Count} 件)。実データは変更していません。\n");
            foreach (var entry in plan.DataMigrations)
            {
                sb.Append($"  - {entry.Migration.Id}: {AssetDatabase.GetAssetPath(entry.Asset)} " +
                          $"(SchemaVersion {entry.Asset.SchemaVersion} -> {entry.Migration.ToSchema})\n");
            }

            foreach (var migration in plan.ProjectMigrations)
            {
                sb.Append($"  - {migration.Id}: プロジェクト全体のマイグレーション\n");
            }

            Debug.Log(sb.ToString());
        }

        [MenuItem(DDriveMenu.Update + "マイグレーション(適用)")]
        public static void ApplyMenuItem()
        {
            var plan = DDriveMigrationRunner.PlanProject();
            if (plan.TotalCount == 0)
            {
                Debug.Log("[DDrive][Migration] 未適用のマイグレーションはありません。");
                return;
            }

            var context = DDriveMigrationRunner.Apply(plan, DDriveProjectSettings.instance);
            Debug.Log($"[DDrive][Migration] {plan.TotalCount} 件のマイグレーションを適用しました。\n" +
                      string.Join("\n", context.Log));
        }
    }
}
