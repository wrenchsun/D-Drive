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
3. コンパイル・テスト確認: Unity MCP（isuzu-unity 優先、無ければ CoplayDev）が繋がっていれば `read_console`/コンソール確認でエラー 0、テストを EditMode と PlayMode の両方で green にする。**繋がっていなければ「未検証」と明示する**（Unity を操作した/確認したと嘘をつかない）
4. 公開 API / データ構造 / エディタ機能を変えたら、対応する `docs/` を同じ PR で更新する（変更履歴は該当節に日付付きで追記する慣習）
5. 新しい `AssetType` を追加する場合の手順は `.claude/skills/ddrive-agent-workflow/references/new-asset-type-checklist.md` を参照（enum 末尾追加のみ・`[AssetIdDefinition]` 属性で ID 生成が自動化される・`AssetNamingService`/`AssetCreationService` への switch 追加・Validator は `IValidator` を実装するだけで自動検出される、等）

## 4. 検証ループの要点

- isuzu-unity MCP: `compile_request` → 20〜25 秒待つ → `compile_status` で `succeeded` 確認 → `test_run mode=edit`/`mode=play` → `test_results` をポーリング（EditMode 20〜30 秒、PlayMode 1〜2 分）
- **テスト実行前に空きメモリを確認する**（低メモリで Unity がクラッシュした実例がある。目安 1GB 未満なら待つ）
- テストは実 `Assets/GameData/` のカタログ・Addressables グループを汚してはいけない。テスト前後で `git status`/`git diff` に差分が出ないことを確認する（特に `Assets/AddressableAssetsData/AssetGroups/*.asset`）
- ネットワーク機能（NGO/`INetBridge`）は PlayMode テストに加えて実機確認が必要（`docs/29_network_device_test.md`）。ユニットテストだけでは検出できない実バグが複数回見つかっている
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
| [docs/29_network_device_test.md](docs/29_network_device_test.md) | 実機ネットワーク確認手順 |
| [docs/32_spec_web.md](docs/32_spec_web.md) | 発注ツール（GAS）設計・実装メモ |
| [.claude/skills/ddrive-agent-workflow/SKILL.md](.claude/skills/ddrive-agent-workflow/SKILL.md) | Claude Code 向けの同内容の詳細版（本ファイルはこちらの要約） |
