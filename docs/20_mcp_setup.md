# 20. MCP（AI ⇄ Unity Editor 連携）セットアップ

> **2026-10-07 改訂（MCP-0）**: MCP は **Editor 組み込みの `jp.shiranui-isuzu.unity-mcp`（isuzu 版）だけ**にした。2026-09-08〜10-07 に併用していた CoplayDev 版（`com.coplaydev.unity-mcp` + 別プロセスの Python サーバー、固定ポート 8081）は `Packages/manifest.json` と `.mcp.json` から外した（理由と経緯は §5、外す直前の手順は [archive/1003](archive/1003_coplaydev_mcp_setup.md)）。v1.5.0 で D-Drive 自身のツール群 `ddrive_*` をこのサーバーに載せる計画は [1002_ddrive_mcp.md](1002_ddrive_mcp.md)。

関連: [09_editor_tools.md](09_editor_tools.md) / [12_review.md](12_review.md) / ルートの [CLAUDE.md](../CLAUDE.md) §4 / [1002_ddrive_mcp.md](1002_ddrive_mcp.md)

AI エージェント（Claude Code 等）が **起動中の Unity Editor を直接操作・観測**できるようにする仕組み。D-Drive では主に「コンパイル結果・テスト結果・Console の確認」「`Tools > D-Drive` のメニュー実行」「C# の実行（`execute_code`）」「専用エディタの起動・確認用シーンの操作」に使う。

```
[Claude Code] ──streamable HTTP + Bearer トークン── [Unity Editor 内の MCP サーバー(jp.shiranui-isuzu.unity-mcp)]
   各自の ~/.claude.json(claude mcp add)                    Packages/manifest.json(リポジトリ同梱、タグ固定 v4.2.0)
   ↑ 接続先は %LOCALAPPDATA%\UnityMCP\instances\<hash>.json から読む(ポートはプロジェクトごとに自動)
```

| 部品 | 実体 | バージョン | 管理場所 |
|---|---|---|---|
| MCP サーバー（Editor 組み込み） | `jp.shiranui-isuzu.unity-mcp`（[isuzu-shiranui/UnityMCP](https://github.com/isuzu-shiranui/UnityMCP)、MIT） | **v4.2.0**（タグ固定） | `Packages/manifest.json`（`?path=jp.shiranui-isuzu.unity-mcp#v4.2.0`） |
| 接続情報（ポート・トークン） | `%LOCALAPPDATA%\UnityMCP\instances\<hash>.json`、`tokens\<hash>.token` | — | 各自の PC（Editor が起動時に書く。**リポジトリには入れない**） |
| クライアント登録 | `claude mcp add --transport http isuzu-unity <mcpUrl> --header "Authorization: Bearer <token>"` | — | 各自の `~/.claude.json`（プロジェクト配下）。`.mcp.json` は置かない（トークンが入るため） |

---

## 1. 初回セットアップ（各自 1 回）

1. **前提**: Git が PATH にある（manifest の git URL 解決に使う）。Python や `uv` は不要。
2. `git pull` 後に Unity を開く → Package Manager が `jp.shiranui-isuzu.unity-mcp` を取得してコンパイルする。サーバーは **Editor 起動時に自動起動**する（`Preferences > Unity MCP` で確認・停止・トークン再生成ができる）。
3. 接続情報を読む: `%LOCALAPPDATA%\UnityMCP\instances\` にある、`projectPath` が `C:/.../D-Drive/Assets` の JSON を開き、`mcpUrl`（例 `http://127.0.0.1:27725/mcp`）と `token` を控える。
4. Claude Code に登録する（プロジェクト直下で）: **`pwsh Tools/Mcp/register-mcp.ps1`**（記述子を読み、pid の生存を確認して `claude mcp remove` → `add` で上書き登録する。`-Print` で接続情報の確認のみ、`-DryRun` でコマンドの確認のみ。[Tools/Mcp/README.md](../Tools/Mcp/README.md)）。手動で行うときのフォールバック:
   ```bash
   claude mcp add --transport http isuzu-unity http://127.0.0.1:27725/mcp --header "Authorization: Bearer <token>"
   ```
5. Claude Code のセッションを開き直すと `mcp__isuzu-unity__*` ツールが載る。`claude mcp list` で `isuzu-unity ✓ Connected` になること。

### ポートについて（競合しない仕組み）

- ポートは**プロジェクトのパスから自動で決まる**（`Assets` のパスの SHA256 → 27200〜27999。D-Drive は 27725）。同じ PC で MS2026 など別プロジェクトを開いてもパスが違うので別ポートになる。万一同じになっても、isuzu が空きポートを走査して別のポートで起動し、記述子に `portMismatch: true` を書く。
- **`Preferences > Unity MCP` でポートを固定しない**（固定すると他プロジェクト・他アプリと衝突する元に戻る。[1002](1002_ddrive_mcp.md) §6.1 (c)）。
- 接続先は常に記述子（`instances/<hash>.json`）から読む。**ポート番号をリポジトリ・docs・スクリプトに直書きしない**。

### 接続確認・トラブル

| 症状 | 原因 | 対処 |
|---|---|---|
| ツールが `ECONNREFUSED` | Unity が起動していない / サーバーが停止している | Unity を起動する。`Preferences > Unity MCP` で Running を確認 |
| 401 / 認証エラー | **Unity を再起動してトークンが変わった**（起動のたびに再生成される設定の場合） | `instances/<hash>.json` を読み直し、`claude mcp remove isuzu-unity` → `add` で登録し直す（`pwsh Tools/Mcp/register-mcp.ps1` で 1 コマンド）。セッションに載っていないときは HTTP 直叩き（§2 のフォールバック）で続けられる |
| 記述子の `pid` のプロセスが無い | Editor のクラッシュ後の古い記述子 | Unity を起動し直すと上書きされる |
| 別プロジェクト（MS2026 等）に繋がっている | 記述子を取り違えた | `projectPath` を見て D-Drive の記述子を使う。v1.5.0 の `ddrive_status.mcp.project` で AI 側でも確認できる |
| ドメインリロード中にエラー | `compile_request` / テスト実行の直後 | 20〜25 秒待ってから `compile_status` / `test_results` をポーリングする（§2） |

---

## 2. D-Drive での使い方（AI に任せる作業の線引き）

| AI に任せてよい | 人間がやる（AI に任せない） |
|---|---|
| スクリプトの生成・編集（通常のファイル編集） | `ProjectSettings/` の変更（Layer/Tag/Physics 等） |
| **`compile_status` / `console_read_logs` でコンパイルエラー・警告の確認** | パッケージの追加・削除（`manifest.json`） |
| **`test_run` + `test_results` で EditMode/PlayMode テストの実行** | 他人が作業中のシーン・Prefab への変更 |
| `Tools > D-Drive` の各メニュー実行（`menu_execute`）と結果確認 | ビルド設定の変更 |
| 専用エディタ（VFX/Audio/Model）の起動・操作確認・スクリーンショット（`capture_screenshot`） | 本番アセットの最終的な見た目・音の判断 |
| GameObject / Prefab の構成確認、単純な Prefab の組み立て（`gameobject_*` / `prefab_*`） | 大規模なシーン作り替え |
| `GameData/` 配下の `*Data.asset` の値変更（**AssetBrowser / 各エディタ経由、`execute_code` で `AssetCreationService` 等を呼ぶ、または v1.5.0 の `ddrive_*` ツール**） | — |

### 運用ルール（[12_review.md](12_review.md) §3 に準ずる）

- **`.unity` / `.prefab` / `.asset` の YAML を AI にテキスト編集させない。** 必ず MCP ツール（Unity Editor 経由）で行う。`.meta` の手作成・GUID 書き換えも禁止
- **Play Mode 中は書き込み系ツールを実行しない**（変更が破棄される）
- コード変更後は AI 自身が `compile_status` でエラー 0 を確認し、関連テストを `test_run` で回してから完了報告する。**接続できていないのに「Unity で確認した」と報告しない**（未検証なら未検証と書く）
- **テスト実行中はメインスレッドが塞がるので `test_results` 以外を呼ばない**。`compile_request` の直後はドメインリロードで接続が切れるので 20〜25 秒待つ
- AI の変更も通常どおり PR に載せる（「AI がやった」はレビュー省略の理由にならない）
- 命名・配置規約（[10_workflow.md](10_workflow.md) §3）はツールが維持する前提のため、AI に Data を作らせる場合も **AssetBrowser の新規作成経路（`AssetCreationService`）を通す**。`CreateAssetMenu` 直叩きは開発者向けフォールバック
- **テストで実データを汚していないか**: テスト前後で `git status` を見る。`Assets/AddressableAssetsData/AssetGroups/*.asset` と `ProjectSettings/DDriveProjectSettings.asset` は差分が出やすい。出たら `git checkout -- <path>` で戻す

### よく使うツール

| 用途 | ツール |
|---|---|
| コンパイル | `compile_request` → `compile_status`（`succeeded` / `errorCount` / `messages`） |
| コンソール | `console_read_logs`（type=error 等）/ `console_get_count` / `console_clear` |
| テスト | `test_run`（mode=edit / play、filter=正規表現）→ `test_results`（失敗の message と stackTrace を含む。ポーリング可） |
| C# 実行 | `execute_code`（Roslyn。System / Linq / UnityEngine / UnityEditor は import 済み、`using` は書けない。長い処理は `job_status` で結果を取る） |
| メニュー | `menu_execute` |
| 状態 | `play_mode_status` / `scene_list` |
| その他 | `gameobject_*` / `asset_*` / `prefab_*` / `inspect_*` / `material_*` / `capture_screenshot` / `editor_dialog_*` / `reflect_*` など（全 141 個） |

### セッションにツールが載っていないときのフォールバック（HTTP 直叩き）

Unity の再起動でトークンが変わると、開いているセッションの `mcp__isuzu-unity__*` は繋がらなくなる。サーバー自体は生きているので、Python で JSON-RPC を直接叩ける（[SKILL.md](../.claude/skills/ddrive-agent-workflow/SKILL.md) §2 に手順）: 記述子から `mcpUrl` と `token` を読み、`Content-Type: application/json` / `Accept: application/json, text/event-stream` / `Authorization: Bearer <token>` を付けて `tools/call` を POST する。

---

## 3. バージョンを上げるとき

1. `Packages/manifest.json` の `#vX.Y.Z` を変更（PR 必須。`#main` 指定は人によって別バージョンが入るため禁止）
2. Unity を開き直す（解決後にスクリプトの再コンパイルが走らないことがある。その場合は `Client.Resolve()` + `RequestScriptCompilation` を明示、2026-09-10 の実測）
3. 本ドキュメントと [CLAUDE.md](../CLAUDE.md) §4 のバージョン表記を更新
4. v1.5.0 以降は、`[McpTool]` の発見規則・ポート規則が変わっていないかを `McpPortPolicyTests` と `mcp-tools.txt` のスナップショットで確認する（[1002](1002_ddrive_mcp.md) §6.3・§7）
5. 同一 PC で複数プロジェクト（MS2026 等）を運用している場合も、サーバーはプロジェクトごとに別なので揃える必要は無い（揃えた方が運用は楽）

---

## 4. 持ち込み先（MS2026 等）での扱い

- `jp.shiranui-isuzu.unity-mcp` は開発専用で、`com.ddrive.core` の `package.json` の依存には**入れない**（[42_distribution.md](42_distribution.md) §4.3）。持ち込み先が AI 連携を使うときは自分の `manifest.json` に足す。
- v1.5.0 の `DDrive.Editor.Mcp`（`ddrive_*` ツール）はパッケージに同梱されるが、isuzu が無いプロジェクトではコンパイルされない（Version Defines）。書き込みツールは既定で無効で、セットアップウィザードが「有効にしますか」と聞く（[1002](1002_ddrive_mcp.md) §9.1 Q-3 / Q-4）。

---

## 5. 経緯（CoplayDev 版の併用と廃止）

- 2026-09-08: CoplayDev 版 `com.coplaydev.unity-mcp` v10.2.0 を導入。固定ポート 8080 が別アプリ（`Livelist.exe`）と衝突したため 8081 へ変更。
- 2026-09-10: 「別プロセスの Python サーバー」「固定ポートの衝突」「`run_tests` が 2 回に 1 回初期化タイムアウト」「失敗テストのスタックトレースが返らない」を解消するため isuzu 版を併用で導入し、繋がっているときは isuzu 版を優先する運用に。評価結果（EditMode 137 件 26 秒・PlayMode 303 件 20 秒、初期化失敗なし、`test_results` が message + stackTrace を返す）は [archive/1003](archive/1003_coplaydev_mcp_setup.md) §4。
- 2026-10-07: 「3 セッション問題なければ外す」の基準を満たしたこと、v1.5.0 の D-Drive MCP（[1002](1002_ddrive_mcp.md)）で**固定ポートをどこにも書かない**ことと**毎ターンのツール定義を減らす**ことを設計の軸にしたことから、ユーザー決定（Q-1）で CoplayDev 版を外した。manifest・`.mcp.json`・CLAUDE.md §4・本書・AGENTS.md・SKILL.md・docs/34・docs/42・パッケージ README を isuzu 版のみに書き換え。戻すときは [archive/1003](archive/1003_coplaydev_mcp_setup.md) §1 の手順で manifest と `.mcp.json` を復元する。
