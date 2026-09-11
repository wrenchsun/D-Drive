using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DDrive.Editor.Menu;
using DDrive.Editor.Validation;
using DDrive.Foundation.Data;
using DDrive.Foundation.Validation;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor
{
    // Unity -batchmode -executeMethod DDrive.Editor.CI.ValidateAll から呼ばれる CI エントリポイント。
    // 個々の IValidator 実装は各アセット種別のチケットで追加されるだけでよく、ここは改修不要(NFR-7)。
    public static class CI
    {
        private const string DefaultOutputPath = "TestResults/ddrive-validation.junit.xml";

        [MenuItem(DDriveMenu.Validation + "Run All")]
        public static void RunAllMenuItem()
        {
            LogSummary(RunValidation());
        }

        public static void ValidateAll()
        {
            var reports = RunValidation();
            var forbiddenApiViolations = ForbiddenApiScanner.Scan("Assets/DDrive");

            WriteJUnitXml(reports, forbiddenApiViolations, ResolveOutputPath());
            LogSummary(reports);
            LogForbiddenApiViolations(forbiddenApiViolations);

            var hasError = forbiddenApiViolations.Count > 0;
            for (var i = 0; i < reports.Count; i++)
            {
                if (reports[i].Result.Severity == ValidationSeverity.Error)
                {
                    hasError = true;
                    break;
                }
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(hasError ? 1 : 0);
            }
        }

        public static IReadOnlyList<ValidationReport> RunValidation()
        {
            var registry = new ValidatorRegistry();
            foreach (var validator in DiscoverValidators())
            {
                registry.Register(validator);
            }

            return registry.RunAll(LoadAllAssetDataAssets());
        }

        private static IEnumerable<IValidator> DiscoverValidators()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                // テスト asmdef(DDrive.Tests.*)内のダミー実装は対象外。テスト実行後に Editor の
                // Run All / Regenerate が拾ってしまい、"always fails" 等の偽の結果を出すため。
                if (asm.GetName().Name.StartsWith("DDrive.Tests", StringComparison.Ordinal))
                {
                    continue;
                }
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }

                if (types == null)
                {
                    continue;
                }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || t.IsInterface || !typeof(IValidator).IsAssignableFrom(t))
                    {
                        continue;
                    }

                    if (t.GetConstructor(Type.EmptyTypes) == null)
                    {
                        continue;
                    }

                    if (Activator.CreateInstance(t) is IValidator validator)
                    {
                        yield return validator;
                    }
                }
            }
        }

        private static List<AssetDataBase> LoadAllAssetDataAssets()
        {
            var result = new List<AssetDataBase>();
            var guids = AssetSearch.FindAssets("t:" + nameof(AssetDataBase));

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

        private static string ResolveOutputPath()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-ddriveOutput")
                {
                    return args[i + 1];
                }
            }

            return DefaultOutputPath;
        }

        private static void WriteJUnitXml(IReadOnlyList<ValidationReport> reports, IReadOnlyList<ForbiddenApiScanner.Violation> forbiddenApiViolations, string outputPath)
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var entries = new List<(string assetPath, ValidationResult result)>(reports.Count + forbiddenApiViolations.Count);
            foreach (var report in reports)
            {
                var assetPath = report.Asset != null ? AssetDatabase.GetAssetPath(report.Asset) : "(unknown)";
                entries.Add((assetPath, report.Result));
            }

            foreach (var violation in forbiddenApiViolations)
            {
                entries.Add((violation.FilePath + ":" + violation.Line, ValidationResult.Error(violation.Message)));
            }

            File.WriteAllText(outputPath, BuildJUnitXml(entries));
        }

        private static void LogForbiddenApiViolations(IReadOnlyList<ForbiddenApiScanner.Violation> violations)
        {
            foreach (var violation in violations)
            {
                Debug.LogError($"[DDrive][ForbiddenApi] {violation.FilePath}:{violation.Line}: {violation.Message}");
            }
        }

        // AssetDatabase に依存しない純粋な整形ロジック。EditMode テストから直接検証できる。
        public static string BuildJUnitXml(IReadOnlyList<(string assetPath, ValidationResult result)> entries)
        {
            var failures = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].result.Severity == ValidationSeverity.Error)
                {
                    failures++;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine($"<testsuite name=\"DDrive.Validation\" tests=\"{entries.Count}\" failures=\"{failures}\">");

            foreach (var (assetPath, result) in entries)
            {
                var message = XmlEscape(result.Message);
                sb.AppendLine($"  <testcase classname=\"{XmlEscape(assetPath)}\" name=\"{message}\">");

                if (result.Severity == ValidationSeverity.Error)
                {
                    sb.AppendLine($"    <failure message=\"{message}\">{result.Severity}</failure>");
                }

                sb.AppendLine("  </testcase>");
            }

            sb.AppendLine("</testsuite>");
            return sb.ToString();
        }

        private static string XmlEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("&", "&amp;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        private static void LogSummary(IReadOnlyList<ValidationReport> reports)
        {
            var errors = 0;
            var warnings = 0;

            foreach (var report in reports)
            {
                var assetPath = report.Asset != null ? AssetDatabase.GetAssetPath(report.Asset) : "(unknown)";
                switch (report.Result.Severity)
                {
                    case ValidationSeverity.Error:
                        errors++;
                        Debug.LogError($"[DDrive][Validation] {assetPath}: {report.Result.Message}");
                        break;
                    case ValidationSeverity.Warning:
                        warnings++;
                        Debug.LogWarning($"[DDrive][Validation] {assetPath}: {report.Result.Message}");
                        break;
                    default:
                        Debug.Log($"[DDrive][Validation] {assetPath}: {report.Result.Message}");
                        break;
                }
            }

            Debug.Log($"[DDrive] Validation complete: {reports.Count} results, {errors} errors, {warnings} warnings.");
        }
    }
}
