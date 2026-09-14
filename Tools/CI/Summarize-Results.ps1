# D-Drive CI: 各種テスト結果 XML を人が読めるサマリ(Markdown)にまとめる。
# GitHub Actions からは $env:GITHUB_STEP_SUMMARY に追記され、PR の Summary タブに出る。
# ローカル実行(Tools/CI/run-ci.cmd)では標準出力にそのまま表示するだけ(GITHUB_STEP_SUMMARY は未設定のため追記しない)。
#
# 入力:
#   -ValidationJUnitPath : CI.ValidateAll が書き出す独自 JUnit 形式(testsuite/testcase)
#   -EditModeResultsPath / -PlayModeResultsPath : Unity Test Framework が書き出す NUnit3 形式(test-run/test-suite/test-case)
#   -PerformanceResultsPath : 6-2(DDrive.Tests.Performance、-testCategory Performance で絞った PlayMode 実行)の結果。同じ NUnit3 形式
#
# どのファイルも「無ければスキップ」(そのステップ自体が実行されなかった/失敗した場合でも、このスクリプトは落ちない)。

param(
    [string]$ValidationJUnitPath = "TestResults/ddrive-validation.junit.xml",
    [string]$EditModeResultsPath = "TestResults/editmode-results.xml",
    [string]$PlayModeResultsPath = "TestResults/playmode-results.xml",
    # 6-2: DDrive.Tests.Performance(category=Performance)の結果。省略時はスキップ(そのステップ自体を
    # 実行しない run-ci.cmd の古い呼び出し・CI 側でも壊れないようにする)。
    [string]$PerformanceResultsPath = "TestResults/performance-results.xml",
    # 6-7: Tools/CI/Run-NetCheck.ps1 が書き出す機械可読な要約(シナリオごとの pass/fail)。省略時はスキップ
    # (run-ci.cmd から任意ステップとして呼ばれなかった/ビルド済み exe が無くスキップされた場合でも
    # このスクリプト自体は落ちない)。
    [string]$NetCheckResultsPath = "TestResults/NetCheck/results.json"
)

$ErrorActionPreference = "Stop"

function Read-NUnitSummary {
    param([string]$Path, [string]$Label)

    if (-not (Test-Path $Path)) {
        return [pscustomobject]@{ Label = $Label; Found = $false; Total = 0; Passed = 0; Failed = 0; FailedNames = @() }
    }

    [xml]$xml = Get-Content -Raw -Path $Path
    $root = $xml.SelectSingleNode("//test-run")
    if ($null -eq $root) {
        # -runTests が異常終了して不完全な XML しか残らなかったケース。件数不明として扱う。
        return [pscustomobject]@{ Label = $Label; Found = $true; Total = -1; Passed = -1; Failed = -1; FailedNames = @() }
    }

    $failedNames = @()
    foreach ($node in $xml.SelectNodes("//test-case[@result='Failed']")) {
        $failedNames += $node.GetAttribute("fullname")
    }

    [pscustomobject]@{
        Label       = $Label
        Found       = $true
        Total       = [int]$root.GetAttribute("total")
        Passed      = [int]$root.GetAttribute("passed")
        Failed      = [int]$root.GetAttribute("failed")
        FailedNames = $failedNames
    }
}

function Read-DDriveValidationSummary {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return [pscustomobject]@{ Found = $false; Tests = 0; Failures = 0; FailedEntries = @() }
    }

    [xml]$xml = Get-Content -Raw -Path $Path
    $suite = $xml.SelectSingleNode("//testsuite")
    if ($null -eq $suite) {
        return [pscustomobject]@{ Found = $true; Tests = -1; Failures = -1; FailedEntries = @() }
    }

    $failedEntries = @()
    foreach ($node in $xml.SelectNodes("//testcase[failure]")) {
        $classname = $node.GetAttribute("classname")
        $name = $node.GetAttribute("name")
        $failedEntries += "${classname}: ${name}"
    }

    [pscustomobject]@{
        Found         = $true
        Tests         = [int]$suite.GetAttribute("tests")
        Failures      = [int]$suite.GetAttribute("failures")
        FailedEntries = $failedEntries
    }
}

function Read-NetCheckSummary {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return [pscustomobject]@{ Found = $false; Total = 0; Passed = 0; Failed = 0; FailedNames = @() }
    }

    $items = Get-Content -Raw -Path $Path | ConvertFrom-Json
    if ($null -eq $items) {
        $items = @()
    }

    $failedNames = @($items | Where-Object { -not $_.pass } | ForEach-Object { "$($_.scenario) (host=$($_.hostReason) client=$($_.clientReason) phase=$($_.phaseReason))" })

    [pscustomobject]@{
        Found       = $true
        Total       = @($items).Count
        Passed      = @($items | Where-Object { $_.pass }).Count
        Failed      = $failedNames.Count
        FailedNames = $failedNames
    }
}

$validation = Read-DDriveValidationSummary -Path $ValidationJUnitPath
$editMode = Read-NUnitSummary -Path $EditModeResultsPath -Label "EditMode"
$playMode = Read-NUnitSummary -Path $PlayModeResultsPath -Label "PlayMode"
$performance = Read-NUnitSummary -Path $PerformanceResultsPath -Label "Performance"
$netCheck = Read-NetCheckSummary -Path $NetCheckResultsPath

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("## D-Drive CI 結果")
$lines.Add("")
$lines.Add("| 項目 | 結果 |")
$lines.Add("|---|---|")

if ($validation.Found) {
    $lines.Add("| Validation (`CI.ValidateAll`) | $($validation.Tests) 件中 $($validation.Failures) 件失敗 |")
} else {
    $lines.Add("| Validation | 結果ファイルなし(ステップ未実行/失敗) |")
}

foreach ($r in @($editMode, $playMode, $performance)) {
    if ($r.Found) {
        $lines.Add("| $($r.Label) テスト | 全 $($r.Total) 件 / Passed $($r.Passed) / Failed $($r.Failed) |")
    } else {
        $lines.Add("| $($r.Label) テスト | 結果ファイルなし(ステップ未実行/失敗) |")
    }
}

if ($netCheck.Found) {
    $lines.Add("| NetCheck (6-7、2 クライアント自動テスト) | 全 $($netCheck.Total) シナリオ / Passed $($netCheck.Passed) / Failed $($netCheck.Failed) |")
} else {
    $lines.Add("| NetCheck (6-7) | 結果ファイルなし(任意ステップ未実行) |")
}

$lines.Add("")

if ($validation.FailedEntries.Count -gt 0) {
    $lines.Add("### Validation 失敗")
    foreach ($e in $validation.FailedEntries) { $lines.Add("- $e") }
    $lines.Add("")
}

foreach ($r in @($editMode, $playMode, $performance)) {
    if ($r.Found -and $r.FailedNames.Count -gt 0) {
        $lines.Add("### $($r.Label) 失敗テスト")
        foreach ($n in $r.FailedNames) { $lines.Add("- $n") }
        $lines.Add("")
    }
}

if ($netCheck.Found -and $netCheck.FailedNames.Count -gt 0) {
    $lines.Add("### NetCheck 失敗シナリオ")
    foreach ($n in $netCheck.FailedNames) { $lines.Add("- $n") }
    $lines.Add("")
}

$summary = ($lines -join "`n")
Write-Output $summary

if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $summary
}

$hasFailure = ($validation.Found -and $validation.Failures -ne 0) -or
              ($editMode.Found -and $editMode.Failed -ne 0) -or
              ($playMode.Found -and $playMode.Failed -ne 0) -or
              ($performance.Found -and $performance.Failed -ne 0) -or
              ($netCheck.Found -and $netCheck.Failed -ne 0)

if ($hasFailure) {
    exit 1
}
