---
name: ddrive-agent-workflow
description: D-Drive（Unity 6 プロジェクト）でコード・データ・ドキュメントを変更するときに読む運用手順。新しい AssetType（種別）を追加する、Unity MCP（isuzu-unity / CoplayDev）でコンパイル・テストを確認する、git worktree で並行作業する、コミット・PR・docs 更新の慣習に従う、Tools/SpecWeb（発注ツール、GAS）を検証する――のいずれかを行うときに使う。D-Drive のリポジトリでの実装作業に着手する前に必ず参照すること。
---

# D-Drive エージェント作業手順

D-Drive の設計の真実は `docs/` にあり、TL;DR の禁止事項は `CLAUDE.md` §0 にある。**このスキルは CLAUDE.md の要約ではなく、それを実行するための具体的な手順書**（実ファイルパス・既存の手本ファイル付き）。矛盾したら `CLAUDE.md` と `docs/` を優先する。

## 0. 着手前に必ず守る禁止事項（CLAUDE.md §0 の要約）

1. `.unity` / `.prefab` / `.asset` / 画像 / 音をテキスト編集しない。Unity Editor（MCP ツール）経由で行う。`.meta` を手で作らない・消さない・GUID を書き換えない
2. `Library/ Temp/ Logs/ UserSettings/ obj/ *.csproj *.sln` は生成物。読むのは可、編集・コミット不可
3. 禁止 API: `Instantiate` / `Resources.Load` / `AudioSource.Play` の直接呼び出し禁止（`ForbiddenApiScanner` が検出）。ランタイム asmdef から `UnityEditor` 参照禁止。Tick/Spawn/Play の定常経路で LINQ・クロージャ・boxing 禁止
4. 例外で止めない。警告 + no-op / Placeholder で継続する
5. Data は読み取り専用。エディタが書き換えるときは必ず `Undo.RecordObject` + `EditorUtility.SetDirty`
6. メニューパス直書き禁止（`DDriveMenu` 定数経由）。新規 EditorWindow は `ScrollView` ルート必須。Data 専用エディタには `[DataEditor(typeof(XxxData), "…で開く")]` を付ける
7. プレビューは実 Manager を Editor から駆動する（ADR-4）。**ウィンドウ内描画は避け、確認用シーン / Prefab を開いて SceneView で確認する**
8. Manager を `new` するのは `DDriveRuntimeBootstrap` / テスト / Editor プレビューだけ。作成した Data は Addressables に同じ address で登録されていること
9. 迷ったら実装せずに聞く。特にシリアライズ形式（フィールド削除・型変更）・asmdef 構成・ProjectSettings
10. **互換性ポリシー（[`../../docs/42_distribution.md`](../../docs/42_distribution.md) §5、2026-09-20 発効）を守る。** D-Drive は `com.ddrive.core` として持ち込み先（MS2026）から git URL で参照されている。シリアライズ形式・enum・ID/定数名・公開 API（`DDrive.Foundation`/`DDrive.Runtime`）・ContentHash・ネットメッセージ・生成コード・Validation の重さは**追加のみ**（削除・改名・型変更は MAJOR = §5.12 の手続きとユーザー承認が必須）。`Packages/com.ddrive.core/Tests/Editor/Compat` のスナップショットテストが赤なら変更しない。意図した追加なら `Tools > D-Drive > Compat > スナップショットを更新`（`CompatSnapshotMenu`）を実行し、同じコミットで `CHANGELOG.md` の `[Unreleased]` 互換性節に「何を・なぜ・MINOR/MAJOR どちらか」を書く（[`../../docs/12_review.md`](../../docs/12_review.md) §3「互換性」、リリース手順は同 §7）

詳細は [`../../CLAUDE.md`](../../CLAUDE.md) と [`../../docs/12_review.md`](../../docs/12_review.md) §3（PR チェックリスト）。

## 1. 新しい AssetType（種別）を追加する

**チェックリストと既存の手本ファイル一覧は [`references/new-asset-type-checklist.md`](references/new-asset-type-checklist.md) を参照。** 概要:

1. `Packages/com.ddrive.core/Foundation/Identity/AssetType.cs` の enum に**末尾追加のみ**（既存値の並び替え・削除は既存アセットの種別を破壊する）
2. Data クラス（`AssetDataBase` 派生）に `[CreateAssetMenu(menuName = "D-Drive/...")]` + `[AssetIdDefinition(AssetType.X, typeof(XMarker), "XID")]` を付ける（ID 定数生成は `AssetIdGenerator` がこの属性を反射で自動収集するため、他に登録作業は不要）
3. Manager（`IAssetManager` 実装）+ Handle/Instance + 静的ファサード（Bind/Unbind パターン）を実装
4. Validator（`IValidator` 実装、public 引数無しコンストラクタ）を追加すれば `CI.cs`/AssetBrowser の Validation に自動で載る（登録リストは無い）
5. `AssetNamingService.GetTypePrefix` / `GetTargetFolder`（`Packages/com.ddrive.core/Editor/AssetBrowser/AssetNamingService.cs`）に switch case を追加
6. `AssetCreationService`（`Packages/com.ddrive.core/Editor/AssetBrowser/AssetCreationService.cs`）にカタログ名マッピングを追加。**Manager が同期 API（`ResolveOrPlaceholder`）のみで解決する種別は既定 Load を Preload にする**（Shake/Haptics/Presentation/Canvas/ControlSkin と同じ理由。[`../../docs/02_core_framework.md`](../../docs/02_core_framework.md) §4 参照）
7. `DDriveRuntimeBootstrap.cs`（`Packages/com.ddrive.core/Runtime/Loop/`）に Manager 生成・`loop.Register`・静的ファサード Bind/Unbind を追加
8. 専用エディタ（EditorWindow、`ScrollView` ルート必須）+ `[DataEditor]` 属性 + `DDriveMenu` 経由のメニュー登録 + プレビューは確認用シーン駆動（ウィンドウ内描画にしない）
9. テスト: Validator の EditMode テスト、Manager の PlayMode テスト（`Packages/com.ddrive.core/Tests/{Editor,Runtime}/`）
10. docs 更新（同じ PR で）: 種別の設計 doc・[`../../docs/02_core_framework.md`](../../docs/02_core_framework.md)（Bootstrap 配線メモ）・[`../../docs/09_editor_tools.md`](../../docs/09_editor_tools.md) §6/§8（メニュー一覧・DataEditor 対応表）・[`../../docs/11_tasks.md`](../../docs/11_tasks.md)（チケット行に ✅ 実装メモ）・デザイナー向け機能なら `docs/DesignerManual/*.html`

## 2. 検証ループ（Unity MCP）

D-Drive には isuzu-unity（組み込み、ポート 27725、`mcp__isuzu-unity__*`）と CoplayDev（別プロセス、HTTP）の 2 系統がある。**両方繋がっているときは isuzu-unity を優先する**（[`../../docs/20_mcp_setup.md`](../../docs/20_mcp_setup.md) §4）。

### 基本ループ

1. `compile_request` → 約 20〜25 秒待つ（`run_in_background: true` の `sleep`。ドメインリロード中は接続エラーになるので即座にポーリングしない）
2. `compile_status` で `succeeded` を確認。エラーがあれば `console_read_logs`（type=error）でスタックトレースを読む
3. **テスト実行前に空きメモリを確認する**（このマシンは低メモリ時に Unity がクラッシュした実例がある。目安: 空きメモリが 1GB を切っているならテストを待つ／ユーザーに他アプリを閉じるよう依頼する）
4. `test_run mode=edit` → `test_results` をポーリング（EditMode 全件で概ね 20〜30 秒）。green を確認
5. `test_run mode=play` → `test_results` をポーリング（PlayMode 全件で概ね 1〜2 分。`Tests/Runtime` は asmdef が全プラットフォーム対象のため PlayMode でしか走らない）。green を確認
6. **EditMode と PlayMode の両方が green になるまで完了報告しない**（[`../../CLAUDE.md`](../../CLAUDE.md) §3）

### テストで実データを汚さない確認

- テストは `ScriptableObject.CreateInstance` や一時 `TestRoot` のみを使うべきで、実 `Assets/GameData/` のカタログ・Addressables グループを書き換えてはいけない
- **テスト前後で `git status`/`git diff` に差分が出ないことを確認する**。特に `Assets/AddressableAssetsData/AssetGroups/*.asset` は差分が出やすい（過去に `DependencyTreeBuilderTests` 等が実 Addressables グループへ登録して残骸を残した実例がある）。差分が出たら `git checkout -- <path>` で戻すか、テスト側の隔離漏れとして修正する

### 未検証時の扱い

- **Unity MCP に接続できていないのに「コンパイル/テストを確認した」と報告しない**。未検証なら「未検証」と明示する
- ネットワーク機能（NGO 統合、`INetBridge` 実装、Presentation の同期再生等）は PlayMode テストに加えて**実機確認が必要**（[`../../docs/29_network_device_test.md`](../../docs/29_network_device_test.md)）。ユニットテスト（Fake/Delayed ブリッジ）だけでは検出できない実バグが過去に複数回見つかっている

### isuzu-unity のトークンが変わったときのフォールバック

Unity Editor を再起動するとポート・Bearer トークンが変わり、既存セッションの `mcp__isuzu-unity__*` ツールが繋がらなくなることがある。サーバー自体は生きていることが多いので、Python で直接 JSON-RPC を叩く:

1. `%LOCALAPPDATA%\UnityMCP\instances\<hash>.json` を読んで `port` / `mcpUrl` / `token` を取得（Unity 再起動直後は毎回読み直す。古いトークンは使えない）
2. `Content-Type: application/json`, `Accept: application/json, text/event-stream`, `Authorization: Bearer <token>` を付けて `urllib`（または `requests`）で POST し、`compile_request`/`compile_status`/`test_run`/`test_results` を叩く
3. スクリプトは scratchpad に置き、`python script.py <tool> '<json>'` の形で呼べるようにする

詳細は [`../../docs/20_mcp_setup.md`](../../docs/20_mcp_setup.md)。

## 3. ワークツリー・並列作業の慣習

- **Unity Editor が開いているのはメインのリポジトリ**（`C:\Users\yamag\wrench\D-Drive`）。`.claude/worktrees/` 配下のワークツリーで作業しているときは Unity MCP を使わない（Unity は別プロセスのメイン checkout を見ている）。ワークツリーでの作業は docs のみ、あるいはコンパイル・テストを親セッション（メイン checkout）に任せる形にする
- `.cs` の新規作成は Unity が `.meta` を自動生成するので**手で作らない**。ワークツリーで `.cs` を書いた場合、マージ後にメインで一度コンパイル・テストを通す
- 複数エージェントが同時に触るとコンフリクトしやすいファイル（`docs/11_tasks.md`、`docs/28_manual_verification_phase5.md`、Addressables グループ 2 ファイル）は並行担当に触らせず、まとめ役が最後に 1 回だけ追記する
- テスト用の一時アセット・大量ファイルの下書きは scratchpad に置き、Assets 配下へのコピーはテストが終わってから行う（Assets への書き込みは自動リフレッシュで再コンパイルが走り、実行中のテストを壊すことがある）

## 4. コミット・docs の慣習

- ブランチ命名: `p6/<ticket>-<slug>` や `feat/<ticket>-<slug>`。1 PR = 1 チケット（[`../../docs/12_review.md`](../../docs/12_review.md) §2）
- コミットメッセージは日本語。1 コミット = 1 チケット単位を意識する（[`../../CLAUDE.md`](../../CLAUDE.md) §3）
- 明示パスで `git add`（`git add -A`/`git add .` は使わない。ユーザーの未コミット作業中のアセットを誤って含めないため）
- 公開 API / データ構造 / エディタ機能を変えたら、対応する `docs/0X_*.md` を**同じ PR**で更新する。変更履歴は該当節に日付付きで追記する慣習（例: 「**2026-09-14 追記(5-2)**: …」の形式を既存 docs から真似る）
- `docs/11_tasks.md` のチケット行は、完了したら AC の右に「→ ✅ 実装（要約）: …」を追記する既存の書式に合わせる
- ユーザーの未コミット資産（デザイナーが編集中の `.asset`/シーン等）には触れない。何が誰の未コミット変更かは `git status` で毎回確認する
- PR は `gh pr create`、マージは `gh pr merge --merge --delete-branch`（または `--squash`）。テストが green ならレビュー待ちで止まらず進めてよい運用（Codex の PR 自動レビューは停止中）
- ワークツリーから `main` にローカルで切り替えられずマージ自体は完了している場合、GitHub 上でマージ済み・ブランチ削除済みであることを確認できれば良い（ワークツリーは worktree-agent 用の隔離チェックアウトのため、`git switch main` は元のリポジトリでしか行えない）

## 5. Web 発注ツール（Tools/SpecWeb、Google Apps Script）の検証の注意

Tools/SpecWeb は Node テスト（`test/`）で大半のロジックを検証できるが、**iframe サンドボックス内の実際の挙動（画面遷移・クリップボード・textarea 操作等）は Node テストでは検証できない**。実デプロイでの目視確認が必須（[`../../docs/32_spec_web.md`](../../docs/32_spec_web.md) 「目視確認」節）。

- コード変更後は `cd Tools/SpecWeb && ./push.cmd`（`build-manual.js` を実行してから `clasp push` する。PowerShell 実行ポリシーの影響を受けないよう `.cmd` を推奨、`.ps1` を使う場合は BOM 付き UTF-8 保存必須）
- **push の前に、ブラウザで開いている Apps Script エディタのタブを閉じる（または再読み込みする）こと。** 古い内容を表示したままのエディタが自動保存すると、push した最新コードが古い内容で上書きされる実例がある
- `clasp push` だけでは既存のデプロイ URL に反映されない。デプロイ①（人向け SPA）②（API）を**同じ URL のまま新しいバージョンに**更新する操作はユーザー作業（[`../../Tools/SpecWeb/README.md`](../../Tools/SpecWeb/README.md) §7/§9）
- `docs/DesignerManual/*.html` を編集した場合は `clasp push` 単体でなく `push.cmd`/`push.ps1` を使うこと（マニュアルの生成物も一緒に送られる）
- Node テストが全部 green でも、実デプロイでの目視確認をスキップして「確認済み」と報告しない
