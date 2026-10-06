using System;
using System.Collections.Generic;
using System.Text;
using DDrive.Editor.Menu;
using DDrive.Foundation.Validation;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Validation
{
    // [64_review_m6_2026-10-06.md] GF-R-11 — `Validation > Run All` の「プロジェクト全体の指摘」(Data に紐付かない = `(project)`)に付いた
    // `FixAction` を実行する経路。Data ごとの個別検証(`DataValidationSection`)は全体向けの Validator を除外するため、
    // 全体の指摘の修正ボタンはここ(メニュー `Validation > 全体の指摘を修正`)から押す。
    // Run All のコンソール出力も、修正できる指摘があればこのメニューを案内する(`CI.LogSummary`)。
    // 実行前に対象の一覧を確認ダイアログで出す(バッチモードでは確認なしで実行)。修正が警告を出したときは捨てずにコンソールへ出す。
    public static class ProjectWideValidationFixes
    {
        public const string MenuName = "全体の指摘を修正";

        [MenuItem(DDriveMenu.Validation + MenuName)]
        public static void ApplyMenuItem()
        {
            var fixable = FindFixable(CI.RunValidation());
            if (fixable.Count == 0)
            {
                Debug.Log("[DDrive][Validation] 修正できる全体の指摘はありません。");
                return;
            }

            if (!Application.isBatchMode)
            {
                var sb = new StringBuilder();
                foreach (var report in fixable)
                {
                    sb.Append("・").Append(report.Result.Message).Append('\n');
                }

                if (!EditorUtility.DisplayDialog("全体の指摘を修正", $"次の {fixable.Count} 件を修正します。\n\n{sb}", "修正する", "キャンセル"))
                {
                    return;
                }
            }

            Apply(fixable);
            CI.RunAllMenuItem(); // 修正後の状態を確認できるよう、もう一度 Run All の結果を出す
        }

        // asset == null(プロジェクト全体)の Error / Warning で、FixAction を持つもの。
        public static List<ValidationReport> FindFixable(IReadOnlyList<ValidationReport> reports)
        {
            var result = new List<ValidationReport>();
            if (reports == null)
            {
                return result;
            }

            foreach (var report in reports)
            {
                if (report.Asset == null
                    && report.Result.FixAction != null
                    && report.Result.Severity != ValidationSeverity.Info)
                {
                    result.Add(report);
                }
            }

            return result;
        }

        // 戻り値: 例外なく完了した修正の数(例外は警告にして次へ進む = 例外で止めない)。
        public static int Apply(IReadOnlyList<ValidationReport> fixable)
        {
            var done = 0;
            foreach (var report in fixable)
            {
                try
                {
                    report.Result.FixAction();
                    done++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DDrive][Validation] 修正に失敗しました({report.Result.Code}): {e.GetType().Name}: {e.Message}");
                }
            }

            Debug.Log($"[DDrive][Validation] 全体の指摘の修正を {done}/{fixable.Count} 件実行しました。");
            return done;
        }
    }
}
