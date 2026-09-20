@echo off
setlocal EnableDelayedExpansion
REM D-Drive: 持ち込み先プロジェクトで D-Drive の互換性・品質チェックをローカル実行するテンプレート。
REM 開発リポジトリの Tools/CI/run-ci.cmd と同じ考え方の、持ち込み先向けの縮小版。
REM CHANGELOG ガード・Performance テスト・NetCheck は開発リポジトリ専用のため含めない。
REM
REM 使い方:
REM   1. このファイルと ddrive-ci.yml・README.md を持ち込み先プロジェクトの Tools/CI/ 等へコピーする
REM      (パッケージの Tools~/CI/ は Unity から不可視のフォルダのため、そのままでは使えない)
REM   2. 持ち込み先のリポジトリ直下から実行する: Tools\CI\run-ddrive-ci.cmd
REM      (Unity の場所を明示する場合: Tools\CI\run-ddrive-ci.cmd "C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe")
REM
REM 前提:
REM   - 対象バージョンの Unity Editor がインストール済み
REM   - このプロジェクトを "Unity Editor で開いていない"(多重起動不可)

set "UNITY_EXE=%~1"
if "%UNITY_EXE%"=="" set "UNITY_EXE=C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe"

if not exist "%UNITY_EXE%" (
    echo [ERROR] Unity実行ファイルが見つかりません: %UNITY_EXE%
    echo   引数で明示するか、実際にインストールした Unity の版に合わせて書き換えてください。例:
    echo   Tools\CI\run-ddrive-ci.cmd "C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe"
    exit /b 1
)

REM %CD% はこのバッチを呼んだディレクトリ。リポジトリ直下から実行することを前提にする。
set "PROJECT_PATH=%CD%"
set "RESULTS_DIR=%PROJECT_PATH%\TestResults"
if not exist "%RESULTS_DIR%" mkdir "%RESULTS_DIR%"

echo === D-Drive 消費側 CI ===
echo Unity      : %UNITY_EXE%
echo Project    : %PROJECT_PATH%
echo Results    : %RESULTS_DIR%
echo.
echo [注意] Unity Editor でこのプロジェクトを開いたままだと失敗します(多重起動不可)。
echo.

set "OVERALL_EXIT=0"

echo [1/4] マイグレーションの未適用チェック (DDrive.Editor.CI.MigrateCheck) を実行します...
"%UNITY_EXE%" -batchmode -nographics -quit -projectPath "%PROJECT_PATH%" -executeMethod DDrive.Editor.CI.MigrateCheck -logFile "%RESULTS_DIR%\ddrive-migrate-check.log"
if not "%ERRORLEVEL%"=="0" (
    echo [FAIL] 未適用のマイグレーションがあります。Tools ^> D-Drive ^> Update ^> 更新ウィンドウ で「更新を適用」を実行してください。ログ: %RESULTS_DIR%\ddrive-migrate-check.log
    set "OVERALL_EXIT=1"
) else (
    echo [OK] マイグレーション未適用チェック
)
echo.

echo [2/4] Validation (DDrive.Editor.CI.ValidateAll) を実行します...
"%UNITY_EXE%" -batchmode -nographics -quit -projectPath "%PROJECT_PATH%" -executeMethod DDrive.Editor.CI.ValidateAll -ddriveOutput "%RESULTS_DIR%\ddrive-validation.junit.xml" -logFile "%RESULTS_DIR%\ddrive-validate.log"
if not "%ERRORLEVEL%"=="0" (
    echo [FAIL] Validation で Error が見つかりました。ログ: %RESULTS_DIR%\ddrive-validate.log
    set "OVERALL_EXIT=1"
) else (
    echo [OK] Validation
)
echo.

echo [3/4] Asset ID 再生成 (DDrive.Editor.CI.RegenerateIds) + git diff 確認 を実行します...
"%UNITY_EXE%" -batchmode -nographics -quit -projectPath "%PROJECT_PATH%" -executeMethod DDrive.Editor.CI.RegenerateIds -logFile "%RESULTS_DIR%\ddrive-regenerate-ids.log"
if not "%ERRORLEVEL%"=="0" (
    echo [FAIL] Asset ID 再生成に失敗しました(重複 ID 等)。ログ: %RESULTS_DIR%\ddrive-regenerate-ids.log
    set "OVERALL_EXIT=1"
) else (
    git diff --exit-code
    if not "!ERRORLEVEL!"=="0" (
        echo [FAIL] ID 再生成でコミットされていない差分が出ました。生成コード(Assets\Generated 等)や
        echo        新規 Id が割り当てられた Data アセットの変更をコミットしてください。
        set "OVERALL_EXIT=1"
    ) else (
        echo [OK] Asset ID 差分なし
    )
)
echo.

REM D-Drive 自身のテスト(EditMode/PlayMode)は、このプロジェクトでテストを有効化している場合
REM (セットアップウィザードの「テストを有効化する」で ON にした場合)だけ意味を持つ。
REM 有効化していない場合は Test Runner に D-Drive のテストが出てこないため、このステップは
REM このプロジェクト自身のテストと同じ実行方法に読み替えて使ってください。
echo [4/4] EditMode/PlayMode テストを実行します...
"%UNITY_EXE%" -batchmode -nographics -projectPath "%PROJECT_PATH%" -runTests -testPlatform EditMode -testResults "%RESULTS_DIR%\ddrive-editmode-results.xml" -logFile "%RESULTS_DIR%\ddrive-editmode.log"
if not "%ERRORLEVEL%"=="0" (
    echo [FAIL] EditMode テスト。ログ: %RESULTS_DIR%\ddrive-editmode.log
    set "OVERALL_EXIT=1"
) else (
    echo [OK] EditMode テスト
)
"%UNITY_EXE%" -batchmode -nographics -projectPath "%PROJECT_PATH%" -runTests -testPlatform PlayMode -testResults "%RESULTS_DIR%\ddrive-playmode-results.xml" -logFile "%RESULTS_DIR%\ddrive-playmode.log"
if not "%ERRORLEVEL%"=="0" (
    echo [FAIL] PlayMode テスト。ログ: %RESULTS_DIR%\ddrive-playmode.log
    set "OVERALL_EXIT=1"
) else (
    echo [OK] PlayMode テスト
)
echo.

echo.
if "%OVERALL_EXIT%"=="0" (
    echo === すべて green です ===
) else (
    echo === 失敗があります(上のログを確認してください) ===
)

exit /b %OVERALL_EXIT%
