using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Codegen;
using DDrive.Editor.Import;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Settings
{
    // [42_distribution.md] §3.4 / §4.3 / §7 B-6(P-4、2026-09-20) — 出力先パスの決め打ちを設定へ外出しする
    // ための最小限の土台。
    //
    // 位置づけ: このチケット(P-4)の §2.3 各項目(CI.cs の走査ルート・ManualPages のパス・
    // ControlSkinPreviewSection のパス・CodeReferenceScan/SpecWebSender の走査範囲)は、いずれも
    // `PackageInfo`(自分がパッケージ化されているか)や GUID 参照で解決するため、実はこの設定を必要としない。
    // ここで先に最小限の器だけ用意しておくのは、[42] §3.4 が P-5 で行うとしている
    // `ImportRuleService.DefaultSourceRoot`・`AssetIdGenerator.DefaultOutputPath`・
    // `TuningCodegen.DefaultOutputPath`・`AssetIconService.DefaultIconRoot`・
    // `ScenePreloadGenerator.DefaultOutputRoot` の設定化と、B-6(持ち込み先がフォルダ構成を選べるように
    // する)の土台にするため。既定値は現状の決め打ちパスと同じ(挙動は変えない)。
    //
    // 実際にこの設定を読みに行くよう各所を差し替える作業・ウィザード UI(P-6)は後続チケットで行う。
    // `AssetDataBase.SchemaVersion`(P-7)・`LastAppliedVersion`(P-8)等、他チケットが持つ責務はここには
    // 含めない(1 つの設定 SO に無関係な責務を混ぜない)。
    [FilePath("ProjectSettings/DDriveProjectSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class DDriveProjectSettings : ScriptableSingleton<DDriveProjectSettings>
    {
        [SerializeField] private string _gameDataRoot = AssetCreationService.DefaultGameDataRoot;
        [SerializeField] private string _generatedRoot = "Assets/Generated";
        [SerializeField] private string _sourceAssetsRoot = ImportRuleService.DefaultSourceRoot;
        [SerializeField] private string _specsRoot = "Specs";

        // [42_distribution.md] §4.5/§7 A-9(P-6、2026-09-20) — 「持ち込み先で D-Drive を改造している
        // 可能性」の Warning(ProjectSetupValidator)を出すための判定材料。開発リポジトリ(このリポジトリ)
        // では true にする(埋め込みパッケージ = PackageSource.Embedded であること自体は開発リポジトリでも
        // 持ち込み先でも起こり得るため、この 2 つを掛け合わせて判定する)。開発リポジトリでの true 化は
        // 人手ではなく `DevRepoSettingsSync`(Editor/Settings)が `DDRIVE_DEV_REPO` 定義時に自動で行う
        // (ProjectSettings/*.asset はテキスト編集しない。Unity 経由の保存のみ)。
        [SerializeField] private bool _isDevelopmentRepo;

        // [42_distribution.md] §2.3-7/§7 A-8(P-6、2026-09-20) — `Tools > D-Drive > Generate >
        // Regenerate Asset IDs` が出力フォルダに `DDrive.Generated.asmdef` を同時出力するかどうか。
        // 既定 ON(A-8 決定)。セットアップウィザードのチェックボックスで OFF にできる。
        [SerializeField] private bool _emitGeneratedAsmdef = true;

        public string GameDataRoot
        {
            get => string.IsNullOrEmpty(_gameDataRoot) ? AssetCreationService.DefaultGameDataRoot : _gameDataRoot;
            set => SetAndSave(ref _gameDataRoot, value);
        }

        public string GeneratedRoot
        {
            get => string.IsNullOrEmpty(_generatedRoot) ? "Assets/Generated" : _generatedRoot;
            set => SetAndSave(ref _generatedRoot, value);
        }

        public string SourceAssetsRoot
        {
            get => string.IsNullOrEmpty(_sourceAssetsRoot) ? ImportRuleService.DefaultSourceRoot : _sourceAssetsRoot;
            set => SetAndSave(ref _sourceAssetsRoot, value);
        }

        public string SpecsRoot
        {
            get => string.IsNullOrEmpty(_specsRoot) ? "Specs" : _specsRoot;
            set => SetAndSave(ref _specsRoot, value);
        }

        public bool IsDevelopmentRepo
        {
            get => _isDevelopmentRepo;
            set
            {
                _isDevelopmentRepo = value;
                Save(true);
            }
        }

        public bool EmitGeneratedAsmdef
        {
            get => _emitGeneratedAsmdef;
            set
            {
                _emitGeneratedAsmdef = value;
                Save(true);
            }
        }

        private void SetAndSave(ref string field, string value)
        {
            field = value;
            Save(true);
        }
    }
}
