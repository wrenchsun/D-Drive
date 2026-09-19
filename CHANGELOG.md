# Changelog

D-Drive（`com.ddrive.core`）の変更履歴。[Keep a Changelog](https://keepachangelog.com/ja/1.0.0/) 形式に準拠し、[Semantic Versioning](https://semver.org/lang/ja/) を採用する。

> **運用ルール（[docs/42_distribution.md](docs/42_distribution.md) §4.1・§5.11-10）**:
> - 各バージョン見出しには **`### 互換性` 節を必ず書く**。「破壊なし / 追加のみ / マイグレーションあり（自動・手動）/ 破壊あり（[docs/migrations/](docs/migrations/) の移行ガイドへリンク）」のいずれかを明記する（空欄は CI の CHANGELOG ガードで fail にする）。
> - どの桁を上げるかは人の裁量ではなく [docs/42_distribution.md](docs/42_distribution.md) §5 の互換面ごとの区分で機械的に決まる（§4.1 の対応表）。
> - **本ファイルは P-2（互換性ポリシーの確定）の成果物として、P チケット完了（P-13 発効）前に用意した雛形**。互換性ポリシー自体は P-13 が発効するまで参考情報であり、`[Unreleased]` は現時点では通常の変更ログとして運用する。

## [Unreleased]

### 互換性

- 破壊なし（互換性ポリシーは未発効。[docs/42_distribution.md](docs/42_distribution.md) §5 は P-13 で発効する草案段階）

### 追加

- `CHANGELOG.md`（本ファイル）・`docs/migrations/README.md`・`docs/migrations/TEMPLATE.md` を新規作成（P-2）
- [docs/12_review.md](docs/12_review.md) §3 に「互換性」チェック節の草案を追加（P-2）

## [1.0.0] - 未リリース（P-5 で発効予定）

`Assets/DDrive/` を `Packages/com.ddrive.core/` へパッケージ化し、UPM（git URL 参照）での配布を開始する最初の版。[docs/42_distribution.md](docs/42_distribution.md) を参照。

### 互換性

- 破壊なし（初回リリース。**1.0.0 から開始**する理由は [docs/42_distribution.md](docs/42_distribution.md) §7 A-3 のとおり: 0.x は SemVer 上「壊してよい期間」を意味し、ユーザー要望「以降は互換性を持たせる」と矛盾するため）
- **発効前の一度きりの整理**（[docs/42_distribution.md](docs/42_distribution.md) §5.13）: `AssetIdGenerator.KnownPrefixes` に `MODEL`/`ANC`/`ANCG`/`SKIN` を追加し、生成定数名の接頭辞重複（例: `MODELID.MODELPlayerModel` → `MODELID.PlayerModel`）を解消済み（2026-09-18、コミット `ead2149`）。ID(ulong) 値は不変

