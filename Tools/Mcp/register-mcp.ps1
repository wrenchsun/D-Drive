<#
.SYNOPSIS
  isuzu 版 Unity MCP の記述子(%LOCALAPPDATA%\UnityMCP\instances\<hash>.json)を読み、
  Claude Code に claude mcp add で登録(上書き)する。ポートもトークンもリポジトリには書かない。

.DESCRIPTION
  ハッシュ規則(isuzu v4.2.0 McpInstanceDescriptor.HashProjectPath と同じ):
    SHA256( UTF-8( Application.dataPath ) ) の先頭 8 バイトを小文字 16 進 16 文字にしたもの。
    Application.dataPath は「/ 区切り・末尾スラッシュ無し・大小文字そのまま」(例 C:/Users/x/D-Drive/Assets)。
  ハッシュで見つからないときは instances\*.json の projectPath が一致するものを探す(警告付き)。

  終了コード: 0 成功 / 2 記述子なし / 3 pid 死亡 / 4 claude が PATH に無い / 5 claude mcp add 失敗

.PARAMETER ProjectPath
  プロジェクトのルート(既定: このスクリプトの ..\..)。

.PARAMETER Name
  登録するサーバー名(既定 isuzu-unity)。

.PARAMETER Scope
  claude mcp add の --scope に渡す(local / project / user)。省略時は claude の既定。

.PARAMETER Print
  接続情報(トークン以外)を表示して終了する。

.PARAMETER DryRun
  実行するコマンドをトークン伏せ字で表示するだけで、何も実行しない。

.EXAMPLE
  pwsh Tools/Mcp/register-mcp.ps1
.EXAMPLE
  pwsh Tools/Mcp/register-mcp.ps1 -Print
#>
param(
    [string]$ProjectPath,
    [string]$Name = 'isuzu-unity',
    [string]$Scope,
    [switch]$Print,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not $ProjectPath) { $ProjectPath = Join-Path $PSScriptRoot '..\..' }
$root = (Resolve-Path -LiteralPath $ProjectPath).Path.TrimEnd('\', '/')
$dataPath = ($root -replace '\\', '/') + '/Assets'

function Get-DescriptorHash([string]$text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($text)) } finally { $sha.Dispose() }
    return (($bytes[0..7] | ForEach-Object { $_.ToString('x2') }) -join '')
}

$baseDir = $env:LOCALAPPDATA
if (-not $baseDir) { $baseDir = Join-Path $HOME '.local/share' }
$instDir = Join-Path (Join-Path $baseDir 'UnityMCP') 'instances'
$hash = Get-DescriptorHash $dataPath
$file = Join-Path $instDir "$hash.json"

if (-not (Test-Path -LiteralPath $file)) {
    $found = $null
    if (Test-Path -LiteralPath $instDir) {
        foreach ($f in Get-ChildItem -LiteralPath $instDir -Filter '*.json') {
            try { $j = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json } catch { continue }
            if ($j.projectPath -and ([string]$j.projectPath).TrimEnd('/') -ieq $dataPath) { $found = $f.FullName; break }
        }
    }
    if (-not $found) {
        Write-Host '記述子が無い。Unity Editor でこのプロジェクトを開いてから実行'
        Write-Host "  探した場所: $file (dataPath = $dataPath)"
        exit 2
    }
    Write-Warning "ハッシュ $hash の記述子は無いが projectPath 一致の別ファイルがある。isuzu のハッシュ規則が変わった可能性。McpPortPolicyTests を確認すること。"
    $file = $found
}

$d = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json

$alive = $false
if ($d.pid) { $alive = [bool](Get-Process -Id ([int]$d.pid) -ErrorAction SilentlyContinue) }
if (-not $alive) {
    Write-Host '古い記述子（pid が終了済み）。Unity を起動し直してから実行'
    exit 3
}

Write-Host "projectName   : $($d.projectName)"
Write-Host "projectPath   : $($d.projectPath)"
Write-Host "port          : $($d.port)"
Write-Host "preferredPort : $($d.preferredPort)"
Write-Host "portMismatch  : $($d.portMismatch)"
Write-Host "pid           : $($d.pid)"
Write-Host "mcpUrl        : $($d.mcpUrl)"
if ($d.portMismatch) {
    Write-Warning '希望ポートが使われていたため別ポートで起動している(portMismatch)。接続先は常に mcpUrl を使うこと。'
}
if ($Print) { exit 0 }

$mcpUrl = [string]$d.mcpUrl
$token = [string]$d.token
$scopeArgs = @()
if ($Scope) { $scopeArgs = @('--scope', $Scope) }
$masked = '<token>'

if ($DryRun) {
    Write-Host "[DryRun] claude mcp remove $($scopeArgs -join ' ') $Name"
    Write-Host "[DryRun] claude mcp add --transport http $($scopeArgs -join ' ') $Name $mcpUrl --header `"Authorization: Bearer $masked`""
    Write-Host "         (トークンは $file の token。表示しない)"
    exit 0
}

if (-not (Get-Command claude -ErrorAction SilentlyContinue)) {
    Write-Host 'claude が PATH に無い。手動で実行する:'
    Write-Host "  claude mcp add --transport http $Name $mcpUrl --header `"Authorization: Bearer $masked`""
    Write-Host "  トークンは $file の token(JSON)。"
    exit 4
}

Push-Location $root
try {
    & claude mcp remove @scopeArgs $Name 2>&1 | Out-Null
    $global:LASTEXITCODE = 0
    $out = & claude mcp add --transport http @scopeArgs $Name $mcpUrl --header "Authorization: Bearer $token" 2>&1
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        $msg = ($out | Out-String).Replace($token, '***')
        Write-Host "claude mcp add が失敗 (exit $code)"
        Write-Host $msg
        exit 5
    }
}
finally { Pop-Location }

Write-Host "登録した: $Name -> $mcpUrl (トークンは表示しない)。Claude Code のセッションを開き直すと mcp__${Name}__* が載る。"
exit 0
