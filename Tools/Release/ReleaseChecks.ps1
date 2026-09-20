# D-Drive リリース手順の共通関数（bump-version.ps1 / check-release.ps1 から dot-source して使う）。
#
# docs/42_distribution.md §4.1（版の付け方）・§5.11-10（CHANGELOG ガード）・§5.12（破壊的変更の手続き）・
# §7 A-5（[Obsolete] の猶予 2 MINOR・MAJOR は年 1 回まで）を実装する。
#
# PowerShell 7 系（pwsh）/ Windows PowerShell 5.1 の両方で動くことを意識し、
# 三項演算子・null 合体演算子など PS7 専用構文は使わない（Tools/SpecWeb/push.ps1 と同じ方針）。

$ErrorActionPreference = 'Stop'

# --- パス解決 ---

# $ScriptRoot は Tools/Release を渡す想定（呼び出し側は $PSScriptRoot をそのまま渡す）。
function Get-DDriveRepoRoot {
    param([Parameter(Mandatory = $true)][string]$ScriptRoot)
    return (Split-Path -Parent (Split-Path -Parent $ScriptRoot))
}

# --- バージョン文字列の読み書き ---

function Get-PackageJsonVersion {
    param([Parameter(Mandatory = $true)][string]$PackageJsonPath)
    if (-not (Test-Path -LiteralPath $PackageJsonPath)) {
        throw "package.json が見つかりません: $PackageJsonPath"
    }
    $json = Get-Content -Raw -LiteralPath $PackageJsonPath
    $match = [regex]::Match($json, '"version"\s*:\s*"([^"]+)"')
    if (-not $match.Success) {
        throw ('package.json に "version" フィールドが見つかりません: {0}' -f $PackageJsonPath)
    }
    return $match.Groups[1].Value
}

function ConvertTo-SemVer {
    param([Parameter(Mandatory = $true)][string]$Version)
    $m = [regex]::Match($Version, '^(\d+)\.(\d+)\.(\d+)$')
    if (-not $m.Success) {
        throw "SemVer(MAJOR.MINOR.PATCH)形式ではありません: '$Version'"
    }
    return [pscustomobject]@{
        Major    = [int]$m.Groups[1].Value
        Minor    = [int]$m.Groups[2].Value
        Patch    = [int]$m.Groups[3].Value
        Original = $Version
    }
}

# A が B より大きければ 1、小さければ -1、等しければ 0。
function Compare-SemVer {
    param([Parameter(Mandatory = $true)]$A, [Parameter(Mandatory = $true)]$B)
    if ($A.Major -ne $B.Major) { return [Math]::Sign($A.Major - $B.Major) }
    if ($A.Minor -ne $B.Minor) { return [Math]::Sign($A.Minor - $B.Minor) }
    if ($A.Patch -ne $B.Patch) { return [Math]::Sign($A.Patch - $B.Patch) }
    return 0
}

# 'Major' | 'Minor' | 'Patch' | 'None' | 'Downgrade' のいずれかを返す。
function Get-SemVerBumpKind {
    param([Parameter(Mandatory = $true)]$OldVersion, [Parameter(Mandatory = $true)]$NewVersion)
    $cmp = Compare-SemVer -A $NewVersion -B $OldVersion
    if ($cmp -eq 0) { return 'None' }
    if ($cmp -lt 0) { return 'Downgrade' }
    if ($NewVersion.Major -ne $OldVersion.Major) { return 'Major' }
    if ($NewVersion.Minor -ne $OldVersion.Minor) { return 'Minor' }
    return 'Patch'
}

# --- 事前チェック ---

function Test-WorkingTreeClean {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)
    Push-Location -LiteralPath $RepoRoot
    try {
        $status = & git status --porcelain
        return [string]::IsNullOrWhiteSpace(($status -join "`n"))
    }
    finally {
        Pop-Location
    }
}

# CHANGELOG.md 全文から "## [Unreleased]" セクション（見出し行を含む、次の "## " 見出し手前まで）を返す。
# 見つからなければ $null。改行コード(CRLF/LF 混在)を吸収するため正規表現分割を使う。
function Get-ChangelogUnreleasedSection {
    param([Parameter(Mandatory = $true)][string]$ChangelogText)
    $lines = [regex]::Split($ChangelogText, "`r`n|`n")
    $startIndex = -1
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^##\s*\[Unreleased\]') { $startIndex = $i; break }
    }
    if ($startIndex -lt 0) { return $null }

    $endIndex = $lines.Length - 1
    for ($i = $startIndex + 1; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^##\s') { $endIndex = $i - 1; break }
    }
    return ($lines[$startIndex..$endIndex] -join "`n")
}

# セクション本文（Get-ChangelogUnreleasedSection の戻り値等）から "### 互換性" サブセクションの本文だけを返す。
function Get-CompatibilitySubsectionText {
    param([Parameter(Mandatory = $true)][string]$SectionText)
    $lines = [regex]::Split($SectionText, "`r`n|`n")
    $startIndex = -1
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^###\s*互換性') { $startIndex = $i; break }
    }
    if ($startIndex -lt 0) { return $null }

    $endIndex = $lines.Length - 1
    for ($i = $startIndex + 1; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^#{2,3}\s') { $endIndex = $i - 1; break }
    }
    if ($endIndex -lt ($startIndex + 1)) { return '' }
    return ($lines[($startIndex + 1)..$endIndex] -join "`n")
}

# [42_distribution.md] §4.1: "[Unreleased] に「互換性」節があり空でない" 事前チェック。
function Test-UnreleasedCompatibilityNonEmpty {
    param([Parameter(Mandatory = $true)][string]$ChangelogPath)
    $text = Get-Content -Raw -LiteralPath $ChangelogPath
    $section = Get-ChangelogUnreleasedSection -ChangelogText $text
    if ($null -eq $section) {
        return [pscustomobject]@{ Ok = $false; Message = 'CHANGELOG.md に "## [Unreleased]" 見出しが見つかりません。' }
    }
    $compat = Get-CompatibilitySubsectionText -SectionText $section
    if ($null -eq $compat -or [string]::IsNullOrWhiteSpace($compat)) {
        return [pscustomobject]@{ Ok = $false; Message = '[Unreleased] の "### 互換性" 節が無いか空です([42_distribution.md] §4.1)。' }
    }
    return [pscustomobject]@{ Ok = $true; Message = '[Unreleased] の "### 互換性" 節に記述があります。' }
}

# [42_distribution.md] §5.12: MAJOR を上げるときは docs/migrations/vN.md（移行ガイド）が要る。
function Test-MajorMigrationGuideExists {
    param([Parameter(Mandatory = $true)][string]$RepoRoot, [Parameter(Mandatory = $true)][int]$NewMajor)
    $guidePath = Join-Path $RepoRoot "docs/migrations/v$NewMajor.md"
    if (Test-Path -LiteralPath $guidePath) {
        return [pscustomobject]@{ Ok = $true; Message = "移行ガイドがあります: docs/migrations/v$NewMajor.md" }
    }
    return [pscustomobject]@{
        Ok      = $false
        Message = "MAJOR(v$NewMajor.0.0)へ上げますが移行ガイドがありません。docs/migrations/v$NewMajor.md を先に用意してください([42_distribution.md] §5.12)。"
    }
}

# [42_distribution.md] §5.6: DDriveProtocol.Current(ネットメッセージ形式の版)を変えた場合は
# CHANGELOG の [Unreleased] 互換性節に「ネットメッセージ」の記述が要る。
# 比較対象(直近の git tag 等)が分からない場合(初回リリース等)は判定できないため警告なしで通す。
function Test-ProtocolVersionChangeNoted {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ProtocolCsRelativePath,
        [Parameter(Mandatory = $true)][string]$ChangelogPath,
        [string]$CompareRef
    )

    if ([string]::IsNullOrWhiteSpace($CompareRef)) {
        return [pscustomobject]@{
            Ok      = $true
            Message = '比較対象(直近の git tag 等)が無いため ProtocolVersion の変更チェックはスキップしました(初回リリース等)。'
        }
    }

    Push-Location -LiteralPath $RepoRoot
    try {
        # [47_review_p_tickets_2026-09-20.md] P2-7(2026-09-20 修正) — `2>&1` を外し、失敗は try/catch で
        # 拾う(Windows PowerShell 5.1 + $ErrorActionPreference='Stop' の組み合わせで NativeCommandError に
        # なり $LASTEXITCODE 判定に到達できない問題を避ける)。
        $diffOutput = $null
        $diffFailed = $false
        try {
            $diffOutput = & git diff --name-only "$CompareRef" -- $ProtocolCsRelativePath
            if ($LASTEXITCODE -ne 0) { $diffFailed = $true }
        }
        catch {
            $diffFailed = $true
        }

        if ($diffFailed) {
            return [pscustomobject]@{
                Ok      = $true
                Message = "比較対象 '$CompareRef' を解決できなかったため ProtocolVersion の変更チェックはスキップしました。"
            }
        }
        $changed = -not [string]::IsNullOrWhiteSpace(($diffOutput -join ''))
        if (-not $changed) {
            return [pscustomobject]@{ Ok = $true; Message = 'DDriveProtocol.cs は前回リリース(' + $CompareRef + ')から変更されていません。' }
        }

        $text = Get-Content -Raw -LiteralPath $ChangelogPath
        $section = Get-ChangelogUnreleasedSection -ChangelogText $text
        if ($null -ne $section -and ($section -match 'ネットメッセージ')) {
            return [pscustomobject]@{
                Ok      = $true
                Message = 'DDriveProtocol.cs の変更が CHANGELOG の [Unreleased] に「ネットメッセージ」として記録されています。'
            }
        }
        return [pscustomobject]@{
            Ok      = $false
            Message = 'DDriveProtocol.Current(Foundation/Net/DDriveProtocol.cs)が変更されていますが、CHANGELOG.md の [Unreleased] 互換性節に「ネットメッセージ」の記述が見つかりません([42_distribution.md] §5.6)。'
        }
    }
    finally {
        Pop-Location
    }
}

# --- CHANGELOG ガード（[42_distribution.md] §5.11-10） ---

function Test-ChangelogGuard {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$BaseRef,
        # [47_review_p_tickets_2026-09-20.md] P2-6(2026-09-20 修正) — package.json の version が
        # $BaseRef から上がっているかの検査は「リリース PR」を想定した条件であって、日々の開発コミット
        # には合わない(スナップショットに差分があるだけで、リリースする前の通常コミットが必ず fail する
        # 設計になっていた)。既定は $false(CHANGELOG.md の変更有無だけを見る)にし、
        # check-release.ps1 の通常実行(リリース時、-GuardOnly 無し)だけが $true を渡す。
        [switch]$RequireVersionBump
    )

    Push-Location -LiteralPath $RepoRoot
    try {
        # [47_review_p_tickets_2026-09-20.md] P2-7(2026-09-20 修正) — Windows PowerShell 5.1 は
        # $ErrorActionPreference='Stop' の下で外部コマンドの stderr を `2>&1` でパイプに載せると
        # NativeCommandError として終了エラーになり、後続の $LASTEXITCODE 判定に到達できない
        # (shallow clone・detached HEAD・origin/main が無い CI で踏む)。`2>&1` を外し、
        # 呼び出し全体を try/catch で囲んで判定する。
        $diffOutput = $null
        $diffFailed = $false
        try {
            $diffOutput = & git diff --name-only "$BaseRef..HEAD"
            if ($LASTEXITCODE -ne 0) { $diffFailed = $true }
        }
        catch {
            $diffFailed = $true
        }

        if ($diffFailed) {
            return [pscustomobject]@{
                Ok      = $false
                Message = "git diff --name-only $BaseRef..HEAD に失敗しました(比較対象が存在しない可能性)。"
            }
        }

        $changedFiles = @($diffOutput | Where-Object { $_ -ne '' })
        $snapshotChanged = @($changedFiles | Where-Object { $_ -like 'Packages/com.ddrive.core/Tests/Editor/Compat/Snapshots/*' })
        if ($snapshotChanged.Count -eq 0) {
            return [pscustomobject]@{
                Ok      = $true
                Message = "互換性スナップショットの変更はありません($BaseRef..HEAD)。CHANGELOG ガードの対象外です。"
            }
        }

        $changelogChanged = $changedFiles -contains 'CHANGELOG.md'
        if (-not $changelogChanged) {
            return [pscustomobject]@{
                Ok      = $false
                Message = "互換性スナップショット($($snapshotChanged -join ', '))が変わっていますが、CHANGELOG.md が変わっていません([42_distribution.md] §5.11-10)。"
            }
        }

        # [47] P2-6(2026-09-20 修正) — package.json の version が上がっているかの検査は、
        # リリース時(check-release.ps1 の通常実行)にだけ要求する(-RequireVersionBump)。
        # 日々の開発コミット(run-ci.cmd の [1/8]、-GuardOnly)では、スナップショットの変更に
        # CHANGELOG.md の記述が伴っていることだけを見る(§5.11-10 の元の意図は「リリース PR」向け)。
        if (-not $RequireVersionBump) {
            return [pscustomobject]@{
                Ok      = $true
                Message = 'CHANGELOG.md の変更を確認しました(version の一致検査はリリース時〔check-release.ps1、-GuardOnly 無し〕だけで行います)。'
            }
        }

        # package.json の version が $BaseRef 時点より上がっているか(比較できないときは警告メッセージのみ付記)。
        $versionNote = ''
        try {
            $basePackageJsonLines = $null
            $baseShowFailed = $false
            try {
                $basePackageJsonLines = & git show "${BaseRef}:Packages/com.ddrive.core/package.json"
                if ($LASTEXITCODE -ne 0) { $baseShowFailed = $true }
            }
            catch {
                $baseShowFailed = $true
            }

            if (-not $baseShowFailed) {
                $baseMatch = [regex]::Match(($basePackageJsonLines -join "`n"), '"version"\s*:\s*"([^"]+)"')
                if ($baseMatch.Success) {
                    $headVersionStr = Get-PackageJsonVersion -PackageJsonPath (Join-Path $RepoRoot 'Packages/com.ddrive.core/package.json')
                    $baseSemVer = ConvertTo-SemVer -Version $baseMatch.Groups[1].Value
                    $headSemVer = ConvertTo-SemVer -Version $headVersionStr
                    if ((Compare-SemVer -A $headSemVer -B $baseSemVer) -le 0) {
                        return [pscustomobject]@{
                            Ok      = $false
                            Message = "package.json の version が $BaseRef 時点($($baseMatch.Groups[1].Value))から上がっていません(現在 $headVersionStr)。[42_distribution.md] §5.11-10。"
                        }
                    }
                    $versionNote = " version は $($baseMatch.Groups[1].Value) -> $headVersionStr に上がっています。"
                }
            }
            else {
                $versionNote = "($BaseRef 時点に package.json が無いため version の比較はスキップしました)"
            }
        }
        catch {
            $versionNote = "(version の比較中にエラーが発生したためスキップしました: $($_.Exception.Message))"
        }

        return [pscustomobject]@{
            Ok      = $true
            Message = "CHANGELOG.md の変更を確認しました。$versionNote"
        }
    }
    finally {
        Pop-Location
    }
}

# --- 同梱物の同期(Documentation~ / CHANGELOG.md、[42_distribution.md] §2.1・§2.2) ---

# ディレクトリ同士をミラー同期する(コピー+上書き、消えたファイルは削除)。Windows 専用(robocopy を使う)。
function Sync-MirrorDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [switch]$DryRun
    )
    if (-not (Test-Path -LiteralPath $Source)) {
        throw "同期元が見つかりません: $Source"
    }

    $roboArgs = @($Source, $Destination, '/MIR', '/NP', '/NJH')
    if ($DryRun) { $roboArgs += '/L' }

    $output = & robocopy @roboArgs
    $exitCode = $LASTEXITCODE
    # robocopy の終了コードはビットフラグ。8 以上は失敗(0-7 は「コピーした/余分を消した」等の成功系)。
    if ($exitCode -ge 8) {
        throw "robocopy が失敗しました(終了コード $exitCode): $Source -> $Destination`n$($output -join [Environment]::NewLine)"
    }
    return [pscustomobject]@{ Source = $Source; Destination = $Destination; ExitCode = $exitCode; Output = $output }
}

# 単一ファイルをハッシュ比較のうえコピーする(CHANGELOG.md 用。ミラーの削除は不要なため robocopy を使わない)。
function Sync-SingleFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [switch]$DryRun
    )
    if (-not (Test-Path -LiteralPath $Source)) {
        throw "同期元が見つかりません: $Source"
    }

    $needsCopy = $true
    if (Test-Path -LiteralPath $Destination) {
        $srcHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Source).Hash
        $dstHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Destination).Hash
        $needsCopy = ($srcHash -ne $dstHash)
    }

    if ($needsCopy -and -not $DryRun) {
        $destDir = Split-Path -Parent $Destination
        if (-not (Test-Path -LiteralPath $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
    }

    return [pscustomobject]@{ Source = $Source; Destination = $Destination; Changed = $needsCopy }
}

# BOM 無し UTF-8 で書き込む(Set-Content -Encoding UTF8 は Windows PowerShell 5.1 だと BOM 付きになり、
# pwsh 7 だと BOM 無しになる差異があるため、.NET の API で明示的に揃える)。
function Set-Utf8NoBomContent {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}
