using System;

namespace DDrive.Foundation.Validation
{
    public readonly struct ValidationResult
    {
        public readonly ValidationSeverity Severity;
        public readonly string Message;
        public readonly Action FixAction;

        public ValidationResult(ValidationSeverity severity, string message, Action fixAction = null)
        {
            Severity = severity;
            Message = message;
            FixAction = fixAction;
        }

        public static ValidationResult Error(string message, Action fixAction = null) => new(ValidationSeverity.Error, message, fixAction);
        public static ValidationResult Warning(string message, Action fixAction = null) => new(ValidationSeverity.Warning, message, fixAction);
        public static ValidationResult Info(string message, Action fixAction = null) => new(ValidationSeverity.Info, message, fixAction);
    }
}
