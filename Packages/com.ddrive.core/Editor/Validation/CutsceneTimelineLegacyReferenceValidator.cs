using System;
using System.Collections.Generic;
using DDrive.Editor.Migration;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using UnityEngine;

namespace DDrive.Editor.Validation
{
    // [64_review_m6_2026-10-06.md] GF-R-01(a) / [42_distribution.md] §4.3 — 旧形式(`m_Script: {fileID: 0}` +
    // `m_EditorClassIdentifier: DDrive.Runtime:…`)の Timeline(.playable)が残っていることを `Validation > Run All` と
    // `CI.ValidateAll` で検出する。マイグレーションの Id を記録した「後」に入ってきた旧形式(古いブランチのマージ・他プロジェクトからのコピー)も対象。
    // 旧形式のままだと Player ではトラック / マーカーが読み込まれず、Editor でも新規インポート時に読めない。
    //
    // 重さ: Assets/ の .playable を文字列で走査するだけ(MonoScript の解決は `m_Script: {fileID: 0}` があるファイルがあるときだけ)。
    // Error にする理由(§5.8 の「緊急(データ破損を招く等)で最初から Error」の例外。[64] GF-R-12): 検出される状態 = Player ビルドで
    // カットシーンのトラック / マーカーが読み込まれない(かつ新規インポートでは Editor でも読めない)ことが確定した状態で、Warning では
    // CI を通ってそのまま Player に出る。同じ状態では `CI.MigrateCheck` も赤で、「更新を適用」(マイグレーション)1 回で両方消えるので、
    // 持ち込み先が新たに踏む手順は増えない。CHANGELOG の「互換性」節に例外として記録する。
    // 直し方: `Tools > D-Drive > Update > マイグレーション(適用)`、または `Tools > D-Drive > Validation > 全体の指摘を修正`(FixAction = `MigratePaths`。GF-R-11)。
    // Run All では `(project)` として 1 回だけ呼ばれる。`_reportedForCtx` は同じ ValidationContext で 2 回呼ばれたときの重複報告ガード
    // (WeakReference なので最後の Context を握り続けない。GF-R-15)。
    public sealed class CutsceneTimelineLegacyReferenceValidator : IUniversalValidator
    {
        public const string Code = "DD-CUTSCENE-LEGACY-SCRIPT-REF";

        private static WeakReference<ValidationContext> _reportedForCtx;

        public AssetType Target => AssetType.None;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (ctx != null && _reportedForCtx != null && _reportedForCtx.TryGetTarget(out var reported) && reported == ctx)
            {
                yield break;
            }

            _reportedForCtx = ctx != null ? new WeakReference<ValidationContext>(ctx) : null;

            var paths = CutsceneTimelineScriptReferenceMigration.FindTargetPaths();
            if (paths.Count == 0)
            {
                yield break;
            }

            var fixTargets = paths.ToArray();
            yield return ValidationResult.Error(
                $"旧形式のスクリプト参照を持つ Timeline(.playable)が {paths.Count} 件あります: {string.Join(", ", paths)}。" +
                "このままだと Player ではカットシーンのトラック / マーカーが読み込まれません。" +
                "Tools > D-Drive > Update > マイグレーション(適用)で直してください(Validation > 全体の指摘を修正 でも直せます)。",
                () => FixPaths(fixTargets),
                Code);
        }

        // [64] GF-R-11 — 修正が警告(未保存の Timeline・ロック等)を出したときは捨てずにコンソールへ出す(無言の no-op にしない)。
        private static void FixPaths(string[] paths)
        {
            var context = new MigrationContext(false);
            var rewritten = CutsceneTimelineScriptReferenceMigration.MigratePaths(context, paths);
            var log = string.Join("\n", context.Log);
            if (context.WarningCount > 0)
            {
                Debug.LogWarning($"[DDrive][Validation] {Code}: 一部の Timeline を修正できませんでした({context.WarningCount} 件の警告)。\n{log}");
            }
            else
            {
                Debug.Log($"[DDrive][Validation] {Code}: {rewritten} 件の Timeline を修正しました。\n{log}");
            }
        }
    }
}
