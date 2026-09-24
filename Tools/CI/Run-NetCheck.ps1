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
#
# [14_networking.md] §16(N-3、2026-09-22) — 上記は 1 Host + 1 Client の $scenarios(無改修)の説明。
# 別途 $quadScenarios(Host 1 + Client 3、MS2026 の 4 人対戦を見据えた確認)を追加した。判定は同じ 2 段構え
# を Client の本数ぶん繰り返す(位相差判定は Client 全本の Player.log に対して行う)。詳細は docs/29 §24。
#
# [14_networking.md] §18/N-6(2026-09-24) — さらに $migrationScenarios(Host 引き継ぎ。旧 Host が短命に
# 終了し、Client1 が successor として Host に昇格、Client2/3 が follower として再接続する)を追加した。
# 位相差判定は「移行後」の successor(Client1)のログを Host ログの代わりに使う。詳細は docs/29 §26。

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

# [14_networking.md] §16(N-3、2026-09-22) — Host 1 + Client 3(MS2026 の 4 人対戦)のローカル確認。上の
# $scenarios(1 Host + 1 Client、無改修)とは別の配列にして既存 4 シナリオの実行ロジックに影響しないように
# する。Host/全 Client が同一 Port へ接続する(NGO は Client ごとの待受ポートを使わないため、Port 競合は
# 起きない)。各シナリオの Clients 配列の要素数が接続するクライアント数。
$quadScenarios = @(
    [pscustomobject]@{
        Name = "quad0"; LatencyMs = 0; Port = 7841; HostSeconds = 40
        Clients = @(
            [pscustomobject]@{ DelaySec = 0; Seconds = 35 }
            [pscustomobject]@{ DelaySec = 0; Seconds = 35 }
            [pscustomobject]@{ DelaySec = 0; Seconds = 35 }
        )
    }
    [pscustomobject]@{
        Name = "quad_latejoin"; LatencyMs = 0; Port = 7851; HostSeconds = 45
        Clients = @(
            [pscustomobject]@{ DelaySec = 0;  Seconds = 40 }
            [pscustomobject]@{ DelaySec = 0;  Seconds = 40 }
            [pscustomobject]@{ DelaySec = 12; Seconds = 20 } # 12 秒遅れて参加する 1 人
        )
    }
    [pscustomobject]@{
        Name = "quad_leave"; LatencyMs = 0; Port = 7861; HostSeconds = 40
        Clients = @(
            [pscustomobject]@{ DelaySec = 0; Seconds = 35 }
            [pscustomobject]@{ DelaySec = 0; Seconds = 35 }
            [pscustomobject]@{ DelaySec = 0; Seconds = 12 } # 先に正常終了して抜ける 1 人
        )
    }
    [pscustomobject]@{
        Name = "quad_hostquit"; LatencyMs = 0; Port = 7871; HostSeconds = 12
        Clients = @(
            [pscustomobject]@{ DelaySec = 0; Seconds = 30 }
            [pscustomobject]@{ DelaySec = 0; Seconds = 30 }
            [pscustomobject]@{ DelaySec = 0; Seconds = 30 }
        )
    }
)

# [14_networking.md] §18/N-6(2026-09-24) — Host 引き継ぎ(ホストマイグレーション)のローカル確認。旧 Host が
# 短時間(12秒)で終了し、Client1(successor)が Stop→StartHost で新 Host に昇格、Client2/Client3(follower)が
# Stop→StartClient で新 Host(= Client1、全員同一 Port の 127.0.0.1)へ再接続することを確認する。
# $quadScenarios(全員が同じ Host に接続し続ける、Client ごとの引数は同一)とは構造が異なる(Client ごとに
# `-ddrive-migrate successor|follower` を切り替える必要がある)ため、専用の配列・実行ブロックにする。
# -ddrive-expect-clients: 旧 Host は 3(successor+follower×2 全員の接続を確認してから終了までに満たす)、
# successor は移行後の期待数(follower×2)として 2 を渡す(NetCheckRunner が Host 役になった時点で使う値。
# [14_networking.md] §18/N-6 実装メモ参照)。
$migrationScenarios = @(
    [pscustomobject]@{
        Name = "host_migration"; Port = 7881; HostSeconds = 12
        ExpectClientsHost = 3
        ExpectClientsSuccessor = 2
        Clients = @(
            [pscustomobject]@{ Migrate = "successor"; Seconds = 50 }
            [pscustomobject]@{ Migrate = "follower";  Seconds = 50 }
            [pscustomobject]@{ Migrate = "follower";  Seconds = 50 }
        )
    }
)

if ($OnlyScenario) {
    $matchedPair = $scenarios | Where-Object { $_.Name -eq $OnlyScenario }
    $matchedQuad = $quadScenarios | Where-Object { $_.Name -eq $OnlyScenario }
    $matchedMigration = $migrationScenarios | Where-Object { $_.Name -eq $OnlyScenario }
    $scenarios = @($matchedPair)
    $quadScenarios = @($matchedQuad)
    $migrationScenarios = @($matchedMigration)
    if ($matchedPair.Count -eq 0 -and $matchedQuad.Count -eq 0 -and $matchedMigration.Count -eq 0) {
        Write-Host "[ERROR] 不明なシナリオ名: $OnlyScenario"
        exit 1
    }
}

function Build-Args {
    param($Role, $Port, $LatencyMs, $Scenario, $Seconds, $LogPath, $ExpectClients = 0, $Migrate = "")

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

    # [14_networking.md] §16(N-3) — 既存 4 シナリオは $ExpectClients を渡さない(既定 0)ため、
    # このフラグは付与されず起動引数は従来どおり不変(既存 4 本は無改修)。
    if ($ExpectClients -gt 0) {
        $argList += @("-ddrive-expect-clients", $ExpectClients)
    }

    # [14_networking.md] §18/N-6(2026-09-24) — 既存 8 シナリオは $Migrate を渡さない(既定 "")ため、
    # このフラグは付与されず起動引数は従来どおり不変(既存 8 本は無改修)。-ddrive-migrate-host/-port は
    # 省略する(全員 127.0.0.1・同一 Port のローカル確認のため、NetCheckRunner 側の既定
    # 〔-ddrive-host/-ddrive-port にフォールバック〕で足りる)。
    if ($Migrate) {
        $argList += @("-ddrive-migrate", $Migrate)
    }

    return $argList
}

# [docs/29] §4 の判定基準(2026-09-15 改訂、[31] A8): 数ティック以内(目安 100ms 以内)。ローカル 2 プロセス
# 実行では負荷でぶれることがあるため、少し余裕を持たせて 150ms を機械判定の「ノイズ耐性マージン」にする
# (目安そのものは docs を優先する)。2026-09-15 追加修正(6-7 判定バグ): `-ddrive-sim-latency` を使う
# シナリオ(pair200 等)では Host→Client の 1 ホップ分の遅延がそのまま位相差に乗るため、マージン単体では
# 遅延シナリオが機械的に FAIL してしまっていた(pair200 実測 maxDiffMs=350、旧しきい値 150ms)。しきい値は
# 「シミュレート遅延(片道 ms)+ ノイズ耐性マージン」にする(Test-SignalPhase の $LatencyMs 引数)。
$PhaseDiffMarginMs = 150.0
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

# 2026-09-15 追加(6-7 判定バグ) — Client の Player.log から「自分が Host に接続した」最初の heartbeat の
# networkTime を読む。"latejoin" シナリオでは Client 接続前に Host が単独で発火させた signal_fire が
# 何件もあり、Client からは原理的に受信できない(受信して当然の対象ではない)。これを分母に含めると
# 中継率(ratio)が実態より低く出て機械的に FAIL する(latejoin 実測 ratio=0.64。接続前 3 件を除くと
# 7/8=0.875 で閾値 0.7 を超える)。
function Get-ClientConnectNetworkTime {
    param([string]$ClientLogPath)

    if (-not (Test-Path $ClientLogPath)) {
        return $null
    }

    foreach ($line in Get-Content -Path $ClientLogPath -ErrorAction SilentlyContinue) {
        if ($line -match '\[DDriveNetCheck\]\s+heartbeat=1\s+role=client\s+.*networkTime=([\d.]+)') {
            return [double]$Matches[1]
        }
    }

    return $null
}

# [14_networking.md] §16(N-3、コーディネーター指摘・2026-09-22) — quad_latejoin/quad_leave のように
# Client が Host より先に(または後から)いなくなるシナリオでは、その Client の最後の heartbeat 以降に
# Host が発火した signal_fire は原理的に受信不可能。これを分母に含めると中継率(ratio)が実態より低く出て
# 機械的に FAIL する(quad_leave の 12 秒で退出する Client で実測 ratio=0.23。quad_latejoin の遅れて
# 参加し先に退出する Client でも ratio=0.6)。Get-ClientConnectNetworkTime(下限)と対になる上限を返す。
# 2026-09-22 再修正: 「最後の heartbeat 行」の networkTime をそのまま使うと、Host との接続が切れた後の
# heartbeat 行は networkTime が 0.00 にリセットされる([docs/29] §8「切断後は heartbeat の... networkTime=0.00」)
# ため、disconnect シナリオ(Host が先に終了→ Client が切断検知)で「最後の行の値」を採用すると上限が
# 0.00 になり、全ての signal_fire が上限を超えている扱いになって fireEvents が空になる偽陽性 FAIL
# (no_signal_fire_in_host_log)を起こす実バグがあった(初回修正時に見落とし)。時系列で単調増加するとは
# 限らないため、「観測した networkTime の最大値」を返すようにする(切断後にリセットされた 0.00 は
# 無視される)。
function Get-ClientLastNetworkTime {
    param([string]$ClientLogPath)

    if (-not (Test-Path $ClientLogPath)) {
        return $null
    }

    $maxTime = $null
    foreach ($line in Get-Content -Path $ClientLogPath -ErrorAction SilentlyContinue) {
        if ($line -match '\[DDriveNetCheck\]\s+heartbeat=1\s+role=client\s+.*networkTime=([\d.]+)') {
            $time = [double]$Matches[1]
            if ($null -eq $maxTime -or $time -gt $maxTime) {
                $maxTime = $time
            }
        }
    }

    return $maxTime
}

function Test-SignalPhase {
    param([string]$HostLogPath, [string]$ClientLogPath, [double]$LatencyMs = 0.0)

    $fireEvents = Get-SignalEvents -LogPath $HostLogPath -Kind "fire"
    $recvEvents = Get-SignalEvents -LogPath $ClientLogPath -Kind "recv"

    # Client が接続する前に Host が単独で発火させた signal_fire は、Client からは受信不可能なので分母から
    # 除外する(late-join シナリオ用。他シナリオは Client 接続が最初の Play より早いため実質無害)。
    # 2026-09-22 追加(コーディネーター指摘) — 同じ理由で、Client が退出した後に Host が発火した分も
    # 分母から除外する(quad_leave/quad_latejoin のように Client が Host より先に/後から生きなくなる
    # シナリオ用。既存 4 シナリオは全 Client が Host と同程度以上生きるため、この上限を追加しても
    # 実質的にフィルタされる件数は変わらない=結果は変わらない)。
    $clientConnectNetworkTime = Get-ClientConnectNetworkTime -ClientLogPath $ClientLogPath
    $clientLastNetworkTime = Get-ClientLastNetworkTime -ClientLogPath $ClientLogPath
    if ($null -ne $clientConnectNetworkTime -or $null -ne $clientLastNetworkTime) {
        $filtered = @{}
        foreach ($key in $fireEvents.Keys) {
            $time = $fireEvents[$key]
            $afterConnect = ($null -eq $clientConnectNetworkTime) -or ($time -ge $clientConnectNetworkTime)
            $beforeExit = ($null -eq $clientLastNetworkTime) -or ($time -le $clientLastNetworkTime)
            if ($afterConnect -and $beforeExit) {
                $filtered[$key] = $time
            }
        }

        $fireEvents = $filtered
    }

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

    # 2026-09-15 修正(6-7 判定バグ): しきい値は「シミュレート遅延(片道 ms)+ ノイズ耐性マージン」。
    # Host→Client の 1 ホップなので片道分がそのまま位相差に乗る([docs/29] §4/§15 参照)。
    # 比較前に整数 ms へ丸める(ログの文字列 "12.66"/"12.54" 等を double 減算するため、しきい値ちょうど
    # (例: 遅延 200ms のとき 350ms)の実測値が浮動小数の丸め誤差で 350.0000000000014 のようにわずかに
    # 超え、機械判定だけ FAIL するのを避ける。ログ表示も同じ丸めなので基準を合わせる)。
    $phaseDiffThresholdMs = $LatencyMs + $PhaseDiffMarginMs
    $maxDiffMsRounded = [Math]::Round($maxDiffMs, 0)
    if ($maxDiffMsRounded -gt $phaseDiffThresholdMs) {
        return [pscustomobject]@{ Pass = $false; Reason = "signal_phase_diff_too_large max_ms=$([Math]::Round($maxDiffMs,0)) threshold_ms=$([Math]::Round($phaseDiffThresholdMs,0))"; FireCount = $fireEvents.Count; MatchedCount = $matched; MaxDiffMs = $maxDiffMs; MeanDiffMs = $meanDiffMs }
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

# [14_networking.md] §16(N-3、quad_leave 専用) — Host の Player.log に「他 Client が 1 人抜けても自分は
# 継続する」ことを示す client_left=<clientId> 行が出ていること、かつその後の heartbeat の clients=<n> が
# 一度は減っていること(3→2 等)を確認する。NetCheckJudge 自体は接続の最大値(MaxConnectedClientsObserved)
# だけを見るため、「実際に 1 人減ったこと」はこのスクリプト側でクロスチェックする。
function Test-ClientLeftAndCountDecrease {
    param([string]$HostLogPath)

    if (-not (Test-Path $HostLogPath)) {
        return [pscustomobject]@{ Pass = $false; Reason = "log_file_missing" }
    }

    $lines = Get-Content -Path $HostLogPath -ErrorAction SilentlyContinue
    $sawClientLeft = $false
    $peakClients = -1
    $sawDecreaseAfterLeft = $false

    foreach ($line in $lines) {
        if ($line -match '\[DDriveNetCheck\]\s+client_left=(\d+)') {
            $sawClientLeft = $true
            continue
        }

        if ($line -match '\[DDriveNetCheck\]\s+heartbeat=1\s+.*\bclients=(-?\d+)') {
            $clients = [int]$Matches[1]
            if ($clients -gt $peakClients) {
                $peakClients = $clients
            } elseif ($sawClientLeft -and $clients -lt $peakClients) {
                $sawDecreaseAfterLeft = $true
            }
        }
    }

    if (-not $sawClientLeft) {
        return [pscustomobject]@{ Pass = $false; Reason = "no_client_left_observed" }
    }

    if (-not $sawDecreaseAfterLeft) {
        return [pscustomobject]@{ Pass = $false; Reason = "clients_count_did_not_decrease peak=$peakClients" }
    }

    return [pscustomobject]@{ Pass = $true; Reason = "ok peak=$peakClients" }
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
    $phase = Test-SignalPhase -HostLogPath $hostLog -ClientLogPath $clientLog -LatencyMs $scenario.LatencyMs

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

# [14_networking.md] §16(N-3、2026-09-22) — Host 1 + Client 3。上の $scenarios ループ(1 Host + 1 Client、
# 無改修)とは別に、Host + 複数 Client を起動して同じ判定パターン(各プロセス自身の RESULT 行 + このスクリプト
# によるクロスログの Signal 位相差)を Client の本数ぶん繰り返す。ログファイル名は
# "<シナリオ名>_client<N>.log"(N=1始まり)。
foreach ($scenario in $quadScenarios) {
    $clientCount = $scenario.Clients.Count
    Write-Host ""
    Write-Host "=== シナリオ: $($scenario.Name) (latency=$($scenario.LatencyMs)ms, host=$($scenario.HostSeconds)s, clients=$clientCount) ==="

    $hostLog = Join-Path $resultsFull "$($scenario.Name)_host.log"
    $clientLogs = @()
    for ($i = 1; $i -le $clientCount; $i++) {
        $clientLogs += Join-Path $resultsFull "$($scenario.Name)_client$i.log"
    }
    Remove-Item -Path (@($hostLog) + $clientLogs) -ErrorAction SilentlyContinue

    $hostArgs = Build-Args -Role "host" -Port $scenario.Port -LatencyMs $scenario.LatencyMs -Scenario $scenario.Name -Seconds $scenario.HostSeconds -LogPath $hostLog -ExpectClients $clientCount
    $hostProc = Start-Process -FilePath $exeFull -ArgumentList $hostArgs -PassThru -WindowStyle Hidden
    Write-Host "  Host 起動($($hostProc.Id))。Client を $clientCount 本起動します。"

    $clientProcs = @()
    $elapsedSinceHostStart = 0
    for ($i = 0; $i -lt $clientCount; $i++) {
        $clientCfg = $scenario.Clients[$i]
        if ($clientCfg.DelaySec -gt $elapsedSinceHostStart) {
            $sleepSec = $clientCfg.DelaySec - $elapsedSinceHostStart
            Write-Host "  Client$($i+1) を $sleepSec 秒後に起動します(遅延参加)。"
            Start-Sleep -Seconds $sleepSec
            $elapsedSinceHostStart = $clientCfg.DelaySec
        }

        $clientArgs = Build-Args -Role "client" -Port $scenario.Port -LatencyMs $scenario.LatencyMs -Scenario $scenario.Name -Seconds $clientCfg.Seconds -LogPath $clientLogs[$i]
        $proc = Start-Process -FilePath $exeFull -ArgumentList $clientArgs -PassThru -WindowStyle Hidden
        Write-Host "  Client$($i+1) 起動($($proc.Id))。"
        $clientProcs += $proc
    }

    $maxClientEndSec = 0
    foreach ($clientCfg in $scenario.Clients) {
        $endSec = $clientCfg.DelaySec + $clientCfg.Seconds
        if ($endSec -gt $maxClientEndSec) { $maxClientEndSec = $endSec }
    }

    $timeoutSec = [Math]::Max($scenario.HostSeconds, $maxClientEndSec) + 30
    Wait-ForExitOrKill -Process $hostProc -TimeoutSec $timeoutSec -Label "Host"
    for ($i = 0; $i -lt $clientProcs.Count; $i++) {
        Wait-ForExitOrKill -Process $clientProcs[$i] -TimeoutSec $timeoutSec -Label "Client$($i+1)"
    }

    $hostResult = Parse-ResultLine -LogPath $hostLog
    $clientResults = @()
    $phases = @()
    for ($i = 0; $i -lt $clientCount; $i++) {
        $clientResults += Parse-ResultLine -LogPath $clientLogs[$i]
        # 位相差判定は Client 全本の Player.log に対して行う([14_networking.md] §16)。
        $phases += Test-SignalPhase -HostLogPath $hostLog -ClientLogPath $clientLogs[$i] -LatencyMs $scenario.LatencyMs
    }

    $allClientsPass = -not ($clientResults | Where-Object { -not $_.Pass })
    $allPhasesPass = -not ($phases | Where-Object { -not $_.Pass })

    # quad_leave だけ追加のクロスチェック(Host の client_left ログ + clients 数の減少)を課す。
    $extraCheck = $null
    if ($scenario.Name -eq "quad_leave") {
        $extraCheck = Test-ClientLeftAndCountDecrease -HostLogPath $hostLog
    }
    $extraPass = if ($null -eq $extraCheck) { $true } else { $extraCheck.Pass }

    $scenarioPass = $hostResult.Pass -and $allClientsPass -and $allPhasesPass -and $extraPass
    if (-not $scenarioPass) { $overallPass = $false }

    $verdict = if ($scenarioPass) { "PASS" } else { "FAIL" }
    Write-Host "  Host    : $(if ($hostResult.Pass) {'PASS'} else {'FAIL'}) ($($hostResult.Reason))"
    for ($i = 0; $i -lt $clientCount; $i++) {
        Write-Host "  Client$($i+1) : $(if ($clientResults[$i].Pass) {'PASS'} else {'FAIL'}) ($($clientResults[$i].Reason)) / 位相差 $(if ($phases[$i].Pass) {'PASS'} else {'FAIL'}) fire=$($phases[$i].FireCount) matched=$($phases[$i].MatchedCount) maxDiffMs=$([Math]::Round($phases[$i].MaxDiffMs,0)) ($($phases[$i].Reason))"
    }
    if ($null -ne $extraCheck) {
        Write-Host "  client_left/clients 減少: $(if ($extraCheck.Pass) {'PASS'} else {'FAIL'}) ($($extraCheck.Reason))"
    }
    Write-Host "  => $verdict"

    $clientSummary = ($clientResults | ForEach-Object { $_.Pass }) -join ","
    $phaseSummary = ($phases | ForEach-Object { $_.Pass }) -join ","
    $reasonSummary = "host=$($hostResult.Reason) / clients=$(($clientResults | ForEach-Object { $_.Reason }) -join ';') / phases=$(($phases | ForEach-Object { $_.Reason }) -join ';')"
    if ($null -ne $extraCheck) {
        $reasonSummary += " / extra=$($extraCheck.Reason)"
    }

    $summaryRows.Add("| $($scenario.Name) | $verdict | host=$($hostResult.Pass) clients=$clientSummary phases=$phaseSummary | $reasonSummary |")
    $jsonResults.Add([pscustomobject]@{
        scenario      = $scenario.Name
        pass          = $scenarioPass
        hostPass      = $hostResult.Pass
        hostReason    = $hostResult.Reason
        clientResults = $clientResults
        phaseResults  = $phases
        extraCheck    = $extraCheck
    })
}

# [14_networking.md] §18/N-6(2026-09-24) — Host 引き継ぎ(ホストマイグレーション)。上の $quadScenarios
# ループとは違い、Client ごとに役割(successor/follower)が異なる・旧 Host は短命・位相差判定の基準ログが
# Host ではなく Client1(successor、移行後)になる、という 3 点が異なるため専用ループにする。
foreach ($scenario in $migrationScenarios) {
    $clientCount = $scenario.Clients.Count
    Write-Host ""
    Write-Host "=== シナリオ: $($scenario.Name) (旧host=$($scenario.HostSeconds)s, clients=$clientCount, Host 引き継ぎ確認) ==="

    $hostLog = Join-Path $resultsFull "$($scenario.Name)_host.log"
    $clientLogs = @()
    for ($i = 1; $i -le $clientCount; $i++) {
        $clientLogs += Join-Path $resultsFull "$($scenario.Name)_client$i.log"
    }
    Remove-Item -Path (@($hostLog) + $clientLogs) -ErrorAction SilentlyContinue

    $hostArgs = Build-Args -Role "host" -Port $scenario.Port -LatencyMs 0 -Scenario $scenario.Name -Seconds $scenario.HostSeconds -LogPath $hostLog -ExpectClients $scenario.ExpectClientsHost
    $hostProc = Start-Process -FilePath $exeFull -ArgumentList $hostArgs -PassThru -WindowStyle Hidden
    Write-Host "  旧 Host 起動($($hostProc.Id))。$($scenario.HostSeconds) 秒で終了予定。Client を $clientCount 本起動します。"

    $clientProcs = @()
    for ($i = 0; $i -lt $clientCount; $i++) {
        $clientCfg = $scenario.Clients[$i]
        $expectClients = if ($clientCfg.Migrate -eq "successor") { $scenario.ExpectClientsSuccessor } else { 0 }
        $clientArgs = Build-Args -Role "client" -Port $scenario.Port -LatencyMs 0 -Scenario $scenario.Name -Seconds $clientCfg.Seconds -LogPath $clientLogs[$i] -ExpectClients $expectClients -Migrate $clientCfg.Migrate
        $proc = Start-Process -FilePath $exeFull -ArgumentList $clientArgs -PassThru -WindowStyle Hidden
        Write-Host "  Client$($i+1)($($clientCfg.Migrate)) 起動($($proc.Id))。"
        $clientProcs += $proc
    }

    $maxClientSeconds = ($scenario.Clients | ForEach-Object { $_.Seconds } | Measure-Object -Maximum).Maximum
    $timeoutSec = [Math]::Max($scenario.HostSeconds, $maxClientSeconds) + 30
    Wait-ForExitOrKill -Process $hostProc -TimeoutSec $timeoutSec -Label "旧Host"
    for ($i = 0; $i -lt $clientProcs.Count; $i++) {
        Wait-ForExitOrKill -Process $clientProcs[$i] -TimeoutSec $timeoutSec -Label "Client$($i+1)"
    }

    $hostResult = Parse-ResultLine -LogPath $hostLog
    $clientResults = @()
    for ($i = 0; $i -lt $clientCount; $i++) {
        $clientResults += Parse-ResultLine -LogPath $clientLogs[$i]
    }

    # 位相差判定: 「移行後」の successor(Client1、$scenario.Clients[0] が常に successor)のログを Host ログの
    # 代わりに使い、follower(Client2/3)の signal_recv と突き合わせる([14_networking.md] §18/N-6)。
    # Client1 は移行前は Client 役なので signal_fire を一切出さず(PlayAndSignal は IsServer のときだけ
    # 呼ばれる)、ログに現れる signal_fire は移行後の分だけになる。Test-SignalPhase 自体は無改修で流用できる
    # (Get-ClientConnectNetworkTime/Get-ClientLastNetworkTime は follower 側の生存窓を見るだけなので、
    # 基準ログが Host か Client かは問わない)。
    $successorLog = $clientLogs[0]
    $phases = @()
    for ($i = 1; $i -lt $clientCount; $i++) {
        $phases += Test-SignalPhase -HostLogPath $successorLog -ClientLogPath $clientLogs[$i] -LatencyMs 0
    }

    $allClientsPass = -not ($clientResults | Where-Object { -not $_.Pass })
    $allPhasesPass = -not ($phases | Where-Object { -not $_.Pass })

    $scenarioPass = $hostResult.Pass -and $allClientsPass -and $allPhasesPass
    if (-not $scenarioPass) { $overallPass = $false }

    $verdict = if ($scenarioPass) { "PASS" } else { "FAIL" }
    Write-Host "  旧Host  : $(if ($hostResult.Pass) {'PASS'} else {'FAIL'}) ($($hostResult.Reason))"
    for ($i = 0; $i -lt $clientCount; $i++) {
        $roleLabel = $scenario.Clients[$i].Migrate
        Write-Host "  Client$($i+1)($roleLabel) : $(if ($clientResults[$i].Pass) {'PASS'} else {'FAIL'}) ($($clientResults[$i].Reason))"
    }
    for ($i = 0; $i -lt $phases.Count; $i++) {
        Write-Host "  位相差(successor→follower$($i+2)): $(if ($phases[$i].Pass) {'PASS'} else {'FAIL'}) fire=$($phases[$i].FireCount) matched=$($phases[$i].MatchedCount) maxDiffMs=$([Math]::Round($phases[$i].MaxDiffMs,0)) ($($phases[$i].Reason))"
    }
    Write-Host "  => $verdict"

    $clientSummary = ($clientResults | ForEach-Object { $_.Pass }) -join ","
    $phaseSummary = ($phases | ForEach-Object { $_.Pass }) -join ","
    $reasonSummary = "host=$($hostResult.Reason) / clients=$(($clientResults | ForEach-Object { $_.Reason }) -join ';') / phases=$(($phases | ForEach-Object { $_.Reason }) -join ';')"

    $summaryRows.Add("| $($scenario.Name) | $verdict | host=$($hostResult.Pass) clients=$clientSummary phases=$phaseSummary | $reasonSummary |")
    $jsonResults.Add([pscustomobject]@{
        scenario      = $scenario.Name
        pass          = $scenarioPass
        hostPass      = $hostResult.Pass
        hostReason    = $hostResult.Reason
        clientResults = $clientResults
        phaseResults  = $phases
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
