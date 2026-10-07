# AGENTS.md — D-Drive（Codex 等 AI エージェント向け）

D-Drive は Unity 6 製の「デザイナー駆動アセットパイプライン」プロジェクト。設計の真実は `docs/` にある。コードと docs が食い違ったら docs を確認し、変更したら docs も同じ PR で更新する。

このファイルは Codex など `.claude/skills/` を読まないエージェント向けの自己完結した要約。**Claude Code 用の詳細手順（新種別追加のフルチェックリスト・MCP 検証ループの具体的なコマンド列等）は [`.claude/skills/ddrive-agent-workflow/SKILL.md`](.claude/skills/ddrive-agent-workflow/SKILL.md) と [`.claude/skills/ddrive-agent-workflow/references/new-asset-type-checklist.md`](.claude/skills/ddrive-agent-workflow/references/new-asset-type-checklist.md) にある。ツールが違うだけで規約は同じなので、詳細で迷ったらそちらも読むこと。** 一次情報は常に [`CLAUDE.md`](CLAUDE.md) と `docs/`。

## 1. 禁止事項（最優先）

1. `.unity` / `.prefab` / `.asset` / 画像 / 音をテキスト編集しない。Unity Editor（人 or MCP ツール経由）で行う。`.meta` を手で作らない・消さない・GUID を書き換えない
2. `Library/ Temp/ Logs/ UserSettings/ obj/ *.csproj *.sln` は生成物。読むのは可、編集・コミット不可
3. 禁止 API: `Instantiate` / `Resources.Load` / `AudioSource.Play` の直接呼び出し禁止。ランタイム asmdef から `UnityEditor` 参照禁止。Tick/Spawn/Play の定常経路で LINQ・クロージャ・boxing 禁止
4. 例外で処理を止めない。警告 + no-op / Placeholder で継続する（デザイナーの作業を止めないため）
5. Data（`AssetDataBase` 派生の ScriptableObject）は読み取り専用。Manager が書き換えない
6. メニューパスの文字列直書き禁止（`DDriveMenu` 定数経由）。新規 EditorWindow は `ScrollView` ルート必須
7. プレビューは実 Manager を Editor から駆動する。**ウィンドウ内描画は避け、確認用シーン / Prefab を開いて SceneView で確認する**
8. Manager を `new` するのは `DDriveRuntimeBootstrap` / テスト / Editor プレビューだけ
9. 迷ったら実装せずに聞く。特にシリアライズ形式（フィールド削除・型変更）・asmdef 構成・ProjectSettings の変更
10. **互換性ポリシー（`docs/42_distribution.md` §5、2026-09-20 発効）を守る。** D-Drive は `com.ddrive.core` として持ち込み先（MS2026）から git URL で参照されている。シリアライズ形式・enum・ID/定数名・公開 API（`DDrive.Foundation`/`DDrive.Runtime`）・ContentHash・ネットメッセージ・生成コード・Validation の重さ・外部拡張の契約（[docs/42 §5.14](docs/42_distribution.md)）は**追加のみ**（削除・改名・型変更は MAJOR = §5.12 の手続きとユーザー承認が必須）。`Packages/com.ddrive.core/Tests/Editor/Compat` のスナップショットテストが赤なら変更しない（意図した追加なら `Tools > D-Drive > Compat > スナップショットを更新` + `CHANGELOG.md` の互換性節に追記。`docs/12_review.md` §3「互換性」）

## 2. ディレクトリ地図

```
Assets/DDrive/                 D-Drive 本体。層 = asmdef
  Foundation/   Registry / Loader / Pool / Handle / EventBus / Pause / ValueDef / Validation
  Runtime/      種別ごとの Data / Manager / 静的ファサード(Audio, Vfx, Anim, CameraFx, Haptics ...)
                Anchoring / Presentation / Loop(GameLoopDriver・DDriveRuntimeBootstrap) / Net
  Editor/       AssetBrowser / 各専用エディタ / Preview / Codegen / Validation
  Tests/        Editor(EditMode) / Runtime(全プラットフォーム対象asmdefのため PlayMode でのみ実行される)
Assets/GameData/               ツールが管理する Data(.asset)・カタログ・標準プレハブ・確認用シーン
Assets/SourceAssets/           人が管理する実データ(音源・モデル・テクスチャ等)
docs/                          設計書(00〜32)。DesignerManual/ はデザイナー向け HTML
Tools/SpecWeb/                 発注ツール(Google Apps Script、clasp 管理)
```

`Assets/GameData/` 配下のファイル名・配置フォルダは AssetBrowser が自動生成・維持するもので、人（エージェント含む）が手で決めない。

## 3. 作業手順

1. 関連する `docs/0X_*.md` と既存コード（似た種別の実装）を grep してから書く。重複実装が最大の事故要因
2. 実装 → `docs/12_review.md` §3 のチェックリストで自己レビュー
3. コンパイル・テスト確認: Unity MCP（`isuzu-unity`。2026-10-07 から isuzu 版のみ）が繋がっていれば `compile_status`/`console_read_logs` でエラー 0、テストを EditMode と PlayMode の両方で green にする。**繋がっていなければ「未検証」と明示する**（Unity を操作した/確認したと嘘をつかない）
4. 公開 API / データ構造 / エディタ機能を変えたら、対応する `docs/` を同じ PR で更新する（変更履歴は該当節に日付付きで追記する慣習）
5. 新しい `AssetType` を追加する場合の手順は `.claude/skills/ddrive-agent-workflow/references/new-asset-type-checklist.md` を参照（enum 末尾追加のみ・`[AssetIdDefinition]` 属性で ID 生成が自動化される・`AssetNamingService`/`AssetCreationService` への switch 追加・Validator は `IValidator` を実装するだけで自動検出される、等）

### 3.1 D-Drive の操作は MCP ツール `ddrive_*` で（v1.5.0）

| やりたいこと | 使うツール（`mcp__isuzu-unity__ddrive_*`） |
|---|---|
| Data の一覧・取得・作成・値の変更・削除 | `ddrive_asset_list` / `get` / `create` / `set` / `delete`（`execute_code` で `AssetCreationService` を呼ばない） |
| 参照元・未使用 Data の確認、専用エディタを開く | `ddrive_asset_usages` / `unused` / `ddrive_editor_open` |
| 検査・自動修正・禁止 API の走査 | `ddrive_validate`（既定は件数と Code 別の表）/ `ddrive_validate_fix` / `ddrive_forbidden_api` |
| ID・Tuning・Addressables・依存グラフ等の生成、マイグレーション | `ddrive_generate`（`kind` = ids / tuning / addressables / preload / prefabs / deps / icons）/ `ddrive_migrate` |
| 互換性スナップショット・リリース前チェック | `ddrive_compat` / `ddrive_compat_update` / `ddrive_release_check` |
| 確認用シーン・実 Manager での再生、NetCheck ビルド | `ddrive_preview`（`action` = open / play / stop / stop_all / sweep / status）/ `ddrive_build_netcheck` |

- **最初に `ddrive_help topic=rules` と `ddrive_status`** を 1 回ずつ呼ぶ（禁止事項の要約と、コンパイル・検査・マイグレーション・書き込み許可の現状が分かる）。`ddrive_help topic=tool:<name>` / `validation:<code>` で個別の案内
- **`execute_code` より `ddrive_*` を優先**する（Undo・SetDirty・Addressables 登録・検査・読み取り専用欄の保護が入っており、返り値も小さい）。`ddrive_*` で足りない操作だけ `execute_code` / `menu_execute`
- **書き込みツールは Project Settings > D-Drive > MCP の「書き込みツールを許可する」（`McpAllowWrite`）が ON のときだけ動く**（OFF は `write_disabled`。開発リポジトリは ON）。Play Mode 中の書き込みは `play_mode` で拒否される。`preview:true` を持つツール（`asset_create` / `set` / `delete` / `generate` / `validate_fix`）は先に `preview` で確認してから実行する
- **Destructive のツール**（`ddrive_asset_delete` / `ddrive_validate_fix` / `ddrive_migrate` / `ddrive_compat_update`）は `confirm:true` が必須。ユーザーの意図が明確なときだけ付ける
- **クライアント登録**は `pwsh Tools/Mcp/register-mcp.ps1`（記述子から接続情報を読んで `claude mcp add`。Unity 再起動のあと 401 になったら再実行）。ポートを直書き・固定しない
- **コンパイル・テストの確認ループは今までどおり** isuzu の `compile_request` / `compile_status` / `test_run` / `test_results`（`ddrive_status` は結果の要約を見るだけで、実行はしない）
- ツールの契約は `Tests/Editor/Compat/Snapshots/mcp-tools.txt`（追加のみ。[docs/42](docs/42_distribution.md) §5.14 E-24）。詳細は [docs/1002](docs/1002_ddrive_mcp.md)・[docs/09](docs/09_editor_tools.md) §15

## 4. 検証ループの要点

- isuzu-unity MCP: `compile_request` → 20〜25 秒待つ → `compile_status` で `succeeded` 確認 → `test_run mode=edit`/`mode=play` → `test_results` をポーリング（EditMode 20〜30 秒、PlayMode 1〜2 分）
- **テスト実行前に空きメモリを確認する**（低メモリで Unity がクラッシュした実例がある。目安 1GB 未満なら待つ）
- テストは実 `Assets/GameData/` のカタログ・Addressables グループを汚してはいけない。テスト前後で `git status`/`git diff` に差分が出ないことを確認する（特に `Assets/AddressableAssetsData/AssetGroups/*.asset`）
- ネットワーク機能（NGO/`INetBridge`）は PlayMode テストに加えて実機確認が必要（`docs/verification/29_network_device_test.md`）。ユニットテストだけでは検出できない実バグが複数回見つかっている
- Unity 再起動でポート/トークンが変わって MCP が繋がらなくなったら、`%LOCALAPPDATA%\UnityMCP\instances\<hash>.json` から port/token を読み直し、Bearer トークン付きで直接 JSON-RPC を POST するフォールバックがある（`docs/20_mcp_setup.md` 参照）

## 5. コミット・PR の慣習

- 1 PR = 1 チケット。500 行超は分割。ブランチ名は `feat/<ticket>-<slug>` や `p6/<ticket>-<slug>`
- コミットメッセージは日本語。1 コミット = 1 チケット単位を意識する
- 明示パスで `git add`（`-A`/`.` は使わない。ユーザーの未コミット作業中のアセットを誤って含めないため）
- `docs/11_tasks.md` のチケット行は完了したら AC の右に「→ ✅ 実装（要約）: …」を追記する既存の書式に合わせる
- ユーザーの未コミット資産（デザイナーが編集中の `.asset`/シーン等）には触れない
- PR は `gh pr create`、テストが green ならレビュー待ちで止まらず `gh pr merge` で main へ進めてよい運用（自動レビューは常設ではない）

## 6. Tools/SpecWeb（発注ツール、Google Apps Script）を触るとき

- Node テスト（`Tools/SpecWeb/test/`）で大半のロジックは検証できるが、**iframe サンドボックス内の実際の挙動（画面遷移・クリップボード等）は Node テストでは検証できない**。実デプロイでの目視確認が必須
- 反映は `cd Tools/SpecWeb && ./push.cmd`（`build-manual.js` を実行してから `clasp push`）
- **push の前にブラウザで開いている Apps Script エディタのタブを閉じる（または再読み込みする）こと。** 開いたままだと自動保存で最新コードが古い内容に上書きされる実例がある
- `clasp push` だけではデプロイ URL に反映されない。デプロイの「新しいバージョン」化は別作業

## 7. 参照ドキュメント

| ファイル | 内容 |
|---|---|
| [CLAUDE.md](CLAUDE.md) | 一次情報。TL;DR 禁止事項・進捗・MCP 状態 |
| [docs/01_architecture.md](docs/01_architecture.md) | 4 層 / Data・Instance・Handle 分離 / ADR |
| [docs/02_core_framework.md](docs/02_core_framework.md) | AssetId / Registry / Loader / Pool / Validation / Bootstrap 配線 |
| [docs/09_editor_tools.md](docs/09_editor_tools.md) | AssetBrowser・メニュー規約・`[DataEditor]`・ウィンドウ規約 |
| [docs/10_workflow.md](docs/10_workflow.md) | ロール・命名・配置規約 |
| [docs/12_review.md](docs/12_review.md) | PR チェックリスト |
| [docs/20_mcp_setup.md](docs/20_mcp_setup.md) | Unity MCP セットアップ・運用ルール |
| [docs/verification/29_network_device_test.md](docs/verification/29_network_device_test.md) | 実機ネットワーク確認手順 |
| [docs/32_spec_web.md](docs/32_spec_web.md) | 発注ツール（GAS）設計・実装メモ |
| [.claude/skills/ddrive-agent-workflow/SKILL.md](.claude/skills/ddrive-agent-workflow/SKILL.md) | Claude Code 向けの同内容の詳細版（本ファイルはこちらの要約） |
