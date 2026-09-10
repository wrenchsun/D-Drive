# 20. MCP（AI ⇄ Unity Editor 連携）セットアップ

関連: [09_editor_tools.md](09_editor_tools.md) / [12_review.md](12_review.md) / ルートの [CLAUDE.md](../CLAUDE.md)

AI エージェント（Claude Code 等）が **起動中の Unity Editor を直接操作・観測**できるようにする仕組み。
D-Drive では主に「コンパイル結果・テスト結果・Console の確認」「専用エディタウィンドウの起動と操作確認」「シーン/プレハブの組み立て」に使う。

```
[Claude Code] ──HTTP (127.0.0.1:8081/mcp)── [MCP サーバ mcpforunityserver] ──── [Unity 内ブリッジ com.coplaydev.unity-mcp]
   .mcp.json(リポジトリ同梱)                 各自の PC(uvx が自動取得)                Packages/manifest.json(リポジトリ同梱)
```

| 部品 | 実体 | バージョン | 管理場所 |
|---|---|---|---|
| Unity ブリッジ | `com.coplaydev.unity-mcp` | **10.2.0**（タグ固定） | `Packages/manifest.json` |
| MCP サーバ | `mcpforunityserver` (PyPI) | **10.2.0** | 各自の PC。Unity の MCP ウィンドウが `uvx` 経由で起動 |
| クライアント設定 | `.mcp.json` | — | リポジトリ直下（プロジェクトスコープ） |

> ⚠ **ブリッジとサーバのバージョンは必ず一致させる。** 片方だけ上げると「繋がるが一部ツールだけ失敗する」分かりにくい壊れ方をする。`#main` 指定は人によって別バージョンが入るため禁止。

---

## 1. 初回セットアップ（各自 1 回）

1. **前提ツール**: Git（PATH 必須。manifest の git URL 解決に使う）と `uv`/`uvx`（<https://docs.astral.sh/uv/>。`uvx --version` が通れば OK）
2. `git pull` 後に Unity を開く（既に開いていればウィンドウをクリックしてフォーカス）→ Package Manager が `MCP for Unity` を取得してコンパイルする
3. Unity メニュー **`Window > MCP for Unity > Toggle MCP Window`**（`Ctrl+Shift+M`）を開く
4. Transport を **HTTP (Local)** にし、**Start Server** を押す → `mcpforunityserver 10.2.0` が `uvx` で取得・起動され、`http://127.0.0.1:8080/mcp` で待ち受ける（**この PC では 8080 を `Livelist.exe` が使っているため、Base URL を `http://127.0.0.1:8081` に変更して運用中**。`.mcp.json` も 8081）
5. 同ウィンドウの **Connect** で Unity ブリッジをサーバに接続する（Auto-Start を有効にしておくと以後は Editor 起動時に自動）
6. プロジェクト直下で Claude Code を起動する。`.mcp.json` が検出され、初回のみ「このプロジェクトの MCP サーバを許可するか」を聞かれるので許可する

### 接続確認

Claude に「Unity の Console を読んで」と頼み、エラーにならず結果が返れば接続できている。

| 症状 | 原因 | 対処 |
|---|---|---|
| `No Unity Editor instances found` | Unity 未起動 / ブリッジ未接続 | Unity を起動し、MCP ウィンドウで Connect |
| 接続拒否 (connection refused) | HTTP サーバが起動していない | MCP ウィンドウで Start Server |
| Claude Code 側が `ENDPOINT_NOT_FOUND` / `curl http://127.0.0.1:8080/mcp` が 403・404 を返す | **8080 を別プロセスが占有**しており `mcpforunityserver` が起動できていない（`Get-NetTCPConnection -LocalPort 8080 -State Listen` で持ち主を確認。2026-09-08 は `Livelist.exe` だった） | 占有プロセスを止めるか、MCP ウィンドウの Base URL を `http://127.0.0.1:8081` 等に変えて Start Server → `.mcp.json` の `url` も同じポートに揃え、Claude Code を再起動 |
| 一部のツールだけ失敗する | **ブリッジとサーバのバージョン不一致** | 両方 10.2.0 に揃える |
| Package Manager で git エラー | Git 未導入 / PATH 未設定 | Git を入れて PC 再起動 |
| 別プロジェクト（MS2026 等）を同時に開いている | 1 つのサーバに複数 Editor が接続 | ツール呼び出し時に AI へ「D-Drive の Unity を対象にして」と明示（サーバは複数インスタンスをルーティングできる。混線時は片方を閉じる） |

### テレメトリ

サーバは既定で利用統計を送信する。切る場合は Unity の MCP ウィンドウ **Advanced > Telemetry** を無効にする（サーバ起動時に環境変数 `UNITY_MCP_TELEMETRY_ENABLED=false` が渡される）。

---

## 2. D-Drive での使い方（AI に任せる作業の線引き）

| AI に任せてよい | 人間がやる（AI に任せない） |
|---|---|
| スクリプトの生成・編集（通常のファイル編集） | `ProjectSettings/` の変更（Layer/Tag/Physics 等） |
| **`read_console` でコンパイルエラー・警告の確認** | パッケージの追加・削除（`manifest.json`） |
| **`run_tests` で EditMode/PlayMode テストの実行** | 他人が作業中のシーン・Prefab への変更 |
| `Tools > D-Drive` の各メニュー実行（`execute_menu_item`）と結果確認 | ビルド設定の変更 |
| 専用エディタ（VFX/Audio/Model）の起動・操作確認・スクリーンショット | 本番アセットの最終的な見た目・音の判断 |
| GameObject / Prefab の構成確認、単純な Prefab の組み立て（`manage_gameobject` / `manage_prefabs`） | 大規模なシーン作り替え |
| `GameData/` 配下の `*Data.asset` の値変更（**AssetBrowser / 各エディタ経由、または `manage_asset` 経由**） | — |

### 運用ルール（[12_review.md](12_review.md) §3 に準ずる）

- **`.unity` / `.prefab` / `.asset` の YAML を AI にテキスト編集させない。** 必ず MCP ツール（Unity Editor 経由）で行う。`.meta` の手作成・GUID 書き換えも禁止
- **状態の取得は resource（`mcpforunity://editor/state` 等）、変更は tool** で行う
- **Play Mode 中は書き込み系ツールを実行しない**（変更が破棄される）
- コード変更後は AI 自身が `read_console` でエラー 0 を確認し、関連テストを `run_tests` で回してから完了報告する。**接続できていないのに「Unity で確認した」と報告しない**（未検証なら未検証と書く）
- AI の変更も通常どおり PR に載せる（「AI がやった」はレビュー省略の理由にならない）
- 命名・配置規約（[10_workflow.md](10_workflow.md) §3）はツールが維持する前提のため、AI に Data を作らせる場合も **AssetBrowser の新規作成経路（`AssetCreationService`）を通す**。`CreateAssetMenu` 直叩きは開発者向けフォールバック

---

## 3. バージョンを上げるとき

1. `Packages/manifest.json` の `#vX.Y.Z` を変更（PR 必須）
2. 各自 Unity の MCP ウィンドウでサーバを **Stop → Start**（新しい `mcpforunityserver==X.Y.Z` が取得される）
3. 本ドキュメントと [CLAUDE.md](../CLAUDE.md) のバージョン表記を更新
4. 同一 PC で複数プロジェクト（MS2026 等）を運用している場合は、**全プロジェクトを同じバージョンに揃える**（サーバは PC で 1 つ）

## 4. 組み込み型サーバー `jp.shiranui-isuzu.unity-mcp` の併用（2026-09-10 評価開始）

CoplayDev 版で困っていた「別プロセスの Python サーバー」「固定ポートの衝突（8080 → 8081）」「`run_tests` が 2 回に 1 回初期化タイムアウト」「失敗テストのスタックトレースが返らない」を解消するため、[isuzu-shiranui/UnityMCP](https://github.com/isuzu-shiranui/UnityMCP)（MIT、サーバーが Editor 内に組み込み）を **CoplayDev と併用**で導入した。当面は両方を登録しておき、isuzu 版で数セッション運用して問題なければ CoplayDev を manifest から外す。

### 導入状態

- `Packages/manifest.json`: `"jp.shiranui-isuzu.unity-mcp": "https://github.com/isuzu-shiranui/UnityMCP.git?path=jp.shiranui-isuzu.unity-mcp#v4.2.0"`（タグ固定。上げるときは §3 と同じ手順）
- サーバーは Editor 起動時に自動起動（`Preferences > Unity MCP` で確認・停止・ポート固定・トークン再生成）。**ポートはプロジェクトパスから決まる**（27200〜27999。D-Drive は 27725）ので他アプリ・他プロジェクトと衝突しない
- 接続情報は `%LOCALAPPDATA%\UnityMCP\instances\<hash>.json`（port / mcpUrl / token / pid）と `tokens\<hash>.token`。Bearer トークン必須
- **クライアント登録はローカル設定に置く（リポジトリには入れない）**: トークンが入るため `.mcp.json` ではなく、各自が 1 回だけ実行する
  ```bash
  claude mcp add --transport http isuzu-unity http://127.0.0.1:27725/mcp --header "Authorization: Bearer <tokens/<hash>.token の中身>"
  ```
  （`~/.claude.json` の D-Drive プロジェクト配下に保存される。`claude mcp list` で両方 ✓ Connected になること）
- 追加後は Claude Code のセッションを開き直すとツールが載る。載っていないセッションでも HTTP 直叩き（`mcpcli2.py` 相当: `Authorization: Bearer` 付きの streamable HTTP）で使える

### ツール対応表（CoplayDev → isuzu）

| 用途 | CoplayDev v10.2.0 | isuzu v4.2.0 |
|---|---|---|
| コンパイル状態 / エラー | `read_console`（`error CS` でフィルタ） | `compile_status`（`succeeded` / `errorCount` / `messages`） |
| コンソール | `read_console` | `console_read_logs`（type=error 等）/ `console_get_count` / `console_clear` |
| 再コンパイル・再インポート | `refresh_unity` | `compile_request` / `asset_reimport` |
| テスト | `run_tests` + `get_test_job`（ジョブ ID） | `test_run`（mode=edit/play、filter=正規表現）+ `test_results`（メイン スレッド不要でポーリング可。失敗の message と stackTrace を含む） |
| C# 実行 | `execute_code`（CodeDom = C# 6 が既定） | `execute_code`（Roslyn。System/Linq/UnityEngine/UnityEditor は import 済み。`using` 不可） |
| メニュー | `execute_menu_item` | `menu_execute` |
| エディタ状態 | resource `mcpforunity://editor/state` | `play_mode_status` / `compile_status` / `scene_list` |
| その他 | `manage_*` 各種 | `gameobject_*` / `asset_*` / `prefab_*` / `inspect_*` / `material_*` / `capture_screenshot` / `editor_dialog_*` / `reflect_*` など 86 個 |

### 評価結果（2026-09-10、D-Drive で実測）

| 項目 | 結果 |
|---|---|
| インストール | manifest 追記 → 解決後にスクリプトの再コンパイルが走らなかったため、`Client.Resolve()` + `RequestScriptCompilation` を明示。以後は自動起動 |
| `execute_code` / `menu_execute` | 動作。`execute_code` は Roslyn（C# 最新）で local function も使える |
| EditMode 全件（137） | 1 回で完走、約 26 秒。初期化失敗なし |
| PlayMode 全件（303） | 1 回で完走、約 20 秒（ドメインリロード含む）。初期化失敗なし |
| 失敗テストの情報 | `test_results` が message + stackTrace を返す（ソース確認。`TestFailureLogger` は保険として残す） |
| 注意 | Roslyn DLL がパッケージに同梱されるため、CoplayDev の `execute_code` も同じプロセスで Roslyn を使うようになる（副作用は未確認だが害はない）。テスト実行中はメインスレッドが塞がるので `test_results` 以外を呼ばない |

### 切り替えの判断基準

- 3 セッション程度、`test_run` / `compile_status` / `console_read_logs` / `execute_code` / `menu_execute` で不都合が出なければ CoplayDev を外す（manifest・`.mcp.json`・CLAUDE.md §4・本書 §1〜3 を isuzu 版に書き換える。MS2026 も同時に）
- 破壊的変更が多い時期（v4.0.0 が 2026-09-04）なので、タグ固定を守り、上げるときは §3 の手順で
