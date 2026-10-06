# [14_networking.md] §22 / [29_network_device_test.md] §27(N-8、2026-10-06) — Cutscene のマーカーの NetCheck 判定(PowerShell 版)。
# Run-NetCheck.ps1 から dot-source される(関数だけ。単体では何もしない)。
#
# 各プロセスのログ(`[NetCheck] cutscene_xxx key=value ...`)から、DDrive.Runtime.Net.NetCheckCutsceneJudge(C#、
# プロセス内の自己判定)と同じ規則で再判定する。実機のログは別々の PC で取るため、時刻の突き合わせではなく
# 各プロセスのログ内の handle / netKey / s だけで判定する:
#   送信者(予測再生した側)  : 全マーカー(0 / 0.1 / 0.4 / 0.6 / 1.5)を 1 回ずつ
#   受信者(開始位置 s)      : t > s のマーカーは 1 回 / s - t <= 0.5 は 1 回 / s - t > 0.5 は 0 回(境界 ±0.6ms は 0 or 1)
#   二重発火 0 / 本体ログの「無音にしたマーカー n 件」は 種類数(2) x 無音にした時刻の数 と一致

$script:CutGraceSec = 0.5
$script:CutBoundaryTol = 0.0006

function Get-CutKv {
    param([string]$Line)
    $kv = @{}
    foreach ($m in [regex]::Matches($Line, '(\w+)=(\S+)')) { $kv[$m.Groups[1].Value] = $m.Groups[2].Value }
    return $kv
}

function Get-CutExpectedRange {
    param([double]$T, [bool]$IsSender, [double]$S)
    if ($IsSender -or $T -gt $S) { return @(1, 1) }
    $d = $S - $T
    if ($d -gt ($script:CutGraceSec + $script:CutBoundaryTol)) { return @(0, 0) }
    if ($d -ge ($script:CutGraceSec - $script:CutBoundaryTol)) { return @(0, 1) }
    return @(1, 1)
}

# 1 つのログを判定する。返り値: Found(cutscene 行があるか)/Pass/Reason/Role/SenderPlays/ReceivedPlays/MinS/MaxS/MeanS/Plays/NetKeys/OwnKeys/Timeline
function Test-CutsceneLog {
    param([string]$LogPath)

    if (-not (Test-Path $LogPath)) {
        return [pscustomobject]@{ Found = $false; Pass = $false; Reason = "log_file_missing"; Role = ""; SenderPlays = 0; ReceivedPlays = 0; MinS = -1; MaxS = -1; MeanS = -1; Plays = @(); NetKeys = @(); OwnKeys = @(); Timeline = "" }
    }

    $role = "observe"; $expectPlays = -1; $configuredPlays = -1; $kinds = 2
    $markers = @(@("m0", 0.0), @("m1", 0.1), @("m4", 0.4), @("m6", 0.6), @("m15", 1.5))
    $timelineLine = ""; $timelineOk = $true; $timelineReason = ""; $signalLoaded = $true
    $accs = [ordered]@{}
    $found = $false

    function Get-Acc($h) {
        if (-not $accs.Contains($h)) {
            $accs[$h] = [pscustomobject]@{ Handle = $h; HasRecv = $false; NetKey = "n/a"; S = 0.0; Silent = -1; Markers = @{}; Signals = @{} }
        }
        return $accs[$h]
    }

    foreach ($line in (Get-Content -Path $LogPath -ErrorAction SilentlyContinue)) {
        if ($line -notmatch '\[NetCheck\]\s+(cutscene_\w+)') { continue }
        $kind = $Matches[1]
        $kv = Get-CutKv -Line $line
        $found = $true
        switch ($kind) {
            "cutscene_config" {
                if ($kv.role) { $role = $kv.role }
                if ($kv.expectPlays) { $expectPlays = [int]$kv.expectPlays }
                if ($kv.plays) { $configuredPlays = [int]$kv.plays }
                if ($kv.kinds) { $kinds = [int]$kv.kinds }
                if ($kv.markers) {
                    $list = @()
                    foreach ($item in $kv.markers.Split(',')) {
                        $p = $item.Split(':')
                        if ($p.Count -eq 2) { $list += , @($p[0], [double]::Parse($p[1], [cultureinfo]::InvariantCulture)) }
                    }
                    if ($list.Count -gt 0) { $markers = $list }
                }
            }
            "cutscene_timeline" {
                $timelineLine = $line.Substring($line.IndexOf("cutscene_timeline")).Trim()
                if ($kv.ok -and $kv.ok -ne "1") { $timelineOk = $false; $timelineReason = "timeline_markers_not_loaded" }
                if ($kv.signal -and $kv.signal -ne "1") {
                    $signalLoaded = $false
                    if ($timelineOk) { $timelineOk = $false; $timelineReason = "timeline_signal_not_loaded" }
                }
                # M-6: 期待する全種別のうち読めなかったもの(missing=none なら全部読めた)
                if ($kv.missing -and $kv.missing -ne "none" -and $timelineOk) { $timelineOk = $false; $timelineReason = "timeline_kinds_missing:" + $kv.missing }
            }
            "cutscene_play" { if ($kv.handle) { [void](Get-Acc $kv.handle) } }
            "cutscene_own_key" { if ($kv.handle -and $kv.netKey) { (Get-Acc $kv.handle).NetKey = $kv.netKey } }
            "cutscene_recv" {
                if ($kv.handle) {
                    $a = Get-Acc $kv.handle
                    $a.HasRecv = $true
                    if ($kv.netKey) { $a.NetKey = $kv.netKey }
                    if ($kv.s) { $a.S = [double]::Parse($kv.s, [cultureinfo]::InvariantCulture) }
                    if ($kv.silent) { $a.Silent = [int]$kv.silent }
                }
            }
            "cutscene_marker" {
                if ($kv.handle -and $kv.key) { $a = Get-Acc $kv.handle; $a.Markers[$kv.key] = 1 + [int]($a.Markers[$kv.key]) }
            }
            "cutscene_signal" {
                if ($kv.handle -and $kv.key) { $a = Get-Acc $kv.handle; $a.Signals[$kv.key] = 1 + [int]($a.Signals[$kv.key]) }
            }
        }
    }

    if (-not $found) {
        return [pscustomobject]@{ Found = $false; Pass = $false; Reason = "no_cutscene_lines"; Role = ""; SenderPlays = 0; ReceivedPlays = 0; MinS = -1; MaxS = -1; MeanS = -1; Plays = @(); NetKeys = @(); OwnKeys = @(); Timeline = "" }
    }

    # 本体の「無音にしたマーカー n 件」は読み込めたマーカーの種類の合算。Signal が読めていなければ外部マーカーの 1 種類だけ。
    if (-not $signalLoaded) { $kinds = 1 }

    $fail = $null
    if (-not $timelineOk) { $fail = $timelineReason }
    $senderPlays = 0; $recvPlays = 0; $sumS = 0.0; $minS = -1.0; $maxS = -1.0
    $playRows = @(); $netKeys = @(); $ownKeys = @()
    $seenNetKeys = @{}

    foreach ($a in $accs.Values) {
        $isSender = -not $a.HasRecv
        $reasons = @()
        # GD-R-12: observe のプロセスに送信者はありえない(受信ログの無い再生 = 判定できない)。同じ netKey の受信は 1 回だけ
        if ($isSender -and $role -ne "trigger") { $reasons += "unmatched_play" }
        if (-not $isSender -and $a.NetKey -ne "n/a") {
            if ($seenNetKeys.ContainsKey($a.NetKey)) { $reasons += "duplicate_netkey($($a.NetKey))" }
            $seenNetKeys[$a.NetKey] = $true
        }
        if ($isSender) {
            $senderPlays++
            if ($a.NetKey -ne "n/a") { $ownKeys += $a.NetKey }
        } else {
            $recvPlays++
            $sumS += $a.S
            if ($minS -lt 0 -or $a.S -lt $minS) { $minS = $a.S }
            if ($a.S -gt $maxS) { $maxS = $a.S }
            $netKeys += $a.NetKey
        }

        $silentLo = 0; $silentHi = 0
        $firedText = @()
        foreach ($m in $markers) {
            $r = Get-CutExpectedRange -T $m[1] -IsSender $isSender -S $a.S
            $got = [int]$a.Markers[$m[0]]
            $gotSig = [int]$a.Signals[$m[0]]
            $firedText += "$($m[0]):$got"
            if ($got -gt 1) { $reasons += "duplicate_fire(key=$($m[0]),count=$got)" }
            elseif ($got -lt $r[0] -or $got -gt $r[1]) { $reasons += "marker_mismatch(key=$($m[0]),got=$got,expected=$($r[0])..$($r[1]))" }
            if (-not $signalLoaded) { }
            elseif ($gotSig -gt 1) { $reasons += "duplicate_signal(key=$($m[0]),count=$gotSig)" }
            elseif ($gotSig -lt $r[0] -or $gotSig -gt $r[1]) { $reasons += "signal_mismatch(key=$($m[0]),got=$gotSig,expected=$($r[0])..$($r[1]))" }
            if (-not $isSender) {
                if ($r[1] -eq 0) { $silentLo += $kinds }
                if ($r[0] -eq 0) { $silentHi += $kinds }
            }
        }

        if (-not $isSender -and $a.Silent -ge 0 -and ($a.Silent -lt $silentLo -or $a.Silent -gt $silentHi)) {
            $reasons += "silent_count_mismatch(got=$($a.Silent),expected=$silentLo..$silentHi)"
        }

        $ok = ($reasons.Count -eq 0)
        if (-not $ok -and $null -eq $fail) { $fail = "play $($a.Handle) $($reasons -join ';')" }
        $playRows += [pscustomobject]@{
            Handle = $a.Handle; NetKey = $a.NetKey; Role = $(if ($isSender) { "sender" } else { "receiver" })
            S = $a.S; Silent = $a.Silent; Fired = ($firedText -join ","); Pass = $ok; Reason = $(if ($ok) { "ok" } else { $reasons -join ';' })
        }
    }

    if ($null -eq $fail) {
        if ($role -eq "trigger" -and $configuredPlays -ge 0 -and $senderPlays -ne $configuredPlays) { $fail = "sender_plays_mismatch got=$senderPlays expected=$configuredPlays" }
        elseif ($role -ne "trigger" -and $expectPlays -ge 0 -and $recvPlays -ne $expectPlays) { $fail = "received_plays_mismatch got=$recvPlays expected=$expectPlays" }
        elseif ($role -ne "trigger" -and $expectPlays -lt 0 -and $recvPlays -eq 0 -and $senderPlays -eq 0) { $fail = "no_play_observed" }
    }

    return [pscustomobject]@{
        Found = $true; Pass = ($null -eq $fail); Reason = $(if ($null -eq $fail) { "ok" } else { $fail }); Role = $role
        SenderPlays = $senderPlays; ReceivedPlays = $recvPlays; MinS = $minS; MaxS = $maxS
        MeanS = $(if ($recvPlays -gt 0) { $sumS / $recvPlays } else { -1 })
        Plays = $playRows; NetKeys = $netKeys; OwnKeys = $ownKeys; Timeline = $timelineLine; SignalLoaded = $signalLoaded
    }
}

function Format-CutSummary {
    param($R)
    $inv = [cultureinfo]::InvariantCulture
    $s = if ($R.ReceivedPlays -gt 0) { "s=$($R.MinS.ToString('F3',$inv))..$($R.MaxS.ToString('F3',$inv)) (mean $($R.MeanS.ToString('F3',$inv)))" } else { "s=n/a" }
    $sig = if ($R.SignalLoaded -eq $false) { " [FAIL] Signal マーカーが Player で読み込まれていません" } else { "" }
    return "role=$($R.Role)$sig sender_plays=$($R.SenderPlays) recv_plays=$($R.ReceivedPlays) $s"
}

# ログ群を集めて判定だけ行う(実機: 別 PC のログを集めた後。時刻ではなく netKey / seq で対応づける)。
#   - 各ログを Test-CutsceneLog で判定し、RESULT 行(プロセスの自己判定)も読む
#   - trigger のログがあれば、observe 側の受信 netKey がすべて trigger の own_key(送信者の netKey)に含まれることを確認する
#     (own_key はログが取れた分だけ。取れていなければ件数の一致だけを見る)
function Invoke-CutsceneJudgeOnly {
    param([string[]]$LogPaths)

    $overall = $true
    $rows = @()
    $triggerOwn = @()
    $triggerPlays = -1
    foreach ($p in $LogPaths) {
        $r = Test-CutsceneLog -LogPath $p
        $res = $null
        if (Test-Path $p) {
            foreach ($line in (Get-Content -Path $p -ErrorAction SilentlyContinue)) {
                if ($line -match '\[DDriveNetCheck\]\s+RESULT=(PASS|FAIL)\s+scenario=(\S+)\s+reason=(.*)$') { $res = $Matches }
            }
        }
        $resText = if ($null -eq $res) { "no_result_line" } else { "$($res[1]) ($($res[3]))" }
        $resPass = ($null -ne $res -and $res[1] -eq "PASS")
        if ($r.Role -eq "trigger") { $triggerOwn += $r.OwnKeys; $triggerPlays = $r.SenderPlays }
        $rows += [pscustomobject]@{ Path = $p; R = $r; ResText = $resText; ResPass = $resPass }
    }

    foreach ($row in $rows) {
        $r = $row.R
        $extra = ""
        $extraPass = $true
        if ($r.Role -ne "trigger" -and $r.Found -and $triggerOwn.Count -gt 0) {
            $unknown = @($r.NetKeys | Where-Object { $_ -ne "n/a" -and $triggerOwn -notcontains $_ })
            if ($unknown.Count -gt 0) { $extraPass = $false; $extra = " unknown_netKey=$($unknown -join ',')" }
        }
        $pass = $r.Pass -and $row.ResPass -and $extraPass
        if (-not $pass) { $overall = $false }
        Write-Host "[$(if ($pass) {'PASS'} else {'FAIL'})] $($row.Path)"
        Write-Host "    $(Format-CutSummary $r)  cutscene=$($r.Reason)  process_result=$($row.ResText)$extra"
        if ($r.Timeline) { Write-Host "    $($r.Timeline)" }
        foreach ($pl in $r.Plays) {
            Write-Host "      $($pl.Role) handle=$($pl.Handle) netKey=$($pl.NetKey) s=$($pl.S.ToString('F3',[cultureinfo]::InvariantCulture)) silent=$($pl.Silent) fired=$($pl.Fired) => $(if ($pl.Pass) {'PASS'} else {'FAIL ' + $pl.Reason})"
        }
    }

    if ($triggerPlays -ge 0) { Write-Host "trigger の再生回数: $triggerPlays" }
    else {
        # GD-R-12: 実機では別々の PC のログを集める。trigger のログが無いと netKey の突き合わせができない = 不足
        Write-Host "[FAIL] trigger のログがありません(不足。送信側の PC のログも -Logs に渡してください)"
        $overall = $false
    }
    return $overall
}
