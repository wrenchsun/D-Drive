@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion
REM D-Drive: run the same checks as CI (.github\workflows\ci.yml) locally on this PC.
REM
REM This file is ASCII only on purpose. It used to contain Japanese text in UTF-8 without BOM.
REM cmd.exe reads batch files in the console code page and mis-splits UTF-8 text:
REM measured on 2026-10-06, lines were cut at wrong byte offsets, comment fragments were
REM executed as commands, no step ran, and the script still printed all green and returned 0.
REM A chcp 65001 line alone did not fix it when the console started in code page 932,
REM and filler lines after it did not fix it for a longer file. Do not add non-ASCII text here.
REM
REM 2026-10-06 fixes for the v1.4.0 release preparation:
REM   - Every step is checked for its own products. Old logs and result files are deleted at the
REM     start, and after each step the log and the result file must exist, otherwise that step FAILS
REM     with a line that starts with [FAIL] step N.
REM   - Test results are judged from the XML failed attribute by Tools\CI\check-test-result.cmd,
REM     not from the Unity exit code alone. Unity returns exit code 2 when there are Inconclusive
REM     tests. With -nographics the rendering tests are Inconclusive, 21 plus 1 tests, which is normal.
REM   - The final green line is printed only when every step ran or was skipped on purpose and all
REM     of them are OK. Skipped steps are counted and shown.
REM   - If Unity Editor has this project open, the script says so first and FAILS.
REM
REM Prerequisites:
REM   - Unity Editor 6000.3.13f1 installed and activated by Unity Hub.
REM   - Unity Editor must NOT have this project open. One project cannot be opened twice.
REM   - pwsh, PowerShell 7 or later, enables step 1 and the result summary. Without it they are skipped.
REM
REM Usage:
REM   Tools\CI\run-ci.cmd
REM   Tools\CI\run-ci.cmd "C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe"
REM   From Git Bash use forward slashes: ./Tools/CI/run-ci.cmd   (bash eats the backslashes of Tools\CI\...).
REM   Through cmd from Git Bash the double backslash form also works: cmd //c "Tools\\CI\\run-ci.cmd"
REM   After the run, that console keeps code page 65001 (chcp is not undone by setlocal). Display only.
REM
REM 2026-10-06 (docs/61 review): git runs with --no-pager, step 4 also looks at untracked files, an unexpected
REM exit code of check-test-result.cmd is a FAIL, Skipped tests are read like Inconclusive ones, the old
REM NetCheck results are deleted at the start, and the result summary shows every count of each test step.
REM Compare the counts with the expected numbers in docs\60_release_1_4_0_prep.md before you trust a green run.
REM
REM Run it from the repository root. Inside a parenthesized block %ERRORLEVEL% is frozen at the
REM value it had before the block, so always read !ERRORLEVEL! and copy it to a variable right away.
REM Do not use parentheses in echo text unless they are escaped with a caret.

set "UNITY_EXE=%~1"
if "%UNITY_EXE%"=="" set "UNITY_EXE=C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe"

if not exist "%UNITY_EXE%" (
    echo [ERROR] Unity executable was not found: %UNITY_EXE%
    echo   Pass the path as the first argument. Example:
    echo   Tools\CI\run-ci.cmd "C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe"
    exit /b 1
)

set "PROJECT_PATH=%CD%"
set "RESULTS_DIR=%PROJECT_PATH%\TestResults"
if not exist "%RESULTS_DIR%" mkdir "%RESULTS_DIR%"

echo === D-Drive local CI ===
echo Unity      : %UNITY_EXE%
echo Project    : %PROJECT_PATH%
echo Results    : %RESULTS_DIR%
echo.

REM Unity Editor holds Temp\UnityLockfile open while it has the project open, so it cannot be read.
REM If it can be read, it is a leftover of a crashed Unity: warn and continue.
set "LOCKFILE=%PROJECT_PATH%\Temp\UnityLockfile"
if exist "%LOCKFILE%" (
    type "%LOCKFILE%" >nul 2>nul
    if not "!ERRORLEVEL!"=="0" (
        echo [FAIL] Unity Editor is open on this project. Close Unity and run this script again.
        echo        Lock file: %LOCKFILE%
        echo === NOT RUN: no step was executed ===
        exit /b 1
    ) else (
        echo [WARN] Temp\UnityLockfile exists but is not locked: a leftover of a crashed Unity. Continuing.
        echo.
    )
)

REM Delete the products of the previous run, so that the file exists means this run made it.
for %%F in (migrate-check.log validate.log ddrive-validation.junit.xml regenerate-ids.log editmode.log editmode-results.xml playmode.log playmode-results.xml performance.log performance-results.xml NetCheck\results.json NetCheck\summary.md) do (
    if exist "%RESULTS_DIR%\%%F" del /q "%RESULTS_DIR%\%%F" >nul 2>nul
    if exist "%RESULTS_DIR%\%%F" (
        echo [FAIL] could not delete the previous result: %RESULTS_DIR%\%%F
        exit /b 1
    )
)

set "OVERALL_EXIT=0"
set "STEPS_EXPECTED=8"
set "STEPS_RAN=0"
set "STEPS_SKIPPED=0"

REM Step 1 - P-9, docs/42_distribution.md 5.11-10: CHANGELOG guard. Fails if a compatibility
REM snapshot changed but CHANGELOG.md or the version did not. It does not start Unity.
REM Skipped when pwsh is missing.
where pwsh >nul 2>nul
set "PWSH_FOUND=!ERRORLEVEL!"
if "!PWSH_FOUND!"=="0" (
    echo [1/8] CHANGELOG guard, check-release.ps1 -GuardOnly ...
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\Release\check-release.ps1" -GuardOnly
    set "STEP_EXIT=!ERRORLEVEL!"
    set /a STEPS_RAN+=1
    if not "!STEP_EXIT!"=="0" (
        echo [FAIL] step 1 CHANGELOG guard: a compatibility snapshot changed but CHANGELOG.md or the version did not.
        set "OVERALL_EXIT=1"
    ) else (
        echo [OK] CHANGELOG guard
    )
) else (
    set /a STEPS_SKIPPED+=1
    echo [1/8] CHANGELOG guard: SKIPPED because pwsh was not found
)
echo.

REM Step 2 - P-7: pending migrations are detected before Validation.
echo [2/8] Migration check, CI.MigrateCheck ...
"%UNITY_EXE%" -batchmode -nographics -quit -projectPath "%PROJECT_PATH%" -executeMethod DDrive.Editor.CI.MigrateCheck -logFile "%RESULTS_DIR%\migrate-check.log"
set "STEP_EXIT=!ERRORLEVEL!"
set /a STEPS_RAN+=1
if not exist "%RESULTS_DIR%\migrate-check.log" (
    echo [FAIL] step 2 Migrate check: log file was not produced, Unity did not run
    set "OVERALL_EXIT=1"
) else if not "!STEP_EXIT!"=="0" (
    REM CI.MigrateCheck writes its own lines with the ASCII prefix [DDrive][Migration] (the pending message is
    REM in Japanese, so the prefix is the only marker that works in an ASCII file). Without the prefix in the
    REM log, Unity itself failed before or while running the method.
    findstr /c:"[DDrive][Migration]" "%RESULTS_DIR%\migrate-check.log" >nul 2>nul
    set "MARKER_EXIT=!ERRORLEVEL!"
    if "!MARKER_EXIT!"=="0" (
        echo [FAIL] step 2 Migrate check: exit code !STEP_EXIT!, probably pending migrations. Run Tools ^> D-Drive ^> Update ^> Migration. Log: %RESULTS_DIR%\migrate-check.log
    ) else (
        echo [FAIL] step 2 Migrate check: Unity failed, exit code !STEP_EXIT!, and the log has no migration line.
        echo        Possible causes: license, compile error, another Unity instance on this project. Read: %RESULTS_DIR%\migrate-check.log
    )
    set "OVERALL_EXIT=1"
) else (
    echo [OK] Migration check
)
echo.

echo [3/8] Validation, CI.ValidateAll ...
"%UNITY_EXE%" -batchmode -nographics -quit -projectPath "%PROJECT_PATH%" -executeMethod DDrive.Editor.CI.ValidateAll -ddriveOutput "%RESULTS_DIR%\ddrive-validation.junit.xml" -logFile "%RESULTS_DIR%\validate.log"
set "STEP_EXIT=!ERRORLEVEL!"
set /a STEPS_RAN+=1
if not exist "%RESULTS_DIR%\validate.log" (
    echo [FAIL] step 3 Validation: log file was not produced, Unity did not run
    set "OVERALL_EXIT=1"
) else if not exist "%RESULTS_DIR%\ddrive-validation.junit.xml" (
    echo [FAIL] step 3 Validation: result file was not produced. Log: %RESULTS_DIR%\validate.log
    set "OVERALL_EXIT=1"
) else if not "!STEP_EXIT!"=="0" (
    echo [FAIL] step 3 Validation: Errors were found. Log: %RESULTS_DIR%\validate.log
    set "OVERALL_EXIT=1"
) else (
    echo [OK] Validation
)
echo.

echo [4/8] Asset ID regeneration, CI.RegenerateIds, and git diff check ...
"%UNITY_EXE%" -batchmode -nographics -quit -projectPath "%PROJECT_PATH%" -executeMethod DDrive.Editor.CI.RegenerateIds -logFile "%RESULTS_DIR%\regenerate-ids.log"
set "STEP_EXIT=!ERRORLEVEL!"
set /a STEPS_RAN+=1
if not exist "%RESULTS_DIR%\regenerate-ids.log" (
    echo [FAIL] step 4 Regenerate IDs: log file was not produced, Unity did not run
    set "OVERALL_EXIT=1"
) else if not "!STEP_EXIT!"=="0" (
    echo [FAIL] step 4 Regenerate IDs: regeneration failed, for example duplicate IDs. Log: %RESULTS_DIR%\regenerate-ids.log
    set "OVERALL_EXIT=1"
) else (
    git --no-pager diff --exit-code --stat
    set "DIFF_EXIT=!ERRORLEVEL!"
    set "STATUS_DIRTY=0"
    for /f "delims=" %%L in ('git --no-pager status --porcelain') do set "STATUS_DIRTY=1"
    if not "!DIFF_EXIT!"=="0" (
        echo [FAIL] step 4 Regenerate IDs: ID regeneration left uncommitted changes. This includes Data assets that got a new Id,
        echo        not only Assets\Generated. Run Tools/D-Drive/Generate/Regenerate Asset IDs in the Editor and commit the result.
        echo        Unity may also save unrelated files when it starts in batch mode, read the list above.
        set "OVERALL_EXIT=1"
    ) else if "!STATUS_DIRTY!"=="1" (
        echo [FAIL] step 4 Regenerate IDs: the working tree is not clean after regeneration, for example new untracked files:
        git --no-pager status --short
        echo        Run Tools/D-Drive/Generate/Regenerate Asset IDs in the Editor and commit the result.
        set "OVERALL_EXIT=1"
    ) else (
        echo [OK] Asset ID: no diff
    )
)
echo.

echo [5/8] EditMode tests ...
"%UNITY_EXE%" -batchmode -nographics -projectPath "%PROJECT_PATH%" -runTests -testPlatform EditMode -testResults "%RESULTS_DIR%\editmode-results.xml" -logFile "%RESULTS_DIR%\editmode.log"
set "STEP_EXIT=!ERRORLEVEL!"
set /a STEPS_RAN+=1
call :judge_tests 5 EditMode "%RESULTS_DIR%\editmode-results.xml" "%RESULTS_DIR%\editmode.log" !STEP_EXIT!
echo.

echo [6/8] PlayMode tests ...
"%UNITY_EXE%" -batchmode -nographics -projectPath "%PROJECT_PATH%" -runTests -testPlatform PlayMode -testResults "%RESULTS_DIR%\playmode-results.xml" -logFile "%RESULTS_DIR%\playmode.log"
set "STEP_EXIT=!ERRORLEVEL!"
set /a STEPS_RAN+=1
call :judge_tests 6 PlayMode "%RESULTS_DIR%\playmode-results.xml" "%RESULTS_DIR%\playmode.log" !STEP_EXIT!
echo.

REM Step 7 - 6-2: only DDrive.Tests.Performance, category Performance, 0 alloc checks, in PlayMode.
echo [7/8] Performance tests, 0 alloc, DDrive.Tests.Performance ...
"%UNITY_EXE%" -batchmode -nographics -projectPath "%PROJECT_PATH%" -runTests -testPlatform PlayMode -testCategory "Performance" -testResults "%RESULTS_DIR%\performance-results.xml" -logFile "%RESULTS_DIR%\performance.log"
set "STEP_EXIT=!ERRORLEVEL!"
set /a STEPS_RAN+=1
call :judge_tests 7 Performance "%RESULTS_DIR%\performance-results.xml" "%RESULTS_DIR%\performance.log" !STEP_EXIT!
echo.

REM Step 8 - 6-7, optional: the 2-client NetCheck runs only when Builds\DDriveNetCheck\DDriveNetCheck.exe
REM exists. Its own judgment lives in run-netcheck.cmd and Run-NetCheck.ps1 and is not changed here.
if exist "%PROJECT_PATH%\Builds\DDriveNetCheck\DDriveNetCheck.exe" (
    echo [8/8] NetCheck, 2-client automatic test ...
    call "%~dp0run-netcheck.cmd"
    set "NETCHECK_EXIT=!ERRORLEVEL!"
    set /a STEPS_RAN+=1
    if not "!NETCHECK_EXIT!"=="0" (
        echo [FAIL] step 8 NetCheck. Log: %RESULTS_DIR%\NetCheck\summary.md
        set "OVERALL_EXIT=1"
    ) else (
        echo [OK] NetCheck
    )
) else (
    set /a STEPS_SKIPPED+=1
    echo [8/8] NetCheck: SKIPPED because Builds\DDriveNetCheck\DDriveNetCheck.exe does not exist
    echo        Build it with Tools ^> D-Drive ^> Build, then run Tools\CI\run-netcheck.cmd alone.
)
echo.

if "!PWSH_FOUND!"=="0" (
    echo === Result summary ===
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0Summarize-Results.ps1" ^
        -ValidationJUnitPath "%RESULTS_DIR%\ddrive-validation.junit.xml" ^
        -EditModeResultsPath "%RESULTS_DIR%\editmode-results.xml" ^
        -PlayModeResultsPath "%RESULTS_DIR%\playmode-results.xml" ^
        -PerformanceResultsPath "%RESULTS_DIR%\performance-results.xml" ^
        -NetCheckResultsPath "%RESULTS_DIR%\NetCheck\results.json"
) else (
    echo [NOTE] pwsh was not found, the result summary was skipped. Read the XML and logs in %RESULTS_DIR% directly.
)

REM Every step must be accounted for as ran or skipped. If the numbers do not add up, it is not green.
set /a STEPS_ACCOUNTED=STEPS_RAN+STEPS_SKIPPED
if not "!STEPS_ACCOUNTED!"=="!STEPS_EXPECTED!" (
    echo [FAIL] step count mismatch: ran=!STEPS_RAN! skipped=!STEPS_SKIPPED! expected=!STEPS_EXPECTED!
    set "OVERALL_EXIT=1"
)

echo.
if "!OVERALL_EXIT!"=="0" (
    echo === ALL GREEN: ran !STEPS_RAN! steps, skipped !STEPS_SKIPPED! steps ===
) else (
    echo === FAILED: ran !STEPS_RAN! steps, skipped !STEPS_SKIPPED! steps. See the lines above and the logs ===
)

exit /b !OVERALL_EXIT!

REM ---------------------------------------------------------------------------
REM :judge_tests  step-number  name  result-xml  log  unity-exit-code
REM Unity returns exit code 2 when there are Inconclusive or Skipped tests, so the XML decides, not the code:
REM OK when the result file exists and failed is 0, and the counts are shown.
REM FAIL when failed is 1 or more, the result file is missing, the Unity exit code is not 0 or 2,
REM or check-test-result.cmd returned a code other than 0 or 10 (for example 255 after a syntax error).
REM Unity exit code 2 with failed 0 and no Inconclusive and no Skipped tests is also a FAIL (unexplained).
REM ---------------------------------------------------------------------------
:judge_tests
set "J_STEP=%~1"
set "J_NAME=%~2"
set "J_XML=%~3"
set "J_LOG=%~4"
set "J_UNITY_EXIT=%~5"
if not exist "%J_LOG%" (
    echo [FAIL] step %J_STEP% %J_NAME%: log file was not produced, Unity did not run
    set "OVERALL_EXIT=1"
    exit /b 0
)
if not exist "%J_XML%" (
    echo [FAIL] step %J_STEP% %J_NAME%: result file was not produced. Log: %J_LOG%
    set "OVERALL_EXIT=1"
    exit /b 0
)
call "%~dp0check-test-result.cmd" "%J_XML%"
set "J_XML_EXIT=!ERRORLEVEL!"
if "!J_XML_EXIT!"=="1" (
    echo [FAIL] step %J_STEP% %J_NAME%: failed tests or an unreadable result file. Log: %J_LOG%
    set "OVERALL_EXIT=1"
    exit /b 0
)
if not "!J_XML_EXIT!"=="0" if not "!J_XML_EXIT!"=="10" (
    echo [FAIL] step %J_STEP% %J_NAME%: check-test-result.cmd returned an unexpected exit code !J_XML_EXIT!. Log: %J_LOG%
    set "OVERALL_EXIT=1"
    exit /b 0
)
if not "%J_UNITY_EXIT%"=="0" if not "%J_UNITY_EXIT%"=="2" (
    echo [FAIL] step %J_STEP% %J_NAME%: Unity exit code %J_UNITY_EXIT%. Log: %J_LOG%
    set "OVERALL_EXIT=1"
    exit /b 0
)
if "%J_UNITY_EXIT%"=="2" if "!J_XML_EXIT!"=="0" (
    echo [FAIL] step %J_STEP% %J_NAME%: Unity exit code 2 but the result file has no failed, inconclusive or skipped tests. Log: %J_LOG%
    set "OVERALL_EXIT=1"
    exit /b 0
)
if "!J_XML_EXIT!"=="10" (
    echo [OK] %J_NAME% tests: failed 0, with Inconclusive or Skipped tests, normal in a run without graphics
) else (
    echo [OK] %J_NAME% tests
)
exit /b 0
