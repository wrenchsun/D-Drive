# Tools/Mcp

isuzu 版 Unity MCP を Claude Code に登録する道具。

## register-mcp.ps1

Unity Editor が書く記述子（`%LOCALAPPDATA%\UnityMCP\instances\<hash>.json`）を読み、`pid` の生存を確認してから `claude mcp remove` → `claude mcp add --transport http` を実行する（リポジトリ直下で実行するので `~/.claude.json` のプロジェクト配下に入る）。

```
pwsh Tools/Mcp/register-mcp.ps1                 # 登録(上書き)
pwsh Tools/Mcp/register-mcp.ps1 -Print          # 接続情報の表示のみ(トークンは出さない)
pwsh Tools/Mcp/register-mcp.ps1 -DryRun         # 実行するコマンドをトークン伏せ字で表示
pwsh Tools/Mcp/register-mcp.ps1 -ProjectPath D:\work\MS2026   # 別プロジェクト
```

その他のオプション: `-Name`（既定 `isuzu-unity`）、`-Scope`（`claude mcp add --scope` に渡す）。

| 終了コード | 意味 |
|---|---|
| 0 | 成功 |
| 2 | 記述子が無い（Unity Editor でプロジェクトを開いてから） |
| 3 | 記述子の pid が終了済み（Unity を起動し直してから） |
| 4 | `claude` が PATH に無い（手動コマンドを表示） |
| 5 | `claude mcp add` 失敗 |

ハッシュ規則: `Application.dataPath`（`/` 区切り・末尾スラッシュ無し）の UTF-8 を SHA256 し、先頭 8 バイトを小文字 16 進 16 文字にする（isuzu `McpInstanceDescriptor.HashProjectPath` と同じ）。

## 規則

**ポート・トークンはリポジトリに書かない。** `.mcp.json` にも置かない。接続先は常に記述子の `mcpUrl` から読む。

参照: [docs/20_mcp_setup.md](../../docs/20_mcp_setup.md) §1、[docs/1002_ddrive_mcp.md](../../docs/1002_ddrive_mcp.md) §6.2

## measure-tokens.py

MCP のトークン計測（[docs/1002](../../docs/1002_ddrive_mcp.md) §5.5 / §10）。起動済みの isuzu サーバーに接続し、`tools/list` の定義の文字数と、`scenarios.json` の代表 5 シナリオ（読み取り / `preview:true` だけ）の引数 + 返り値の文字数を測る。トークンは文字数 ÷ 3 の近似。「前」は `scenarios.json` の見積もり（実測ではない）。

```
python Tools/Mcp/measure-tokens.py               # 表を標準出力へ
python Tools/Mcp/measure-tokens.py --write-doc   # docs/1002 §10 の表を置き換える
python Tools/Mcp/measure-tokens.py --project C:/work/MS2026   # 別プロジェクトの記述子
```

サーバーが忙しいときは 10 秒おき最大 5 回再試行する（標準エラーに `[retry n/5]` を出す）。Python 3.8 以上・標準ライブラリのみ。Unity は起動しない。
