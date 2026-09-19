# Changelog

D-Drive（`com.ddrive.core`）の変更履歴。[Keep a Changelog](https://keepachangelog.com/ja/1.0.0/) 形式に準拠し、[Semantic Versioning](https://semver.org/lang/ja/) を採用する。

> **運用ルール（[docs/42_distribution.md](docs/42_distribution.md) §4.1・§5.11-10）**:
> - 各バージョン見出しには **`### 互換性` 節を必ず書く**。「破壊なし / 追加のみ / マイグレーションあり（自動・手動）/ 破壊あり（[docs/migrations/](docs/migrations/) の移行ガイドへリンク）」のいずれかを明記する（空欄は CI の CHANGELOG ガードで fail にする）。
> - どの桁を上げるかは人の裁量ではなく [docs/42_distribution.md](docs/42_distribution.md) §5 の互換面ごとの区分で機械的に決まる（§4.1 の対応表）。
> - **本ファイルは P-2（互換性ポリシーの確定）の成果物として、P チケット完了（P-13 発効）前に用意した雛形**。互換性ポリシー自体は P-13 が発効するまで参考情報であり、`[Unreleased]` は現時点では通常の変更ログとして運用する。

## [Unreleased]

### 互換性

- 破壊なし（互換性ポリシーは未発効。[docs/42_distribution.md](docs/42_distribution.md) §5 は P-13 で発効する草案段階）
- P-3（2026-09-20）: `ValidationResult` に `Code`（string、既定引数）を追加。既存の `Error/Warning/Info` 呼び出しはすべて変更不要（省略可能引数のため既定は空文字）。追加のみなので互換性への影響なし
- P-3（2026-09-20）: `AddressablesRegistrationValidator` の 5 種のメッセージに `Code`（`DD-ADDR-CATALOG-MISSING` / `DD-ADDR-NO-SETTINGS` / `DD-ADDR-MISSING` / `DD-ADDR-MISMATCH` / `DD-ADDR-PRELOAD-REQUIRED`）を付与。メッセージ文言・Severity（いずれも Error）は変更なし
- P-3（2026-09-20）: `AssetIdGenerator.Regenerate` / `CI.LoadAllAssetDataAssets` が `Tests/Editor/Compat/Fixtures/` 配下のアセットを常に除外するようにした（互換性スナップショットの旧版フィクスチャが実生成物・実 Validation に混入するのを防ぐ）。実 GameData の挙動に影響なし

### 追加

- `CHANGELOG.md`（本ファイル）・`docs/migrations/README.md`・`docs/migrations/TEMPLATE.md` を新規作成（P-2）
- [docs/12_review.md](docs/12_review.md) §3 に「互換性」チェック節の草案を追加（P-2）
- P-3（2026-09-20）: 互換性スナップショットテスト群（`Assets/DDrive/Tests/Editor/Compat/`）。[docs/42_distribution.md](docs/42_distribution.md) §5.11 の 1〜10 に対応する EditMode テストとゴールデン（`Tests/Editor/Compat/Snapshots/*`）、旧版フィクスチャ（`Tests/Editor/Compat/Fixtures/v1_0_0/*.asset`、19 種別）、更新メニュー `Tools > D-Drive > Compat > スナップショットを更新`（`CompatSnapshotMenu`）、環境変数 `DDRIVE_UPDATE_COMPAT_SNAPSHOTS=1` による一時フィクスチャ依存ゴールデンの更新経路。`Assets/DDrive/Runtime/DDriveVersion.cs`（`DDriveVersion.Value = "1.0.0-dev"`）を新設し、CHANGELOG 最新見出しとの一致を検査する `PackageVersionConsistencyTests` を追加

## [1.0.0] - 未リリース（P-5 で発効予定）

`Assets/DDrive/` を `Packages/com.ddrive.core/` へパッケージ化し、UPM（git URL 参照）での配布を開始する最初の版。[docs/42_distribution.md](docs/42_distribution.md) を参照。

### 互換性

- 破壊なし（初回リリース。**1.0.0 から開始**する理由は [docs/42_distribution.md](docs/42_distribution.md) §7 A-3 のとおり: 0.x は SemVer 上「壊してよい期間」を意味し、ユーザー要望「以降は互換性を持たせる」と矛盾するため）
- **発効前の一度きりの整理**（[docs/42_distribution.md](docs/42_distribution.md) §5.13）: `AssetIdGenerator.KnownPrefixes` に `MODEL`/`ANC`/`ANCG`/`SKIN` を追加し、生成定数名の接頭辞重複（例: `MODELID.MODELPlayerModel` → `MODELID.PlayerModel`）を解消済み（2026-09-18、コミット `ead2149`）。ID(ulong) 値は不変

