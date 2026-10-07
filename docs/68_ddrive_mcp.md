# 68. D-Drive MCP（v1.5.0 仕様書）

> **状態**: 仕様書（2026-10-07 起票、未実装）。v1.5.0（MINOR、追加のみ）の実装内容。チケットは [11_tasks.md](11_tasks.md) の「MCP チケット」節、未完了事項の索引は [67_open_items.md](67_open_items.md)。
> **要点**: D-Drive の Editor 機能（AssetBrowser・専用エディタ・Validation・生成・マイグレーション・確認用シーン・リリース道具）を **AI エージェントが MCP ツールとして直接呼べる**ようにする。**サーバーは新設しない**（Editor 組み込みの `jp.shiranui-isuzu.unity-mcp` にツールを足す）。**AI が使うトークンを最小にする**ことと、**ポート競合を構造的に起こさない**ことを設計の軸にする。
> **決めてほしいこと**は §9（Q-1〜Q-12）。それ以外はこの文書の案で進める。

---

## 1. 目的・ゴール

| # | ゴール | 測り方 |
|---|---|---|
| G-1 | AI が D-Drive の作業（Data の作成・値の変更・検査・生成・プレビュー・リリース前チェック）を、`execute_code` で C# を書かずに **1 ツール呼び出し 1 操作**で行える | [20](20_mcp_setup.md) §2「AI に任せてよい」列の作業が全部ツールで足りる |
| G-2 | 1 回の作業（例: SE を 1 件作って検査して確認用シーンで鳴らす）で AI が消費するトークンが、現状（`execute_code` + `read_console` + docs 参照）の **1/3 以下** | §5 の計測手順。代表シナリオ 5 本で前後比較 |
| G-3 | ポート競合が**起きない**（同じ PC で D-Drive と MS2026 を同時に開いても、他アプリが 8080/8081 を使っていても） | §6。固定ポートをどこにも書かない。競合しうる経路を列挙して全部潰す |
| G-4 | 互換性ポリシー（[42](42_distribution.md) §5）に従う。**ツール名・引数名・返り値のキーは契約**（追加のみ。改名・削除は MAJOR） | スナップショット `mcp-tools.txt` を Compat テストに追加（§7） |
| G-5 | 人が Unity で行う操作と同じ安全装置（Undo・SetDirty・Data は読み取り専用・YAML を直接触らない・Play Mode 中は書かない） | §4.3 の共通ガード。EditMode テストで検証 |

**やらないこと（v1.5.0）**: 新しい MCP サーバー・新しいトランスポートの実装、Unity 標準機能（GameObject 操作・シーン編集・スクリーンショット）のツール化（isuzu 版に既にある）、持ち込み先ごとのゲーム固有ツール（拡張点だけ用意する、§4.6）、SpecWeb（GAS）側の MCP 化。

---

## 2. 前提と現状

| 項目 | 現状（2026-10-07） | 出典 |
|---|---|---|
| MCP サーバー | 2 系統を併用: **CoplayDev** `com.coplaydev.unity-mcp` v10.2.0（別プロセスの Python サーバー、固定ポート 8080 → 8081 に変更した経緯あり）と **isuzu** `jp.shiranui-isuzu.unity-mcp` v4.2.0（Editor 組み込み、ポートはプロジェクトパスから自動決定、Bearer トークン必須） | [20](20_mcp_setup.md) §1・§4、`Packages/manifest.json` |
| 運用上の優先 | 両方繋がっているときは isuzu 版を優先（`test_run`/`compile_status`/`execute_code`/`menu_execute`）。「3 セッション問題なければ CoplayDev を外す」の判断が未了 | [CLAUDE.md](../CLAUDE.md) §4、[20](20_mcp_setup.md)「切り替えの判断基準」、[67](67_open_items.md) §5 |
| D-Drive 側の MCP 専用コード | **0 件**。AI は `execute_code`（Roslyn）で `AssetCreationService.Create(...)` 等の static メソッドを直接呼んでいる | 調査（2026-10-07） |
| isuzu の拡張 API | `[McpTool(name, description)]` を付けた **public static メソッド**を、全アセンブリから自動発見（`ToolCatalog.Build`。`DDrive.*` は除外されない）。引数は `[McpArg]`、`Destructive=true` で `confirm` / `dry_run` が自動注入、`MainThread`（既定 true）、`Idempotency`、`Group`、`MaxResultSizeChars`、`Examples`。名前は `^[a-z][a-z0-9_]{0,63}$` | `Library/PackageCache/jp.shiranui-isuzu.unity-mcp@*/Editor/Core/Attributes/McpToolAttribute.cs`、`ToolCatalog.cs` |
| isuzu のポート決定 | `Application.dataPath` を正規化（`\`→`/`、末尾 `/` 除去、Windows は小文字化）→ UTF-8 SHA256 → 先頭 4 バイト LE uint → `27200 + value % 800`。バインドは希望ポートを 200ms × 5 回試し、だめなら 27200〜27999 を走査して最初の空きを使う。希望と違えば記述子に `portMismatch=true`。Preferences の `httpPort` が正なら優先 | `McpPortPolicy.cs`、`McpHttpServer.cs` |
| isuzu の接続情報 | `%LOCALAPPDATA%\UnityMCP\instances\<hash>.json`（port / mcpUrl / token / pid / projectPath / protocolVersion）、`tokens\<hash>.token`。`<hash>` は `Application.dataPath` の SHA256 先頭 8 バイト | `McpInstanceDescriptor.cs`、`McpAuthToken.cs` |
| Editor の呼べる機能 | メニュー約 60 項目（`DDriveMenu`）、EditorWindow 32、UI を持たないサービス約 45（`AssetCreationService` / `CI.RunValidation` / `AssetIdGenerator.Regenerate` / `DDriveMigrationRunner` / `DependencyGraphService` / `SafeDeleteService` / `*PreviewSceneSetup` / `NetCheckBuilder.Build` / `ReleaseChecks` ほか）。メニューパス・`CI` のメソッド名・`[DataEditor]` 対応は `editor-contract.txt` で契約化済み | [09](09_editor_tools.md)、`Tests/Editor/Compat/Snapshots/editor-contract.txt` |

**結論（方式）**: isuzu 版の `[McpTool]` に乗る。理由は (1) サーバー・ポート・認証・ジョブ化（`job_status`）・ツール発見が既にあり、D-Drive が持つのは**ツール本体だけ**で済む、(2) Editor 内で動くので `execute_code` と同じメインスレッド規約で Undo/SetDirty が使える、(3) ポート競合の対策が既に「プロジェクトパスのハッシュ + 空きポート走査」で、固定ポートの CoplayDev 方式より筋がよい。CoplayDev 版は v1.5.0 で**外す**提案（Q-1）。

---

## 3. 全体像

```
AI クライアント(Claude Code 等)
   │  streamable HTTP + Bearer(isuzu 既存)
   ▼
jp.shiranui-isuzu.unity-mcp(Editor 組み込みサーバー、ポート自動)
   │  [McpTool] 自動発見
   ▼
DDrive.Editor.Mcp(新 asmdef。ツール本体 = 薄いアダプタ)   ← v1.5.0 で追加
   │  既存の static サービスを呼ぶだけ(ロジックは持たない)
   ▼
DDrive.Editor(AssetCreationService / CI / DDriveMigrationRunner / DependencyGraphService / PreviewSceneSetup / ...)
```

- **`DDrive.Editor.Mcp` asmdef**（`Packages/com.ddrive.core/Editor/Mcp/`）: `UnityMCP.Editor` を参照し、**Version Defines** `jp.shiranui-isuzu.unity-mcp >= 4.2.0` → `DDRIVE_UNITY_MCP` を定義、`defineConstraints: ["DDRIVE_UNITY_MCP"]`。**isuzu パッケージが無いプロジェクトではアセンブリごとコンパイルされない**（持ち込み先が MCP を使わなければ何も増えない）。`DDrive.Editor` 本体は `DDrive.Editor.Mcp` を参照しない（逆方向のみ）。
- **ツールはアダプタに徹する**: 引数の検証 → 共通ガード（§4.3）→ 既存サービス呼び出し → 返り値の圧縮（§5）。新しいロジックを `Mcp/` に書かない（書きたくなったら `DDrive.Editor` 側のサービスに足し、ツールはそれを呼ぶ。EditMode テストはサービス側で書く）。
- **ツール名の接頭辞は `ddrive_`**（isuzu 同梱の 141 ツールと混ざらない。`Group` は `authoring` / `diagnostics` / `build` のいずれかを明示）。

---

## 4. ツール一覧（v1.5.0）

凡例: **R** = 読み取りのみ（`Idempotency=Idempotent`）、**W** = 書き込み（Undo + SetDirty、`dry_run` 対応）、**D** = `Destructive=true`（`confirm` / `dry_run` 自動注入）、**J** = 長時間なのでジョブ化（isuzu の `job_status` で結果を取る）。

### 4.1 状態・案内（diagnostics）

| ツール | 種別 | 引数 | 返り値（要点） | 呼ぶ先 |
|---|---|---|---|---|
| `ddrive_status` | R | `sections?`（`compile,tests,validation,migration,addressables,mcp` のカンマ区切り。既定は全部） | `{version, schema, compile:{ok,errors}, tests:{last:{mode,passed,failed,at}}, validation:{errors,warnings,infos,at}, migration:{pending}, addressables:{missing}, mcp:{port,portMismatch,project}}` | `DDriveVersion`、`compile_status` 相当、前回の `ddrive_validate` の要約キャッシュ、`DDriveMigrationRunner.HasPendingMigrations`、`AddressablesSync`（dry）、`McpInstanceDescriptor` |
| `ddrive_help` | R | `topic`（`rules` / `types` / `tool:<name>` / `validation:<code>` / `menu`）、`max_chars?` | 短い案内文（§5.4 の「AI 向けカード」）。`rules` = CLAUDE.md §0 の禁止事項 10 行、`types` = AssetType と ID 接頭辞の表、`validation:<code>` = その Code の意味と直し方 1〜3 行 | 文字列テーブル（`Documentation~/Mcp/*.md` を `Resources` に埋めず、Editor の `TextAsset` 参照で持つ） |

### 4.2 Data の操作（authoring）

| ツール | 種別 | 引数 | 返り値（要点） | 呼ぶ先 |
|---|---|---|---|---|
| `ddrive_asset_list` | R | `type`（AssetType 名）、`category?`、`query?`（DisplayName / identifier の部分一致）、`fields?`（既定 `id,name,category`）、`cursor?`、`limit?`（既定 50、最大 200） | `{items:[{id,name,category,...}], next?}` | `AssetSearch.FindAssets` |
| `ddrive_asset_get` | R | `type`、`id`（ulong）または `path`、`fields?`（既定: 共通欄 + 種別の主要欄。`*` で全部） | `{id,name,path,fields:{...}, validation:{errors,warnings}}` | `SerializedObject` を読む（型ごとの欄の表は §4.5） |
| `ddrive_asset_create` | W | `type`、`name`（DisplayName）、`category`、`identifier?`（省略時は `AssetNamingService.ToIdentifier(name)`）、`fields?`（作成時に設定する欄）、`dry_run?` | `{id, path, addressable:true}`（dry_run なら `{wouldCreate:path}`） | `AssetCreationService.Create` → `AddressablesSync.EnsureEntry`。**`CreateAssetMenu` 直叩きはしない**（[20](20_mcp_setup.md) §2 の規約） |
| `ddrive_asset_set` | W | `type`、`id`、`fields`（`{欄名: 値}`。入れ子は `Common.Color` のようなドット区切り）、`dry_run?` | `{changed:[{field,from,to}], validation:{errors,warnings}}` | `SerializedProperty` 経由で書く。**`Undo.RecordObject` + `EditorUtility.SetDirty` + `DDriveAssetSave.SaveDirty`**。`Id` / `SchemaVersion` / `ImportSourceGuid` / `Version` / `UpdatedAt` は拒否（読み取り専用欄の一覧は §4.5） |
| `ddrive_asset_usages` | R | `type`、`id` | `{usages:[{path,objectPath}], count}` | `DependencyGraphService.FindUsages`（グラフ未構築なら `{needsRebuild:true}` を返し、`ddrive_generate(deps)` を案内） |
| `ddrive_asset_unused` | R | `type?`、`limit?` | `{items:[{type,id,name}], count}` | `DependencyGraphService.FindUnusedIds` |
| `ddrive_asset_delete` | D | `type`、`id`、`confirm`、`dry_run` | `{deleted:bool, blockers:[...]}` | `SafeDeleteService.TryDelete`（ダイアログは `ConfirmDialogOverride` で無効化し、結果を JSON で返す） |
| `ddrive_editor_open` | R | `type`、`id` | `{opened:"<Window 名>"}` | `DataEditorRegistry.OpenDefault`（人が見るためのもの。AI の返り値は 1 行） |

### 4.3 検査・生成・更新（diagnostics / authoring）

| ツール | 種別 | 引数 | 返り値（要点） | 呼ぶ先 |
|---|---|---|---|---|
| `ddrive_validate` | R | `scope`（`all` / `type:<name>` / `asset:<type>:<id>` / `project`）、`detail?`（`summary` 既定 / `errors` / `all`）、`codes?`（絞り込み）、`limit?` | `summary`: `{errors,warnings,infos, byCode:[{code,severity,count}]}`。`errors` / `all`: 加えて `items:[{code,sev,type,id,name,msg}]`（msg は 200 文字で切る） | `CI.RunValidation` / `DataValidationRunner.Run`。結果は `ddrive_status` 用にキャッシュ |
| `ddrive_validate_fix` | W | `codes?`、`dry_run?` | `{applied:[{code,count}], skipped:[...]}` | `ProjectWideValidationFixes.FindFixable` → `Apply`。`FixAction` 付きの指摘だけ |
| `ddrive_forbidden_api` | R | `root?`、`detail?` | `{violations:n, notices:n, items?:[{rule,file,line}]}` | `ForbiddenApiScanner.ScanDetailed` |
| `ddrive_generate` | W | `target`（`ids` / `tuning` / `addressables` / `preload` / `prefabs` / `deps` / `icons`）、`dry_run?` | `{target, changed:bool, summary}`（`ids` は `{total,assigned,duplicates:[...]}`） | `AssetIdGenerator.Regenerate` / `TuningCodegen.Regenerate` / `AddressablesSync.SyncAll` / `ScenePreloadGenerator` / `DefaultPrefabs.GenerateAll` / `DependencyGraphService.RebuildAll` / `AssetIconService.CreateDefaultIconsForAll(onlyMissing:true)` |
| `ddrive_migrate` | D | `mode`（`plan` / `apply`）、`confirm`、`dry_run` | `plan`: `{pending:[{id,targets}]}`。`apply`: `{applied:[...], failed:[...]}` | `DDriveMigrationRunner.PlanProject` / `ApplyToProject` |
| `ddrive_compat` | R / D | `mode`（`diff` 既定 / `update`）、`confirm`（update のみ） | `diff`: `{changed:[{snapshot, added:n, removed:n, sample:[...]}]}`。removed > 0 は互換性違反の疑いとして `warning` を付ける | `EditorContractSnapshotBuilder` 等の既存ビルダーで生成し、保存済みファイルと比較。`update` = `CompatSnapshotMenu.UpdateAll` |
| `ddrive_release_check` | R | `base?`（タグ名。既定 `origin/main`）、`guard_only?` | `{ok, checks:[{name,ok,msg}]}` | `Tools/Release/ReleaseChecks.ps1` を `pwsh` で起動し JSON を読む（Editor 内で再実装しない） |

### 4.4 プレビュー・ビルド（authoring / build）

| ツール | 種別 | 引数 | 返り値（要点） | 呼ぶ先 |
|---|---|---|---|---|
| `ddrive_preview_open` | W | `scene`（`common` / `canvas` / `cutscene` / `shake`）、`type?`、`id?`（開いた後に対象を配置する） | `{scene:path, placed?:objectPath}` | `VfxPreviewSceneSetup` / `CanvasPreviewSceneSetup` / `CutscenePreviewSceneSetup` / `CameraShakePreviewSceneSetup` の `TryOpenOrCreate`（保存確認ダイアログは**出さず**、未保存シーンがあれば `{blocked:"unsaved scene"}` で返す） |
| `ddrive_preview_play` | W | `type`、`id`、`action`（`play` / `stop` / `stop_all`） | `{handle?:int, ok}` | 各ファサード（`Audio.Play` / `Vfx.Play` / `Presentation.Play` ...）を **Editor プレビュー経路（実 Manager、ADR-4）**で呼ぶ。Play Mode 中は拒否 |
| `ddrive_preview_sweep` | W | — | `{destroyed:n}` | `EditorPreviewSweeper.DestroyOrphans` |
| `ddrive_build_netcheck` | J | `development?`（既定 true） | ジョブ ID → `job_status` で `{success, exe, zip, error}` | `NetCheckBuilder.Build` |

### 4.5 型ごとの欄の表（`ddrive_asset_get` / `ddrive_asset_set` の `fields`）

- **共通欄**（`AssetDataBase`）: `DisplayName` / `Description` / `Category` / `Tags` / `Assignee` / `SpecUrl` / `Author` / `ChangeNote` / `Flags.*` / `Events.*`。**読み取り専用**: `Id` / `SchemaVersion` / `ImportSourceGuid` / `Version` / `UpdatedAt` / `Icon`（`ddrive_asset_set` は拒否して `{rejected:[field]}` を返す）。
- **種別ごとの主要欄**（既定で返すもの）は、各種別の設計 doc の「データ構造」節の**先頭 10 欄以内**とし、実装時に `Editor/Mcp/FieldTables.cs` に表で持つ（例: SeData = `Clip,Volume,Pitch,Loop,Spatial,Priority,Category`、VfxData = `Prefab,Anchor,Duration,RenderMode,Scale`）。`fields:"*"` で全欄。欄名はシリアライズ名そのまま（`m_` を付けない。`SerializedProperty` のパス）。
- 値の型: `ValueDef` は `{mode:"Constant",value:1.0}` / `{mode:"Curve",...}` の最小 JSON（[17](17_value_definition.md) の形式をそのまま）。`AssetId` 参照は `{type,id}`。Unity オブジェクト参照はアセットパス文字列。

### 4.6 拡張点（持ち込み先がツールを足す）

- 持ち込み先は、自分の Editor asmdef で `[McpTool("ms2026_xxx", ...)]` を付けた static メソッドを書くだけ（isuzu の自動発見に乗る）。D-Drive は**接頭辞 `ddrive_` を予約**し、それ以外には関与しない。
- D-Drive のツールの返り値を加工したい場合に備え、`DDrive.Editor.Mcp` の各ツールは**薄いアダプタ**で、呼び先のサービス（`DDrive.Editor` の public static）を持ち込み先が直接呼べる。外部拡張の契約（[42](42_distribution.md) §5.14）に「`ddrive_*` ツールの名前・引数・返り値キー」を**追加**する。

---

## 5. トークンを減らす設計（G-2）

AI が消費するトークンは「ツール定義（毎ターン送られる）」「呼び出しの引数」「返り値」「返り値を読んで次を決めるための docs 参照」の 4 つ。順に手を打つ。

### 5.1 ツール定義を小さく

- **ツール数は 20 個以内**（§4 は 19 個）。似た操作は 1 ツール + `mode` / `target` 引数にまとめる（`ddrive_generate` の 7 種、`ddrive_validate` の `scope`）。
- **説明文は 1 行 80 文字以内**、引数の説明は 40 文字以内。詳しい説明は `ddrive_help topic=tool:<name>` に逃がす（必要なときだけ読む）。
- `AlwaysLoad=false`（既定）。isuzu のツール遅延ロードに乗る。
- `Examples` は 1 ツール 1 個まで（JSON 1 行）。

### 5.2 返り値を小さく

- **要約が既定、詳細は頼まれたときだけ**: `detail=summary` が既定（`ddrive_validate` は件数 + Code 別の表だけ。指摘の本文は `errors` / `all` で）。
- **既定の `fields` は主要欄だけ**（§4.5）。`*` は明示したときだけ。
- **ページング**: `limit`（既定 50、最大 200）+ `cursor`。`next` が無ければ終わり。
- **文字数の上限**: すべてのツールに `max_chars`（既定 4000、isuzu の `MaxResultSizeChars` と同じ値）。超えたら末尾を切って `truncated:true` と `next` を付ける（黙って切らない）。
- **JSON は圧縮形**（インデント無し・キーは短い英単語・`null` の欄は出さない・bool の既定値は省略）。メッセージ本文（Validation の `msg`）は**日本語のまま**（既存の文言を翻訳しない。Q-5）。
- **差分で返す**: `ddrive_asset_set` は変更した欄だけ `{field,from,to}`。全欄を返さない。
- **パスより ID**: 返り値の主キーは `{type,id}`。パスは `ddrive_asset_get` でだけ返す。
- **ログを返さない**: コンソールのログは返り値に含めない（必要なら isuzu の `console_read_logs` を AI が呼ぶ）。例外は `{error:{code,msg}}` の 1 行に畳む。

### 5.3 往復回数を減らす

- `ddrive_status` 1 回で「コンパイル・最後のテスト・検査の要約・未適用マイグレーション・Addressables 欠落・MCP の接続情報」が揃う（従来は `compile_status` + `read_console` + Validation メニュー実行 + ログ読み = 4 往復以上）。
- `ddrive_asset_create` は作成 + Addressables 登録 + その 1 件の検査結果を**まとめて返す**（作って → 検査して、の 2 往復を 1 回に）。`ddrive_asset_set` も同じ。
- `ddrive_preview_open` は「シーンを開く + 対象を配置」を 1 回で。

### 5.4 docs 参照を減らす（AI 向けカード）

- AI が作業前に読む `CLAUDE.md` §0（約 1,500 字）と各設計 doc の該当節（数千字）を、`ddrive_help` の**短いカード**（各 300〜600 字）に置き換える: `rules`（禁止事項 10 行）・`types`（AssetType / ID 接頭辞 / Data クラス名の表）・`validation:<code>`（Code の意味と直し方）・`tool:<name>`（引数と例）・`menu`（メニュー一覧）。
- カードの正本は `docs/68_ddrive_mcp/cards/*.md`（この文書の隣。P-9 の同梱物の同期で `Documentation~/Mcp/cards/` へ）。**docs と二重管理にしない**: カードは「docs のどの節の要約か」を先頭行に書き、docs 側を変えたらカードも同じ PR で直す（[12](12_review.md) §3 に 1 行足す）。
- `.claude/skills/ddrive-agent-workflow/SKILL.md` に「MCP が繋がっているときは `ddrive_help rules` と `ddrive_status` から始める。`execute_code` で D-Drive のサービスを直接呼ぶのは、ツールに無い操作だけ」を追記する。

### 5.5 計測

- `Tools/Mcp/measure-tokens.py`（新規）: 代表シナリオ 5 本（SE 作成→検査→試聴 / Validation の Error を 1 件直す / Data の値を 3 つ変える / マイグレーション確認 / リリース前チェック）を、**ツール定義 + 引数 + 返り値の文字数**で前（`execute_code` 方式の実測ログ）と後（新ツール）を比べる。文字数 ÷ 3.5 をトークンの近似とし、結果を本文書 §10 に表で残す。G-2 の 1/3 を満たさないツールは返り値を見直す。

---

## 6. ポート競合を起こさない設計（G-3）

### 6.1 競合しうる経路（全部潰す）

| 経路 | 現状 | v1.5.0 の対策 |
|---|---|---|
| (a) 固定ポートの別プロセスサーバー（CoplayDev の 8080 → 8081） | 他アプリと衝突した実例あり（2026-09-08） | **CoplayDev を外す**（Q-1）。`.mcp.json` の `UnityMCP`（8081）エントリも削除し、固定ポートをリポジトリの**どこにも書かない** |
| (b) 同じ PC で複数プロジェクト（D-Drive + MS2026）を同時に開く | isuzu はプロジェクトパスのハッシュで 27200〜27999 に分散（衝突確率は 2 プロジェクトで 1/800）。衝突しても空きポートを走査して別ポートに逃げ、`portMismatch=true` を記述子に書く | そのまま使う。D-Drive は**ポートを前提にしない**: 接続先は常に `instances/<hash>.json` の `mcpUrl` から読む（§6.2） |
| (c) Preferences でポートを固定（`httpPort` > 0） | 固定すると (a) と同じ問題が戻る | **運用で禁止**（[20](20_mcp_setup.md) に明記）。`ddrive_status.mcp.fixedPort=true` のとき Warning を返し、`ProjectSetupValidator` に Info `DD-MCP-FIXED-PORT` を足す |
| (d) Editor のクラッシュ後に古い記述子が残る（pid が死んでいる） | 新しい Editor は同じハッシュの記述子を上書きする。古い記述子を読んだクライアントは接続エラー | `Tools/Mcp/register-mcp.ps1`（§6.2）が記述子の `pid` の生存を確認してから登録する。死んでいれば「Unity を起動してから」と案内 |
| (e) Unity の再起動でトークンが変わり、既存セッションの登録が古くなる | 既存の運用メモに「毎回読み直す」とある | 同上のスクリプトで**登録を上書き**（`claude mcp remove` → `add`）。トークンはファイルに書かない（引数で渡す） |
| (f) 2 つの Editor が同じプロジェクトを開く（ワークツリー） | パスが違うのでハッシュも違う。`Temp/UnityLockfile` で同じパスは開けない | 対策不要（記述子が別になる）。ワークツリーでは Unity MCP を使わない運用のまま（SKILL.md §3） |
| (g) ポート範囲 27200〜27999 が全部埋まる | 800 個。現実には起きない | 起きたら isuzu がエラーログを出す。`ddrive_status` が `mcp.port=null` を返す |

### 6.2 クライアント登録の自動化（`Tools/Mcp/register-mcp.ps1`、新規）

```
pwsh Tools/Mcp/register-mcp.ps1            # 現在のプロジェクトの記述子を読んで claude mcp add(上書き)
pwsh Tools/Mcp/register-mcp.ps1 -Print     # mcpUrl と pid だけ表示(トークンは出さない)
```

- `Application.dataPath`（= `<リポジトリ>/Assets`）から isuzu と同じ規則でハッシュを計算し、`%LOCALAPPDATA%\UnityMCP\instances\<hash>.json` を読む。`pid` が生きていることを確認してから `claude mcp remove isuzu-unity; claude mcp add --transport http isuzu-unity <mcpUrl> --header "Authorization: Bearer <token>"` を実行する（登録先は `~/.claude.json` のプロジェクト配下 = 既存運用と同じ。**リポジトリにはトークンもポートも入らない**）。
- `.mcp.json` には isuzu のエントリを置かない（トークンが入るため。既存方針のまま）。CoplayDev のエントリは削除する（Q-1）。
- MS2026 側も同じスクリプトで登録できる（パッケージの `Tools~/Mcp/` に同梱、P-9 の同期対象に追加）。

### 6.3 D-Drive 側で確認できること

- `ddrive_status.mcp` = `{port, preferredPort, portMismatch, fixedPort, project, pid}`。AI は作業開始時にこれで「どのプロジェクトに繋がっているか」を確認する（同じ PC で D-Drive と MS2026 を開いているときの取り違え防止。`project` は `projectName`）。
- EditMode テスト `McpPortPolicyTests`: 「D-Drive のパスから計算したポートが 27200〜27999 に入る」「パスが違えばポートが違う（D-Drive と MS2026 のパスで検算）」「ハッシュの規則が isuzu と一致する（`McpPortPolicy.Resolve` の結果と比較）」。isuzu を上げたとき規則が変わればここで気づく。

---

## 7. 互換性・テスト・配布

- **版**: v1.5.0（MINOR、追加のみ）。`DDrive.Foundation` / `DDrive.Runtime` の公開 API は変えない。Editor の追加は契約外だが、**ツール名・引数名・返り値のキー・`ddrive_help` の topic 名は契約**（[42](42_distribution.md) §5.14 に E-21 として追加）。
- **スナップショット**: `Tests/Editor/Compat/Snapshots/mcp-tools.txt`（新規。ツール名 / 引数名と型 / 必須か / Destructive か / 返り値の上位キー）。`EditorContractSnapshotBuilder` と同じ方式で `[McpTool]` を反射で集める。行が減ったら赤（削除・改名は MAJOR）。
- **テスト**（`Tests/Editor/Mcp/`）: 各ツールの static メソッドを **MCP を通さず直接呼ぶ**（isuzu のトランスポートはテストしない）。一時 `TestRoot` と `ScriptableObject.CreateInstance` だけ使い、実 `Assets/GameData/` と Addressables グループを汚さない（SKILL.md §2）。共通ガード（Play Mode 中の拒否・読み取り専用欄の拒否・`max_chars` の切り詰め・`dry_run` で何も書かない）は 1 つのテストクラスにまとめる。`DDRIVE_UNITY_MCP` が無い環境ではテスト asmdef ごと外れる（同じ Version Defines）。
- **CI**: `run-ci.cmd` の EditMode 段に自動で載る（追加の段は作らない）。
- **配布**: `DDrive.Editor.Mcp` はパッケージに同梱するが、isuzu が無ければコンパイルされない。持ち込み先が使うときは `manifest.json` に isuzu を足す（`Tools > D-Drive > Update` の「他パッケージ」対応 P-15 で版管理できる。Q-3）。`package.json` の `dependencies` には**入れない**（必須依存にしない）。
- **docs**: [20](20_mcp_setup.md) を「isuzu 版のみ + register-mcp.ps1」に書き換え、[09](09_editor_tools.md) に §15「MCP ツール」を追加、[42](42_distribution.md) §5.14 に E-21、[34](34_onboarding.md) §7 と SKILL.md を更新、ProgrammerManual に 1 ページ（`mcp.html`）。[CLAUDE.md](../CLAUDE.md) §4 を isuzu 版のみに。

---

## 8. チケット分割（v1.5.0、[11](11_tasks.md) の「MCP チケット」節に転記）

| ID | 内容 | 人日 | 依存 |
|---|---|---|---|
| MCP-0 | 決め事 Q-1〜Q-12 の回答を本文書 §9 に反映。CoplayDev を外す（manifest・`.mcp.json`・CLAUDE.md §4・docs/20） | 0.5 | — |
| MCP-1 | `DDrive.Editor.Mcp` asmdef（Version Defines / defineConstraints）、共通ガード（`McpGuard`: Play Mode・読み取り専用欄・`max_chars`・`dry_run`・例外の畳み込み）、返り値の圧縮ヘルパー（`McpJson`） | 1 | MCP-0 |
| MCP-2 | `ddrive_status` / `ddrive_help`（カード 5 種の初版） | 1 | MCP-1 |
| MCP-3 | `ddrive_asset_list` / `get` / `create` / `set` + `FieldTables`（全 18 種別の主要欄と読み取り専用欄） | 2 | MCP-1 |
| MCP-4 | `ddrive_asset_usages` / `unused` / `delete` / `ddrive_editor_open` | 1 | MCP-3 |
| MCP-5 | `ddrive_validate` / `validate_fix` / `forbidden_api`（要約キャッシュ含む） | 1 | MCP-1 |
| MCP-6 | `ddrive_generate`（7 種）/ `ddrive_migrate` / `ddrive_compat` / `ddrive_release_check` | 1.5 | MCP-1 |
| MCP-7 | `ddrive_preview_open` / `preview_play` / `preview_sweep` / `ddrive_build_netcheck`（ジョブ化） | 1.5 | MCP-3 |
| MCP-8 | `Tools/Mcp/register-mcp.ps1` + `McpPortPolicyTests` + `DD-MCP-FIXED-PORT` | 0.5 | MCP-1 |
| MCP-9 | スナップショット `mcp-tools.txt` + Compat テスト + §5.14 E-21 | 0.5 | MCP-2〜7 |
| MCP-10 | トークン計測（`measure-tokens.py`、代表 5 シナリオ、§10 に結果） | 0.5 | MCP-2〜7 |
| MCP-11 | docs / SKILL.md / ProgrammerManual / CHANGELOG、人による確認手順（`verification/69_manual_verification_mcp.md`） | 1 | MCP-9 |
| MCP-12 | 自前レビュー → 修正 → v1.5.0 リリース（[12](12_review.md) §7） | 1 | MCP-11 |

合計 約 12 人日。実装は Sonnet のエージェントに 1 チケットずつ委任し、レビューとリリース判断は上位モデルが行う（2026-10-07 の運用指示）。

---

## 9. 決めてほしいこと（Q-1〜Q-12）

各行の「案」で進めてよければ返事は不要。変えたい行だけ指示をもらう。

| # | 問い | 案（推奨） | 理由・影響 |
|---|---|---|---|
| Q-1 | **CoplayDev 版を外すか**（v1.5.0 で isuzu 版に一本化） | **外す** | 固定ポートの競合源（§6.1 (a)）。isuzu 版で 3 セッション以上問題なく運用済み（docs/20 §4 の基準を満たしている）。MS2026 も同時に外す |
| Q-2 | **isuzu のタグを v4.2.0 のまま固定するか、最新に上げるか** | **v4.2.0 のまま**で実装し、MCP-12 の直前に最新を 1 回だけ評価 | `[McpTool]` の属性名・`ToolCatalog` の発見規則が変わると全ツールに影響する。上げるなら `McpPortPolicyTests` と `mcp-tools.txt` で差分を検出できる |
| Q-3 | **持ち込み先（MS2026）にも配るか** | **配る**（同梱・既定は無効 = isuzu が無ければ何も増えない） | MS2026 の AI 作業でも同じツールが使える。isuzu を入れるかは MS2026 の判断 |
| Q-4 | **書き込みツール（W / D）を既定で有効にするか** | **有効**。ただし `DDriveProjectSettings.McpAllowWrite`（既定 true）で**プロジェクト単位に無効化**できる | デザイナーの作業ファイルを AI が触るのを止めたいプロジェクト向け。無効時は W / D が `{error:{code:"write_disabled"}}` を返す |
| Q-5 | **返り値のメッセージの言語** | **キーは英語、本文（Validation の `msg` 等）は既存の日本語のまま** | 翻訳の二重管理を避ける。英語本文の方がトークンは少し減るが、docs・マニュアルと食い違う方が損 |
| Q-6 | **`ddrive_asset_set` が書ける欄の範囲** | **シリアライズされた全欄**（読み取り専用欄を除く）。`Flags.Load` 等の「変えると Preload / 同期 API に影響する欄」は変更後の検査結果を必ず返す | 欄ごとのホワイトリストは保守が重い。検査で守る |
| Q-7 | **AI が変えた Data に印を付けるか**（`ChangeNote` に `[mcp]` を前置、`Author` を `mcp:<client>` に） | **`ChangeNote` の先頭に `[mcp] ` を付ける**（`Author` は触らない） | 誰が変えたかを AssetBrowser と SpecWeb で追える。`VersionStampProcessor` の `Version` / `UpdatedAt` は従来どおり自動 |
| Q-8 | **Play Mode 中の読み取り（R）を許すか** | **許す**（W / D は拒否） | 実機確認中に状態を読めると便利。書き込みは変更が破棄されるので拒否（[20](20_mcp_setup.md) §2 の規約） |
| Q-9 | **`ddrive_release_check` / `ddrive_build_netcheck` のような「Editor の外の道具」をツールにするか** | **する**（`pwsh` 起動の薄いラッパー）。`git tag` / `push` を伴う `bump-version.ps1` は**ツールにしない** | リリースの判断と push は人の操作のまま（[12](12_review.md) §7 の原則） |
| Q-10 | **`ddrive_help` のカードの正本の置き場** | `docs/68_ddrive_mcp/cards/*.md`（docs と同じ PR で更新） | §5.4。二重管理を避けるため docs 側の節を要約した形にし、要約元を先頭行に書く |
| Q-11 | **SpecWeb（GAS）との連携ツール**（`ddrive_spec_sync`）を v1.5.0 に入れるか | **入れない**（v1.5.x で検討） | 書き込みトークンを AI に渡す設計が要る。[32](32_spec_web.md) §7 のセキュリティ節と一緒に決める |
| Q-12 | **ツール名の接頭辞と最大数** | `ddrive_`、**20 個以内**（増やすときは既存ツールの `mode` に足すのを先に検討） | §5.1。ツール定義は毎ターン送られるので、数が増えるほど常時コストになる |

---

## 10. 計測結果（MCP-10 で記入）

| シナリオ | 前（文字数 / 概算トークン） | 後 | 比 |
|---|---|---|---|
| SE を 1 件作成 → 検査 → 試聴 | — | — | — |
| Validation の Error を 1 件直す | — | — | — |
| Data の値を 3 つ変える | — | — | — |
| マイグレーションの確認 | — | — | — |
| リリース前チェック | — | — | — |

---

## 更新履歴

- 2026-10-07: 起票（v1.5.0 の仕様。ユーザー指示「D-Drive MCP: D-Drive 周りを全面サポート、トークン最小、ポート競合ゼロ」を受けて作成。Editor 機能の棚卸しと isuzu 版の拡張 API・ポート規則の調査結果に基づく）
