@echo off
REM This file is ASCII only on purpose (2026-10-06, docs/59).
REM cmd.exe reads a batch file with the console code page (932 on Japanese Windows), so a UTF-8 file
REM with Japanese text can print garbage. The detailed Japanese description lives in docs/29 section 24
REM and docs/60 section 2.2. Judging logic is in Tools\CI\Run-NetCheck.ps1 (unchanged).
setlocal EnableDelayedExpansion
REM D-Drive NetCheck: automatic multi-process network check over 127.0.0.1 (Loopback and NGO bridges).
REM
REM Prerequisites:
REM   - Builds\DDriveNetCheck\DDriveNetCheck.exe must exist. Build it first in the Unity Editor with the
REM     menu Tools ^> D-Drive ^> Build ^> (Windows development build for device check).
REM     This script does not build.
REM   - pwsh (PowerShell 7 or later) must be installed. The judging logic, process start/wait/kill and
REM     log comparison are all in Tools\CI\Run-NetCheck.ps1, so pwsh is required.
REM
REM Usage:
REM   Tools\CI\run-netcheck.cmd                 all scenarios (pair0 / pair200 / latejoin / disconnect /
REM                                              quad0 / quad_latejoin / quad_leave / quad_hostquit /
REM                                              host_migration)
REM   Tools\CI\run-netcheck.cmd pair0            one scenario only (1 Host + 1 Client)
REM   Tools\CI\run-netcheck.cmd quad0            1 Host + 3 Clients, all at 0 ms
REM   Tools\CI\run-netcheck.cmd quad_latejoin    1 Host + 3 Clients, one joins 12 s late
REM   Tools\CI\run-netcheck.cmd quad_leave       1 Host + 3 Clients, one leaves normally first
REM   Tools\CI\run-netcheck.cmd quad_hostquit    1 Host + 3 Clients, the Host quits first
REM   Tools\CI\run-netcheck.cmd host_migration   old Host quits at 12 s, Client1 is promoted to Host,
REM                                              Client2 and Client3 reconnect as followers
REM
REM Exit code: 0 = all scenarios passed, otherwise the exit code of Run-NetCheck.ps1 (1 if the exe or pwsh is missing).
REM Do not use parentheses in REM comments; cmd.exe can misread them.

set "EXE=%CD%\Builds\DDriveNetCheck\DDriveNetCheck.exe"
if not exist "%EXE%" (
    echo [ERROR] Built exe not found: %EXE%
    echo   Build it first in the Unity Editor: Tools ^> D-Drive ^> Build ^> Windows development build for device check.
    exit /b 1
)

where pwsh >nul 2>nul
if not "%ERRORLEVEL%"=="0" (
    echo [ERROR] pwsh not found. Install PowerShell 7 or later.
    echo   The judging logic is in Tools\CI\Run-NetCheck.ps1 and needs pwsh.
    echo   https://github.com/PowerShell/PowerShell
    exit /b 1
)

set "SCENARIO=%~1"

echo === D-Drive NetCheck ===
echo Exe        : %EXE%
echo Project    : %CD%
if not "%SCENARIO%"=="" echo Scenario   : %SCENARIO%
echo.
echo [NOTE] You can run this while the project is open in the Unity Editor, but make sure the UDP ports
echo        7801/7811/7821/7831 for 1v1, 7841/7851/7861/7871 for quad and 7881 for host_migration
echo        on 127.0.0.1 are not used by another process.
echo.

pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-NetCheck.ps1" -OnlyScenario "%SCENARIO%"
set "OVERALL_EXIT=%ERRORLEVEL%"

exit /b %OVERALL_EXIT%
