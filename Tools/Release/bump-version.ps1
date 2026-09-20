<#
.SYNOPSIS
  D-Drive(com.ddrive.core)のリリース版を一括で上げる([42_distribution.md] §4.1)。

.DESCRIPTION
  以下を 1 回の実行でまとめて行う:
    1. 事前チェック(-SkipChecks で省略可。緊急時のみ):
       - 作業ツリーがクリーン(git status --porcelain が空)
       - CHANGELOG.md の "## [Unreleased]" に "### 互換性" 節があり、空でない
       - MAJOR を上げるときは docs/migrations/vN.md(移行ガイド)がある
       - DDriveProtocol.Current(Foundation/Net/DDriveProtocol.cs)を変えている場合、
         CHANGELOG の [Unreleased] 互換性節に「ネットメッセージ」の記述がある
    2. バージョンファイルの更新(新旧が同じなら何もしない):
       - Packages/com.ddrive.core/package.json の "version"
       - Packages/com.ddrive.core/Runtime/DDriveVersion.cs の Value
       - CHANGELOG.md: "## [Unreleased]" を "## [x.y.z] - YYYY-MM-DD" に変え、
         新しい空の "## [Unreleased]" を上に追加する
    3. 同梱物の同期(§2.1・§2.2。バージョンが変わらないときも常に実行する):
       - docs/DesignerManual/  -> Packages/com.ddrive.core/Documentation~/DesignerManual/(ミラー)
       - docs/ProgrammerManual/ -> Packages/com.ddrive.core/Documentation~/ProgrammerManual/(ミラー)
       - CHANGELOG.md          -> Packages/com.ddrive.core/CHANGELOG.md(単一ファイルコピー)
       .claude/skills は同期しない(消費側スキルは P-10 で Documentation~/skills/ddrive-consumer/ に別途用意する)。
    4. -Tag を付けたときだけ、明示パスで `git add` + `git commit -m "Release vX.Y.Z"` してから
       `git tag -a vX.Y.Z` を作成する(push はしない)。
       [47_review_p_tickets_2026-09-20.md] P1-7(2026-09-20 修正) — 以前は「版を書き換える(コミットしない)
       →タグを打つ」の順だったため、タグが指す HEAD は常に「版を上げる前」のコミットだった
       (`#vX.Y.Z` で参照した持ち込み先に旧版の package.json が届く実バグ)。`-NoCommit` を付けると
       コミットを省略し、従来どおり現在の HEAD にタグだけを打つ(タグ対象のコミットを自分で用意済みの
       場合の逃げ道)。

  -DryRun を付けると、上記のうち「ファイルへの書き込み」をすべて行わず、変更内容の表示だけを行う
  (同梱物の同期も robocopy の /L(一覧表示のみ)で差分を見せるだけにする)。

  PowerShell 7 系(pwsh)/ Windows PowerShell 5.1 の両方で動く(Tools/SpecWeb/push.ps1 と同じ方針)。
  git を書き換える操作は -Tag を明示したときの `git tag` だけで、push は一切行わない。

.PARAMETER Version
  新しいバージョン(例 "1.1.0")。-Part と排他。

.PARAMETER Part
  major|minor|patch のいずれか。現在の package.json の version からこの桁を 1 上げる
  (それより下の桁は 0 にリセットする通常の SemVer の意味)。

.PARAMETER DryRun
  実際にはファイルを書き換えず、変更される内容だけを表示する。

.PARAMETER Tag
  成功したら、変更ファイルを明示パスで `git add` + `git commit -m "Release vX.Y.Z"` してから
  `git tag -a vX.Y.Z` を作成する(push はしない)。-NoCommit と併用するとコミットを省略する。

.PARAMETER NoCommit
  -Tag と併用したとき、コミットを省略して現在の HEAD にそのままタグを打つ(従来動作。P1-7 参照)。
  -Tag を付けていないときは何もしない。

.PARAMETER SkipChecks
  事前チェックをすべて省略する(緊急時のみ。通常は使わない)。

.PARAMETER ProtocolCompareRef
  ProtocolVersion 変更チェックで比較する git の参照(既定: 直近の `git describe --tags --abbrev=0`)。
  タグが無いリポジトリ(初回リリース前)ではチェック自体をスキップする。

.EXAMPLE
  pwsh Tools/Release/bump-version.ps1 -Version 1.1.0 -DryRun

.EXAMPLE
  pwsh Tools/Release/bump-version.ps1 -Part minor -Tag
#>

[CmdletBinding(DefaultParameterSetName = 'ByVersion')]
param(
    [Parameter(ParameterSetName = 'ByVersion')]
    [string]$Version,

    [Parameter(ParameterSetName = 'ByPart')]
    [ValidateSet('major', 'minor', 'patch')]
    [string]$Part,

    [switch]$DryRun,
    [switch]$Tag,
    [switch]$NoCommit,
    [switch]$SkipChecks,
    [string]$ProtocolCompareRef
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseChecks.ps1')

if ([string]::IsNullOrWhiteSpace($Version) -and [string]::IsNullOrWhiteSpace($Part)) {
    throw '-Version か -Part のどちらかを指定してください。例: -Version 1.1.0 / -Part minor'
}

$repoRoot = Get-DDriveRepoRoot -ScriptRoot $PSScriptRoot
$packageJsonPath = Join-Path $repoRoot 'Packages/com.ddrive.core/package.json'
$versionCsPath = Join-Path $repoRoot 'Packages/com.ddrive.core/Runtime/DDriveVersion.cs'
$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'
$protocolCsRelativePath = 'Packages/com.ddrive.core/Foundation/Net/DDriveProtocol.cs'

$currentVersionStr = Get-PackageJsonVersion -PackageJsonPath $packageJsonPath
$currentVersion = ConvertTo-SemVer -Version $currentVersionStr

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $newVersion = ConvertTo-SemVer -Version $Version
}
else {
    switch ($Part) {
        'major' { $newVersion = ConvertTo-SemVer -Version ('{0}.0.0' -f ($currentVersion.Major + 1)) }
        'minor' { $newVersion = ConvertTo-SemVer -Version ('{0}.{1}.0' -f $currentVersion.Major, ($currentVersion.Minor + 1)) }
        'patch' { $newVersion = ConvertTo-SemVer -Version ('{0}.{1}.{2}' -f $currentVersion.Major, $currentVersion.Minor, ($currentVersion.Patch + 1)) }
    }
}

$bumpKind = Get-SemVerBumpKind -OldVersion $currentVersion -NewVersion $newVersion
if ($bumpKind -eq 'Downgrade') {
    throw "新しいバージョン($($newVersion.Original))が現在のバージョン($($currentVersion.Original))より小さいです。"
}

Write-Host '=== D-Drive bump-version ==='
Write-Host "現在の版: $($currentVersion.Original)"
Write-Host "新しい版: $($newVersion.Original) ($bumpKind)"
if ($DryRun) { Write-Host '(DryRun: ファイルは書き換えません)' }
Write-Host ''

# --- 事前チェック ---
if (-not $SkipChecks) {
    Write-Host '--- 事前チェック ---'
    $checks = New-Object System.Collections.Generic.List[object]

    $cleanTree = Test-WorkingTreeClean -RepoRoot $repoRoot
    $cleanMessage = 'OK'
    if (-not $cleanTree) { $cleanMessage = 'git status --porcelain に差分があります。コミットしてから実行してください。' }
    $checks.Add([pscustomobject]@{ Name = '作業ツリーがクリーン'; Ok = $cleanTree; Message = $cleanMessage })

    $compatCheck = Test-UnreleasedCompatibilityNonEmpty -ChangelogPath $changelogPath
    $checks.Add([pscustomobject]@{ Name = '[Unreleased] の互換性節'; Ok = $compatCheck.Ok; Message = $compatCheck.Message })

    if ($bumpKind -eq 'Major') {
        $migCheck = Test-MajorMigrationGuideExists -RepoRoot $repoRoot -NewMajor $newVersion.Major
        $checks.Add([pscustomobject]@{ Name = 'MAJOR の移行ガイド'; Ok = $migCheck.Ok; Message = $migCheck.Message })
    }

    $protoRef = $ProtocolCompareRef
    if ([string]::IsNullOrWhiteSpace($protoRef)) {
        $protoRef = $null
        try {
            $describeOutput = & git -C $repoRoot describe --tags --abbrev=0 2>&1
            if ($LASTEXITCODE -eq 0) { $protoRef = ($describeOutput | Select-Object -First 1) }
        }
        catch {
            $protoRef = $null
        }
    }
    $protoCheck = Test-ProtocolVersionChangeNoted -RepoRoot $repoRoot -ProtocolCsRelativePath $protocolCsRelativePath -ChangelogPath $changelogPath -CompareRef $protoRef
    $checks.Add([pscustomobject]@{ Name = 'ネットメッセージ版(ProtocolVersion)の変更記録'; Ok = $protoCheck.Ok; Message = $protoCheck.Message })

    $anyFailed = $false
    foreach ($c in $checks) {
        $mark = '[OK]'
        if (-not $c.Ok) { $mark = '[FAIL]'; $anyFailed = $true }
        Write-Host "$mark $($c.Name): $($c.Message)"
    }
    Write-Host ''

    if ($anyFailed) {
        throw '事前チェックに失敗しました。上記を解消してから再実行してください(緊急時のみ -SkipChecks で無視できます)。'
    }
}
else {
    Write-Host '(-SkipChecks: 事前チェックを省略しました)'
    Write-Host ''
}

# --- バージョンファイルの更新 ---
if ($bumpKind -ne 'None') {
    Write-Host '--- バージョンファイルの更新 ---'

    # package.json: "version" キーはトップレベルに 1 箇所しか無い(dependencies はパッケージ名がキーのため衝突しない)。
    $pkgJsonRaw = Get-Content -Raw -LiteralPath $packageJsonPath
    $newPkgJsonRaw = [regex]::Replace($pkgJsonRaw, '"version"\s*:\s*"[^"]+"', ('"version": "{0}"' -f $newVersion.Original), 1)
    Write-Host "package.json           : version $($currentVersion.Original) -> $($newVersion.Original)"
    if (-not $DryRun) { Set-Utf8NoBomContent -Path $packageJsonPath -Content $newPkgJsonRaw }

    # DDriveVersion.cs
    $versionCsRaw = Get-Content -Raw -LiteralPath $versionCsPath
    $newVersionCsRaw = [regex]::Replace($versionCsRaw, 'public const string Value = "[^"]+";', ('public const string Value = "{0}";' -f $newVersion.Original))
    Write-Host "DDriveVersion.cs       : Value   $($currentVersion.Original) -> $($newVersion.Original)"
    if (-not $DryRun) { Set-Utf8NoBomContent -Path $versionCsPath -Content $newVersionCsRaw }

    # CHANGELOG.md: 行配列に分解して組み立て直す(文字列 Replace だと改行コードの揺れで一致しないことがあるため)。
    $changelogRaw = Get-Content -Raw -LiteralPath $changelogPath
    $normalized = $changelogRaw -replace "`r`n", "`n"
    $lines = $normalized -split "`n"

    $unreleasedIdx = -1
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^##\s*\[Unreleased\]') { $unreleasedIdx = $i; break }
    }
    if ($unreleasedIdx -lt 0) {
        throw 'CHANGELOG.md に "## [Unreleased]" 見出しが見つかりません。'
    }

    $nextHeadingIdx = $lines.Length
    for ($i = $unreleasedIdx + 1; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^##\s') { $nextHeadingIdx = $i; break }
    }

    $today = Get-Date -Format 'yyyy-MM-dd'
    $before = @()
    if ($unreleasedIdx -gt 0) { $before = $lines[0..($unreleasedIdx - 1)] }
    $oldBody = @()
    if ($nextHeadingIdx -gt ($unreleasedIdx + 1)) { $oldBody = $lines[($unreleasedIdx + 1)..($nextHeadingIdx - 1)] }
    $rest = @()
    if ($nextHeadingIdx -lt $lines.Length) { $rest = $lines[$nextHeadingIdx..($lines.Length - 1)] }

    $newLines = New-Object System.Collections.Generic.List[string]
    $newLines.AddRange([string[]]$before)
    $newLines.Add('## [Unreleased]')
    $newLines.Add('')
    $newLines.Add('### 互換性')
    $newLines.Add('')
    $newLines.Add('- 破壊なし(このリリース以降の変更はまだありません)')
    $newLines.Add('')
    $newLines.Add(('## [{0}] - {1}' -f $newVersion.Original, $today))
    $newLines.AddRange([string[]]$oldBody)
    $newLines.AddRange([string[]]$rest)

    $newChangelogText = [string]::Join("`r`n", $newLines.ToArray())
    Write-Host "CHANGELOG.md           : [Unreleased] -> [$($newVersion.Original)] - $today(新しい空の [Unreleased] を追加)"
    if (-not $DryRun) { Set-Utf8NoBomContent -Path $changelogPath -Content $newChangelogText }

    Write-Host ''
}
else {
    Write-Host "バージョンファイルは既に $($newVersion.Original) のため変更しません。"
    Write-Host ''
}

# --- 同梱物の同期(Documentation~ / CHANGELOG.md) ---
Write-Host '--- 同梱物の同期(Documentation~ / CHANGELOG.md) ---'
$packageDir = Join-Path $repoRoot 'Packages/com.ddrive.core'
$designerSrc = Join-Path $repoRoot 'docs/DesignerManual'
$designerDst = Join-Path $packageDir 'Documentation~/DesignerManual'
$programmerSrc = Join-Path $repoRoot 'docs/ProgrammerManual'
$programmerDst = Join-Path $packageDir 'Documentation~/ProgrammerManual'
$migrationsSrc = Join-Path $repoRoot 'docs/migrations'
$migrationsDst = Join-Path $packageDir 'Documentation~/migrations'
$changelogDst = Join-Path $packageDir 'CHANGELOG.md'

$designerResult = Sync-MirrorDirectory -Source $designerSrc -Destination $designerDst -DryRun:$DryRun
Write-Host "docs/DesignerManual    -> Documentation~/DesignerManual  (robocopy 終了コード=$($designerResult.ExitCode))"
$programmerResult = Sync-MirrorDirectory -Source $programmerSrc -Destination $programmerDst -DryRun:$DryRun
Write-Host "docs/ProgrammerManual  -> Documentation~/ProgrammerManual(robocopy 終了コード=$($programmerResult.ExitCode))"
# [47_review_p_tickets_2026-09-20.md] P2-9(2026-09-20 修正) — 消費側ドキュメント(README/AGENTS_CONSUMER/
# ddrive-consumer スキル)が「破壊あり」の移行ガイドの参照先として docs/migrations/ を案内しているが、
# パッケージにも Documentation~ にも同梱されていなかった(持ち込み先からは辿れない)。DesignerManual/
# ProgrammerManual と同じミラー同期の対象に加える。
$migrationsResult = Sync-MirrorDirectory -Source $migrationsSrc -Destination $migrationsDst -DryRun:$DryRun
Write-Host "docs/migrations        -> Documentation~/migrations      (robocopy 終了コード=$($migrationsResult.ExitCode))"
# CHANGELOG.md 自体は今回の更新(あれば)を反映した後の内容を同期する。DryRun のときは元ファイルのままで比較する。
$changelogResult = Sync-SingleFile -Source $changelogPath -Destination $changelogDst -DryRun:$DryRun
Write-Host "CHANGELOG.md           -> Packages/com.ddrive.core/CHANGELOG.md (変更=$($changelogResult.Changed))"
Write-Host ''

if ($DryRun) {
    Write-Host '=== DryRun: 実際の書き込みは行っていません ==='
    exit 0
}

if ($Tag) {
    $tagName = "v$($newVersion.Original)"
    $committed = $false

    if (-not $NoCommit) {
        Write-Host '--- git commit(バージョン更新 + 同梱物の同期) ---'
        # [47] P1-7 — タグが指すコミットに版の更新を含めるため、-Tag のときは明示パスでコミットしてから
        # タグを打つ(git add -A/-. は使わず、このスクリプトが実際に書き換えた/同期したパスだけを add する)。
        $addPaths = @($packageJsonPath, $versionCsPath, $changelogPath, $designerDst, $programmerDst, $migrationsDst, $changelogDst) |
            Where-Object { Test-Path -LiteralPath $_ }

        if ($addPaths.Count -gt 0) {
            & git -C $repoRoot add -- $addPaths
            if ($LASTEXITCODE -ne 0) { throw "git add に失敗しました(終了コード $LASTEXITCODE)。" }
        }

        $stagedDiff = & git -C $repoRoot diff --cached --name-only
        if ($LASTEXITCODE -ne 0) { throw "git diff --cached に失敗しました(終了コード $LASTEXITCODE)。" }

        if (-not $stagedDiff -or $stagedDiff.Count -eq 0) {
            Write-Host '(コミットする変更がありません。バージョン・同梱物は既に最新のようです)'
        }
        else {
            & git -C $repoRoot commit -m "Release $tagName"
            if ($LASTEXITCODE -ne 0) { throw "git commit に失敗しました(終了コード $LASTEXITCODE)。" }
            $committed = $true
            Write-Host "コミットしました: Release $tagName"
        }
        Write-Host ''
    }
    else {
        Write-Host '(-NoCommit: コミットを省略します。現在の HEAD にそのままタグを打ちます)'
        Write-Host ''
    }

    Write-Host '--- git tag ---'
    & git -C $repoRoot tag -a $tagName -m "D-Drive $($newVersion.Original)"
    if ($LASTEXITCODE -ne 0) { throw "git tag に失敗しました(終了コード $LASTEXITCODE)。" }
    Write-Host "タグを作成しました: $tagName (push はしていません。'git push --tags' を別途実行してください)"
    if (-not $committed -and -not $NoCommit) {
        Write-Host '(コミット対象が無かったため、タグは実行前の HEAD を指しています)'
    }
    Write-Host ''
}

Write-Host '=== 完了 ==='
if ($Tag -and -not $NoCommit) {
    Write-Host 'バージョン更新・同梱物の同期はコミット済みです。他に変更されたファイルがあれば確認してください。'
}
else {
    Write-Host '変更されたファイルを確認し、コミットしてください(git add -A/-. は使わず、明示パスで add すること)。'
}
