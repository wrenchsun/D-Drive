<#
.SYNOPSIS
  bump-version.ps1 の事前チェック + CHANGELOG ガード([42_distribution.md] §5.11-10)だけを実行する
  検査専用スクリプト。ファイルは一切書き換えない。CI(Tools/CI/run-ci.cmd)からも呼べる。

.DESCRIPTION
  通常実行(引数無し)では以下をすべて検査する:
    - 作業ツリーがクリーン(git status --porcelain が空。-SkipCleanCheck で省略可)
    - CHANGELOG.md の "## [Unreleased]" に "### 互換性" 節があり、空でない
    - DDriveProtocol.Current を変えている場合、CHANGELOG の互換性節に「ネットメッセージ」の記述がある
    - CHANGELOG ガード: `git diff --name-only <Base>..HEAD` で
      Packages/com.ddrive.core/Tests/Editor/Compat/Snapshots/** が変わっているのに
      CHANGELOG.md が変わっていなければ fail。version が上がっていない場合も fail

  -GuardOnly を付けると、CI(Tools/CI/run-ci.cmd)から呼ぶ想定で CHANGELOG ガードだけを実行する
  (他のチェックは bump-version.ps1 実行時にしか意味を持たないため省略する)。

.PARAMETER Base
  CHANGELOG ガードの比較対象。既定は "origin/main"。

.PARAMETER SkipCleanCheck
  作業ツリーのクリーンチェックを省略する(実装作業の途中で確認だけしたいとき用)。

.PARAMETER GuardOnly
  CHANGELOG ガードだけを実行する(Tools/CI/run-ci.cmd から呼ぶときに使う)。

.EXAMPLE
  pwsh Tools/Release/check-release.ps1

.EXAMPLE
  pwsh Tools/Release/check-release.ps1 -GuardOnly -Base origin/main
#>

param(
    [string]$Base = 'origin/main',
    [switch]$SkipCleanCheck,
    [switch]$GuardOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseChecks.ps1')

$repoRoot = Get-DDriveRepoRoot -ScriptRoot $PSScriptRoot
$packageJsonPath = Join-Path $repoRoot 'Packages/com.ddrive.core/package.json'
$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'
$protocolCsRelativePath = 'Packages/com.ddrive.core/Foundation/Net/DDriveProtocol.cs'

Write-Host '=== D-Drive check-release ==='
Write-Host "比較対象(Base): $Base"
Write-Host ''

if ($GuardOnly) {
    $guardCheck = Test-ChangelogGuard -RepoRoot $repoRoot -BaseRef $Base
    $mark = '[OK]'
    if (-not $guardCheck.Ok) { $mark = '[FAIL]' }
    Write-Host "$mark CHANGELOG ガード([42_distribution.md] §5.11-10): $($guardCheck.Message)"
    Write-Host ''
    if (-not $guardCheck.Ok) {
        Write-Host '=== check-release(-GuardOnly): 失敗 ==='
        exit 1
    }
    Write-Host '=== check-release(-GuardOnly): green です ==='
    exit 0
}

$checks = New-Object System.Collections.Generic.List[object]

if (-not $SkipCleanCheck) {
    $cleanTree = Test-WorkingTreeClean -RepoRoot $repoRoot
    $cleanMessage = 'OK'
    if (-not $cleanTree) { $cleanMessage = 'git status --porcelain に差分があります。リリース手順(docs/12_review.md §7)ではコミット済みの状態で実行してください。' }
    $checks.Add([pscustomobject]@{ Name = '作業ツリーがクリーン'; Ok = $cleanTree; Message = $cleanMessage })
}
else {
    Write-Host '(-SkipCleanCheck: 作業ツリーのクリーンチェックを省略しました)'
}

$compatCheck = Test-UnreleasedCompatibilityNonEmpty -ChangelogPath $changelogPath
$checks.Add([pscustomobject]@{ Name = '[Unreleased] の互換性節'; Ok = $compatCheck.Ok; Message = $compatCheck.Message })

$protoRef = $null
try {
    $describeOutput = & git -C $repoRoot describe --tags --abbrev=0 2>&1
    if ($LASTEXITCODE -eq 0) { $protoRef = ($describeOutput | Select-Object -First 1) }
}
catch {
    $protoRef = $null
}
$protoCheck = Test-ProtocolVersionChangeNoted -RepoRoot $repoRoot -ProtocolCsRelativePath $protocolCsRelativePath -ChangelogPath $changelogPath -CompareRef $protoRef
$checks.Add([pscustomobject]@{ Name = 'ネットメッセージ版(ProtocolVersion)の変更記録'; Ok = $protoCheck.Ok; Message = $protoCheck.Message })

$guardCheck = Test-ChangelogGuard -RepoRoot $repoRoot -BaseRef $Base
$checks.Add([pscustomobject]@{ Name = 'CHANGELOG ガード([42_distribution.md] §5.11-10)'; Ok = $guardCheck.Ok; Message = $guardCheck.Message })

$anyFailed = $false
foreach ($c in $checks) {
    $mark = '[OK]'
    if (-not $c.Ok) { $mark = '[FAIL]'; $anyFailed = $true }
    Write-Host "$mark $($c.Name): $($c.Message)"
}
Write-Host ''

if ($anyFailed) {
    Write-Host '=== check-release: 失敗があります ==='
    exit 1
}
Write-Host '=== check-release: すべて green です ==='
exit 0
