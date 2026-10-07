# 1005. D-Drive MCP（v1.5.0）の人による確認手順

関連: [1002_ddrive_mcp.md](../1002_ddrive_mcp.md)（仕様・実装メモ） / [20_mcp_setup.md](../20_mcp_setup.md) / [09_editor_tools.md](../09_editor_tools.md) §15 / [../1004_tasks.md](../1004_tasks.md)（MCP-11 / MCP-12）

> MCP-1〜10 の自動検証（コンパイル 0 エラー・EditMode / PlayMode green・実機 HTTP での呼び出し）は各チケットで済んでいる。ここは **人が Unity と AI クライアントを動かして確認**する手順。
> **書式**: 各項目は「操作 → 期待 → 結果欄」。結果欄は `□ 未` を `OK` / `NG`（メモ）に書き換える。**確認用に作った Data は §3 の最後で必ず消し、`git status` に残骸（`Assets/GameData` の差分）が無いことを確認する。**
> 所要時間の目安: 約 60 分（§7 のビルドは別に 2〜3 分）。呼び出しは AI クライアントに頼んでも、isuzu の HTTP を直接叩いてもよい（`claude mcp list` で Connected なら `mcp__isuzu-unity__ddrive_*`）。

## 0. 前提

| # | 操作 | 期待 | 結果 |
|---|---|---|---|
| 0-1 | `main`（v1.5.0 候補）を取得し Unity 6000.3.13f1 で開く。Console のエラーが 0 | コンパイルエラー無し。`Packages/manifest.json` に isuzu v4.4.2 がある | OK（manifest に `#v4.4.2`。`ddrive_status.compile.ok:true`、`mcp.isuzuVersion:"4.4.2"`。Console エラーは確認中に出ず） |
| 0-2 | `pwsh Tools/Mcp/register-mcp.ps1` を実行 | 終了コード 0。`-Print` でポート・URL が出る（トークンは出ない） | OK（`-Print` のみ実行。終了コード 0、projectName=D-Drive・port 27725・mcpUrl が出てトークンは出ない。`claude mcp add` を走らせる通常実行はしていない） |
| 0-3 | Claude Code を再起動し `claude mcp list` | `isuzu-unity ✓ Connected` | NG（メモ: この環境で `claude mcp list` は `UnityMCP: http://127.0.0.1:8081/mcp ✘ Failed to connect — ECONNREFUSED` の 1 行のみで `isuzu-unity` が未登録。旧 CoplayDev 設定が残っている様子。isuzu 自体は HTTP 直叩きで接続でき、`register-mcp.ps1`（`-Print` なし）は未実行。再起動後の Connected 確認は要人確認） |

## 1. 接続と状態

| # | 操作 | 期待 | 結果 |
|---|---|---|---|
| 1-1 | `ddrive_status {}` | `version` / `compile.ok:true` / `validation` / `migration.pending` / `addressables` / `mcp` が返る（Validator は走らない） | OK（`version:1.5.0`/`compile.ok:true`/`validation`/`migration.pending:0`/`addressables.missing:0`/`mcp` が返る。Validator は走らず `validation:{cached:false}`。`tests.last` も付く） |
| 1-2 | `ddrive_status.mcp.port` と `%LOCALAPPDATA%\UnityMCP\instances\<hash>.json` の `port`（`projectPath` が D-Drive のもの）を比べる | 一致。`portMismatch:false`、`fixedPort:false` | OK（status port 27725 = 記述子 `a26b71fdfd662823.json`（projectPath=D-Drive/Assets）の port 27725。`portMismatch`・`fixedPort` はキーごと出ない＝false 扱い） |
| 1-3 | `ddrive_status.mcp.writeEnabled` と Project Settings > D-Drive > MCP のチェックを比べる | 一致（開発リポジトリは ON） | 要人確認（`writeEnabled:true`。Project Settings の UI チェックは見ていない。設定の実体 `McpAllowWrite` は 8-1 で ON/OFF 切替と連動を確認済み） |

## 2. 案内（`ddrive_help`）

| # | 操作 | 期待 | 結果 |
|---|---|---|---|
| 2-1 | `topic=rules` / `types` / `menu` を順に呼ぶ | `rules` = 禁止事項 10 行 + MCP の使い方、`types` = 18 種別の表、`menu` = `Tools/D-Drive/…` のパス一覧 | OK（rules=10 項目+MCP の行、types=18 種別の表、menu=`Tools/D-Drive/…` のパス一覧） |
| 2-2 | `topic=tool:ddrive_asset_set`、`topic=validation:DD-ADDR-CATALOG-MISSING` | 引数の一覧 / その Code の意味と直し方 | OK（`tool:ddrive_asset_set` は引数一覧、`validation:DD-ADDR-CATALOG-MISSING` は意味と直し方） |
| 2-3 | `topic=xxx`（存在しない） | `invalid_params` と topic の一覧 | OK（`invalid_params`、msg に使える topic: rules, types, menu, tool:<name>, validation:<code>） |

## 3. Data（作成 → 変更 → 戻す → 削除）

| # | 操作 | 期待 | 結果 |
|---|---|---|---|
| 3-1 | `ddrive_asset_list {type:"Se", limit:5}` と `ddrive_asset_get`（先頭の id、`fields:"*"`） | 5 件以内 + `total`。`id` は文字列。get は欄の値と `validation` | OK（list は 2 件+`total:2`、id は文字列。get は欄の値と `validation`。`fields:"*"` は m_* や Flags など欄が全部出る） |
| 3-2 | `ddrive_asset_create {type:"Se", name:"McpVerify", category:"Test", preview:true}` | `wouldCreate` と `identifier` だけ。ファイルは増えない | OK（`wouldCreate:"Assets/GameData/Audio/SE/Test/SE_Test_McpVerify.asset"` と `identifier` のみ。ファイルは増えず）。気づき: 実作成の path は既存カテゴリ `test`（小文字）に寄って `…/SE/test/SE_Test_McpVerify.asset` になり、preview の `Test` と食い違う |
| 3-3 | 同じ引数で `preview` を外して実行 | `id` / `path` / `addressable:true`。AssetBrowser の `Test` カテゴリに現れる | OK（`id:14899531262222843603`、path `Assets/GameData/Audio/SE/test/SE_Test_McpVerify.asset`、`addressable:true`、`validation:{errors:1,warnings:1}`。`ddrive_asset_list` で category `Test` として現れる。AssetBrowser の目視はしていない） |
| 3-4 | `ddrive_asset_set {type:"Se", id, fields:{"Description":"mcp"}}` | `changed:[{field:"Description",...}]`。Inspector の Description が変わり、ChangeNote が `[mcp] …` で始まる | OK（`changed:[{field:"Description",from:"",to:"mcp"}]`。get で Description=`mcp`、ChangeNote=`[mcp] 作成`＝`[mcp] ` で始まる。set で ChangeNote が書き換わるかは作成時の値のため不明。Inspector 目視なし） |
| 3-5 | Unity で `Ctrl+Z` | Description が元に戻る（Undo できる） | 要人確認（代用: `execute_code` で `Undo.PerformUndo()` → Undo グループ名 `D-Drive MCP: Data の変更`、直後の get で Description が `""` に戻った。Ctrl+Z キー操作は未実施） |
| 3-6 | `ddrive_asset_set` で `fields:{"Id":"1"}` | `read_only_field`。何も変わらない | OK（`read_only_field`「'Id' は読み取り専用です」。get で Id は変わらず） |
| 3-7 | `ddrive_asset_delete {type, id, preview:true, confirm:true}` | `wouldDelete`、参照が無ければ `blockerCount:0`。ファイルは残る | OK（`{path, wouldDelete}` のみでファイルは残る。`blockerCount` キーは出ない＝0 のとき省略の様子） |
| 3-8 | `confirm` を付けずに `ddrive_asset_delete` | `confirmation_required`。削除されない | OK（`Error [confirmation_required]: 'ddrive_asset_delete' is destructive. Pass confirm=true…`。ファイル残存を確認） |
| 3-9 | `preview` なし・`confirm:true` で削除 | `deleted:true`。AssetBrowser から消え、`Assets/GameData` に差分が残らない（`git status`） | OK（`deleted:true`、ファイルと .meta が消え list から消えた）。気づき: `git status` に `Assets/AddressableAssetsData/AssetGroups/DDrive_GameData.asset` と `Assets/GameData/Catalogs/AudioCatalog.asset` が M で残った（内容差分なし・改行 LF/CRLF のみ。`git diff --numstat` は空）。`git checkout --` で戻し、クリーンを確認（Unity が作成/削除時に LF で書き直す） |

## 4. 検査

| # | 操作 | 期待 | 結果 |
|---|---|---|---|
| 4-1 | `ddrive_validate {scope:"all"}` | `errors` / `warnings` / `infos` と `byCode` の表（Code 無しは `(none)`）。続けて `ddrive_status.validation` が同じ件数 | OK（`errors:0 / warnings:22 / infos:35`、`byCode` は全て `(none)`（sev 別）。続く `ddrive_status.validation` も 0/22/35）。気づき: `ddrive_validate` は Data 単位の検査のみで `ProjectSetupValidator` 等のプロジェクト全体の検査（`DD-MCP-*`）は含まない様子 |
| 4-2 | `ddrive_validate {scope:"all", detail:"errors", limit:5}` | Error の `items`（`msg` は短い）。`truncated` / `next` が必要なときだけ付く | OK（Error 0 件のため `items:[]`、`truncated`/`next` なし。Error の `msg` 短縮は未確認。`detail:"warnings"` は `invalid_params`（summary/errors/all のみ）） |
| 4-3 | `ddrive_validate_fix {preview:true, confirm:true}` | 直せる Code の件数だけ（`wouldApply`）。何も変わらない。直せるものが無ければ空 | OK（`wouldApply:[]`。直せるものなし） |
| 4-4 | `ddrive_forbidden_api {limit:10}` | `violations:0`（クリーンなとき）と `byRule` | OK（`violations:0`、`notices:1`、`byRule:[{DD-FORBIDDEN-ALLOW-SUMMARY,1}]`、root=`Packages/com.ddrive.core`） |

## 5. 生成・更新

| # | 操作 | 期待 | 結果 |
|---|---|---|---|
| 5-1 | `ddrive_generate {kind:"ids", preview:true}` | 書き込まない（`Assets/Generated/AssetIds.g.cs` の更新時刻が変わらない） | OK（`summary:{wouldWrite:"Assets/Generated/AssetIds.g.cs",exists:true}`。更新時刻 2026-10-07 23:02:18 が前後で不変） |
| 5-2 | `ddrive_generate {kind:"deps"}` → `ddrive_asset_usages` / `unused` | `summary` に件数。以後 `needsRebuild` が出ない | OK（`deps`: `changed:true`、`summary:{files:99,edges:68,seconds:1.4}`。`ddrive_asset_usages`（count 1）/`ddrive_asset_unused`（count 33）に `needsRebuild` は出ず） |
| 5-3 | `ddrive_migrate {mode:"plan", confirm:true}` | `pending:[]`、`count:0`（クリーンなとき） | OK（`pending:[]`、`count:0`） |
| 5-4 | `ddrive_compat {}` | `ok:true`、`changed:[]` | OK（`ok:true`、`changed:[]`） |
| 5-5 | `ddrive_release_check {guard_only:true}` | `ok` と CHANGELOG ガードの 1 件。`pwsh` が無い環境は `exception` で分かる | OK（`ok:true`、checks 1 件「CHANGELOG ガード … 対象外です」） |

## 6. プレビュー

| # | 操作 | 期待 | 結果 |
|---|---|---|---|
| 6-1 | シーンを未保存（変更ありの状態）にして `ddrive_preview {action:"open", scene:"common"}` | `{blocked:"unsaved scene", scenes:[…]}`。保存ダイアログは出ない | OK（`execute_code` で `MarkSceneDirty` 後 → `{blocked:"unsaved scene",scenes:["(untitled)"]}`、ダイアログなし）。注: 開いていたのは無題シーンで `SaveOpenScenes` は保存ダイアログが出るため、代わりに `NewScene` で空シーンに替えて dirty を解消した（git 差分なし） |
| 6-2 | シーンを保存して同じ呼び出し | 確認用シーンが開く。`ddrive_preview {action:"status"}` で `scene` / `previewScene` が分かる | OK（`scene:"Assets/GameData/PreviewScenes/PreviewScene.unity"` が開き、status は `previewScene:true`、`playing:[]`、`playMode:false`） |
| 6-3 | `action:"play", type:"Se", id:<音が設定済みの SE>` → 音が鳴る → `action:"stop", handle` | 実際に鳴り、止まる。`status.playing` に載って消える | 要人確認（`play` → `{handle:"1",ok:true}`、status.playing に載り、`stop` → `{stopped:1}` で空に戻った。実際に音が鳴ったかは未確認） |
| 6-4 | `action:"open", scene:"common", type:"Vfx", id:<VFX>` | `[D-Drive] VFX Preview` に VFX が出る（SceneView が寄る） | 要人確認（`{placed:"[D-Drive] VFX Preview",handle:"2"}`、シーンに `[D-Drive] VFX Preview`（子 1・active）を確認。SceneView が寄るかの目視は未確認） |
| 6-5 | `action:"stop_all"` → `action:"sweep"` | `stopped` の件数。`sweep` は孤児だけ消し、シーン内のプレビューは残す | OK（`stop_all` → `{stopped:1}`、`sweep` → `{destroyed:0}`。`[D-Drive] VFX Preview` はシーンに残った） |
| 6-6 | `action:"play", type:"Canvas", id:…` | `invalid_params`（対応種別の列挙） | OK（`invalid_params`「play は Se / Bgm / Vfx / Presentation だけです(Canvas は専用エディタで)」） |

## 7. ビルド

| # | 操作 | 期待 | 結果 |
|---|---|---|---|
| 7-1 | `ddrive_build_netcheck {development:true}`（約 75 秒。isuzu が `job_status` の jobId を返す） | `success:true`、`exe` / `zip`（プロジェクト相対）。`Builds/DDriveNetCheck` の中身が新しい(`DDriveNetCheck_Data/Managed/*.dll` など。`DDriveNetCheck.exe` 本体は Unity のプレイヤー雛形で、内容が同じなら書き直されないため mtime は古いままでよい) | OK（`job_status` が completed、`success:true`、exe=`Builds/DDriveNetCheck/DDriveNetCheck.exe`、zip=`Builds/DDriveNetCheck.zip`、63.2 秒。zip の更新時刻・zip 内 exe は 2026-10-08 00:07 で新しい。ただしディスク上の exe の mtime は 2026-09-14 のまま（Unity が元の更新時刻で書く様子）） |

## 8. 設定

| # | 操作 | 期待 | 結果 |
|---|---|---|---|
| 8-1 | Project Settings > D-Drive > MCP の書き込み許可を OFF → `ddrive_asset_create`（`preview` なし）と `ddrive_preview {action:"play"}` | どちらも `write_disabled`。`ddrive_asset_list` / `ddrive_status` / `ddrive_preview {action:"status"}` は動く。確認後に ON へ戻す | OK（`DDrive.Editor.Settings.DDriveProjectSettings.instance.SetMcpAllowWrite(false,false)` で OFF → `ddrive_asset_create`・`ddrive_preview play` とも `write_disabled`。`ddrive_asset_list`/`ddrive_status`/`ddrive_preview status` は動く。ON へ戻し `writeEnabled:true` を確認）。注: 1 回目は名前空間を誤り OFF にできないまま create/play が成功したため、作った `McpVerify2` は削除・再生は停止済み |
| 8-2 | `Tools > D-Drive > Setup > セットアップウィザード` の「4. 既定フォルダ・設定の生成」 | 「AI（MCP）の書き込みツールを有効にする」のチェックが 8-1 と連動して見える | 要人確認（ウィザードの目視は未実施。コード上 `ProjectSetupWizardWindow.cs:282` のトグルは `settings.McpAllowWrite` を読み書きするので連動する。期待欄の文言は実ラベルに合わせた 2026-10-08） |
| 8-3 | `Preferences > Unity MCP` で HTTP ポートを固定（導出ポートと別の値）→ 少し待つ → `ddrive_status.mcp` と `Validation > Run All` | `fixedPort:true` + `warning`、Validation に Info `DD-MCP-FIXED-PORT`。確認後にポートの固定を解除（0 / 自動）し、`register-mcp.ps1` を再実行して繋がること | 要人確認（代用: ポートを実際には変えず、`UnityMCP.Editor.Settings.McpSettings.instance.httpPort=31777`（未保存・サーバー再起動なし）にして `ddrive_status.mcp` に `fixedPort:true` と `warning`、0 に戻すとキーが消えることを確認。`DD-MCP-FIXED-PORT` の Info は `ddrive_validate`（Data 検査のみ）に出ず `Validation > Run All` が要人確認。実ポート変更と `register-mcp.ps1` 再実行は未実施。httpPort は 0 に戻し済み） |

## 9. 持ち込み先（MS2026）

> MCP-14（2026-10-07）で、isuzu の導入は更新ウィンドウに統合した（[1002](../1002_ddrive_mcp.md) §11）。manifest.json を手で編集せず、更新ウィンドウから導入する。
>
> **2026-10-08: §9 はユーザー判断でスキップ**（MS2026 は isuzu 導入済み・UI ダイアログは AI から駆動できないため。必要になったら実施）。

| # | 操作 | 期待 | 結果 |
|---|---|---|---|
| 9-1 | isuzu を入れていない MS2026（または空プロジェクト）に v1.5.x を導入 | コンパイルエラー無し。`DDrive.Editor.Mcp` アセンブリが存在しない（`Library/ScriptAssemblies` に無い）。`Tools > D-Drive > Update > 更新ウィンドウ` の「パッケージ」に「Unity MCP（isuzu）— D-Drive の AI 連携に必要」と「導入」ボタンが出る | □ 未 |
| 9-2 | 「導入」を押す（他の MCP は無い状態） | ダイアログ無しで `manifest.json` に `jp.shiranui-isuzu.unity-mcp` が `#v4.4.2` 付きで 1 行だけ増える。一覧に管理対象として出る。導入後の案内パネル（解決待ち・登録スクリプト・書き込み設定）が出る | □ 未 |
| 9-3 | 案内パネルの「登録スクリプトを実行」→ `ddrive_status` | Unity の解決後に実行すると「登録しました」。`ddrive_status` の `mcp.project` が MS2026、`mcp.port` が D-Drive と違う、`mcp.isuzuVersion` が `4.4.2`、`otherMcp` が無い。`DDrive.Editor.Mcp` がコンパイルされる。解決前なら失敗の理由（記述子なし）が出て、再実行できる | □ 未 |
| 9-4 | 「書き込みツールの設定を開く」 | Project Settings > D-Drive > MCP が開く（既定 OFF） | □ 未 |
| 9-5 | 導入済みで更新ウィンドウを開く | 行は「導入済み（v4.4.2、推奨 v4.4.2）」の表示だけで「導入」ボタンは出ない | □ 未 |
| 9-6 | CoplayDev（`com.coplaydev.unity-mcp`）を入れた状態で isuzu 未導入 →「導入」 | 確認ダイアログ「続行（両方残す）/ CoplayDev … を外して続行 / キャンセル」。「キャンセル」で manifest は変わらない | □ 未 |
| 9-7 | 9-6 で「外して続行」 | manifest から `com.coplaydev.unity-mcp` の 1 行だけが消え isuzu が増える。`.mcp.json` は変わらず、案内パネルに「手で消してください」が出る | □ 未 |
| 9-8 | 9-6 で「続行（両方残す）」→ `Validation > Run All` | Info `DD-MCP-MULTIPLE` が出る。`ddrive_status.mcp.otherMcp` に CoplayDev の id | □ 未 |
| 9-9 | 未知の MCP（例 `com.foo.mcp-bridge`）を入れて「導入」 | 「外して続行」は出ず 2 択（続行 / キャンセル）。未知のものは manifest に残る | □ 未 |
| 9-10 | isuzu を古いタグ（例 `#v4.2.0`）にして Run All | Info `DD-MCP-ISUZU-OUTDATED`。`#main` やコミットにすると出ない | □ 未 |
| 9-11 | セットアップウィザードの「9. AI 連携（MCP、任意）」 | 既定 OFF。ON +「適用」で 9-2 と同じ処理（他の MCP があれば同じダイアログ） | □ 未 |

## 10. トークン

| # | 操作 | 期待 | 結果 |
|---|---|---|---|
| 10-1 | `python Tools/Mcp/measure-tokens.py`（`--write-doc` は付けない） | [1002](../1002_ddrive_mcp.md) §10 の表と同程度（呼んだツールの定義だけなら前の 0.12〜0.31、定義 20 個込みなら 2.3〜3.5 倍） | OK（`measure-tokens.py` を `--write-doc` なしで実行。定義込み 2.33〜3.52 倍、呼んだ分だけ 0.11〜0.31、`ddrive_*` 20 個 3425 トークンで docs/1002 §10 と同程度。S1 は 42271 vs 1002 の 42224 とほぼ同じ） |

## 確認の記録

| 項目 | 内容 |
|---|---|
| 確認日・確認者 | 2026-10-08・Claude（Sonnet、HTTP 直叩き）。§0〜§8・§10。§9 は別担当 |
| 環境（PC・ブランチ・Unity） | main（コミット 0cf83a6）・Unity 6000.3.13f1・isuzu 4.4.2 |
| NG・気づいたこと | NG: 0-3（`claude mcp list` に isuzu-unity が無く、旧 UnityMCP:8081 が ECONNREFUSED）。要人確認: 1-3, 3-5, 6-3, 6-4, 8-2, 8-3。気づき: 3-2 の preview path（`Test`）と実作成 path（`test`）の差、3-9 後に GameData/Addressables 2 ファイルが改行差分のみで M になる、`ddrive_validate` が `DD-MCP-*` を含まない、8-2 のラベル文言が表と違う |
