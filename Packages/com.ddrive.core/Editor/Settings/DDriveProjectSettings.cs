using System;
using System.Collections.Generic;
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
        // 2026-09-20([42_distribution.md] §2.3 #12、P-12 の実移植〔docs/49〕で発見) — 「既定値が
        // 特定の文字列である」ことを検査するテスト(DDriveProjectSettingsTests.Defaults_MatchCurrentHardcodedPaths)が
        // 実際の GameDataRoot 等(持ち込み先で変更され得る値)を直接比較していたため、置き場所を変更した
        // 持ち込み先で testables を ON にすると Fail していた。既定値そのものは公開 const として切り出し、
        // テストは実際の設定値ではなくこの const と比較する。
        public const string DefaultGeneratedRoot = "Assets/Generated";
        public const string DefaultSpecsRoot = "Specs";

        // [M-1b、2026-09-25] ScenePreloadList のコード参照検出(ScenePreloadCodeReferenceScanner)が
        // 走査するルート(プロジェクトルートからの相対パス)。既定は "Assets"(持ち込み先のゲームコードが
        // どこにあっても拾えるように Assets 配下全体)。GeneratedRoot 配下(定数の定義ファイル自身)と
        // Packages/ 配下は常に除外する([11_tasks.md] M-1b)。
        public const string DefaultCodeScanRoot = "Assets";

        [SerializeField] private string _gameDataRoot = AssetCreationService.DefaultGameDataRoot;
        [SerializeField] private string _generatedRoot = DefaultGeneratedRoot;
        [SerializeField] private string _sourceAssetsRoot = ImportRuleService.DefaultSourceRoot;
        [SerializeField] private string _specsRoot = DefaultSpecsRoot;
        [SerializeField] private string _codeScanRoot = DefaultCodeScanRoot;

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

        // [42_distribution.md] §4.3/§6 P-7(2026-09-20) — 更新ツール(P-8)が使う「前回適用した版」と、
        // マイグレーション基盤(DDriveMigrationRunner)が二重適用を防ぐための適用済み ID 台帳。
        // Data 側の SchemaVersion と違い、これは「Data を持たないマイグレーション」(IProjectMigration。
        // カタログ等の一括処理)を主な対象にする(Data 側の二重適用防止は SchemaVersion < ToSchema の
        // 比較で足りるが、DDriveMigrationRunner は適用したものを両方ここへ記録する。§4.3)。
        [SerializeField] private string _lastAppliedVersion = string.Empty;
        [SerializeField] private string[] _appliedMigrationIds = Array.Empty<string>();

        // [42_distribution.md] §4.2/§6 P-14(2026-09-20) — 更新ウィンドウの「更新チェック」が
        // `Packages/manifest.json` の `com.ddrive.core` の値を書き換える直前に退避する、差し替え前の
        // 値そのもの(`GitPackageUrl.RawValue` 相当。ロールバック用)。「前の参照に戻す」を押すと
        // このフィールドと現在の manifest 値を入れ替える(2 回押すと元に戻せる)。
        [SerializeField] private string _previousPackageRef = string.Empty;

        public string LastAppliedVersion
        {
            get => _lastAppliedVersion ?? string.Empty;
            set => SetAndSave(ref _lastAppliedVersion, value ?? string.Empty);
        }

        public string PreviousPackageRef
        {
            get => _previousPackageRef ?? string.Empty;
            set => SetAndSave(ref _previousPackageRef, value ?? string.Empty);
        }

        public IReadOnlyList<string> AppliedMigrationIds => _appliedMigrationIds ?? Array.Empty<string>();

        public bool HasAppliedMigration(string id)
        {
            if (string.IsNullOrEmpty(id) || _appliedMigrationIds == null)
            {
                return false;
            }

            for (var i = 0; i < _appliedMigrationIds.Length; i++)
            {
                if (string.Equals(_appliedMigrationIds[i], id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // 冪等(既に記録済みの Id は無視する)。ScriptableSingleton の保存(ProjectSettings/*.asset)を伴うため、
        // Unity 経由(このメソッド)以外でこのファイルを編集しないこと(CLAUDE.md §0-1)。
        public void MarkMigrationApplied(string id)
        {
            if (string.IsNullOrEmpty(id) || HasAppliedMigration(id))
            {
                return;
            }

            var list = new List<string>(_appliedMigrationIds ?? Array.Empty<string>()) { id };
            _appliedMigrationIds = list.ToArray();
            Save(true);
        }

        public string GameDataRoot
        {
            get => string.IsNullOrEmpty(_gameDataRoot) ? AssetCreationService.DefaultGameDataRoot : _gameDataRoot;
            set => SetAndSave(ref _gameDataRoot, value);
        }

        public string GeneratedRoot
        {
            get => string.IsNullOrEmpty(_generatedRoot) ? DefaultGeneratedRoot : _generatedRoot;
            set => SetAndSave(ref _generatedRoot, value);
        }

        public string SourceAssetsRoot
        {
            get => string.IsNullOrEmpty(_sourceAssetsRoot) ? ImportRuleService.DefaultSourceRoot : _sourceAssetsRoot;
            set => SetAndSave(ref _sourceAssetsRoot, value);
        }

        public string SpecsRoot
        {
            get => string.IsNullOrEmpty(_specsRoot) ? DefaultSpecsRoot : _specsRoot;
            set => SetAndSave(ref _specsRoot, value);
        }

        public string CodeScanRoot
        {
            get => string.IsNullOrEmpty(_codeScanRoot) ? DefaultCodeScanRoot : _codeScanRoot;
            set => SetAndSave(ref _codeScanRoot, value);
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
