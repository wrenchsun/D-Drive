using System;

namespace DDrive.Foundation.Validation
{
    public readonly struct ValidationResult
    {
        public readonly ValidationSeverity Severity;
        public readonly string Message;
        public readonly Action FixAction;

        // [42_distribution.md] §5.8 / §5.11-8(P-3、2026-09-20) — 検査ごとに安定した識別子を持たせ、
        // 「どの検査が Error/Warning か」をゴールデンテスト(ValidatorSeverityRegistryTests)で固定できるように
        // する。省略可能引数として追加したため既存呼び出し(Error(message)・Warning(message, fixAction) 等)は
        // 一切変更不要(空文字のまま = ゴールデン対象外。docs/42 §5.11-8「Code 未設定の結果はゴールデン対象外に
        // してよい」)。新規に書く Validator は Code を必ず渡す方針([12_review.md] §3「互換性」節)。
        public readonly string Code;

        // [1010_editor_ux_migration.md] §2 検証(UX-1、2026-10-12) — この結果が結びつく欄のフィールド名(例: "Prefab" / "Params")。
        // 新デザインの専用エディターが「欄の下にインライン表示する」ための任意の付加情報で、省略(null)なら従来どおり
        // メッセージ照合・「その他の検証」に出る。Severity / Code / Message / FixAction は一切変えない(追加のみ = MINOR)。
        // 付けるときは WithField を使う(例: ValidationResult.Error("Prefab が未設定です").WithField("Prefab"))。
        public readonly string Field;

        public ValidationResult(ValidationSeverity severity, string message, Action fixAction = null, string code = "")
        {
            Severity = severity;
            Message = message;
            FixAction = fixAction;
            Code = code ?? string.Empty;
            Field = null;
        }

        private ValidationResult(ValidationResult source, string field)
        {
            Severity = source.Severity;
            Message = source.Message;
            FixAction = source.FixAction;
            Code = source.Code;
            Field = field;
        }

        // 結びつく欄名を付けた同じ結果を返す。
        public ValidationResult WithField(string field) => new ValidationResult(this, field);

        public static ValidationResult Error(string message, Action fixAction = null, string code = "") => new(ValidationSeverity.Error, message, fixAction, code);
        public static ValidationResult Warning(string message, Action fixAction = null, string code = "") => new(ValidationSeverity.Warning, message, fixAction, code);
        public static ValidationResult Info(string message, Action fixAction = null, string code = "") => new(ValidationSeverity.Info, message, fixAction, code);
    }
}
