using System;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Codegen;
using DDrive.Editor.Migration;
using DDrive.Editor.Settings;
using DDrive.Foundation.Validation;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 手順 5/§6 P-8(2026-09-20) — `UpdateActions.Steps` の実配線
    // (実 AssetDatabase / Addressables / Validation を呼ぶ側)。`UpdateWindow` から呼ぶ。
    // ここは EditMode テストの対象にしない([42_distribution.md] §6 P-8 の指示「更新を適用の実行は
    // EditMode テストから呼ばない。検証は UpdateActions のフェイクの段で行う」)。
    public static class UpdateStepsFactory
    {
        public static UpdateActions.Steps CreateRealSteps(string currentVersion)
        {
            return new UpdateActions.Steps
            {
                Migrate = MigrateStep,
                RegenerateIdsAndTuning = RegenerateIdsAndTuningStep,
                SyncAddressables = SyncAddressablesStep,
                RunValidation = RunValidationStep,
                MarkApplied = () => DDriveProjectSettings.instance.LastAppliedVersion = currentVersion,
            };
        }

        private static UpdateActions.StepOutcome MigrateStep()
        {
            const string name = "マイグレーション";
            try
            {
                var plan = DDriveMigrationRunner.PlanProject();
                if (plan.TotalCount == 0)
                {
                    return new UpdateActions.StepOutcome(name, true, "未適用のマイグレーションはありません。");
                }

                var context = DDriveMigrationRunner.Apply(plan, DDriveProjectSettings.instance);
                return new UpdateActions.StepOutcome(name, true, $"{plan.TotalCount} 件を適用しました。\n" + string.Join("\n", context.Log));
            }
            catch (Exception e)
            {
                return new UpdateActions.StepOutcome(name, false, "失敗: " + e.Message);
            }
        }

        private static UpdateActions.StepOutcome RegenerateIdsAndTuningStep()
        {
            const string name = "ID/Tuning 再生成";
            try
            {
                var idResult = AssetIdGenerator.Regenerate();
                if (!idResult.Success)
                {
                    return new UpdateActions.StepOutcome(name, false, $"重複 ID が {idResult.Duplicates.Count} 件あります。コンソールを確認してください。");
                }

                var tuningResult = TuningCodegen.Regenerate();
                if (!tuningResult.Success)
                {
                    return new UpdateActions.StepOutcome(name, false, "Tuning キーの再生成に失敗しました。");
                }

                return new UpdateActions.StepOutcome(name, true, $"AssetIds {idResult.TotalCount} 件(新規 {idResult.AssignedCount})・Tuning {tuningResult.TotalCount} 件を再生成しました。");
            }
            catch (Exception e)
            {
                return new UpdateActions.StepOutcome(name, false, "失敗: " + e.Message);
            }
        }

        private static UpdateActions.StepOutcome SyncAddressablesStep()
        {
            const string name = "Addressables 登録を同期";
            if (!AddressablesSync.IsAvailable)
            {
                return new UpdateActions.StepOutcome(name, false, "Addressables が初期化されていません(セットアップウィザードで初期化してください)。");
            }

            try
            {
                var (fixedAssets, catalogs, missingCatalog) = AddressablesSync.SyncAll(log: true);
                return new UpdateActions.StepOutcome(name, true, $"修正 {fixedAssets} 件・カタログ {catalogs} 件・カタログ未登録 {missingCatalog} 件。");
            }
            catch (Exception e)
            {
                return new UpdateActions.StepOutcome(name, false, "失敗: " + e.Message);
            }
        }

        private static UpdateActions.StepOutcome RunValidationStep()
        {
            const string name = "Validation";
            try
            {
                var reports = DDrive.Editor.CI.RunValidation();
                var errors = 0;
                var warnings = 0;
                for (var i = 0; i < reports.Count; i++)
                {
                    if (reports[i].Result.Severity == ValidationSeverity.Error)
                    {
                        errors++;
                    }
                    else if (reports[i].Result.Severity == ValidationSeverity.Warning)
                    {
                        warnings++;
                    }
                }

                // [42_distribution.md] §4.2 手順 5-4 — ここでの Error は「更新そのものの失敗」ではなく
                // 既存データの品質指摘(§5.8 の 2 段階ルールにより更新直後に Error が新設されることは無い)。
                // そのため件数を表示するだけで、この段自体は例外が出ない限り成功として扱う
                // (Error があっても LastAppliedVersion は更新される。人が別途 Run All で確認する)。
                return new UpdateActions.StepOutcome(name, true, $"{reports.Count} 件(Error {errors} / Warning {warnings})。");
            }
            catch (Exception e)
            {
                return new UpdateActions.StepOutcome(name, false, "失敗: " + e.Message);
            }
        }
    }
}
