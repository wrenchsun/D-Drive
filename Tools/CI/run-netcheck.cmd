@echo off
REM 2026-09-15 修正(6-7): このファイルは UTF-8(BOM 無し)で保存されている。cmd.exe は既定のコード
REM ページ(日本語 Windows では通常 932 = Shift-JIS)でバッチファイルを読むため、コードページが 65001
REM (UTF-8)以外だと以下の日本語コメント・echo 行が文字化けし、稀に「コマンドとして認識されない」
REM エラーになる(実行時に確認済み)。ファイル先頭でコードページを揃えることで回避する。
chcp 65001 >nul
setlocal EnableDelayedExpansion
REM D-Drive: 6-7 の 2 クライアント自動テスト。ローカル 2 プロセスで Loopback から差し替えた NGO ブリッジを確認する。
REM
REM 前提:
REM   - ビルド済みの Builds\DDriveNetCheck\DDriveNetCheck.exe が存在すること。
REM     Unity Editor で「Tools ^> D-Drive ^> Build ^> 実機確認用 Windows 開発ビルド」を先に実行する。
REM     このスクリプト自体はビルドしない。
REM   - pwsh(PowerShell 7+)が入っていること。判定ロジック一式を Tools\CI\Run-NetCheck.ps1 に置き、
REM     プロセス起動・待機・強制終了・2 プロセスのログ突き合わせを行うため、run-ci.cmd の
REM     Summarize-Results.ps1 呼び出しと違って pwsh は必須。無い場合はエラーで終了する。
REM
REM 使い方:
REM   Tools\CI\run-netcheck.cmd                 全シナリオ(pair0 / pair200 / latejoin / disconnect)
REM   Tools\CI\run-netcheck.cmd pair0            1 シナリオだけ実行
REM
REM .ps1 を主にしない理由は run-ci.cmd と同じ。この PC の PowerShell 5.1 は既定の実行ポリシーで
REM .ps1 実行がブロックされる、BOM 無し UTF-8 のコメントが化けることがあるため、内部で pwsh を
REM 明示的に -ExecutionPolicy Bypass 付きで呼ぶことでこれを回避している。
REM
REM 半角の丸括弧はコメント中で使わない。cmd.exe のパーサが日本語コメント直後の半角の丸括弧を
REM 誤って別コマンドの開始と解釈することがあるため、全角の（）に統一する。

set "EXE=%CD%\Builds\DDriveNetCheck\DDriveNetCheck.exe"
if not exist "%EXE%" (
    echo [ERROR] ビルド済み exe が見つかりません: %EXE%
    echo   先に Unity Editor で「Tools ^> D-Drive ^> Build ^> 実機確認用 Windows 開発ビルド」を実行してください。
    exit /b 1
)

where pwsh >nul 2>nul
if not "%ERRORLEVEL%"=="0" (
    echo [ERROR] pwsh が見つかりません。PowerShell 7 以上をインストールしてください。
    echo   判定ロジック一式は Tools\CI\Run-NetCheck.ps1 にあり、pwsh の実行が必要です。
    echo   https://github.com/PowerShell/PowerShell からインストールできます。
    exit /b 1
)

set "SCENARIO=%~1"

echo === D-Drive 6-7 NetCheck ===
echo Exe        : %EXE%
echo Project    : %CD%
if not "%SCENARIO%"=="" echo Scenario   : %SCENARIO%
echo.
echo [注意] このプロジェクトを Unity Editor で開いていても実行はできますが、127.0.0.1 の UDP ポート
echo        7801/7811/7821/7831 が他プロセスで使用中でないことを確認してください。
echo.

pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-NetCheck.ps1" -OnlyScenario "%SCENARIO%"
set "OVERALL_EXIT=%ERRORLEVEL%"

exit /b %OVERALL_EXIT%
