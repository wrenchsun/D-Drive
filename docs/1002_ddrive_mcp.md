# 1002. D-Drive MCP（v1.5.0 仕様書）

> **状態**: 仕様書（2026-10-07 起票、未実装）。v1.5.0（MINOR、追加のみ）の実装内容。チケットは [11_tasks.md](11_tasks.md) の「MCP チケット」節、未完了事項の索引は [1001_open_items.md](1001_open_items.md)。
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
| 運用上の優先 | 両方繋がっているときは isuzu 版を優先（`test_run`/`compile_status`/`execute_code`/`menu_execute`）。「3 セッション問題なければ CoplayDev を外す」の判断が未了 | [CLAUDE.md](../CLAUDE.md) §4、[20](20_mcp_setup.md)「切り替えの判断基準」、[1001](1001_open_items.md) §5 |
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

### 実装メモ（2026-10-07、MCP-1）

- **asmdef**: `DDrive.Editor.Mcp`（`Packages/com.ddrive.core/Editor/Mcp/`、Editor 専用）。参照は `DDrive.Foundation` / `DDrive.Runtime` / `DDrive.Editor` / `UnityMCP.Editor`（Newtonsoft は `DDrive.Editor` と同じく自動参照）。Version Defines `jp.shiranui-isuzu.unity-mcp >= 4.2.0` → `DDRIVE_UNITY_MCP`、`defineConstraints: ["DDRIVE_UNITY_MCP"]`。テスト asmdef は `DDrive.Tests.Editor.Mcp`（`Tests/Editor/Mcp/`、同じ Version Defines）。
- **`McpGuard` / `McpToolError`**（`Editor/Mcp/McpGuard.cs`）: ガードは `McpToolError(code, msg)` を投げ、`McpGuard.Run(() => JObject)` が `{"error":{"code":..,"msg":..}}`（msg は 200 文字まで、スタックトレース無し）に畳む。コード一覧: `play_mode`（Play Mode 中の書き込み。読み取りは可）/ `write_disabled`（`McpAllowWrite == false`）/ `read_only_field`（`Id` `SchemaVersion` `ImportSourceGuid` `Version` `UpdatedAt` `Icon`。`AssetDataBase` の実フィールド名で、`Icon.Array...` のようにパスの先頭で判定）/ `invalid_params` / `exception`（上記以外の例外。msg は `<型名>: <本文>`）。`truncated` はエラーではなく、`McpGuard.Truncate(json, maxChars=4000)` が返す `(text, truncated)` を見てツールが返り値に付ける印。
- **`McpJson`**（`Editor/Mcp/McpJson.cs`）: `Obj`（null・false・空配列を省く。出すときは `McpJson.Keep(value)`）/ `Page`（引数順は `items, cursor, limit, map, maxLimit=200`。`cursor` = 整数オフセットの文字列、`next` は続きがあるときだけ。`limit` 既定 50）/ `Compact`（インデント無し）。
- **設定の置き場所**: `DDriveProjectSettings.McpAllowWrite`（既定 false、Q-4 (c)）。開発リポジトリでは `DevRepoSettingsSync` が ON にする。UI は Project Settings > D-Drive > MCP（`McpSettingsProvider`）とセットアップウィザード「4. 既定フォルダ・設定の生成」のチェックボックス。
- `ddrive_status` は version / compile / mcp の 3 セクションのみ（残りは MCP-2、下の「実装メモ（MCP-2）」）。

### 実装メモ（2026-10-07、MCP-5）

- **ファイル**: `Editor/Mcp/Tools/DDriveValidationTools.cs`（ツール 3 個 + 純粋関数 `ValidationScope` / `ValidationSummary` / `ItemPaging` / `ForbiddenApiSummary`）。純粋関数は、リポジトリの方針（InternalsVisibleTo を置かない）に合わせて `internal` ではなく `public`（Editor 専用 asmdef `DDrive.Editor.Mcp` 内で、`DDrive.Foundation` / `DDrive.Runtime` の公開 API ではない）。
- **`scope` → 呼ぶ先**: `all` = `CI.RunValidation(true)` 全件（`McpValidationCache.Record` を更新する唯一の scope）。`project` = 同じ結果から `Asset == null` だけ（全体の Validator だけを走らせる安い経路は無い）。`type:<T>` = 同じ結果を Asset の種別（`[AssetIdDefinition]`）で絞る。`DataValidationRunner.Run` を種別内の全アセットに回す案は、重複 ID・参照先など「全アセットの文脈」を要る検査が Run All とずれるため採らなかった。`asset:<T>:<id>` = `DataValidationRunner.Run(asset, false)`（1 アセットだけで安い。`<id>` は 10 進または `0x` 16 進）。不正な scope / 種別 / id / detail / cursor は `invalid_params`。
- **返り値**: `{scope, errors, warnings, infos, byCode:[{code,sev,count}], fixable?:[{code,count}], items?, next?, truncated?}`。`byCode` は Error → Warning → Info、同じ重大度は件数の多い順。Code 無しは `(none)`。`codes` は件数・byCode・items のすべてに掛かる。`fixable` = 全体の指摘（Asset 無し）で `FixAction` を持つ Error / Warning の Code 別件数（`ddrive_validate_fix` の対象）。items の `type` / `id` / `name` は Data の指摘だけ（全体の指摘では省く）、`msg` は 200 文字で切る。
- **収め込み**: `ItemPaging.Fit` が `limit`（既定 50・最大 200）で切ったあと、`max_chars`（既定 4000）に収まるまで末尾の項目を丸ごと落とし、`next` を「落とした先頭の位置」にして `truncated:true` を付ける（`McpGuard.Truncate` の文字切りは JSON が壊れるので使わない）。`summary` で `byCode` だけが `max_chars` を超える極端なときは件数 + `truncated:true` に縮める。
- **`confirm` / `dry_run` の渡り方**: isuzu の `ToolInvoker` が `Destructive = true` のツールにだけスキーマへ注入し、**メソッドの引数としては渡さない**。`confirm` 無しは `confirmation_required`、`dry_run=true` はメソッドを呼ばずに `{dry_run, tool, would_execute, arguments}` の定型だけ返す。そのため `ddrive_validate_fix` は `wouldApply`（Code 別の件数）を返す読み取り専用の経路を自前の引数 `preview` で持つ（これも `Destructive` の関門を通るので `confirm=true` は要る）。事前の確認だけなら `ddrive_validate` の `fixable` で足りる。
- **`ddrive_validate_fix`**: `McpGuard.EnsureCanWrite()` → `CI.RunValidation(true)` → `ProjectWideValidationFixes.FindFixable` → `codes` で絞る → `OrderForApply` の順に **1 件ずつ** `Apply`（Code 別に成否を数えるため）→ `{applied:[{code,count}], skipped:[{code,count,reason}], after:{errors,warnings}}`。`skipped` の reason は `no_fixable`（`codes` に挙げたが直せる指摘が無い）/ `fix_failed`（`FixAction` が例外・失敗）。直した後は全体を再実行して `McpValidationCache` を更新する。`UndoGroup = "D-Drive MCP: 検査の修正"` は付けたが、修正はファイル・Addressables・名前の書き換えを含み Undo では戻らない（`ProjectWideValidationFixes` の既存の注意と同じ）。Data 単位の指摘の `FixAction` は対象外（既存メニューと同じ範囲）。
- **`ddrive_forbidden_api`**: `ForbiddenApiScanner.ScanDetailed(root, DDriveProjectSettings.instance.ForbiddenApiAllowEntries)`（`CI.ValidateAll` と同じ許可の反映）。`root` 省略は `CI.ResolveForbiddenApiScanRoot()`。`{root, violations, notices, byRule:[{rule,count}], items?:[{rule,file,line,msg?,notice?}]}`。規則名の無い全体エラー（走査ルートが無い等）は rule `(scan)`。file はプロジェクト相対。
- **`ddrive_validate` `all` の所要時間**: `all` = HTTP 往復で 1.3〜2.2 秒（初回 2.2 秒、2 回目以降 約 1.3 秒。`type:` と `project` も同じ全体実行なので約 1.3 秒、`asset:` は 1 アセットだけ）。`ddrive_forbidden_api` は 2.6〜3.0 秒（`.cs` の全走査）。このプロジェクトの `all` は Error 0 / Warning 22 / Info 35 で、既存 Validator の多くが Code を付けていないため `byCode` は `(none)` の 2 行になる（Code の付与は Validator 側の追加作業）

### 実装メモ（2026-10-07、MCP-2）

- **`ddrive_status` の各セクションが読むもの**（どれも「安く・書かずに」読む。全部入りで HTTP 往復 0.2〜0.9 秒）:
  - `version` = `DDriveVersion.Value` + `schema`（`DDriveSchema.Current`）
  - `compile` = `{ok}`（`EditorUtility.scriptCompilationFailed`）。`errors`（件数）は**省略**: コンソールの件数は UnityEditor 内部 API（`LogEntries`）でしか取れず、AI は `compile_status` の `errorCount` を見れば足りるため
  - `tests` = `{last:{mode,passed,failed,inconclusive,at}}`。isuzu の `TestRunnerTools`（internal）が `SessionState["UnityMCP.LastTestRun"]` に保存する JSON のうち `status=completed` のものを読む。無ければ `TestResults/{editmode,playmode}-results.xml` の新しい方のルート要素（`XmlReader` で先頭だけ）。どちらも無ければ省略。**既知の癖**: ドメインリロード（PlayMode 実行後など）を挟むと isuzu 側が `mode` を復元しないため、`mode` が欠けることがある
  - `validation` = `McpValidationCache`（`Editor/Mcp/McpValidationCache.cs`、SessionState に `{errors,warnings,infos,at}`）。**`ddrive_status` は Validator を走らせない**。`CI.RunValidation` の結果を `McpValidationCache.Record(reports)` で書くのは `ddrive_validate`（MCP-5）の役目。未実行なら `{cached:false}`
  - `migration` = `{pending:n}`（`DDriveMigrationRunner.PlanProject().TotalCount`）。このプロジェクトでは HTTP 込みで 0.25 秒（本体は十分速い）なので、`HasPendingMigrations()` の bool への縮退は不要だった
  - `addressables` = `{missing:n}`。`AddressablesSync.CountMissingEntries()`（新規・読み取り専用。`SyncAll` が直す `fixedAssets` と同じ判定: カタログに ID があるのに Addressables 未登録 / address 不一致の Data 数）。Addressables 設定が無ければセクションごと省略
  - `mcp` = `{writeEnabled, playing, project, port, preferredPort, portMismatch, fixedPort, pid}`。isuzu の記述子 `%LOCALAPPDATA%/UnityMCP/instances/<hash>.json`（ハッシュ = `Application.dataPath` の UTF-8 の SHA256 先頭 8 バイトの小文字 16 進。`register-mcp.ps1` と同じ）を読み、**トークンは読み捨てる**。`fixedPort` = isuzu `McpSettings.instance.httpPort > 0`（公開 API）
  - 未知の `sections` は**無視**（将来の追加に古いクライアントが耐える。MCP-1 では `invalid_params` だったのを変更）
- **`ddrive_help`**（`Editor/Mcp/Tools/DDriveHelpTools.cs`）: `rules`（カード）/ `types`（`[AssetIdDefinition]` を反射で集めた「種別 / Data クラス / ID 定数 / ファイル接頭辞」+ `McpGuard.ReadOnlyFields`）/ `menu`（`TypeCache` で `[MenuItem]` を集め `Tools/D-Drive/` 以降のパスだけ。ショートカット指定は落とす）/ `tool:<name>`（`[McpTool]`/`[McpArg]` から生成。必須判定は `Required` または既定値なし）/ `validation:<code>`（カードの `## <CODE>` 節。大小文字無視。`validation` だけなら code 一覧）。未知の topic・tool・code は `invalid_params`（topic 一覧つき）。返り値 `{topic,text,truncated?}`、`max_chars` 既定 4000
- **カードの正本は `Packages/com.ddrive.core/Editor/Mcp/Cards/{rules,validation}.md`**（Q-10 の変更。§5.4 参照）。`PackageInfo.FindForAssembly(...).resolvedPath` から引くので、埋め込み・git URL（`Library/PackageCache`）のどちらでも読める。`rules.md` は 600 文字以内（テストが固定）。`validation.md` は実在する `DD-*` コード 43 件（Addressables・Setup・Schema・Cutscene・Canvas・Anim・Shake・Haptics・Material・ForbiddenApi）を各 1〜2 行で。**意味と直し方は各 Validator のメッセージ文から取った**（新しい Code を足したらカードにも足す）
- **Validator の Code の元データ**: 指示では `Tests/Editor/Compat/Snapshots/validator-severity.txt` を「よく出るコード 25 件」の元にする想定だったが、そのスナップショットは `DD-ADDR-CATALOG-MISSING=Error` の 1 行だけ（Code 付き Validator は少数。[42](42_distribution.md) §5.11-8）。そこで `Editor` / `Runtime` のソースにある `code: "DD-…"` の全件を拾った（ContentHash / Spec 系の検査は Code 未設定のためカード対象外）
- `McpJson.Parse(string)` を追加: `JObject.Parse` は日付形式の文字列を `DateTime` に変換してしまい `at` が壊れるため、`DateParseHandling.None` で読む

### 実装メモ（2026-10-07、MCP-3）

- **ファイル**: `Editor/Mcp/FieldTables.cs`（種別 → Data クラス・既定の欄）/ `Editor/Mcp/SerializedFieldIo.cs`（SerializedProperty ⇄ JSON）/ `Editor/Mcp/Tools/DDriveAssetTools.cs`（ツール 4 個）。`McpJson` に `FormatId` / `TryParseId`（10 進・`0x` 16 進・JSON 整数）を追加。`AddressablesSync.IsRegistered(Object)`（bool）を追加（`DDrive.Editor.Mcp` asmdef は Addressables を参照しないため、`AddressableAssetEntry` を返す `FindEntry` を直接呼べない。asmdef は変えていない）。
- **ID は 10 進文字列**（決定）: 出力 `"id":"1234567890123"`、入力は 10 進 / `0x` 16 進の文字列か JSON の整数（整数は 2^53 超で送り側が桁落ちするので推奨しない）。MCP-5 の `ddrive_validate` の `items[].id` を文字列に変更した（`MapRow`。MCP-5 は未リリースなので契約の変更ではない）。`AssetId` 型の欄の `{type,id}` も `id` は文字列。
- **`FieldTables`**: 全 18 種別を持ち、既定の欄 = 共通 4 欄（`DisplayName,Category,Tags,Description`）+ 種別の主要欄（6 個まで、合計 10 以内）。`FieldTablesTests` が、全 `AssetType` が載っていること・Runtime の `[AssetIdDefinition]` 付き具象クラスが全部載っていること・既定の欄名が実在すること（`SerializedObject.FindProperty`）を固定する（新しい種別・Data クラスを足すと赤くなる）。

  | 種別 | Data クラス | 主要欄（共通 4 欄に足す） |
  |---|---|---|
  | Se | SeData | Clips, Volume, PitchRange, Loop, Spatial, MaxConcurrent |
  | Bgm | BgmData | Intro, LoopBody, Volume, FadeIn, FadeOut, Bpm |
  | Vfx | VfxData | Prefab, AnchorId, LifeMode, Duration, FadeOutSec, Render |
  | Anim | AnimData | Clip, StateName, Layer, Loop, DefaultCrossFade, Mask |
  | Anim2D | Anim2DData | Clip, StateName, Loop, DefaultCrossFade, Directions, DirectionClips |
  | Material | MaterialData | Shader, Common, RenderQueueOffset, RenderingLayerMask, EnabledKeywords |
  | Texture | TextureData | Texture, Usage, Sprite, AllowScale, SliceBorder, Channel |
  | Canvas | CanvasData | Prefab, Layer, SortOffset, CloseOnBack, ModalBlocksInput, PauseGameWhileOpen |
  | Prefab | PrefabData | Prefab, Kind, GameplayTags, CollisionLayer, Lod |
  | Presentation | PresentationData | TotalDuration, Interruptible, PredictLocal, Tracks |
  | Shake | CameraShakeData | Pattern, PosAmplitude, RotAmplitude, Frequency, Envelope, Space |
  | Haptics | HapticsData | LowFreq, HighFreq, Priority, LocalPlayerOnly |
  | UiTween | UiTweenData | TotalDuration, Tracks |
  | Model | ModelData | Prefab, Slots, DefaultAnimation, Avatar, RenderLayer, Lod |
  | Anchor | AnchorData | Parent, Space, Path, LocalOffset, LocalEuler, LocalScale |
  | AnchorGroup | AnchorGroupData | OriginAnchorId, Layout, GridCountX, GridCountY, CircleCount, CircleRadius |
  | ControlSkin | ButtonSkinData / SliderSkinData | Button: HoverSe, ClickSe, LongPressSe, DeniedSe / Slider: GrabSe, ReleaseSe, NotchSe, LimitSe, DeniedSe |
  | Cutscene | CutsceneData | Timeline, Origin, FrameRate, Skip, Wrap, LockInput |

  ControlSkin だけ Data クラスが 2 つあるため、`ddrive_asset_create` は `data_class`（`ButtonSkinData` / `SliderSkinData`）が必須（無ければ `invalid_params`）。`get` の既定の欄は、そのアセットの実クラスの表を使う。Anim2D は `AnimData` の派生なので、`list` / `get` は `t:AnimData` が Anim2D を拾っても `[AssetIdDefinition]` の種別で絞り直す。
- **`SerializedFieldIo` が対応する型**（Read の出力 = Write の入力）: int 系（int/uint/long/short/byte…。範囲検査あり）・**ulong は 10 進文字列**・float/double（float は `0.1f → 0.1` の最短表記、整数値は `1` と出す）・bool・string・char・LayerMask・enum（名前は大小無視、数値も可）・Color（`{r,g,b,a}`、Write は `"#RRGGBB[AA]"` も可）・Vector2/3/4・Quaternion・Vector2Int/3Int（`{x,y,..}`、Write は配列も可、無い成分は既存値のまま）・Rect（`{x,y,w,h}`）・Bounds・AnimationCurve（`{keys:[{t,v,in,out}]}`）・Object 参照（アセットパス文字列 / サブアセットは `path#名前` / null。Write は型検査あり。`PPtr<$型>` から必要な型を読む）・`AssetId<T>`（`{type,id}`、未設定は null。Write は `id` だけでも既存の type を保って入る）・`ValueDef`（最小 JSON: `{mode:"Constant",value}` / `{mode:"Parametric",ease|bezier,from,to,time:{mode,value,speed?,ignoreTimeScale?},loop?,loopCount?}` / `{mode:"Curve",curve,from,to|normalized,time,...}`。`mode` を省くと渡したキーから決まる。Write は渡したキーだけ上書き）・その他の struct/class（入れ子オブジェクト。**Write は渡したキーだけを上書きするパッチ**）・配列（JArray。**Write は全体置換のみ**で、`Tags[0]` / `Tags.Array.data[0]` は `invalid_params`。増やした要素は直前の要素の複製から始まるので、struct の配列は全キーを渡すのが安全）・ManagedReference（Read は `$type` 付き、Write は既存インスタンスのパッチと null のみ）。対応外（Gradient 等）は Read が `"<unsupported:型>"`、Write が `invalid_params`。エラー文は欄のパスと期待する形を含む。
- **`ddrive_asset_set` の流れ**: `EnsureCanWrite` → 欄名の検査（読み取り専用 = `read_only_field`、存在しない・要素単位 = `invalid_params`）→ `SerializedObject` に全欄を書く（ここで型が合わなければ例外。まだ `ApplyModifiedProperties` していないので何も変わらない = **全欄が書けるときだけ書く**）→ 変わった欄だけ `changed` に（値が同じなら `changed:[]` で何も保存せず、`Version` も進めない）→ `ChangeNote` に `[mcp] ` を前置（既に付いていれば何もしない。`fields` に `ChangeNote` があれば新しい値の先頭に付ける。**空のときは `[mcp] 変更: <欄名,...>`** を入れる）→ `Undo.RecordObject` + `ApplyModifiedProperties` + `SetDirty` + `DDriveAssetSave.SaveDirty`（`VersionStampProcessor` が `Version` / `UpdatedAt` を付ける）。`from` / `to` は 200 文字で切る。`preview` は同じ検査と差分計算までで何も書かない（`Destructive=false` なので isuzu の `dry_run` ではなく自前の引数。MCP-5 と同じ理由）。
- **`ddrive_asset_create` の流れ**: 引数検査（`identifier` 省略は `AssetNamingService.ToIdentifier(name, 種別名)`、`IsValidIdentifier` でなければ `invalid_params`）→ 予定パスが既にあれば `invalid_params`（`AssetPathToGUID` は `OnlyExistingAssets` を付ける。既定だと直前に削除したパスを「ある」と返す）→ `fields` を使い捨てのインスタンスで先に書いてみる（書けなければ何も作らない）→ `preview` なら `{wouldCreate, identifier}` → `AssetCreationService.Create(configure: fields を適用 + ChangeNote = "[mcp] 作成")`。**Addressables 登録は `Create` が済ませる**（カタログ登録 + `AddressablesSync.EnsureEntry` + カタログのエントリ + 保存）ので、ツール側では二重に呼ばない（`addressable` は `AddressablesSync.IsRegistered` で確かめた結果）。`StampNew` により `Version` は 1 のまま（作成後に `SaveDirty` は呼ばない = 版が 2 に進まない）。`CreateIn(gameDataRoot, ...)` は MCP に出さないテスト用の入口。
- **`ddrive_asset_list`**: `fields` は `id` / `name` / `category` / `path` と欄名（`*` で全欄）。`name` は `DisplayName`、空ならアセット名。`query` は `DisplayName` とアセット名（= 識別子を含むファイル名）の部分一致。`category` は完全一致か配下（`Pg` で `Pg/One` も）。パス順で安定、`{total, items, next?, truncated?}`。ページ切り・`max_chars` は MCP-5 の `ItemPaging.Fit`。
- **テスト**: `FieldTablesTests` / `SerializedFieldIoTests`（全型の往復。`McpIoProbe` = テスト専用の ScriptableObject）/ `DDriveAssetToolTests`（一時 GameData ルート `Assets/Tests/DDriveTemp/McpAssetToolsGameData` で create → set → get → list の実往復。`AssetCreationService.Create` は実 Addressables グループへ一時ルートのアセットを登録するので、TearDown で `AddressablesSync.RemoveEntriesUnder` + `DeleteAsset` + 保存して持ち越さない。AssetCreationServiceTests と同じ後始末）。
- **既知の制限**: `Gradient` と、新しい型の `ManagedReference` の生成は対象外。`ddrive_asset_set` は 1 アセット単位で、複数アセットの一括変更・複製・改名は無い。

### 実装メモ（2026-10-07、MCP-4）

- **ファイル**: `Editor/Mcp/Tools/DDriveDependencyTools.cs`（ツール 4 個 + 純粋関数 `UsagesJson` / `UnusedJson` / `ResultJson` / `PreviewJson` / `KindOf` / `ToProjectRelative`）。type / id の解決は MCP-3 の `DDriveAssetTools.Locate` を使う（新規ロジックなし）。`AddressablesSync.IsGuidRegistered(string)` を追加（テスト用。削除後に控えた GUID で Addressables の残りを確かめる。`DDrive.Editor.Mcp` の asmdef は Addressables を参照しないので bool で返す）。
- **グラフ未構築**: `DependencyGraphService.CachedFileCount == 0`（UsagesWindow / UnusedAssetsWindow と同じ判定）なら `usages` / `unused` / `delete`（`preview` も）が `{needsRebuild:true, hint:"ddrive_generate target=deps"}` を返す（例外にしない）。`ddrive_generate` は MCP-6 で実装予定（それまでは Tools > D-Drive > Generate > 依存関係グラフを再構築）。テストは `DDriveDependencyTools.GraphBuiltOverride`（テスト専用の差し替え）で未構築を作り、実グラフは再構築しない。
- **`ddrive_asset_usages`**: `{count, usages:[{path, objectPath?, kind?}], next?, truncated?}`（`ItemPaging.Fit` の `items` を `usages` に改名）。`kind` = 参照元のファイル種別（`ClassifiedReference.ClassifyPath`）: `scene` / `prefab` / `playable`（FC-7 の Timeline）、`.asset`（Data）は参照元の Data 型名（`DependencyReference.ComponentType`。例 `PresentationData`）。判定できないときは省略。
- **`ddrive_asset_unused`**: `{count, items:[{type,id,name,archived?}], next?}`。`UnusedAssetsWindow` は Archived を**除外せず**一覧し Archived 列を出すので、それに合わせて除外せず `archived:true` の印だけ付ける（`ArchiveTagService.IsArchived`）。並びはパス順（窓と同じ）。`type` で絞る。
- **`ddrive_asset_delete`**（`Destructive`。`confirm` は isuzu が注入しメソッドには渡らない = `ToolInvoker` で確認済み。`confirm` なしは `confirmation_required`。`preview` だけ自前の引数）: `EnsureCanWrite` → Locate → グラフ検査 → **読み取り専用の分析**（blockers = `FindUsages`、codeRefs = `CodeReferenceScan.FindPossibleReferenceHits`。`TryDelete` が削除前に見るものと同じ。`TryDelete` は確認の前に Archived タグを付けるので、分析は先に自前で行い、拒否・preview では何も書かない）。`preview` → `{path, wouldDelete, blocked?, blockers?, blockerCount?, codeRefs?}`。実行 → blockers か codeRefs があれば `{deleted:false, path, blockers, codeRefs}`（削除しない）、無ければ `SafeDeleteService.TryDelete(requireGraphBuilt:true, scanCodeReferences:false)`（コード参照は分析済み）を、`ConfirmDialogOverride`（常に続行）/ `InfoDialogOverride`（握りつぶす）を差してから呼び、`finally` で元の差し替えに戻す → `{deleted:true, path}`。Addressables のエントリ・カタログ登録は `TryDelete` の `PerformDelete` が外す（テストで確認）。OS のゴミ箱へ移動なのでファイルは復元できるが、カタログ / Addressables 登録は自動では戻らない。`blockers` は `{kind, path, objectPath?}`、`codeRefs` は `{file, line}`（プロジェクト相対。`CodeReferenceScan.Hit` がパッケージ内を絶対パスで返すので揃える）、どちらも 20 件まで（超えた分は `blockerCount`）。**コード参照がある Data はコードを直すまで消せない**（`scan_code=false` で検査を外せる）。コード参照の検出は定数名・ファイル名の文字列一致なので、コメントや生成器の文字列にも当たる（偽陽性あり。既存の削除ダイアログと同じ挙動）。`UndoGroup` は付けたが、ゴミ箱移動は Undo では戻らない。
- **`ddrive_editor_open`**: `DataEditorRegistry.TryGetPrimary` → `Entry.Open`（`OpenDefault` と同じ。ウィンドウ名を返すため分けて呼ぶ）→ `{opened:"AudioEditorWindow"}`。専用エディタが無ければ `Selection.activeObject` + `PingObject` → `{opened:null, inspector:true}`。モーダルは出さない。Play Mode でも書き込みではないので拒否しない。
- **テスト**: `DDriveDependencyToolTests`（16 件。整形は合成データ、削除は一時 GameData ルートに作った Se で preview / 実削除〔ファイル・Addressables の消去〕/ `write_disabled` / 差し替えの復元、`ddrive_editor_open` は Audio Editor を開いて閉じる。バッチモードでは Ignore）。
- **所要時間**（HTTP 往復）: usages 0.3 秒、unused 0.7 秒、delete `preview`（コード参照走査込み）0.25 秒。
- **既知の制限**: 参照されている Data は `delete` で消せない（差し替え・強制削除は Asset Browser の削除画面〔`AssetDeleteWindow`〕の仕事で、MCP には出さない）。複数アセットの一括削除は無い。

### 実装メモ（2026-10-07、MCP-6）

- **ファイル**: `Editor/Mcp/Tools/DDriveGenerateTools.cs` / `DDriveMigrateTools.cs` / `DDriveCompatTools.cs` / `DDriveReleaseTools.cs`（ツール 5 個。`ddrive_compat` は **`ddrive_compat`（Safe の差分）と `ddrive_compat_update`（Destructive）に分割**した。`Destructive` はツール単位で、1 ツールにすると差分の読み取りにも `confirm` が要るため。§4.3 の表の「R / D」は 2 ツールの意味）。
- **`ddrive_generate`**（`McpGuard.EnsureCanWrite` + `UndoGroup`、`Destructive` ではない）。**引数名は `kind`**（isuzu の `ToolCatalog` が `target` を予約語として拒否し、ツールごと登録されなかったため。§4.3 の表の `target` は `kind` の意味）。返り値は `{target, changed?, preview?, ok?, summary}`（返り値のキーは `target` のまま）。未知の `kind` は `invalid_params` で 7 種を列挙。`dry_run` ではなく `preview`（isuzu の `dry_run` は Destructive だけに注入される定型のため。MCP-3 以降と同じ）。kind ごとの呼び先と `preview` の意味:
  - `ids` → `AssetIdGenerator.Regenerate()`（既定出力 = `DDriveProjectSettings.GeneratedRoot/AssetIds.g.cs`）。`summary` = `{total, assigned, duplicates:[{id,paths}], path}`、重複があれば `ok:false`（ファイルは書かれない）。`changed` = 出力ファイルの前後の文字列が違う **か** 新しい ID を払い出した。`preview` は **何も実行せず** `{wouldWrite:path, exists}` だけ（`Regenerate` は ID の払い出し = Data の書き換えと保存を伴い、出力先を一時パスにしても副作用が消えないため）。
  - `tuning` → `TuningCodegen.Regenerate()`。`summary` = `{keys, tables, columns, path}`。`preview` は **一時ファイル**（`Path.GetTempPath()`、Assets の外なので import も走らない）へ書いて現ファイルと比較するので `changed` まで正確（`TuningCodegen` は Data を書き換えない）。0 件で既存に定数があるときの安全弁は `ok:false`。
  - `addressables` → `AddressablesSync.SyncAll(log:false)`。`summary` = `{fixedAssets, catalogs, missingCatalog}`、`changed` = `fixedAssets > 0`。`preview` は `CountMissingEntries()` → `{missing}`（`changed` = `missing > 0`）。設定が無ければ `ok:false, summary:{available:false}`。
  - `preload` → `scene=current`（既定。開いているシーン。未保存なら `invalid_params`）は `ScenePreloadGenerator.GenerateForScene`、`scene=all` は `GenerateForAllBuildScenes`。`summary` = `{scenes, entries, lists:[path]}`、`changed` = 全 `ScenePreloadList` ファイルのハッシュ表の前後比較。**依存グラフが未構築なら空のリストで上書きしないよう書かずに** `{needsRebuild:true, hint}`（`ddrive_generate kind=deps` を案内）。`preview` は `{scenes, wouldUpdate:[scene path]}`。
  - `prefabs` → `DefaultPrefabs.EnsureSeEmitterPrefab()`（`GenerateAll` と同じ処理で Ping・ログだけ省く。既存は上書きしない = 冪等）。`summary` = `{created:[path]}`、`preview` は `{wouldCreate:[path]}`（既にあれば空）。
  - `deps` → `DependencyGraphService.RebuildAll()`。`summary` = `{files, edges, seconds}`、`changed` は常に true。`preview` は `{wouldRebuild:true, cachedFiles}` だけ。テストは `DDriveGenerateTools.RebuildOverride` で再構築を差し替える（実グラフは再構築しない）。
  - `icons` → `AssetIconService.CreateDefaultIconsForAll(onlyMissing:true)`。`summary` = `{created}`（開始した件数）。`preview` は対象の数え上げが内部処理なので `{wouldRun:true, onlyMissing:true}` だけ。
- **`ddrive_migrate`**（`Destructive` + `UndoGroup`。isuzu が `confirm` を要求するのは `plan` も同じ = 仕様どおり 1 ツール）。`mode` の検査 → `apply` のときだけ `EnsureCanWrite`（`plan` は書き込み許可が無くても動く = 読み取りだけ）。`plan` = `PlanProject()` を Id ごとの行に畳む `{pending:[{id, kind:"data"|"project", targets, description?}], count}`（`SchemaStampOnly` は仮の Id `schema-stamp` の 1 行）。`apply` = `ApplyToProject()` → `{applied:[{id,targets}], failed:[{id,msg}], after:{pending}, warnings?, log?}`。`DDriveMigrationRunner` は Id ごとの成否を返さないので、`failed` = 「適用前の計画にあり適用後の計画にも残った Id」、`applied` = それ以外。警告があれば `warnings` と先頭 20 行の `log`。
- **`ddrive_compat`**（Safe、`Idempotency=Safe`）: `CompatSnapshotMenu.UpdateAll` が書く 6 種（public-api Foundation / Runtime、serialized-layout、enums、net-messages、editor-contract）を、`Tests/Editor/Compat/*Tests.cs` と同じビルダーで現在の文字列にし、保存済みファイルと **行集合**（空行・CRLF は無視）で比べる。`{ok, changed:[{snapshot, added, removed, sample:[最大 5 行、removed(-) を先に、+/- 接頭辞], missingFile?}], warning?}`。`ok` = 全 `removed == 0`、removed があれば `warning`（MAJOR の疑い）。差が無ければ `changed:[]`。一時フィクスチャ依存の 2 種（tuning-codegen / validator-severity）はテスト側でしか作れないので対象外。
- **`ddrive_compat_update`**（`Destructive` + `EnsureCanWrite`）: 更新前に差分を取り、`CompatSnapshotMenu.UpdateAll()` を呼ぶ → `{updated:[書き換わったスナップショット名], hint, warning?}`。`hint` は CHANGELOG の `[Unreleased]` 互換性節への追記と、対象外の 2 種の更新方法（環境変数 `DDRIVE_UPDATE_COMPAT_SNAPSHOTS=1` でテスト再実行）。**MCP-6 では互換性スナップショットは更新していない**（`ddrive_compat` が差分なし）。
- **`ddrive_release_check`**（Safe）: 引数 `base?`（タグ名等。`^[A-Za-z0-9_][A-Za-z0-9._/-]*$` だけ許可 = 先頭 `-` や空白・`;` は `invalid_params`）・`guard_only?`。`pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/Release/check-release.ps1 -Json [-Base x] [-GuardOnly]` を `ProcessStartInfo.ArgumentList`（シェル無し・UTF-8・`GIT_TERMINAL_PROMPT=0`）で 120 秒のタイムアウト付きで起動（超えたら `GitProcess.KillTree`）し、JSON をそのまま返す `{ok, checks:[{name,ok,msg}]}`。`pwsh` が無ければ `{error:{code:"exception", msg:"pwsh が無い…"}}`、JSON が読めなければ `{ok:false, raw:<先頭 500 文字>, exitCode?}`。開発リポジトリ（`PackageInfo.FindForAssembly` が `Embedded`、かつ `Tools/Release/check-release.ps1` がある）以外は `invalid_params`。`bump-version` はツールにしない（Q-9）。
- **`check-release.ps1 -Json`**（追加スイッチ。人向けの出力は変更なし）: `-Json` のときは `Write-Host` を握りつぶし、標準出力に JSON 1 個だけ（`ConvertTo-Json -Compress -EscapeHandling EscapeNonAscii` なので呼び出し側のコードページに依らず読める）。`-GuardOnly` との併用は CHANGELOG ガード 1 件だけ。想定外の例外も `trap` で `{ok:false, checks:[{name:"check-release の実行",…}]}` にする。終了コードは通常実行と同じ（失敗 = 1）。
- **ツール数**: ここまでで 18 個（`ddrive_status` / `help` / `validate` / `validate_fix` / `forbidden_api` / `asset_list` / `get` / `create` / `set` / `usages` / `unused` / `delete` / `editor_open` + 本チケット 5 個）。**MCP-7 の 4 個を足すと 22 個で §5.1 の「20 個以内」を超える**（`ddrive_compat_update` の分割が +1）。MCP-7 着手前に統合（例: `ddrive_asset_usages` / `unused` を 1 ツール化、`ddrive_editor_open` を `ddrive_asset_get` に統合）を決める必要がある。
- **テスト**: `DDriveGenerateToolTests` / `DDriveMigrateToolTests` / `DDriveCompatToolTests` / `DDriveReleaseToolTests`。実 `Assets/Generated`・実依存グラフ・実スナップショットは書かない（`ids` / `tuning` の `preview` が実ファイルの内容・更新時刻を変えないことを確認、`compat` は実スナップショットとの差分が空であることの実検査、`release_check` は `pwsh` があるときだけ形を検証）。

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
| `ddrive_asset_list` | R | `type`（AssetType 名）、`category?`、`query?`（DisplayName / identifier の部分一致）、`fields?`（既定 `id,name,category`）、`cursor?`、`limit?`（既定 50、最大 200） | `{total, items:[{id,name,category,...}], next?}`（`id` は 10 進文字列） | `AssetSearch.FindAssets` |
| `ddrive_asset_get` | R | `type`、`id`（10 進文字列。`0x` 16 進・JSON の整数も受ける）または `path`、`fields?`（既定: 共通欄 + 種別の主要欄。`*` で全部） | `{id,name,path,fields:{...}, validation:{errors,warnings}}`（`id` は 10 進文字列。`max_chars` を超える分は末尾の欄を丸ごと落として `truncated` + `omitted`） | `SerializedObject` を読む（型ごとの欄の表は §4.5） |
| `ddrive_asset_create` | W | `type`、`name`（DisplayName）、`category`、`identifier?`（省略時は `AssetNamingService.ToIdentifier(name)`）、`data_class?`（ControlSkin のみ）、`fields?`（作成時に設定する欄）、`preview?` | `{id, path, addressable:true, validation:{errors,warnings}}`（`id` は 10 進文字列。preview なら `{wouldCreate:path, identifier}`） | `AssetCreationService.Create` → `AddressablesSync.EnsureEntry`。**`CreateAssetMenu` 直叩きはしない**（[20](20_mcp_setup.md) §2 の規約） |
| `ddrive_asset_set` | W | `type`、`id`、`fields`（`{欄名: 値}`。入れ子は `Common.Color` のようなドット区切り。配列は全体置換）、`preview?` | `{changed:[{field,from,to}], validation:{errors,warnings}}`（preview なら `{wouldChange:[...]}`） | `SerializedProperty` 経由で書く。**`Undo.RecordObject` + `EditorUtility.SetDirty` + `DDriveAssetSave.SaveDirty`**。`Id` / `SchemaVersion` / `ImportSourceGuid` / `Version` / `UpdatedAt` / `Icon` は `read_only_field` で拒否（読み取り専用欄の一覧は §4.5）。`ChangeNote` の先頭に `[mcp] ` を付ける（Q-7） |
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

- **共通欄**（`AssetDataBase`）: `DisplayName` / `Description` / `Category` / `Tags` / `Assignee` / `SpecUrl` / `Author` / `ChangeNote` / `Flags.*` / `Events.*`。**読み取り専用**: `Id` / `SchemaVersion` / `ImportSourceGuid` / `Version` / `UpdatedAt` / `Icon`（`ddrive_asset_set` は `{error:{code:"read_only_field"}}` で拒否し、何も書かない）。
- **種別ごとの主要欄**（既定で返すもの）は、各種別の設計 doc の「データ構造」節の**先頭 10 欄以内**とし、実装時に `Editor/Mcp/FieldTables.cs` に表で持つ（例: SeData = `Clip,Volume,Pitch,Loop,Spatial,Priority,Category`、VfxData = `Prefab,Anchor,Duration,RenderMode,Scale`）。`fields:"*"` で全欄。欄名はシリアライズ名そのまま（`m_` を付けない。`SerializedProperty` のパス）。
- **ID は JSON では 10 進文字列**（`"id":"1234567890123"`。ulong が 2^53 を超えると JS のクライアントで桁落ちするため。入力は 10 進 / `0x` 16 進の文字列か JSON の整数）。MCP-5 の `ddrive_validate` の `items[].id` も同じく文字列（MCP-3 で変更）。
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
- **決定（MCP-2）: カードの正本は `Packages/com.ddrive.core/Editor/Mcp/Cards/`**（Q-10 の「`docs/1002_ddrive_mcp/cards/`」から変更。同期の手間をなくし、git URL で入れた持ち込み先でもそのまま読めるため）。以下は当初の方針:
- カードの正本は `docs/1002_ddrive_mcp/cards/*.md`（この文書の隣。P-9 の同梱物の同期で `Documentation~/Mcp/cards/` へ）。**docs と二重管理にしない**: カードは「docs のどの節の要約か」を先頭行に書き、docs 側を変えたらカードも同じ PR で直す（[12](12_review.md) §3 に 1 行足す）。
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
- **実装メモ（2026-10-07）**: ハッシュ規則は isuzu v4.2.0 `McpInstanceDescriptor.HashProjectPath` と同一 = `SHA256(UTF-8(Application.dataPath))` の先頭 8 バイトを小文字 16 進 16 文字にする。`dataPath` は `/` 区切り・末尾スラッシュ無し・大小文字そのまま・正規化なし（`C:/Users/yamag/wrench/D-Drive/Assets` → `a26b71fdfd662823` で実機の記述子と一致を確認）。ハッシュで見つからなければ `instances\*.json` を `projectPath`（大小無視）で走査し、警告を出す。終了コード: 0 成功 / 2 記述子なし / 3 pid 死亡 / 4 `claude` が PATH に無い / 5 `claude mcp add` 失敗。`-Print`（トークン非表示で表示のみ）・`-DryRun`・`-ProjectPath`・`-Name`・`-Scope` あり。`McpPortPolicyTests` と `DD-MCP-FIXED-PORT` は MCP-1 の asmdef の後。

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

## 8. チケット分割（v1.5.0。状態の正本は [1004_tasks.md](1004_tasks.md) §1。ここは起票時の案）

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
| MCP-11 | docs / SKILL.md / ProgrammerManual / CHANGELOG、人による確認手順（`verification/1003_manual_verification_mcp.md`） | 1 | MCP-9 |
| MCP-12 | 自前レビュー → 修正 → v1.5.0 リリース（[12](12_review.md) §7） | 1 | MCP-11 |

合計 約 12 人日。実装は Sonnet のエージェントに 1 チケットずつ委任し、レビューとリリース判断は上位モデルが行う（2026-10-07 の運用指示）。

---

## 9. 決めてほしいこと（Q-1〜Q-12）

各行の「案」で進めてよければ返事は不要。変えたい行だけ指示をもらう。

| # | 問い | 案（推奨） | 理由・影響 |
|---|---|---|---|
| Q-1 | **CoplayDev 版を外すか**（詳細 §9.2）（v1.5.0 で isuzu 版に一本化） | **外す** | 固定ポートの競合源（§6.1 (a)）。isuzu 版で 3 セッション以上問題なく運用済み（docs/20 §4 の基準を満たしている）。MS2026 も同時に外す |
| Q-2 | **isuzu のタグを v4.2.0 のまま固定するか、最新に上げるか** | **v4.2.0 のまま**で実装し、MCP-12 の直前に最新を 1 回だけ評価 | `[McpTool]` の属性名・`ToolCatalog` の発見規則が変わると全ツールに影響する。上げるなら `McpPortPolicyTests` と `mcp-tools.txt` で差分を検出できる |
| Q-3 | **持ち込み先（MS2026）にも配るか** | **配る**（同梱・既定は無効 = isuzu が無ければ何も増えない） | MS2026 の AI 作業でも同じツールが使える。isuzu を入れるかは MS2026 の判断 |
| Q-4 | **書き込みツール（W / D）を既定で有効にするか**（詳細 §9.3） | **有効**。ただし `DDriveProjectSettings.McpAllowWrite`（既定 true）で**プロジェクト単位に無効化**できる | デザイナーの作業ファイルを AI が触るのを止めたいプロジェクト向け。無効時は W / D が `{error:{code:"write_disabled"}}` を返す |
| Q-5 | **返り値のメッセージの言語** | **キーは英語、本文（Validation の `msg` 等）は既存の日本語のまま** | 翻訳の二重管理を避ける。英語本文の方がトークンは少し減るが、docs・マニュアルと食い違う方が損 |
| Q-6 | **`ddrive_asset_set` が書ける欄の範囲** | **シリアライズされた全欄**（読み取り専用欄を除く）。`Flags.Load` 等の「変えると Preload / 同期 API に影響する欄」は変更後の検査結果を必ず返す | 欄ごとのホワイトリストは保守が重い。検査で守る |
| Q-7 | **AI が変えた Data に印を付けるか**（`ChangeNote` に `[mcp]` を前置、`Author` を `mcp:<client>` に） | **`ChangeNote` の先頭に `[mcp] ` を付ける**（`Author` は触らない） | 誰が変えたかを AssetBrowser と SpecWeb で追える。`VersionStampProcessor` の `Version` / `UpdatedAt` は従来どおり自動 |
| Q-8 | **Play Mode 中の読み取り（R）を許すか** | **許す**（W / D は拒否） | 実機確認中に状態を読めると便利。書き込みは変更が破棄されるので拒否（[20](20_mcp_setup.md) §2 の規約） |
| Q-9 | **`ddrive_release_check` / `ddrive_build_netcheck` のような「Editor の外の道具」をツールにするか** | **する**（`pwsh` 起動の薄いラッパー）。`git tag` / `push` を伴う `bump-version.ps1` は**ツールにしない** | リリースの判断と push は人の操作のまま（[12](12_review.md) §7 の原則） |
| Q-10 | **`ddrive_help` のカードの正本の置き場** | `docs/1002_ddrive_mcp/cards/*.md`（docs と同じ PR で更新） | §5.4。二重管理を避けるため docs 側の節を要約した形にし、要約元を先頭行に書く |
| Q-11 | **SpecWeb（GAS）との連携ツール**（`ddrive_spec_sync`）を v1.5.0 に入れるか | **入れない**（v1.5.x で検討） | 書き込みトークンを AI に渡す設計が要る。[32](32_spec_web.md) §7 のセキュリティ節と一緒に決める |
| Q-12 | **ツール名の接頭辞と最大数** | `ddrive_`、**20 個以内**（増やすときは既存ツールの `mode` に足すのを先に検討） | §5.1。ツール定義は毎ターン送られるので、数が増えるほど常時コストになる |

---

### 9.1 決定状況（2026-10-07、ユーザー回答）

| # | 状態 |
|---|---|
| Q-1 | **確定: 外す**（2026-10-07 ユーザー回答「外して OK」。MCP-0 で実施） |
| Q-2 | **案で確定**（v4.2.0 固定のまま実装。ユーザー「どっちでも OK」） |
| Q-3 | **確定: 同梱**（isuzu が無ければ無効） |
| Q-4 | **確定: (c)**（既定 false。セットアップウィザードが開発リポジトリでは true を書き、持ち込み先では「有効にしますか」と聞く。2026-10-07 ユーザー回答） |
| Q-5〜Q-12 | **案で確定** |

### 9.2 Q-1 の詳細: CoplayDev 版を外すとどうなるか

**今の状態**: `Packages/manifest.json` に CoplayDev（`com.coplaydev.unity-mcp` v10.2.0）と isuzu（v4.2.0）の両方が入り、`.mcp.json` に CoplayDev のエントリ（HTTP 8081）がある。Claude Code は両方に接続し、両方のツール定義（CoplayDev 約 30 個 + isuzu 141 個のうち常時ロード分）を**毎ターン**コンテキストに載せている。

**CoplayDev にしか無いもの**: 実質的に無い。`read_console` / `run_tests` / `execute_menu_item` / `manage_*` は isuzu の `console_read_logs` / `test_run` + `test_results` / `menu_execute` / `gameobject_*` `asset_*` `prefab_*` で置き換え済み（[20](20_mcp_setup.md) §4 の対応表）。CoplayDev で困っていた点（別プロセスの Python サーバー、固定ポートの衝突、`run_tests` の初期化タイムアウト、失敗テストのスタックトレースが返らない）は isuzu で解消している。CI（`run-ci.cmd`）はどちらにも依存しない。

**外すと得られるもの**:
1. ポート競合の唯一の固定ポート源（8081）が消える（§6.1 (a)）。
2. 毎ターンのツール定義が減る（トークン最小化の G-2 に直接効く。CoplayDev の約 30 ツールぶん）。
3. Python サーバー（`uvx mcpforunityserver`）の起動・更新・Python 環境の管理が不要になる（[20](20_mcp_setup.md) §1・§3 が丸ごと消える）。
4. 2 つのサーバーが同じ Editor を同時に操作する事故（片方がテスト実行中にもう片方が書き込む）が構造的に起きなくなる。
5. manifest から 1 パッケージ減る（Roslyn DLL の重複供給も無くなる）。

**外すリスクと対策**:
1. isuzu は個人保守のプロジェクトで、破壊的変更が多い時期（v4.0.0 が 2026-09-04）。→ タグ固定（Q-2）と、`McpPortPolicyTests` / `mcp-tools.txt` で上げたときの差分を検出する。壊れたら CoplayDev を manifest に戻すだけで元の運用に戻れる（手順は [20](20_mcp_setup.md) §1 を archive に残す）。
2. isuzu のトークンが Unity 再起動のたびに変わり、登録し直しが要る。→ `register-mcp.ps1`（MCP-8）で 1 コマンドにする。CoplayDev は固定ポート・認証無しだったので登録し直し不要だったが、それがポート競合の原因でもある。
3. MS2026 側でも CoplayDev を使っている場合、外すタイミングを揃える必要がある（PC で Python サーバーは 1 つなので、片方だけ残しても動く）。→ MS2026 の manifest から外すのは MS2026 の担当の作業（v1.5.0 の案内に書く）。
4. isuzu は Bearer トークン必須なので、`.mcp.json`（リポジトリ共有）に書けず、各自 1 回の登録が要る。→ 既にそういう運用（2026-09-10〜）。

**外さない場合**: manifest と `.mcp.json` は現状維持。ただし §6.1 (a) の「固定ポートをどこにも書かない」は満たせないので、G-3 は「isuzu 側だけ競合ゼロ」に弱まる。トークンも CoplayDev のツール定義ぶん毎ターン余計にかかる。**折衷案**: manifest には残すが `.mcp.json` のエントリを消して**既定では接続しない**（必要な人だけ `claude mcp add` で足す）。これなら固定ポートの衝突も毎ターンのトークンも避けつつ、戻すのが速い。

**推奨**: 外す。折衷案でも可。

### 9.3 Q-4 の詳細: 書き込みツールを既定で有効にするか

**対象**: 種別 W / D のツール（`ddrive_asset_create` / `set` / `delete`、`ddrive_validate_fix`、`ddrive_generate`、`ddrive_migrate`、`ddrive_compat update`、`ddrive_preview_open` / `play` / `sweep`、`ddrive_build_netcheck`）。R（読み取り）は常に有効。

**CLAUDE.md §0-5「Data は読み取り専用」との関係**: あの規則は**実行時に Manager が Data を書き換えない**こと。Editor（人の操作・専用エディタ）が Data を書くのは正当で、条件は `Undo.RecordObject` + `EditorUtility.SetDirty`。MCP の W ツールも同じ条件で書く（AI が専用エディタを操作するのと同じ経路）。

**既に入れる安全装置**（有効・無効に関わらず全部入る）:
1. `Undo` + `SetDirty` + `DDriveAssetSave.SaveDirty`（人が Ctrl+Z で戻せる）。
2. `dry_run`（何も書かずに「こうなる」を返す）。D は加えて `confirm` 必須（isuzu が注入）。
3. Play Mode 中は拒否（変更が破棄されるため）。
4. 読み取り専用欄（`Id` / `SchemaVersion` / `ImportSourceGuid` / `Version` / `UpdatedAt` / `Icon`）は拒否。
5. 作成は `AssetCreationService` 経由（命名・配置・Addressables 登録が規約どおりになる）。削除は `SafeDeleteService`（使用中なら止まる）。
6. YAML を直接触らない（`SerializedProperty` 経由。`.unity` / `.prefab` / `.meta` には触れない）。
7. 変えた Data の `ChangeNote` に `[mcp] ` を前置（Q-7。誰が変えたか追える）。
8. Validation の結果を書き込みの返り値に必ず付ける（壊したらその場で分かる）。

**残るリスク**: (a) デザイナーが**未コミットで編集中**の `.asset` を AI が上書きする（Undo は AI 側の Editor でしか効かない。別 PC なら git で衝突）。→ 運用規則「書く前に `git status` で他人の未コミット変更を見る」（SKILL.md §4）は AI の判断に依存する。(b) AI の誤った一括変更（`ddrive_generate` / `validate_fix` / `migrate apply`）。→ `dry_run` と `confirm`、そして git で戻せる。

**選択肢**:
| 案 | 内容 | 向くプロジェクト |
|---|---|---|
| (a) 既定で有効 | `DDriveProjectSettings.McpAllowWrite = true` が既定。止めたいプロジェクトは設定で false にする（`ProjectSettings/` に入るので git で共有される） | 開発リポジトリ（D-Drive 自身）。AI に Data を作らせる前提の運用 |
| (b) 既定で無効 | 既定 false。使うプロジェクトが明示的に true にする。無効時は W / D が `{error:{code:"write_disabled"}}` を返す | 持ち込み先（MS2026）でデザイナーの作業ファイルを守りたい場合 |
| (c) 開発リポジトリは有効・持ち込み先は無効 | 既定値は (b) と同じ false だが、セットアップウィザード（P-6）が開発リポジトリでは true を書き、持ち込み先では「有効にしますか」と聞く | 両方。設定の意味を導入時に 1 回見せられる |

**推奨**: (c)。理由は、持ち込み先ではデザイナーが Data を直接編集しているので「AI が勝手に書けない」が既定として安全で、開発リポジトリでは AI に書かせる運用が前提だから。(a) でも実害は小さい（安全装置 1〜8 は同じ）。(b) は「使うときに毎回設定を探す」手間が開発リポジトリで無駄になる。

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

- 2026-10-07（MCP-6）: `ddrive_generate`（7 種。引数名は `kind`、`preview` は kind ごとに意味が違う）/ `ddrive_migrate` / `ddrive_compat`（Safe）+ `ddrive_compat_update`（Destructive、分割）/ `ddrive_release_check` を実装。`check-release.ps1` に `-Json` を追加。ツールは 18 個になり MCP-7 の 4 個を足すと 22 個（§5.1 の上限超過）。実装メモ（MCP-6）を追加
- 2026-10-07（MCP-4）: `ddrive_asset_usages` / `unused` / `delete` / `ddrive_editor_open` を実装。`dry_run` → `preview`、`delete` は分析 → 実行の 2 段（コード参照があれば拒否）。実装メモ（MCP-4）を追加
- 2026-10-07（MCP-3）: `ddrive_asset_list/get/create/set` + `FieldTables` を実装。ID を 10 進文字列に決定（MCP-5 の `items[].id` も変更）、§4.2・§4.5 を実装に合わせた（`dry_run` → `preview`、読み取り専用欄は `read_only_field` エラー）。実装メモ（MCP-3）を追加
- 2026-10-07（同日 2）: Q-1 = 外す、Q-4 = (c) で確定（ユーザー回答）。全 Q 確定、MCP-0 に着手
- 2026-10-07（同日）: ユーザー回答を §9.1 に記録（Q-2 案で確定・Q-3 同梱・Q-5〜12 案で確定・Q-1 / Q-4 は詳細 §9.2 / §9.3 を書いて説明待ち）。文書番号を 68 → 1002 に変更（docs の番号は 10xx に統一）
- 2026-10-07: 起票（v1.5.0 の仕様。ユーザー指示「D-Drive MCP: D-Drive 周りを全面サポート、トークン最小、ポート競合ゼロ」を受けて作成。Editor 機能の棚卸しと isuzu 版の拡張 API・ポート規則の調査結果に基づく）
