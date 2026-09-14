<#
.SYNOPSIS
  デザイナーマニュアル（docs/DesignerManual）を Tools/SpecWeb/html/manual へ再生成してから
  `clasp push` する。

.DESCRIPTION
  docs/32_spec_web.md「マニュアル配信」節: 生成物（Tools/SpecWeb/html/manual/*.html・
  src/ManualPages.js）は git にコミットする方針だが、docs/DesignerManual を更新してから
  再生成を忘れる（ドリフト）事故を避けるため、`clasp push` の前に必ず
  tools/build-manual.js を実行する。これを毎回手で覚える代わりにこのスクリプトを使う。

  Node が PATH に無い環境でも動くよう、まず `node` を試し、無ければ
  `C:\Program Files\nodejs\node.exe` にフォールバックする（README.md §1 と同じ扱い）。

.EXAMPLE
  cd Tools/SpecWeb
  ./push.ps1
#>

$ErrorActionPreference = 'Stop'

$specWebDir = $PSScriptRoot
$buildScript = Join-Path $specWebDir 'tools/build-manual.js'

function Resolve-NodeExe {
  $onPath = Get-Command node -ErrorAction SilentlyContinue
  if ($onPath) { return $onPath.Source }
  $fallback = 'C:\Program Files\nodejs\node.exe'
  if (Test-Path $fallback) { return $fallback }
  throw 'node が見つかりません。README.md §1 の手順で Node.js をインストールしてください。'
}

$nodeExe = Resolve-NodeExe

Write-Host "[push.ps1] マニュアルを再生成します: $buildScript"
& $nodeExe $buildScript
if ($LASTEXITCODE -ne 0) {
  throw "[push.ps1] build-manual.js が失敗しました（終了コード $LASTEXITCODE）。clasp push は行いません。"
}

Write-Host '[push.ps1] git の差分を確認してください（生成物が変わっていたらコミットも忘れずに）:'
Push-Location (Join-Path $specWebDir '../..')
try {
  & git status --porcelain -- Tools/SpecWeb/html/manual Tools/SpecWeb/src/ManualPages.js
} finally {
  Pop-Location
}

Write-Host '[push.ps1] clasp push を実行します...'
Push-Location $specWebDir
try {
  & clasp push
} finally {
  Pop-Location
}
