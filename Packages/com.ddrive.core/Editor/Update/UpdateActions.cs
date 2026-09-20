using System;
using System.Collections.Generic;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 手順 5/§6 P-8(2026-09-20) — 「更新を適用」の 4 段
    // (マイグレーション → ID/Tuning 再生成 → Addressables 同期 → Validation)+ LastAppliedVersion 更新を
    // 「ウィンドウ内で純粋に検証できる」形に切り出したもの。各段は `Func<StepOutcome>` として注入するため、
    // EditMode テストは実 Unity API(AssetDatabase 等)に一切触れずに「途中で失敗したら以降を実行しない」
    // 「全段成功したときだけ LastAppliedVersion 相当の後処理が走る」を固定できる。
    // 実際の Unity 呼び出しは UpdateStepsFactory(このチケットの実配線側)が組み立てる。
    public static class UpdateActions
    {
        public readonly struct StepOutcome
        {
            public readonly string Name;
            public readonly bool Success;
            public readonly string Message;

            public StepOutcome(string name, bool success, string message)
            {
                Name = name;
                Success = success;
                Message = message;
            }
        }

        public sealed class Steps
        {
            // 順番はそのまま実行順([42_distribution.md] §4.2 手順 5 の 1〜4)。
            public Func<StepOutcome> Migrate;
            public Func<StepOutcome> RegenerateIdsAndTuning;
            public Func<StepOutcome> SyncAddressables;
            public Func<StepOutcome> RunValidation;

            // 全段が成功したときだけ 1 回呼ばれる(手順 5「LastAppliedVersion を更新」)。
            public Action MarkApplied;
        }

        public sealed class Result
        {
            public readonly List<StepOutcome> StepOutcomes = new();

            // 1 件も失敗が無く、かつ 1 段以上実行された(何も注入されていない Steps で呼ばれた場合は
            // false のままにする = 「何も起きていない」を「成功」と誤認しない)。
            public bool AllSucceeded { get; internal set; }
            public bool MarkAppliedCalled { get; internal set; }
        }

        // 途中で失敗したら以降を実行しない。全段成功したときだけ MarkApplied を呼ぶ。
        public static Result Apply(Steps steps)
        {
            var result = new Result();
            if (steps == null)
            {
                return result;
            }

            var stepFuncs = new[] { steps.Migrate, steps.RegenerateIdsAndTuning, steps.SyncAddressables, steps.RunValidation };

            foreach (var fn in stepFuncs)
            {
                if (fn == null)
                {
                    continue;
                }

                var outcome = fn();
                result.StepOutcomes.Add(outcome);

                if (!outcome.Success)
                {
                    result.AllSucceeded = false;
                    return result; // [42_distribution.md] §6 P-8 AC「各段の成否をログに出し、途中で失敗したら止まる」。
                }
            }

            result.AllSucceeded = result.StepOutcomes.Count > 0;
            if (result.AllSucceeded)
            {
                steps.MarkApplied?.Invoke();
                result.MarkAppliedCalled = true;
            }

            return result;
        }
    }
}
