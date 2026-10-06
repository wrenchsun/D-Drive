using System.Collections.Generic;
using DDrive.Editor.Migration;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;

namespace DDrive.Editor.Validation
{
    // [64_review_m6_2026-10-06.md] GF-R-01(a) / [42_distribution.md] §4.3 — 旧形式(`m_Script: {fileID: 0}` +
    // `m_EditorClassIdentifier: DDrive.Runtime:…`)の Timeline(.playable)が残っていることを `Validation > Run All` と
    // `CI.ValidateAll` で検出する。マイグレーションの Id を記録した「後」に入ってきた旧形式(古いブランチのマージ・他プロジェクトからのコピー)も対象。
    // 旧形式のままだと Player ではトラック / マーカーが読み込まれず、Editor でも新規インポート時に読めない。
    //
    // 重さ: Assets/ の .playable を文字列で走査するだけ(MonoScript の解決は `m_Script: {fileID: 0}` があるファイルがあるときだけ)。
    // Error にする理由: 対象が残っている = Player ビルドでカットシーンが壊れる確定の状態で、v1.4.0 で新設する検査のため
    // 既存の持ち込み先の CI を後から赤くしない(§5.8 は公開済みの検査の昇格の規則)。修正は FixAction(`MigratePaths`)か
    // `Tools > D-Drive > Update > マイグレーション(適用)`。
    // ValidatorRegistry.RunAll は Validator を asset ごとに呼ぶため、1 回の Run All(= 1 つの ValidationContext)につき 1 回だけ報告する。
    public sealed class CutsceneTimelineLegacyReferenceValidator : IUniversalValidator
    {
        public const string Code = "DD-CUTSCENE-LEGACY-SCRIPT-REF";

        private static ValidationContext _reportedForCtx;

        public AssetType Target => AssetType.None;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (ctx != null && _reportedForCtx == ctx)
            {
                yield break;
            }

            _reportedForCtx = ctx;

            var paths = CutsceneTimelineScriptReferenceMigration.FindTargetPaths();
            if (paths.Count == 0)
            {
                yield break;
            }

            var fixTargets = paths.ToArray();
            yield return ValidationResult.Error(
                $"旧形式のスクリプト参照を持つ Timeline(.playable)が {paths.Count} 件あります: {string.Join(", ", paths)}。" +
                "このままだと Player ではカットシーンのトラック / マーカーが読み込まれません。" +
                "Tools > D-Drive > Update > マイグレーション(適用)(または修正ボタン)で直してください。",
                () => CutsceneTimelineScriptReferenceMigration.MigratePaths(new MigrationContext(false), fixTargets),
                Code);
        }
    }
}
