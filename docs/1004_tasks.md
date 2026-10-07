# 1004. タスク一覧（2026-10-07 以降の全チケット）

> **これ以降のチケットはすべてこの 1 枚に置く**（ユーザー指示 2026-10-07「チケットがいろんな場所にあると確認しづらい」）。過去のチケット（Phase 0〜7・P・N・M・FC・U・v1.4.1 候補）は [11_tasks.md](11_tasks.md) のまま動かさない。未完了事項の**索引**（未確認・既知の不具合・判断待ち）は [1001_open_items.md](1001_open_items.md)。そこから着手するものは、ここに行を足して ID を振る。
> **書き方**: 1 行 1 チケット。「状態」列は ⬜ 未着手 / 🔧 実装中（誰が・ブランチ）/ 🔍 レビュー中 / ✅ 完了（日付・PR）/ 🔶 一部 / ⏸ 保留。設計の詳細は各仕様書（1002 など）に書き、ここには要約・依存・AC・状態だけを書く。

## 0. 進行中の全体像

| 版 | 内容 | 状態 |
|---|---|---|
| v1.4.1 | Canvas Editor の折りたたみ | ✅ 2026-10-07 リリース済み（タグ push 済み） |
| **v1.5.0** | D-Drive MCP（下の §1） | 🔧 実装中（MCP-0・MCP-1・MCP-2・MCP-3・MCP-4・MCP-5 完了、MCP-8 前半完了） |
| v1.4.x / 1.5.x 候補 | [11](11_tasks.md)「v1.4.1 候補」表と [1001](1001_open_items.md) §2 の候補。着手するときにここへ起票 | ⬜ |

## 1. MCP チケット: D-Drive MCP（v1.5.0 MINOR。仕様 = [1002](1002_ddrive_mcp.md)）

> AI エージェントが D-Drive の Editor 機能（Data の作成・値の変更・検査・生成・マイグレーション・確認用シーン・リリース前チェック）を MCP ツールとして直接呼べるようにする。サーバーは新設せず、Editor 組み込みの `jp.shiranui-isuzu.unity-mcp` の `[McpTool]` に乗る。設計の軸は「トークン最小」（[1002](1002_ddrive_mcp.md) §5）と「ポート競合ゼロ」（同 §6）。決め事 Q-1〜Q-12 は同 §9。実装は Sonnet のエージェントに 1 チケットずつ委任し、レビュー・リリース判断は上位モデルが行う。

| ID | 状態 | 内容 | 人日 | 依存 | AC |
|---|---|---|---|---|---|
| MCP-0 | ✅ 完了（2026-10-07、PR #165） | 決め事 Q-1〜Q-12 の回答を [1002](1002_ddrive_mcp.md) §9 に反映。CoplayDev 版を外す（`manifest.json`・`.mcp.json`・CLAUDE.md §4・[20](20_mcp_setup.md)） | 0.5 | — | 固定ポートがリポジトリのどこにも無い |
| MCP-1 | ✅ 完了（2026-10-07、ブランチ mcp/mcp-1-infra） | `DDrive.Editor.Mcp` asmdef（Version Defines `DDRIVE_UNITY_MCP` / defineConstraints）、共通ガード `McpGuard`（Play Mode・読み取り専用欄・`max_chars`・`dry_run`・例外の畳み込み）、返り値圧縮 `McpJson` | 1 | MCP-0 | isuzu が無いプロジェクトでアセンブリが外れる。ガードの EditMode テスト green。→ 実装: `Editor/Mcp/`（asmdef・`McpGuard`・`McpJson`・`Tools/DDriveStatusTools`）、設定 `McpAllowWrite`（Project Settings > D-Drive > MCP・ウィザード）、`Tests/Editor/Mcp/`。EditMode 1831 / PlayMode 964 green、`tools/list` に `ddrive_status` 確認（[1002](1002_ddrive_mcp.md) §3 実装メモ） |
| MCP-2 | ✅ 完了（2026-10-07、ブランチ mcp/mcp-2-status-help） | `ddrive_status` / `ddrive_help`（カード `rules` / `types` / `validation:<code>` / `tool:<name>` / `menu` の初版） | 1 | MCP-1 | `ddrive_status` 1 回でコンパイル・テスト・検査・マイグレーション・Addressables・MCP 接続情報が揃う → 実装: `ddrive_status` 7 セクション（version/compile/tests/validation/migration/addressables/mcp。`validation` は `McpValidationCache`〔MCP-5 の `ddrive_validate` が `Record`〕）、`ddrive_help` 5 topic、カード正本は `Editor/Mcp/Cards/`（Q-10 変更）、`AddressablesSync.CountMissingEntries`（読み取り専用）、EditMode 1854 / PlayMode 964 green。詳細は [1002](1002_ddrive_mcp.md) 実装メモ（MCP-2） |
| MCP-3 | ✅ 完了（2026-10-07、ブランチ mcp/mcp-3-asset-tools） | `ddrive_asset_list` / `get` / `create` / `set` + `FieldTables`（全 18 種別の主要欄・読み取り専用欄） | 2 | MCP-1 | `create` は `AssetCreationService` 経由 + Addressables 登録 + 検査結果を 1 回で返す。`set` は Undo + SetDirty、読み取り専用欄は拒否 → 実装: `ddrive_asset_list` / `get` / `create` / `set`（`Editor/Mcp/Tools/DDriveAssetTools.cs`）、`FieldTables`（全 18 種別の Data クラスと既定の欄 10 個以内。ControlSkin は `data_class` 必須）、`SerializedFieldIo`（SerializedProperty ⇄ JSON。ValueDef の最小 JSON・`AssetId` の `{type,id}`・Object 参照はアセットパス）。**ID は JSON では 10 進文字列**（MCP-5 の `items[].id` も文字列に変更）。`create` は `AssetCreationService.Create`（カタログ + Addressables 登録まで）+ 検査件数を 1 回で返し `preview` を持つ。`set` は全欄が書けるときだけ書く（`SerializedObject` に全部書いてから Apply）、Undo + SetDirty + SaveDirty、`ChangeNote` に `[mcp] ` を前置（Q-7）、読み取り専用欄は `read_only_field`。`AddressablesSync.IsRegistered` を追加。詳細は [1002](1002_ddrive_mcp.md) 実装メモ（MCP-3） |
| MCP-4 | ✅ 完了（2026-10-07、ブランチ mcp/mcp-4-usages-delete） | `ddrive_asset_usages` / `unused` / `delete` / `ddrive_editor_open` | 1 | MCP-3 | `delete` は `SafeDeleteService`、ダイアログを出さず JSON で返す → 実装: `Editor/Mcp/Tools/DDriveDependencyTools.cs`。グラフ未構築は `{needsRebuild:true, hint}`。`usages` は `{count, usages:[{path,objectPath?,kind?}]}`、`unused` は Archived を除外せず `archived` の印（窓と同じ）。`delete` は読み取り専用の分析（blockers + コード参照）→ 何も無いときだけ `TryDelete`（ダイアログは自動承諾、`finally` で復元）、`preview` 対応、Addressables 登録も外れる。`ddrive_editor_open` は `DataEditorRegistry` の主エディタ。`AddressablesSync.IsGuidRegistered` を追加。詳細は [1002](1002_ddrive_mcp.md) 実装メモ（MCP-4） |
| MCP-5 | ✅ 完了（2026-10-07、ブランチ mcp/mcp-5-validate） | `ddrive_validate` / `validate_fix` / `forbidden_api` | 1 | MCP-1 | `summary` が既定、`byCode` の表、`FixAction` 付きだけ適用 → 実装: `ddrive_validate`（scope `all`/`project`/`type:<T>`/`asset:<T>:<id>`、detail `summary`/`errors`/`all`、`codes`・`limit`・`cursor`・`max_chars`、`all` だけ `McpValidationCache` を更新、`fixable` 表付き）、`ddrive_validate_fix`（Destructive + UndoGroup、`preview` 引数で wouldApply）、`ddrive_forbidden_api`（`byRule` + items）。items は max_chars に収まるまで末尾の項目を丸ごと落とし `next` を付ける。詳細は [1002](1002_ddrive_mcp.md) 実装メモ（MCP-5） |
| MCP-6 | ✅ 完了（2026-10-07、ブランチ mcp/mcp-6-generate-migrate） | `ddrive_generate`（ids / tuning / addressables / preload / prefabs / deps / icons）/ `ddrive_migrate` / `ddrive_compat` / `ddrive_release_check` | 1.5 | MCP-1 | `compat diff` で removed > 0 に warning。`release_check` は `ReleaseChecks.ps1` のラッパー、`bump-version` はツール化しない → 実装: `Editor/Mcp/Tools/DDriveGenerateTools.cs`（7 種、引数名は `kind`〔isuzu が `target` を予約〕。`preview` は種類ごと: `tuning` は一時ファイルで正確、`ids` / `icons` / `deps` は予定のみ、`addressables` は `missing` 件数、`preload` は依存グラフ未構築なら書かず `needsRebuild`）/ `DDriveMigrateTools.cs`（`plan` は書き込み許可不要、`apply` は適用前後の計画の差で applied/failed）/ `DDriveCompatTools.cs`（`ddrive_compat` Safe + `ddrive_compat_update` Destructive に分割。removed > 0 で `warning`）/ `DDriveReleaseTools.cs`（`pwsh` で `check-release.ps1 -Json`、新規スイッチ）。**ツールは 18 個、MCP-7 の 4 個で 22 個になり上限 20 を超える**。詳細は [1002](1002_ddrive_mcp.md) 実装メモ（MCP-6） |
| MCP-7 | ⬜ 未着手 | `ddrive_preview_open` / `preview_play` / `preview_sweep` / `ddrive_build_netcheck`（ジョブ化） | 1.5 | MCP-3 | 確認用シーン + 配置を 1 回で。実 Manager 駆動（ADR-4）。Play Mode 中は拒否 |
| MCP-8 | 🔶 前半完了（`register-mcp.ps1`、PR #166）。後半は MCP-1 の後 | `Tools/Mcp/register-mcp.ps1`（記述子 → `claude mcp add` 上書き、pid 生存確認）+ `McpPortPolicyTests` + Info `DD-MCP-FIXED-PORT` | 0.5 | MCP-1 | トークン・ポートをリポジトリに書かない。D-Drive と MS2026 のパスでポートが異なることをテストで検算  |
| MCP-9 | ⬜ 未着手 | スナップショット `mcp-tools.txt` + Compat テスト + [42](42_distribution.md) §5.14 E-21 | 0.5 | MCP-2〜7 | 行が減ったら赤 |
| MCP-10 | ⬜ 未着手 | トークン計測 `Tools/Mcp/measure-tokens.py`（代表 5 シナリオ、[1002](1002_ddrive_mcp.md) §10 に結果） | 0.5 | MCP-2〜7 | 前比 1/3 以下（G-2）。満たさないツールは返り値を見直す |
| MCP-11 | ⬜ 未着手 | docs（[20](20_mcp_setup.md) 書き換え・[09](09_editor_tools.md) §15・[34](34_onboarding.md) §7・SKILL.md・ProgrammerManual `mcp.html`）・CHANGELOG・人による確認手順 `verification/1003_manual_verification_mcp.md` | 1 | MCP-9 | — |
| MCP-12 | ⬜ 未着手 | 自前レビュー → 修正 → v1.5.0 リリース（[12](12_review.md) §7） | 1 | MCP-11 | run-ci 全段 green、`check-release -Base v1.4.1` green |

## 2. 次に起票する候補（[1001](1001_open_items.md) から。着手を決めたら §1 と同じ表形式で節を足す）

- GC-R-01（BGM ループ位置の Warning）・GC-R-07（尺 0 のゴールデン）: v1.5.0 に同梱するか、次の PATCH か
- `UiTweenManager.Tick` / `UiManager.Tick` の再入耐性（v1.3.1 からの不具合）
- M-5（時間源の公開 API）: ユーザー判断待ち

## 更新履歴

- 2026-10-07: 作成。docs/11 の「MCP チケット」節をここへ移した（docs/11 には参照だけ残す）。MCP-0 ✅・MCP-8 前半 🔶 を反映
