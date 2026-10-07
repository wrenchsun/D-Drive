using System.Collections.Generic;
using DDrive.Editor.Settings;
using DDrive.Editor.Setup;
using DDrive.Editor.Update;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;

namespace DDrive.Editor.Validation
{
    // [42_distribution.md] §3.6/§6 P-6(2026-09-20) — セットアップウィザード(ProjectSetupWizardWindow)の
    // 検査 1(依存)・2(ProjectSettings)・4(既定フォルダ/設定)・5(Addressables 初期化)と同じ判定を
    // `Validation > Run All` にも載せる(CI で継続検出できるようにするため)。
    //
    // 新しい検査は §5.8 の方針(「新しい検査は Warning として MINOR で追加し、次の MINOR 以降で
    // Error に昇格する」)に従い、本チケットではすべて Warning にする(セットアップの不備は
    // 「動かないと初めて気づく」性質のものが多く、いきなり Error にすると持ち込み直後の CI が
    // 意図せず落ちるため)。
    //
    // 制約: IUniversalValidator は `ValidatorRegistry.RunAll` が「1 件以上の AssetDataBase がある
    // ときにだけ」呼ぶ(既存の AddressablesRegistrationValidator と同じ制約。RunAll は
    // `context.AllAssets` を foreach するだけで、Data が 0 件のプロジェクトでは
    // IUniversalValidator も一度も呼ばれない)。これは Foundation 側の既存設計であり本チケットでは
    // 変更しない。Data が 1 件も無い真っ新なプロジェクトでは本 Validator は実行されないため、
    // 「Run All で Error 0」は当然だが「検査自体が走った」ことの保証にはならない点に注意。
    public sealed class ProjectSetupValidator : IUniversalValidator
    {
        // 1 回の Run All(= 1 つの ValidationContext)につき 1 回だけ報告する
        // (AddressablesRegistrationValidator の `_noSettingsReported` と同じ手筋)。
        private static ValidationContext _reportedForCtx;

        public const string CodeMcpFixedPort = "DD-MCP-FIXED-PORT";
        public const string CodeMcpMultiple = "DD-MCP-MULTIPLE";
        public const string CodeMcpIsuzuOutdated = "DD-MCP-ISUZU-OUTDATED";

        // テスト用: MCP 系の検査が読む manifest の差し替え(null で既定 = Packages/manifest.json)。
        public static System.Func<Newtonsoft.Json.Linq.JObject> McpManifestReaderOverride;

        public AssetType Target => AssetType.None;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (_reportedForCtx == ctx)
            {
                yield break;
            }

            _reportedForCtx = ctx;

            // 1. 依存
            var manifest = ManifestJson.LoadProjectManifest();
            foreach (var missing in ProjectSetupInspector.InspectMissingGitDependencies(manifest))
            {
                yield return ValidationResult.Warning(
                    $"{missing.Message} セットアップウィザード(Tools > D-Drive > Setup > セットアップウィザード)から追加できます。",
                    code: missing.Code);
            }

            // 2. ProjectSettings
            var settings = ProjectSetupInspector.InspectProjectSettings();
            if (!settings.UrpActive)
            {
                yield return ValidationResult.Warning(
                    "URP(Universal Render Pipeline)がアクティブなレンダーパイプラインになっていません。D-Drive の標準シェーダーは URP 専用です。",
                    code: "DD-SETUP-URP");
            }

            if (!settings.InputSystemActive)
            {
                yield return ValidationResult.Warning(
                    "Active Input Handling が Input System(または Both)になっていません(Project Settings > Player)。GamepadHapticOutput 等が動作しません。",
                    code: "DD-SETUP-INPUT");
            }

            if (!settings.ApiCompatibilityOk)
            {
                yield return ValidationResult.Warning(
                    "API Compatibility Level が .NET Standard 2.1 相当になっていません(Project Settings > Player)。R3 が要求します。",
                    code: "DD-SETUP-API-LEVEL");
            }

            // 5. Addressables 初期化
            if (!ProjectSetupInspector.IsAddressablesInitialized())
            {
                yield return ValidationResult.Warning(
                    "Addressables が初期化されていません(AddressableAssetSettings が無い)。セットアップウィザードの「初期化」で作成できます。",
                    code: "DD-SETUP-ADDRESSABLES");
            }
            else if (ProjectSetupInspector.HasAddressablesDefaultNameWithSpace())
            {
                // [42_distribution.md] §2.3 #12(b)(P-12 で発見、docs/49) — Addressables 自身が既定で
                // 作る "Default Local Group"・"Packed Assets" はアセット名にスペースを含み、パスに
                // スペースを禁止する持ち込み先(MS2026 の Unity Hygiene 等)で Error になる。
                yield return ValidationResult.Warning(
                    "Addressables の既定アセット名(\"Default Local Group\"/\"Packed Assets\")にスペースが含まれています。" +
                    "パスにスペースを禁止する持ち込み先の命名規則チェックに抵触します。セットアップウィザードの「5. Addressables 同期」の" +
                    "「スペースを含む既定アセット名をリネーム」から直せます。",
                    fixAction: () => ProjectSetupActions.RenameDefaultAddressablesAssetsToAvoidSpaces(),
                    code: "DD-SETUP-ADDR-NAME-SPACE");
            }

            // 4. 既定フォルダ・設定
            var folders = ProjectSetupInspector.InspectFolderLayout(DDriveProjectSettings.instance);
            if (!folders.GameDataRootExists)
            {
                yield return ValidationResult.Warning(
                    $"GameData ルート({DDriveProjectSettings.instance.GameDataRoot})がまだありません。セットアップウィザードで作成できます。",
                    code: "DD-SETUP-GAMEDATA-ROOT");
            }

            if (!folders.UiLayerSettingsExists)
            {
                yield return ValidationResult.Warning(
                    "UiLayerSettings(UI_LayerSettings.asset)がまだ作られていません。セットアップウィザードで作成できます。",
                    code: "DD-SETUP-UI-LAYER-SETTINGS");
            }

            if (!folders.SpecSettingsExists)
            {
                yield return ValidationResult.Warning(
                    "DDriveSpecSettings がまだ作られていません。セットアップウィザードで作成できます(仕様書同期を使わない場合は無視して構いません)。",
                    code: "DD-SETUP-SPEC-SETTINGS");
            }

            // A-9: 持ち込み先での改造の可能性
            if (ProjectSetupInspector.IsPossiblyModifiedEmbeddedPackage())
            {
                yield return ValidationResult.Warning(
                    "D-Drive パッケージが埋め込み(Embedded)状態で、開発リポジトリの印(DDriveProjectSettings.IsDevelopmentRepo)がありません。" +
                    "パッケージを直接改造している可能性があります([docs/42_distribution.md] §4.5)。改造は更新が取り込めなくなるため、" +
                    "拡張点(IValidator/ImportRule/IHapticOutput/INetBridge/IAssetBehaviour/[DataEditor])での解決か、開発リポジトリへの PR を検討してください。",
                    code: "DD-SETUP-EMBEDDED-MODIFIED");
            }

            // [1002_ddrive_mcp.md] §6.1 (c) MCP-8 後半(2026-10-07) — Unity MCP(isuzu 版)のポートが Preferences で固定されている
            // (記述子の preferredPort がパスから導いたポートと違う)。固定は他プロジェクト・他アプリとの衝突の元。
            // isuzu が無い(記述子が無い)プロジェクトでは何も出さない。DDrive.Editor は isuzu を参照しないので記述子だけで判断する。
            if (McpPortProbe.LooksFixed(UnityEngine.Application.dataPath))
            {
                yield return ValidationResult.Info(
                    "Unity MCP のポートが Preferences で固定されています(他プロジェクト・他アプリと衝突する元。[1002] §6.1 (c))",
                    code: CodeMcpFixedPort);
            }

            // [1002_ddrive_mcp.md] §11.2 D MCP-14(2026-10-07) — MCP パッケージが 2 つ以上 / isuzu が推奨版より古い(どちらも Info)。
            foreach (var mcpResult in InspectMcpPackages(McpManifestReaderOverride != null ? McpManifestReaderOverride() : manifest))
            {
                yield return mcpResult;
            }

            // [11_tasks.md] M-4(2026-10-05) — 禁止 API の除外設定(Project Settings > D-Drive > 禁止 API の除外)の
            // 無効な要素(理由なし・パスなし・不明な規則名)。無効な要素は除外として効かない。
            var allowEntries = DDriveProjectSettings.instance.ForbiddenApiAllowEntries;
            for (var i = 0; i < allowEntries.Count; i++)
            {
                var problem = ForbiddenApiScanner.DescribeEntryProblem(allowEntries[i], CI.ResolveForbiddenApiScanRoot());
                if (problem != null)
                {
                    yield return ValidationResult.Warning(
                        $"禁止 API の除外設定(#{i + 1}、パス '{allowEntries[i]?.Path}')が無効です: {problem}",
                        code: ForbiddenApiScanner.CodeAllowSettingsInvalid);
                }
            }

            // [42_distribution.md] §6 P-8(2026-09-20) — LastAppliedVersion が現在のパッケージ版より古い
            // (または「未適用」のまま)なら、更新ツール(Tools > D-Drive > Update > 更新ウィンドウ)の
            // 「更新を適用」がまだ実行されていない可能性がある。§5.8 の 2 段階ルールに従い Warning にする。
            // 開発リポジトリ(このリポジトリ自身)は「更新を取り込む側」ではなく「更新を作る側」なので、
            // LastAppliedVersion が空のままでも対象外にする(A-9 の判定と同じ IsDevelopmentRepo を使う)。
            if (!DDriveProjectSettings.instance.IsDevelopmentRepo)
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DDrive.Runtime.DDriveVersion).Assembly);
                var currentVersion = !string.IsNullOrEmpty(packageInfo?.version) ? packageInfo.version : DDrive.Runtime.DDriveVersion.Value;
                var lastApplied = DDriveProjectSettings.instance.LastAppliedVersion;
                if (string.IsNullOrEmpty(lastApplied) || SemVer.IsOlderThan(lastApplied, currentVersion))
                {
                    yield return ValidationResult.Warning(
                        $"D-Drive の更新が未適用の可能性があります(前回適用した版: {(string.IsNullOrEmpty(lastApplied) ? "未適用" : lastApplied)} / 現在の版: {currentVersion})。" +
                        "Tools > D-Drive > Update > 更新ウィンドウ から「更新を適用」を実行してください。",
                        code: "DD-SETUP-UPDATE-PENDING");
                }
            }
        }

        // MCP パッケージの検査(純関数。manifest は JObject で受け取る)。isuzu が無い / 他の MCP が無いプロジェクトでは何も出さない。
        public static IEnumerable<ValidationResult> InspectMcpPackages(Newtonsoft.Json.Linq.JObject manifest)
        {
            var scan = McpPackageSupport.ScanManifest(manifest);
            var ids = new List<string>();
            if (scan.IsuzuInstalled)
            {
                ids.Add(McpPackageSupport.IsuzuPackageId);
            }

            foreach (var other in scan.Others)
            {
                ids.Add(other.Id);
            }

            if (ids.Count >= 2)
            {
                yield return ValidationResult.Info(
                    $"MCP パッケージが {ids.Count} つ入っています({string.Join(", ", ids)})。同じ Editor を同時に操作でき、固定ポートのものは衝突の元です。" +
                    "不要なものは manifest.json から外してください([docs/1002] §11)",
                    code: CodeMcpMultiple);
            }

            if (scan.IsuzuInstalled
                && McpPackageSupport.CompareToRecommended(scan.IsuzuRef) == McpPackageSupport.RefComparison.Older)
            {
                yield return ValidationResult.Info(
                    $"Unity MCP(isuzu)の版が D-Drive の推奨より古いです(導入 {scan.IsuzuRef} / 推奨 {McpPackageSupport.RecommendedIsuzuRef})。" +
                    "更新ウィンドウの「更新チェック」で上げられます([docs/1002] §11)",
                    code: CodeMcpIsuzuOutdated);
            }
        }
    }
}
