using System.Collections.Generic;
using DDrive.Editor.Settings;
using DDrive.Editor.Setup;
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
        }
    }
}
