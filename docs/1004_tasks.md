# 1004. タスク一覧（2026-10-07 以降の全チケット）

> **これ以降のチケットはすべてこの 1 枚に置く**（ユーザー指示 2026-10-07「チケットがいろんな場所にあると確認しづらい」）。過去のチケット（Phase 0〜7・P・N・M・FC・U・v1.4.1 候補）は [11_tasks.md](11_tasks.md) のまま動かさない。未完了事項の**索引**（未確認・既知の不具合・判断待ち）は [1001_open_items.md](1001_open_items.md)。そこから着手するものは、ここに行を足して ID を振る。
> **書き方**: 1 行 1 チケット。「状態」列は ⬜ 未着手 / 🔧 実装中（誰が・ブランチ）/ 🔍 レビュー中 / ✅ 完了（日付・PR）/ 🔶 一部 / ⏸ 保留。設計の詳細は各仕様書（1002 など）に書き、ここには要約・依存・AC・状態だけを書く。

## 0. 進行中の全体像

| 版 | 内容 | 状態 |
|---|---|---|
| v1.4.1 | Canvas Editor の折りたたみ | ✅ 2026-10-07 リリース済み（タグ push 済み） |
| **v1.5.0** | D-Drive MCP（下の §1） | ✅ 2026-10-07 リリース（タグ push 済み）。次は MCP-13（isuzu v4.4.2）と人による確認 [verification/1005](verification/1005_manual_verification_mcp.md) |
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
| MCP-7 | ✅ 完了（2026-10-07、ブランチ mcp/mcp-7-preview-build） | `ddrive_preview`（`action` = open / play / stop / stop_all / sweep / status。spec の `preview_open` / `preview_play` / `preview_sweep` を統合）/ `ddrive_build_netcheck` | 1.5 | MCP-3 | 確認用シーン + 配置を 1 回で。実 Manager 駆動（ADR-4）。Play Mode 中は拒否 → 実装: `Editor/Mcp/Tools/DDrivePreviewTools.cs`（`open` は保存ダイアログを出さず未保存シーンがあれば `blocked`、Vfx は配置まで、`play` は Se / Bgm / Vfx / Presentation）/ `DDriveBuildTools.cs`（同期実行。isuzu が自動でジョブ化）。**ツールは 20 個ちょうど**（§5.1 の上限。4 個→2 個に統合した理由は [1002](1002_ddrive_mcp.md) 実装メモ〔MCP-7〕） |
| MCP-8 | ✅ 2026-10-07（前半 `register-mcp.ps1` = PR #166、後半 = `McpPortProbe` / `McpInstanceInfo` / `McpPortPolicyTests` / Info `DD-MCP-FIXED-PORT` + `ddrive_status.mcp.warning`。[1002] 実装メモ「MCP-8 後半 / MCP-9」） | `Tools/Mcp/register-mcp.ps1`（記述子 → `claude mcp add` 上書き、pid 生存確認）+ `McpPortPolicyTests` + Info `DD-MCP-FIXED-PORT` | 0.5 | MCP-1 | トークン・ポートをリポジトリに書かない。D-Drive と MS2026 のパスでポートが異なることをテストで検算  |
| MCP-9 | ✅ 2026-10-07（`mcp-tools.txt` = 7 つ目のスナップショット、`McpToolsSnapshotBuilder` + `[McpReturns]` を 20 ツールに、`McpToolsSnapshotTests`、E-21 は FC-6 が使用済みのため [42] §5.14 **E-24**） | スナップショット `mcp-tools.txt` + Compat テスト + [42](42_distribution.md) §5.14 E-21 | 0.5 | MCP-2〜7 | 行が減ったら赤 |
| MCP-10 | ✅ 完了 2026-10-07 | トークン計測 `Tools/Mcp/measure-tokens.py`（代表 5 シナリオ、[1002](1002_ddrive_mcp.md) §10 に結果） | 0.5 | MCP-2〜7 | 前比 1/3 以下（G-2）。満たさないツールは返り値を見直す。結果: 呼んだツールの定義だけなら 0.12〜0.31 で達成、毎ターン全 20 定義（10,275 字）を再送する見方では 2.3〜3.5 倍で未達（遅延ロード前提。[1002](1002_ddrive_mcp.md) §10） |
| MCP-11 | ✅ 完了（2026-10-07、ブランチ mcp/mcp-11-docs） | docs（[20](20_mcp_setup.md) は MCP-0 で書き換え済み・[09](09_editor_tools.md) §15・[34](34_onboarding.md) §7・`AGENTS.md` §3.1・SKILL.md §2・[1002](1002_ddrive_mcp.md) の §4 を出荷形に整合・ProgrammerManual `mcp.html` + SpecWeb 再生成）・CHANGELOG `[Unreleased]` を v1.5.0 の 1 ブロックに整理・人による確認手順 [verification/1005](verification/1005_manual_verification_mcp.md)（番号は 1003 が archive に使用済みのため 1005） | 1 | MCP-9 | docs 間のリンク切れ無し（`Tools/Docs/check_links.py`）、SpecWeb のテスト green |
| MCP-12 | ✅ 完了（2026-10-07、Release v1.5.0。記録 = [archive/1006](archive/1006_release_1_5_0.md)） | 自前レビュー → 修正 → v1.5.0 リリース（[12](12_review.md) §7） | 1 | MCP-11 | run-ci 全段 green、`check-release -Base v1.4.1` green |
| MCP-13 | ✅ 2026-10-07（ブランチ `mcp/mcp-13-isuzu-4-4-2`。EditMode 2030・PlayMode 964 green、`tools/list` に `ddrive_*` 20 個、`max_chars` を 16000 に丸めて `MaxResultSizeChars` を明示、Version Defines の最小は 4.2.0 のまま、実装メモ = [1002](1002_ddrive_mcp.md)） | isuzu MCP を v4.2.0 → **v4.4.2** に上げる（[archive/1006](archive/1006_release_1_5_0.md) §4 の検討結果。`McpPortPolicyTests` / `McpToolsSnapshotTests` / `tools/list` 20 個 / `measure-tokens.py` 再実行、`console_read_logs` の `stack_trace:true` を SKILL.md に追記、`MaxResultSizeChars` の明示を検討）。MS2026 は別タイミング | 0.5 | MCP-12 | 4 テストと HTTP スモーク green、docs/20 §3 更新 |
| MCP-14 | ✅ 実装 2026-10-07（ブランチ `mcp/mcp-14-isuzu-install`。EditMode 2075・PlayMode 964 green。持ち込み先での実押下は未検証 = [1005](verification/1005_manual_verification_mcp.md) §9。実装メモ = [1002](1002_ddrive_mcp.md) §11.4） | **isuzu MCP の導入を更新ウィンドウに統合**（[1002](1002_ddrive_mcp.md) §11）: 「パッケージ」一覧の「導入」ボタン（推奨版のタグ固定で manifest に追加 + 管理対象に登録）、ウィザードのチェック、導入後の案内（開き直し・`register-mcp.ps1` 実行・書き込み可否）、**他の MCP が入っているときの確認（続行 / 既知のものを外して続行 / キャンセル。未知のものは外さない）**、Info `DD-MCP-MULTIPLE` / `DD-MCP-ISUZU-OUTDATED`、`ddrive_status.mcp.otherMcp` / `isuzuVersion`。docs（42 / 50 / mcp.html / 20 / 消費者 SKILL / 1005 §9） | 1.5 | MCP-13 | MS2026 で未導入 → 導入 → 登録 → `ddrive_status` が通る。CoplayDev 入りで「外して続行」が manifest の 1 行だけ消す。ユーザー指示 2026-10-07 |

## 2. 次に起票する候補（[1001](1001_open_items.md) から。着手を決めたら §1 と同じ表形式で節を足す）

- GC-R-01（BGM ループ位置の Warning）・GC-R-07（尺 0 のゴールデン）: v1.5.0 に同梱するか、次の PATCH か
- `UiTweenManager.Tick` / `UiManager.Tick` の再入耐性（v1.3.1 からの不具合）
- M-5（時間源の公開 API）: ユーザー判断待ち

**MCP の実装中に見つかった後続候補（v1.5.0 には含めない。MCP-12 のレビューで優先度を決める）**:

- Validator への Code 付与: `ddrive_validate` の `byCode` が、Code を持たない既存 Validator の指摘を `(none)` にまとめてしまう（このプロジェクトでは Warning 22 / Info 35 がほぼ `(none)` の 2 行）。主要な Validator から順に `DD-*` の Code を付ければ、AI が `byCode` と `ddrive_help validation:<code>` で原因を絞れる（追加のみ）
- `ddrive_preview play` の対応種別を広げる: いまは Se / Bgm / Vfx / Presentation だけ。Shake / Haptics / Anim / UiTween / Cutscene は再生の経路が各エディタのウィンドウ内部にあり、MCP から呼べる共有サービスが無い。サービスを `DDrive.Editor` へ抽出すれば、ウィンドウと MCP の両方が使える
- `ddrive_preview open` の配置を Vfx 以外にも広げる（Se / Bgm 以外の種別で `{placed:false, hint}` になっている。同じく配置経路がウィンドウ内部のため、サービスの抽出が前提）
- ツール定義の圧縮: `ddrive_*` 20 個の定義で 10,275 文字（[1002](1002_ddrive_mcp.md) §10）。毎ターン再送する見方では G-2（前の 1/3 以下）が未達なので、説明・引数の説明文を詰める、または isuzu の `AlwaysLoad=false`（遅延ロード）に乗せられるかの確認
- `DD-MCP-FIXED-PORT` の検出精度: Validator は記述子の `preferredPort` が導出ポートと違うかで判定するため、固定値がたまたま導出ポートと同じだと検出できない（`ddrive_status.mcp.fixedPort` は `McpSettings` を直接読むので正確）。`ddrive_status` と同じ精度に揃える

## 更新履歴

- 2026-10-07: MCP-11 ✅（docs・CHANGELOG・人による確認手順 1005）。§0 の v1.5.0 を「MCP-12 待ち」に。§2 に MCP 実装中に見つかった後続候補 5 件を追記
- 2026-10-07: 作成。docs/11 の「MCP チケット」節をここへ移した（docs/11 には参照だけ残す）。MCP-0 ✅・MCP-8 前半 🔶 を反映
- 2026-10-07: v1.5.0 リリース（MCP-12 ✅）。MCP-13（isuzu v4.4.2 への更新）を起票
- 2026-10-07: MCP-13 完了（isuzu v4.4.2）
- 2026-10-07: MCP-14（isuzu 導入の更新ウィンドウ統合、ユーザー指示）を起票。MCP-13 着手
- 2026-10-07: MCP-14 を実装（`McpPackageSupport` / `McpInstallActions`、更新ウィンドウの「導入」、ウィザード節、Info `DD-MCP-MULTIPLE` / `DD-MCP-ISUZU-OUTDATED`、`ddrive_status.mcp.otherMcp` / `isuzuVersion`）
