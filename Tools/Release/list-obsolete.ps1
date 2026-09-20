<#
.SYNOPSIS
  Packages/com.ddrive.core/{Foundation,Runtime,Editor} の [Obsolete] 属性を棚卸しし、
  docs/migrations/next-major.md に一覧を書き出す([42_distribution.md] §6 P-9・§7 A-5)。

.DESCRIPTION
  静的な正規表現スキャン(C# コンパイラや Unity を使わない。行コメント "//" は除外するが、
  ブロックコメント "/* ... */" の中は判定できない簡易実装)。

  [Obsolete] のメッセージに "since x.y.z"(付与したパッケージ版)が書かれていれば、それを基準に
  「削除できるのは付与から少なくとも 2 MINOR を経た次の MAJOR から」という [42_distribution.md] §5.12・
  §7 A-5 のルールを一覧に添える。実際に削除してよいかどうかの最終判断は人が行う(本スクリプトは
  棚卸し=可視化のみ)。

  なぜ C# の Editor メニューではなく PowerShell スクリプトにしたか: Unity を起動しなくても
  (Unity MCP が繋がっていなくても)いつでも実行でき、CI からも呼びやすいため
  ([42_distribution.md] §6 P-9 実装メモ参照)。

.PARAMETER DryRun
  ファイルに書き込まず、生成される内容を標準出力に表示するだけにする。

.EXAMPLE
  pwsh Tools/Release/list-obsolete.ps1
#>

param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scanRootRelatives = @(
    'Packages/com.ddrive.core/Foundation',
    'Packages/com.ddrive.core/Runtime',
    'Packages/com.ddrive.core/Editor'
)

$entries = New-Object System.Collections.Generic.List[object]

foreach ($relative in $scanRootRelatives) {
    $root = Join-Path $repoRoot $relative
    if (-not (Test-Path -LiteralPath $root)) { continue }

    $files = Get-ChildItem -LiteralPath $root -Recurse -Filter '*.cs' -File
    foreach ($file in $files) {
        $lines = Get-Content -LiteralPath $file.FullName
        for ($i = 0; $i -lt $lines.Length; $i++) {
            $line = $lines[$i]
            $trimmed = $line.TrimStart()
            if ($trimmed.StartsWith('//')) { continue }

            $m = [regex]::Match($line, '\[Obsolete\s*\(\s*"(?<msg>(?:[^"\\]|\\.)*)"')
            if (-not $m.Success) { continue }

            $message = $m.Groups['msg'].Value -replace '\\"', '"'
            $sinceMatch = [regex]::Match($message, 'since\s+(?<ver>\d+\.\d+\.\d+)')
            $since = $null
            if ($sinceMatch.Success) { $since = $sinceMatch.Groups['ver'].Value }

            # 属性の直後にある宣言行を「対象シンボル」の目安として拾う(空行・他の属性行はスキップ)。
            $symbolLine = $null
            for ($j = $i + 1; $j -lt $lines.Length; $j++) {
                $candidate = $lines[$j].Trim()
                if ($candidate.Length -eq 0) { continue }
                if ($candidate.StartsWith('[')) { continue }
                if ($candidate.StartsWith('//')) { continue }
                $symbolLine = $candidate
                break
            }

            $relativePath = $file.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
            $entries.Add([pscustomobject]@{
                    File    = $relativePath
                    Line    = $i + 1
                    Symbol  = $symbolLine
                    Message = $message
                    Since   = $since
                })
        }
    }
}

$generatedAt = (Get-Date).ToString('yyyy-MM-dd HH:mm')

$header = @(
    '# 次の MAJOR で削除する候補（[Obsolete] 棚卸し）'
    ''
    '> `Tools/Release/list-obsolete.ps1` が自動生成する。手で編集しない(リリース準備のときに再実行して上書きする)。'
    '> 関連: [../42_distribution.md](../42_distribution.md) §5.4・§5.12・§7 A-5（`[Obsolete]` の猶予は付与から少なくとも 2 MINOR、MAJOR は年 1 回まで）・[../12_review.md](../12_review.md) §7（リリース手順）。'
    ''
    "生成日時: $generatedAt"
    ''
    '`Packages/com.ddrive.core/{Foundation,Runtime,Editor}` の `[Obsolete(...)]` 属性を正規表現で静的に走査した一覧。'
    '行コメント(`//`)中の言及は対象外。ブロックコメント(`/* ... */`)の中にある場合は誤検出し得る(簡易スキャンのため。'
    '実際に削除してよいかは必ず人が最終確認すること)。'
    ''
)

$body = New-Object System.Collections.Generic.List[string]
if ($entries.Count -eq 0) {
    $body.Add('現在、`[Obsolete]` 属性が付与された公開 API はありません。')
    $body.Add('')
}
else {
    $body.Add('| ファイル:行 | 対象(推定) | メッセージ | 付与版(since) | 削除できる最短の条件 |')
    $body.Add('|---|---|---|---|---|')
    $sorted = $entries | Sort-Object File, Line
    foreach ($e in $sorted) {
        $symbolDisplay = '(不明)'
        if ($e.Symbol) { $symbolDisplay = '`' + $e.Symbol + '`' }
        $sinceDisplay = '(記載なし)'
        $minCondition = '(`since x.y.z` が無いため判定不可。メッセージに追記してください)'
        if ($e.Since) {
            $sinceDisplay = $e.Since
            $sinceMajor = [int]($e.Since.Split('.')[0])
            $minCondition = "$($sinceMajor + 1).0.0 以降の MAJOR(かつ $($e.Since) から 2 回以上の MINOR リリースを経ていること)"
        }
        $messageDisplay = $e.Message -replace '\|', '\|'
        $body.Add("| ``$($e.File):$($e.Line)`` | $symbolDisplay | $messageDisplay | $sinceDisplay | $minCondition |")
    }
    $body.Add('')
}

$footer = @(
    '## 運用ルール'
    ''
    '- 新しく `[Obsolete]` を付けるときは、メッセージに `since x.y.z`(付与したパッケージ版)を含める([../12_review.md](../12_review.md) §3・§7)。'
    '- 削除できるのは「付与から少なくとも 2 回の MINOR リリースを経た」後の次の MAJOR([../42_distribution.md](../42_distribution.md) §5.12・§7 A-5)。'
    '- MAJOR リリースの前に `Tools/Release/list-obsolete.ps1` を再実行し、削除候補を確認してから [../42_distribution.md](../42_distribution.md) §5.12 の手続きに進む。'
    ''
)

$content = (@($header) + @($body) + @($footer)) -join "`r`n"
$content = $content -replace "`r`n`r`n$", "`r`n"

$outputPath = Join-Path $repoRoot 'docs/migrations/next-major.md'

if ($DryRun) {
    Write-Host "=== DryRun: $outputPath は書き換えません ==="
    Write-Host $content
}
else {
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($outputPath, $content, $encoding)
    Write-Host "書き出しました: $outputPath ([Obsolete] 該当 $($entries.Count) 件)"
}
