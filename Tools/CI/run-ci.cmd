@echo off
setlocal EnableDelayedExpansion
REM D-Drive: CI(.github\workflows\ci.yml)と同じ検査をこの PC でローカル実行する。
REM
REM 前提:
REM   - Unity Editor 6000.3.13f1 がインストール済み(Unity Hub からアクティベート済み)
REM   - このリポジトリを "Unity Editor で開いていない"(同じプロジェクトを 2 つの Unity で
REM     開くことはできないため。開いている場合は Unity を閉じるか、別の checkout で実行する)
REM   - pwsh(PowerShell 7+)が入っている(結果サマリの整形に使う。無ければ Summarize は自動でスキップ)
REM
REM 使い方:
REM   Tools\CI\run-ci.cmd
REM   Tools\CI\run-ci.cmd "C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe"
REM
REM .ps1 を主にしない理由: この PC の PowerShell 5.1(powershell.exe)は既定の実行ポリシーで
REM .ps1 実行がブロックされる/BOM 無し UTF-8 のコメントが化けることがあるため、.cmd を主経路にする。
REM (内部で pwsh を明示的に -ExecutionPolicy Bypass 付きで呼ぶので、この問題を回避している)

set "UNITY_EXE=%~1"
if "%UNITY_EXE%"=="" set "UNITY_EXE=C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe"

if not exist "%UNITY_EXE%" (
    echo [ERROR] Unity実行ファイルが見つかりません: %UNITY_EXE%
    echo   引数で明示するか、環境変数 UNITY_EXE を設定してください。例:
    echo   Tools\CI\run-ci.cmd "C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe"
    exit /b 1
)

REM %CD% はこのバッチを呼んだディレクトリ。リポジトリ直下から実行することを前提にする。
set "PROJECT_PATH=%CD%"
set "RESULTS_DIR=%PROJECT_PATH%\TestResults"
if not exist "%RESULTS_DIR%" mkdir "%RESULTS_DIR%"

echo === D-Drive ローカル CI ===
echo Unity      : %UNITY_EXE%
echo Project    : %PROJECT_PATH%
echo Results    : %RESULTS_DIR%
echo.
echo [注意] Unity Editor でこのプロジェクトを開いたままだと失敗します(多重起動不可)。
echo.

set "OVERALL_EXIT=0"

echo [1/6] Validation (CI.ValidateAll) を実行します...
"%UNITY_EXE%" -batchmode -nographics -quit -projectPath "%PROJECT_PATH%" -executeMethod DDrive.Editor.CI.ValidateAll -ddriveOutput "%RESULTS_DIR%\ddrive-validation.junit.xml" -logFile "%RESULTS_DIR%\validate.log"
if not "%ERRORLEVEL%"=="0" (
    echo [FAIL] Validation で Error が見つかりました。ログ: %RESULTS_DIR%\validate.log
    set "OVERALL_EXIT=1"
) else (
    echo [OK] Validation
)
echo.

echo [2/6] Asset ID 再生成 ^(CI.RegenerateIds^) + git diff 確認 を実行します...
"%UNITY_EXE%" -batchmode -nographics -quit -projectPath "%PROJECT_PATH%" -executeMethod DDrive.Editor.CI.RegenerateIds -logFile "%RESULTS_DIR%\regenerate-ids.log"
if not "%ERRORLEVEL%"=="0" (
    echo [FAIL] Asset ID 再生成に失敗しました^(重複 ID 等^)。ログ: %RESULTS_DIR%\regenerate-ids.log
    set "OVERALL_EXIT=1"
) else (
    git diff --exit-code
    if not "!ERRORLEVEL!"=="0" (
        echo [FAIL] ID 再生成でコミットされていない差分が出ました。Assets\Generated だけでなく、
        echo        新規 Id が割り当てられた Data アセット自体の変更も含みます。ローカルで
        echo        "Tools/D-Drive/Generate/Regenerate Asset IDs" を実行してからコミットしてください。
        set "OVERALL_EXIT=1"
    ) else (
        echo [OK] Asset ID 差分なし
    )
)
echo.

echo [3/6] EditMode テストを実行します...
"%UNITY_EXE%" -batchmode -nographics -projectPath "%PROJECT_PATH%" -runTests -testPlatform EditMode -testResults "%RESULTS_DIR%\editmode-results.xml" -logFile "%RESULTS_DIR%\editmode.log"
if not "%ERRORLEVEL%"=="0" (
    echo [FAIL] EditMode テスト。ログ: %RESULTS_DIR%\editmode.log
    set "OVERALL_EXIT=1"
) else (
    echo [OK] EditMode テスト
)
echo.

echo [4/6] PlayMode テストを実行します...
"%UNITY_EXE%" -batchmode -nographics -projectPath "%PROJECT_PATH%" -runTests -testPlatform PlayMode -testResults "%RESULTS_DIR%\playmode-results.xml" -logFile "%RESULTS_DIR%\playmode.log"
if not "%ERRORLEVEL%"=="0" (
    echo [FAIL] PlayMode テスト。ログ: %RESULTS_DIR%\playmode.log
    set "OVERALL_EXIT=1"
) else (
    echo [OK] PlayMode テスト
)
echo.

REM 6-2(パフォーマンス計測・0 alloc 検証): DDrive.Tests.Performance(category=Performance)のみを
REM PlayMode で実行する。GitHub Actions 側の CI(.github\workflows\ci.yml)は P7 末の CI 導入まで
REM このステップを実処理化しない(2026-09-15 ユーザー決定)ため、当面はローカル実行がこの一式の
REM 唯一の実行経路になる。
echo [5/6] Performance テスト(0 alloc 検証、DDrive.Tests.Performance)を実行します...
"%UNITY_EXE%" -batchmode -nographics -projectPath "%PROJECT_PATH%" -runTests -testPlatform PlayMode -testCategory "Performance" -testResults "%RESULTS_DIR%\performance-results.xml" -logFile "%RESULTS_DIR%\performance.log"
if not "%ERRORLEVEL%"=="0" (
    echo [FAIL] Performance テスト。ログ: %RESULTS_DIR%\performance.log
    set "OVERALL_EXIT=1"
) else (
    echo [OK] Performance テスト
)
echo.

REM 6-7(任意ステップ): ビルド済みの Builds\DDriveNetCheck\DDriveNetCheck.exe があるときだけ、
REM 2 クライアント自動テスト(Tools\CI\run-netcheck.cmd)を実行する。ビルドが無い場合は
REM(このステップは Unity Editor でのビルドを前提にしており、run-ci.cmd 自体はビルドしないため)
REM スキップするだけで CI 全体を失敗させない([11_tasks.md] 6-7、CI 本稼働は P7 末のため任意ステップ扱い)。
if exist "%PROJECT_PATH%\Builds\DDriveNetCheck\DDriveNetCheck.exe" (
    echo [6/6] NetCheck^(6-7、2 クライアント自動テスト^)を実行します...
    call "%~dp0run-netcheck.cmd"
    if not "%ERRORLEVEL%"=="0" (
        echo [FAIL] NetCheck。ログ: %RESULTS_DIR%\NetCheck\summary.md
        set "OVERALL_EXIT=1"
    ) else (
        echo [OK] NetCheck
    )
) else (
    echo [6/6] NetCheck: ビルド済み exe が無いためスキップします
    echo        ^(Tools ^> D-Drive ^> Build ^> 実機確認用 Windows 開発ビルド の後に Tools\CI\run-netcheck.cmd を単体実行できます^)
)
echo.

where pwsh >nul 2>nul
if "%ERRORLEVEL%"=="0" (
    echo === 結果サマリ ===
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0Summarize-Results.ps1" ^
        -ValidationJUnitPath "%RESULTS_DIR%\ddrive-validation.junit.xml" ^
        -EditModeResultsPath "%RESULTS_DIR%\editmode-results.xml" ^
        -PlayModeResultsPath "%RESULTS_DIR%\playmode-results.xml" ^
        -PerformanceResultsPath "%RESULTS_DIR%\performance-results.xml" ^
        -NetCheckResultsPath "%RESULTS_DIR%\NetCheck\results.json"
) else (
    echo [注意] pwsh が見つからないため、結果サマリの整形はスキップしました。
    echo         %RESULTS_DIR% 配下の XML / ログを直接確認してください。
)

echo.
if "%OVERALL_EXIT%"=="0" (
    echo === すべて green です ===
) else (
    echo === 失敗があります^(上のログを確認してください^) ===
)

exit /b %OVERALL_EXIT%
