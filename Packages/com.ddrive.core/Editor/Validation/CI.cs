using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DDrive.Editor.Codegen;
using DDrive.Editor.Menu;
using DDrive.Editor.Migration;
using DDrive.Editor.Settings;
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
            // [11_tasks.md] M-4 — 行単位の許可コメントと設定の許可リストを反映した走査結果を使う。
            var forbiddenApiReport = ForbiddenApiScanner.ScanDetailed(
                ResolveForbiddenApiScanRoot(), DDriveProjectSettings.instance.ForbiddenApiAllowEntries);
            var forbiddenApiViolations = forbiddenApiReport.Violations;

            WriteJUnitXml(reports, forbiddenApiViolations, ResolveOutputPath(), forbiddenApiReport.Notices);
            LogSummary(reports);
            LogForbiddenApiViolations(forbiddenApiViolations);
            LogForbiddenApiNotices(forbiddenApiReport.Notices);

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

        // Unity -batchmode -executeMethod DDrive.Editor.CI.RegenerateIds から呼ばれる、6-1 の CI 用エントリポイント。
        // ID 定数(Assets/Generated/AssetIds.g.cs)を再生成するだけの単体メソッド。
        // 「生成漏れ」の検出自体は呼び出し側(CI ワークフロー / ローカルスクリプト)が
        // この実行後に `git diff --exit-code` で行う(このメソッド自体は重複 ID があるときだけ fail する)。
        public static void RegenerateIds()
        {
            var result = AssetIdGenerator.Regenerate();

            if (result.Success)
            {
                Debug.Log($"[DDrive] AssetIds regenerated: {result.TotalCount} entries, {result.AssignedCount} newly assigned.");
            }
            else
            {
                foreach (var d in result.Duplicates)
                {
                    Debug.LogError($"[DDrive] Duplicate AssetId 0x{d.Id:X} between '{d.PathA}' and '{d.PathB}'. Fix before regenerating.");
                }
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(result.Success ? 0 : 1);
            }
        }

        // [42_distribution.md] §4.3/§4.2 手順 6/§6 P-7(2026-09-20) — 未適用のマイグレーションがあれば
        // fail する CI エントリポイント。ValidateAll の前段として run-ci.cmd から呼ぶ想定
        // (Unity -batchmode -executeMethod DDrive.Editor.CI.MigrateCheck)。
        // バッチモードでの exit と、テストからの検証(戻り値)を両立するため、実際の判定・ログは
        // DDriveMigrationRunner.HasPendingMigrations() へ委譲する(このメソッド自体は薄いラッパー)。
        public static void MigrateCheck()
        {
            var pending = DDriveMigrationRunner.HasPendingMigrations();

            if (pending)
            {
                Debug.LogError("[DDrive][Migration] 未適用のマイグレーションがあります。" +
                                "Tools > D-Drive > Update > マイグレーション(適用) を実行してください。");
            }
            else
            {
                Debug.Log("[DDrive][Migration] 未適用のマイグレーションはありません。");
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(pending ? 1 : 0);
            }
        }

        public static IReadOnlyList<ValidationReport> RunValidation() => RunValidation(includeProjectWideValidators: true);

        // 2026-09-17(docs/41_phase6_review_2026-09-17.md P2-6 (a)) —
        // `ValidatorRegistry.RunAll` は `IUniversalValidator` の結果も「その時渡されたアセット」の
        // `ValidationReport` にする。プロジェクト全体を 1 回まとめて見る Validator
        // (`SpecDiffValidator` の調整値の範囲チェック・`ContentHashCatalogCoverageValidator` の
        // カタログのラベル未登録)は 1 回だけ結果を出す作りなので、その Error が**無関係なアセットに
        // 紐付く**。「このアセットに Error があるか」をアセット単位で判定したい用途
        // (`SpecWebSender` の isPlaceholder)では false を渡して除外する。
        // 除外対象の定義は `DataValidationRunner.IsProjectWide`(docs/09 §11 で導入したものを共用。
        // 1 アセット単位で意味がある `IUniversalValidator`(ValueDef / Addressables 登録 / NetMode)は残す)。
        // 本筋は「全体結果は Asset を持たない別経路にする」だが、`ValidatorRegistry` は Foundation
        // (本チケットの担当範囲外)にあるため、ここでは呼び出し側で除外する方式にした。
        //
        // 2026-10-06(P-15 確認 Q-3): `PackageDependencyValidator`(導入済みパッケージの依存の宣言)は**プロジェクト全体の指摘**で、
        // どの Data にも属さない。これも `RunAll` に渡すと「たまたま最初に呼ばれた Data」(例: Anchor/Anim/ANC_Anim_Jump.asset)に
        // 紐付いて、無関係なアセットのパスが付いた警告になっていた。そのため `RunAll` には載せず、Data 0 件のときの
        // `RunAll` と同じ形(asset = null)で 1 回だけ実行して報告に足す(Run All 1 回につき 1 回。Validator 自身の
        // ValidationContext ガードも効く)。表示は `DescribeReportLocation`(asset が無ければ「(project)」)。
        public static IReadOnlyList<ValidationReport> RunValidation(bool includeProjectWideValidators)
        {
            var registry = new ValidatorRegistry();
            var projectScoped = new List<IValidator>();
            foreach (var validator in DiscoverValidators())
            {
                if (!includeProjectWideValidators && DataValidationRunner.IsProjectWide(validator))
                {
                    continue;
                }

                if (IsProjectScoped(validator))
                {
                    projectScoped.Add(validator);
                    continue;
                }

                registry.Register(validator);
            }

            var assets = LoadAllAssetDataAssets();
            var reports = new List<ValidationReport>(registry.RunAll(assets));
            if (projectScoped.Count > 0)
            {
                var context = new ValidationContext(assets);
                foreach (var validator in projectScoped)
                {
                    if (validator is not IUniversalValidator universal)
                    {
                        continue;
                    }

                    foreach (var result in universal.Validate(null, context))
                    {
                        reports.Add(new ValidationReport(null, result));
                    }

                    // SpecDiffValidator だけは「全体の指摘」(上の null の呼び出しで 1 回)に加えて、Data ごとの指摘(仕様書との差分・
                    // Placeholder の不一致)も出す。後者は従来どおり該当の Data に紐付ける(同じ context なので全体の指摘は繰り返さない)。
                    if (validator is SpecDiffValidator)
                    {
                        foreach (var asset in assets)
                        {
                            foreach (var result in universal.Validate(asset, context))
                            {
                                reports.Add(new ValidationReport(asset, result));
                            }
                        }
                    }
                }
            }

            return reports;
        }

        // Data に紐付けず、プロジェクト全体として報告する Validator(asset = null で呼ぶ)。
        // 2026-10-06(docs/58 GA-R-07): PackageDependencyValidator だけだった対象を、「プロジェクト全体の指摘」を出す Validator 全部
        // (`DataValidationRunner.IsProjectWide` の一覧 + `ProjectSetupValidator`)に広げた。件数・重さ・コードは変えず、紐付け先だけが
        // 「たまたま最初の Data」から「(project)」になる。判定は `DataValidationRunner.IsProjectScopedInRunAll` が唯一の定義。
        private static bool IsProjectScoped(IValidator validator) => DataValidationRunner.IsProjectScopedInRunAll(validator);

        // 報告の場所の表示(コンソール・JUnit の classname)。Data が無い(プロジェクト全体の)報告は「(project)」。
        private static string DescribeReportLocation(ValidationReport report)
            => report.Asset != null ? AssetDatabase.GetAssetPath(report.Asset) : "(project)";

        // 2026-09-17(U-13): 各専用エディタの「個別検証」(DataValidationRunner)からも同じ発見規則を
        // 使うため public にした。ここが唯一の IValidator 発見経路(重複実装を作らない)。
        public static IEnumerable<IValidator> DiscoverValidators()
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

            // [47_review_p_tickets_2026-09-20.md] P2-1(2026-09-20) — 互換性スナップショットの
            // 「旧版フィクスチャ」(LegacyAssetFixtureTests、Tests/Editor/Compat/Fixtures/)の除外は
            // AssetSearch.FindAssets 自身が行う(唯一の検索口に集約。以前は呼び出し側ごとに個別実装しており
            // 漏れがあった)。
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

        // [47_review_p_tickets_2026-09-20.md] P1-2 — 走査対象は「持ち込み先が実際に書いたコード」であるべきで、
        // D-Drive 自身のパッケージを走査すると、持ち込み先では必ず(D-Drive 内の既存の当たり + Samples~/Tests の
        // 誤検出分だけ)Error が出て `CI.ValidateAll` を fail 条件にした消費側 CI が初日から赤くなる
        // ([42_distribution.md] §2.3-2 の実測、26 件)。
        // 開発リポジトリ(`DDriveProjectSettings.IsDevelopmentRepo == true`)だけ、従来どおり D-Drive 自身の
        // パッケージ実パスを走査する(D-Drive を開発するときは D-Drive 自身の禁止 API 違反を検出したい)。
        // 持ち込み先では `Assets` 配下(ゲームコード全体)を走査する。
        public static string ResolveForbiddenApiScanRoot()
        {
            if (DDriveProjectSettings.instance.IsDevelopmentRepo)
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CI).Assembly);
                return packageInfo != null ? packageInfo.resolvedPath : "Assets/DDrive";
            }

            return "Assets";
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

        private static void WriteJUnitXml(IReadOnlyList<ValidationReport> reports, IReadOnlyList<ForbiddenApiScanner.Violation> forbiddenApiViolations, string outputPath, IReadOnlyList<ForbiddenApiScanner.Notice> notices = null)
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var entries = new List<(string assetPath, ValidationResult result)>(reports.Count + forbiddenApiViolations.Count);
            foreach (var report in reports)
            {
                var assetPath = DescribeReportLocation(report);
                entries.Add((assetPath, report.Result));
            }

            foreach (var violation in forbiddenApiViolations)
            {
                entries.Add((violation.FilePath + ":" + violation.Line, ValidationResult.Error(violation.Message)));
            }

            if (notices != null)
            {
                foreach (var notice in notices)
                {
                    entries.Add((notice.FilePath + ":" + notice.Line, new ValidationResult(notice.Severity, notice.Message, null, notice.Code)));
                }
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

        // [11_tasks.md] M-4 — 許可まわりの Warning / Info(CI を fail させない)をログに出す。
        private static void LogForbiddenApiNotices(IReadOnlyList<ForbiddenApiScanner.Notice> notices)
        {
            foreach (var notice in notices)
            {
                var text = $"[DDrive][ForbiddenApi] {notice.FilePath}:{notice.Line}: {notice.Message} ({notice.Code})";
                if (notice.Severity == ValidationSeverity.Warning)
                {
                    Debug.LogWarning(text);
                }
                else
                {
                    Debug.Log(text);
                }
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
                var assetPath = DescribeReportLocation(report);
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
