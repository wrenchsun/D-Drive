# Changelog

D-Drive（`com.ddrive.core`）の変更履歴。[Keep a Changelog](https://keepachangelog.com/ja/1.0.0/) 形式に準拠し、[Semantic Versioning](https://semver.org/lang/ja/) を採用する。

> **運用ルール（[docs/42_distribution.md](docs/42_distribution.md) §4.1・§5.11-10）**:
> - 各バージョン見出しには **`### 互換性` 節を必ず書く**。「破壊なし / 追加のみ / マイグレーションあり（自動・手動）/ 破壊あり（[docs/migrations/](docs/migrations/) の移行ガイドへリンク）」のいずれかを明記する（空欄は CI の CHANGELOG ガードで fail にする）。
> - どの桁を上げるかは人の裁量ではなく [docs/42_distribution.md](docs/42_distribution.md) §5 の互換面ごとの区分で機械的に決まる（§4.1 の対応表）。
> - **本ファイルは P-2（互換性ポリシーの確定）の成果物として、P チケット完了（P-13 発効）前に用意した雛形**。互換性ポリシー自体は P-13 が発効するまで参考情報であり、`[Unreleased]` は現時点では通常の変更ログとして運用する。

## [Unreleased]

### 互換性

- 破壊なし（互換性ポリシーは未発効。[docs/42_distribution.md](docs/42_distribution.md) §5 は P-13 で発効する草案段階）

## [1.0.0] - 2026-09-20

`Assets/DDrive/` を `Packages/com.ddrive.core/` へパッケージ化し、UPM（git URL 参照）での配布を開始する最初の版。[docs/42_distribution.md](docs/42_distribution.md) を参照。

### 互換性

- 破壊なし（初回リリース。**1.0.0 から開始**する理由は [docs/42_distribution.md](docs/42_distribution.md) §7 A-3 のとおり: 0.x は SemVer 上「壊してよい期間」を意味し、ユーザー要望「以降は互換性を持たせる」と矛盾するため）
- **発効前の一度きりの整理**（[docs/42_distribution.md](docs/42_distribution.md) §5.13）: `AssetIdGenerator.KnownPrefixes` に `MODEL`/`ANC`/`ANCG`/`SKIN` を追加し、生成定数名の接頭辞重複（例: `MODELID.MODELPlayerModel` → `MODELID.PlayerModel`）を解消済み（2026-09-18、コミット `ead2149`）。ID(ulong) 値は不変
- P-3（2026-09-20）: `ValidationResult` に `Code`（string、既定引数）を追加。既存の `Error/Warning/Info` 呼び出しはすべて変更不要（省略可能引数のため既定は空文字）。追加のみなので互換性への影響なし
- P-3（2026-09-20）: `AddressablesRegistrationValidator` の 5 種のメッセージに `Code`（`DD-ADDR-CATALOG-MISSING` / `DD-ADDR-NO-SETTINGS` / `DD-ADDR-MISSING` / `DD-ADDR-MISMATCH` / `DD-ADDR-PRELOAD-REQUIRED`）を付与。メッセージ文言・Severity（いずれも Error）は変更なし
- P-3（2026-09-20）: `AssetIdGenerator.Regenerate` / `CI.LoadAllAssetDataAssets` が `Tests/Editor/Compat/Fixtures/` 配下のアセットを常に除外するようにした（互換性スナップショットの旧版フィクスチャが実生成物・実 Validation に混入するのを防ぐ）。実 GameData の挙動に影響なし
- P-4（2026-09-20）: `CI` に public static メソッド `ResolveForbiddenApiScanRoot` を追加(追加のみ)。`Tests/Editor/Compat/Snapshots/editor-contract.txt` を更新済み(`Tools > D-Drive > Compat > スナップショットを更新`)
- P-5（2026-09-20）: `DDriveVersion.Value` を `"1.0.0-dev"` → `"1.0.0"` に変更(パッケージ化発効に伴う正式表記。`PackageVersionConsistencyTests` で `package.json` の `version` と一致することを確認済み)
- P-5（2026-09-20）: `AssetSearch.Roots` の既定値を `{"Assets"}` から `{"Assets", <D-Drive 自身のパッケージ asset パス>}` に拡張(`PackageInfo` で解決。他パッケージは対象外のまま)。D-Drive 自身が `Packages/com.ddrive.core/` に移った後も `Tests/` 配下の一時フィクスチャ等を検索できるようにするための必須修正(追加のみ、`DDrive.Editor` は互換面 §5.4 の対象外)
- P-5（2026-09-20）: `CameraExecutionOrderValidator.IsDDrivePath` が `PackageInfo` 経由でパッケージの実 asset パスも D-Drive 自身のスクリプトと判定するようにした(`"Assets/DDrive/"` 前方一致は後方互換のため維持)。`ScanRiskyPatternFiles` の走査対象を `DDriveCodeScanRoots`(Assets 全体 + D-Drive 自身のパッケージパス)に拡張

### 追加

- `CHANGELOG.md`（本ファイル）・`docs/migrations/README.md`・`docs/migrations/TEMPLATE.md` を新規作成（P-2）
- [docs/12_review.md](docs/12_review.md) §3 に「互換性」チェック節の草案を追加（P-2）
- P-3（2026-09-20）: 互換性スナップショットテスト群（`Tests/Editor/Compat/`）。[docs/42_distribution.md](docs/42_distribution.md) §5.11 の 1〜10 に対応する EditMode テストとゴールデン（`Tests/Editor/Compat/Snapshots/*`）、旧版フィクスチャ（`Tests/Editor/Compat/Fixtures/v1_0_0/*.asset`、19 種別）、更新メニュー `Tools > D-Drive > Compat > スナップショットを更新`（`CompatSnapshotMenu`）、環境変数 `DDRIVE_UPDATE_COMPAT_SNAPSHOTS=1` による一時フィクスチャ依存ゴールデンの更新経路。`Runtime/DDriveVersion.cs`(`DDriveVersion.Value`)を新設し、CHANGELOG 最新見出しとの一致を検査する `PackageVersionConsistencyTests` を追加
- P-4（2026-09-20、[docs/42_distribution.md](docs/42_distribution.md) §2.3・§5.13）: **境界違反の解消**
  - システム用 shader(`DDrive_Lit`/`DDrive_Unlit`/`AiStandardSurface` 一式)を `Assets/SourceAssets/Shaders/` から `Assets/DDrive/Runtime/Shaders/` へ移設(Unity Editor 経由、GUID 不変。P-5 でさらに `Packages/com.ddrive.core/Runtime/Shaders/` へ移設)
  - `CI.ValidateAll` の `ForbiddenApiScanner` 走査ルートを `PackageInfo.FindForAssembly` から解決するようにし(パッケージ化後も追従)、走査対象フォルダが無い/`.cs` が 0 件のときに Error を返すようにした(禁止 API チェックの恒久的な無効化を防ぐ)
  - `ManualPages.GetManualFolder` がパッケージ化後の `Documentation~/...Manual` を先に探すようにした(P-5 でパッケージ化済みだが `Documentation~/...Manual` 自体の同梱は P-9 待ちのため、現状は既存の `docs/...Manual` にフォールバックし挙動は変わらない)
  - `ControlSkinPreviewSection.DefaultScrollMaterialPath` を GUID 参照(`AssetDatabase.GUIDToAssetPath`)による解決に変更(パッケージ化でパスが変わっても追従。const → static プロパティ)
  - `CodeReferenceScan`/`SpecWebSender` の走査範囲を `Assets/DDrive`・`Assets/Generated` 限定から `Assets` 全体(+ D-Drive 自身のパッケージパス、`DDriveCodeScanRoots` 新設)へ拡張し、持ち込み先のゲームコードも「安全な削除」チェックの対象にした
  - NGO(`com.unity.netcode.gameobjects`)を `versionDefines`(`DDRIVE_NGO`)で必須依存から切り離した。`NgoNetBridge`/`NetDebugOverlay`/`NgoTransportConfigurator`/`Samples/NetCheckRunner`/`Samples/NetBridgeSmokeTest`/`DDriveRuntimeBootstrap` の NGO 分岐/`PrefabDataValidator` の `NetworkObject` 検査を `#if DDRIVE_NGO` で囲んだ(`DDrive.Runtime`/`DDrive.Samples`/`DDrive.Tests.Runtime` の 3 asmdef に versionDefines を追加)。NGO ありの現状(開発リポジトリ)の挙動は変わらない(EditMode/PlayMode green で確認済み)。NGO 無し状態でのコンパイル確認は P-11(空プロジェクト)で行う
  - `com.cysharp.unitask` の manifest 参照をタグ固定(`#2.5.11`)。旧 `packages-lock.json` の hash(`ceac8d69...`)は 2.5.11 より新しい未リリースコミットだったため一致するタグが無く、最新リリースタグ 2.5.11(`2e993ff1...`)へ更新した(実質的な UniTask の更新を伴う。再解決後 EditMode/PlayMode green を確認済み)
  - `DDriveSpecSettings` の旧フィールド `SpreadsheetUrl`/`AssetSheetName`/`TuningSheetName`(および未使用になった `DefaultAssetSheetName`/`DefaultTuningSheetName` 定数)を削除。実 `.asset` に値が入っていないことを P-1 で確認済み
  - `Assets/DDrive/Editor/Settings/DDriveProjectSettings.cs` を新設(`GameDataRoot`/`GeneratedRoot`/`SourceAssetsRoot`/`SpecsRoot`。既定値は現状のまま。実際の参照差し替えとウィザード UI は P-5/P-6)
  - `Tests/Editor/{ImportRuleServiceTests,CutsceneImportServiceTests}.cs` の UnityChan FBX 依存テスト(計 9 件)に `[Category("DevRepoOnly")]` を付与し、`DDRIVE_DEV_REPO`(開発リポジトリの `ProjectSettings` の Scripting Define Symbols にのみ追加)が無ければ Inconclusive にする `DevRepoOnlyGuard` を新設
- P-5（2026-09-20、[docs/42_distribution.md](docs/42_distribution.md) §6 P-5）: **パッケージ化**
  - `Assets/DDrive/{Foundation,Runtime,Editor,Tests}` を `Packages/com.ddrive.core/{Foundation,Runtime,Editor,Tests}` へ Unity Editor 経由(`AssetDatabase.MoveAsset`)で移設(.meta ごと、GUID 不変)
  - `Assets/DDrive/Samples`(`NetCheckRunner`/`NetBridgeSmokeTest`/`PresentationSkillSlashDemo`)を `Packages/com.ddrive.core/Samples~/Demo/` へ移設(スクリプトのみ。対応する確認用シーン・GameData の同梱は見送り)
  - `package.json` を新設(`name: "com.ddrive.core"`、`version: "1.0.0"`、`unity: "6000.3"`、`dependencies`(`com.unity.addressables`/`com.unity.inputsystem`/`com.unity.nuget.newtonsoft-json`/`com.unity.render-pipelines.universal`/`com.unity.timeline`/`com.unity.ugui`)、`samples`)
  - `Packages/com.ddrive.core/README.md`・`Documentation~/README.md`(雛形。P-9/P-10 で完成)を新設
  - 開発 `Packages/manifest.json` に `"testables": ["com.ddrive.core"]` を追加(Test Runner での実行に必要)
  - `AssetCreationService`/`ImportRuleService`/`AssetIdGenerator`/`TuningCodegen`/`AssetIconService`/`ScenePreloadGenerator`/`SpecSnapshotWriter`/`DDriveSpecSettings`/`AssetReorganizer`/`SourceDataCreation`/`CutsceneImportService` の `"Assets/GameData"`/`"Assets/Generated"`/`"Assets/SourceAssets"`/`"Specs"` 決め打ちを `DDriveProjectSettings.{GameDataRoot,GeneratedRoot,SourceAssetsRoot,SpecsRoot}` 経由の解決に置き換え(既定値は現状のままなので挙動は不変。実際にウィザードで変更できるようにするのは P-6)
  - `AiStandardSurface` shader の `#include` 絶対パス・`AiStandardSurfacePreprocessor.ShaderPath` を `Packages/com.ddrive.core/Runtime/Shaders/...` へ更新
  - `Tests/Editor/Compat/*`(`CompatSnapshotPaths`・`LegacyAssetFixtureTests`・`CodegenGoldenTests`・`ConstantNameGoldenTests`・`ValidatorSeverityRegistryTests`)と、その他 `Assets/DDrive/Tests/Editor/Temp*` を自前のスクラッチフォルダにしていたテスト約 50 件のパスを `Packages/com.ddrive.core/Tests/Editor/...` へ更新

