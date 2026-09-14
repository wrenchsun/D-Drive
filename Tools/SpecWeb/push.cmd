@echo off
rem Regenerate the designer manual (tools/build-manual.js) and then run `clasp push`.
rem Same steps as push.ps1, but runs from cmd.exe without the PowerShell execution policy.
rem Messages are ASCII only so that cmd.exe code pages do not garble them.
rem Usage: double-click, or run `push.cmd` in Tools\SpecWeb.
setlocal
cd /d "%~dp0"

set "NODE_EXE=node"
where node >nul 2>nul
if errorlevel 1 (
  set "NODE_EXE=C:\Program Files\nodejs\node.exe"
  rem clasp.cmd itself calls "node", so put Node on PATH for this window too.
  set "PATH=C:\Program Files\nodejs;%PATH%"
)

echo [push.cmd] Regenerating manual pages...
"%NODE_EXE%" tools\build-manual.js
if errorlevel 1 (
  echo [push.cmd] build-manual.js failed. clasp push was NOT run.
  goto :end
)

echo [push.cmd] Changed generated files (commit them if any are listed):
git -C ..\.. status --porcelain -- Tools/SpecWeb/html/manual Tools/SpecWeb/src/ManualPages.js

echo [push.cmd] Running clasp push...
call clasp push

:end
echo.
pause
endlocal
