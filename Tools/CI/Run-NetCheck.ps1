# D-Drive 6-7: 2 クライアント自動テスト(ローカル 2 プロセス、Loopback ⇔ NGO 両ブリッジ)。
# Tools\CI\run-netcheck.cmd から呼ばれる本体。単体で pwsh から直接実行してもよい。
#
# 前提: NetCheckBuilder.Build()(Tools > D-Drive > Build > 実機確認用 Windows 開発ビルド)でビルド済みの
# Builds\DDriveNetCheck\DDriveNetCheck.exe が存在すること。このスクリプト自体はビルドを行わない
# (Unity Editor を必要とする操作は run-ci.cmd と同じ理由で分離してある)。
#
# 各シナリオで Host/Client の 2 プロセスを 127.0.0.1 上に起動し、両方のプロセスが自然終了するのを待つ。
# 判定は 2 段構え:
#   1) 各プロセス自身の [DDriveNetCheck] RESULT=PASS|FAIL 行(DDrive.Runtime.Net.NetCheckJudge。
#      Exception/Error・接続・偽造Cancel全件破棄・LateJoin復元・A7猶予・切断後VFX0・ContentHash一致を
#      自分のログだけで判定したもの)
#   2) このスクリプトが両方の Player.log を突き合わせて判定する Signal 中継の位相差(②の後半。
#      Host の signal_fire と Client の signal_recv を HandleNetKey で対にして networkTime 差を見る。
#      [docs/29] §4「両方のログを外部スクリプトが判定」)
# シナリオは 1) と 2) の両方が通れば PASS。全シナリオ PASS ならこのスクリプトは終了コード 0 を返す。

param(
    [string]$ExePath = "Builds/DDriveNetCheck/DDriveNetCheck.exe",
    [string]$ResultsDir = "TestResults/NetCheck",
    [string]$OnlyScenario = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Get-Location).Path
$exeFull = Join-Path $repoRoot $ExePath
if (-not (Test-Path $exeFull)) {
    Write-Host "[ERROR] ビルド済み exe が見つかりません: $exeFull"
    Write-Host "  先に Unity Editor で [Tools > D-Drive > Build > 実機確認用 Windows 開発ビルド] を実行してください。"
    exit 1
}

$resultsFull = Join-Path $repoRoot $ResultsDir
New-Item -ItemType Directory -Force -Path $resultsFull | Out-Null

# 前回の異常終了で残ったプロセスがあれば掃除する(ポート競合の事故を避ける)。
$stray = Get-Process -Name "DDriveNetCheck" -ErrorAction SilentlyContinue
if ($stray) {
    Write-Host "[WARN] 前回残った DDriveNetCheck.exe プロセス $($stray.Count) 件を終了します。"
    $stray | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

# シナリオ定義。HostSeconds > ClientSeconds のペアは「Host が接続を保ち続ける」通常パス、
# "disconnect" だけ HostSeconds < ClientSeconds にして Host が先に(正常終了で)いなくなることを利用し、
# Client 側の切断検知 + 演出後片付け(PASS 条件⑤)を Stop-Process 等を使わずに再現する。
$scenarios = @(
    [pscustomobject]@{ Name = "pair0";      LatencyMs = 0;   Port = 7801; ClientDelaySec = 0;  HostSeconds = 35; ClientSeconds = 30 }
    [pscustomobject]@{ Name = "pair200";    LatencyMs = 200; Port = 7811; ClientDelaySec = 0;  HostSeconds = 35; ClientSeconds = 30 }
    [pscustomobject]@{ Name = "latejoin";   LatencyMs = 0;   Port = 7821; ClientDelaySec = 12; HostSeconds = 35; ClientSeconds = 20 }
    [pscustomobject]@{ Name = "disconnect"; LatencyMs = 0;   Port = 7831; ClientDelaySec = 0;  HostSeconds = 10; ClientSeconds = 25 }
)

if ($OnlyScenario) {
    $scenarios = $scenarios | Where-Object { $_.Name -eq $OnlyScenario }
    if ($scenarios.Count -eq 0) {
        Write-Host "[ERROR] 不明なシナリオ名: $OnlyScenario"
        exit 1
    }
}

function Build-Args {
    param($Role, $Port, $LatencyMs, $Scenario, $Seconds, $LogPath)

    $argList = @(
        "-ddrive-net", $Role,
        "-ddrive-host", "127.0.0.1",
        "-ddrive-port", $Port,
        "-ddrive-autotest", $Scenario,
        "-ddrive-autotest-seconds", $Seconds,
        "-batchmode", "-nographics",
        "-logFile", $LogPath
    )

    if ($LatencyMs -gt 0) {
        $argList += @("-ddrive-sim-latency", $LatencyMs)
    }

    return $argList
}

# [docs/29] §4 の判定基準(2026-09-15 改訂、[31] A8): 数ティック以内(目安 100ms 以内)。ローカル 2 プロセス
# 実行では負荷でぶれることがあるため、少し余裕を持たせて 150ms を機械判定のしきい値にする(目安そのものは
# docs を優先し、ここでの 150ms は「自動判定のノイズ耐性」として明記する)。
$PhaseDiffThresholdMs = 150.0
$PhaseMatchRatioThreshold = 0.7

function Parse-ResultLine {
    param([string]$LogPath)

    if (-not (Test-Path $LogPath)) {
        return [pscustomobject]@{ Found = $false; Pass = $false; Reason = "log_file_missing" }
    }

    $lines = Get-Content -Path $LogPath -ErrorAction SilentlyContinue
    $matchLine = $null
    foreach ($line in $lines) {
        if ($line -match '\[DDriveNetCheck\]\s+RESULT=(PASS|FAIL)\s+scenario=(\S+)\s+reason=(.*)$') {
            $matchLine = $Matches
        }
    }

    if ($null -eq $matchLine) {
        return [pscustomobject]@{ Found = $false; Pass = $false; Reason = "no_result_line" }
    }

    return [pscustomobject]@{ Found = $true; Pass = ($matchLine[1] -eq "PASS"); Reason = $matchLine[3] }
}

function Get-SignalEvents {
    param([string]$LogPath, [string]$Kind)

    $events = @{}
    if (-not (Test-Path $LogPath)) {
        return $events
    }

    $pattern = "\[DDriveNetCheck\]\s+signal_$Kind=hit\s+key=(0x[0-9A-Fa-f]+)\s+networkTime=([\d.]+)"
    foreach ($line in Get-Content -Path $LogPath -ErrorAction SilentlyContinue) {
        if ($line -match $pattern) {
            $key = $Matches[1]
            $time = [double]$Matches[2]
            if (-not $events.ContainsKey($key)) {
                # 最初の 1 件だけを見る(signal_recv は OnSignal トラック数ぶん複数回出る。[docs/29] §4)。
                $events[$key] = $time
            }
        }
    }

    return $events
}

function Test-SignalPhase {
    param([string]$HostLogPath, [string]$ClientLogPath)

    $fireEvents = Get-SignalEvents -LogPath $HostLogPath -Kind "fire"
    $recvEvents = Get-SignalEvents -LogPath $ClientLogPath -Kind "recv"

    if ($fireEvents.Count -eq 0) {
        return [pscustomobject]@{ Pass = $false; Reason = "no_signal_fire_in_host_log"; FireCount = 0; MatchedCount = 0; MaxDiffMs = -1 }
    }

    $matched = 0
    $maxDiffMs = 0.0
    $sumDiffMs = 0.0

    foreach ($key in $fireEvents.Keys) {
        if ($recvEvents.ContainsKey($key)) {
            $matched++
            $diffMs = [Math]::Abs(($recvEvents[$key] - $fireEvents[$key]) * 1000.0)
            $sumDiffMs += $diffMs
            if ($diffMs -gt $maxDiffMs) {
                $maxDiffMs = $diffMs
            }
        }
    }

    $ratio = $matched / $fireEvents.Count
    $meanDiffMs = if ($matched -gt 0) { $sumDiffMs / $matched } else { -1 }

    if ($ratio -lt $PhaseMatchRatioThreshold) {
        return [pscustomobject]@{ Pass = $false; Reason = "signal_relay_ratio_low ratio=$([Math]::Round($ratio,2))"; FireCount = $fireEvents.Count; MatchedCount = $matched; MaxDiffMs = $maxDiffMs; MeanDiffMs = $meanDiffMs }
    }

    if ($maxDiffMs -gt $PhaseDiffThresholdMs) {
        return [pscustomobject]@{ Pass = $false; Reason = "signal_phase_diff_too_large max_ms=$([Math]::Round($maxDiffMs,0))"; FireCount = $fireEvents.Count; MatchedCount = $matched; MaxDiffMs = $maxDiffMs; MeanDiffMs = $meanDiffMs }
    }

    return [pscustomobject]@{ Pass = $true; Reason = "ok"; FireCount = $fireEvents.Count; MatchedCount = $matched; MaxDiffMs = $maxDiffMs; MeanDiffMs = $meanDiffMs }
}

function Wait-ForExitOrKill {
    param($Process, [int]$TimeoutSec, [string]$Label)

    if ($null -eq $Process) {
        return
    }

    $exited = $Process.WaitForExit($TimeoutSec * 1000)
    if (-not $exited) {
        Write-Host "  [WARN] $Label がタイムアウト内(${TimeoutSec}s)に終了しなかったため強制終了します。"
        try { $Process.Kill() } catch {}
    }
}

$overallPass = $true
$summaryRows = New-Object System.Collections.Generic.List[string]
$jsonResults = New-Object System.Collections.Generic.List[object]

foreach ($scenario in $scenarios) {
    Write-Host ""
    Write-Host "=== シナリオ: $($scenario.Name) (latency=$($scenario.LatencyMs)ms, host=$($scenario.HostSeconds)s, client=$($scenario.ClientSeconds)s) ==="

    $hostLog = Join-Path $resultsFull "$($scenario.Name)_host.log"
    $clientLog = Join-Path $resultsFull "$($scenario.Name)_client.log"
    Remove-Item -Path $hostLog, $clientLog -ErrorAction SilentlyContinue

    $hostArgs = Build-Args -Role "host" -Port $scenario.Port -LatencyMs $scenario.LatencyMs -Scenario $scenario.Name -Seconds $scenario.HostSeconds -LogPath $hostLog
    $hostProc = Start-Process -FilePath $exeFull -ArgumentList $hostArgs -PassThru -WindowStyle Hidden

    if ($scenario.ClientDelaySec -gt 0) {
        Write-Host "  Host 起動($($hostProc.Id))。Client を $($scenario.ClientDelaySec)s 後に起動します(latejoin)。"
        Start-Sleep -Seconds $scenario.ClientDelaySec
    }

    $clientArgs = Build-Args -Role "client" -Port $scenario.Port -LatencyMs $scenario.LatencyMs -Scenario $scenario.Name -Seconds $scenario.ClientSeconds -LogPath $clientLog
    $clientProc = Start-Process -FilePath $exeFull -ArgumentList $clientArgs -PassThru -WindowStyle Hidden
    Write-Host "  Client 起動($($clientProc.Id))。両プロセスの終了を待ちます..."

    $timeoutSec = [Math]::Max($scenario.HostSeconds, $scenario.ClientDelaySec + $scenario.ClientSeconds) + 30
    Wait-ForExitOrKill -Process $hostProc -TimeoutSec $timeoutSec -Label "Host"
    Wait-ForExitOrKill -Process $clientProc -TimeoutSec $timeoutSec -Label "Client"

    $hostResult = Parse-ResultLine -LogPath $hostLog
    $clientResult = Parse-ResultLine -LogPath $clientLog
    $phase = Test-SignalPhase -HostLogPath $hostLog -ClientLogPath $clientLog

    $scenarioPass = $hostResult.Pass -and $clientResult.Pass -and $phase.Pass
    if (-not $scenarioPass) { $overallPass = $false }

    $verdict = if ($scenarioPass) { "PASS" } else { "FAIL" }
    Write-Host "  Host   : $(if ($hostResult.Pass) {'PASS'} else {'FAIL'}) ($($hostResult.Reason))"
    Write-Host "  Client : $(if ($clientResult.Pass) {'PASS'} else {'FAIL'}) ($($clientResult.Reason))"
    Write-Host "  Signal 中継(位相差): $(if ($phase.Pass) {'PASS'} else {'FAIL'}) fire=$($phase.FireCount) matched=$($phase.MatchedCount) maxDiffMs=$([Math]::Round($phase.MaxDiffMs,0)) ($($phase.Reason))"
    Write-Host "  => $verdict"

    $summaryRows.Add("| $($scenario.Name) | $verdict | host=$($hostResult.Pass) client=$($clientResult.Pass) phase=$($phase.Pass) | $($hostResult.Reason) / $($clientResult.Reason) / $($phase.Reason) |")
    $jsonResults.Add([pscustomobject]@{
        scenario     = $scenario.Name
        pass         = $scenarioPass
        hostPass     = $hostResult.Pass
        hostReason   = $hostResult.Reason
        clientPass   = $clientResult.Pass
        clientReason = $clientResult.Reason
        phasePass    = $phase.Pass
        phaseReason  = $phase.Reason
    })
}

$summaryPath = Join-Path $resultsFull "summary.md"
$summaryLines = @("# D-Drive 6-7 NetCheck 結果", "", "| シナリオ | 結果 | 内訳 | 理由 |", "|---|---|---|---|") + $summaryRows
Set-Content -Path $summaryPath -Value ($summaryLines -join "`n")

# Tools\CI\Summarize-Results.ps1(-NetCheckResultsPath)が読む機械可読な要約。run-ci.cmd から任意ステップ
# として呼ばれた場合、Validation/EditMode/PlayMode/Performance と同じ表に載せられるようにする。
$resultsJsonPath = Join-Path $resultsFull "results.json"
# -AsArray: シナリオが 1 件だけ(-OnlyScenario 指定時)でも配列として書き出す(読む側の分岐を減らす)。
ConvertTo-Json -InputObject $jsonResults -AsArray | Set-Content -Path $resultsJsonPath

Write-Host ""
Write-Host "=== 要約 (詳細: $summaryPath) ==="
$summaryRows | ForEach-Object { Write-Host "  $_" }

if ($overallPass) {
    Write-Host ""
    Write-Host "=== すべてのシナリオが PASS です ==="
    exit 0
} else {
    Write-Host ""
    Write-Host "=== FAIL したシナリオがあります(上のログ/summary.md を確認してください) ==="
    exit 1
}
