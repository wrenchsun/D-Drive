@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion
REM D-Drive: small helper that reads a Unity Test Framework result XML and returns pass or fail.
REM Works without pwsh. Called by run-ci.cmd, and can be run alone. ASCII only on purpose,
REM see the note at the top of run-ci.cmd.
REM
REM Usage:  Tools\CI\check-test-result.cmd path-to-result.xml
REM Exit code:
REM   0  = OK, failed is 0 and nothing is inconclusive or skipped
REM   10 = OK, failed is 0 and some tests are Inconclusive or Skipped (Assert.Ignore). Normal for
REM        rendering tests without graphics. The counts are printed.
REM   1  = FAIL: file missing or unreadable, failed is 1 or more, result is Failed, or zero tests ran
REM
REM Only the attributes of the first test-run element are read. Attribute values with spaces or
REM special characters are not expected there.

set "XML=%~1"
if "%XML%"=="" (
    echo [FAIL] check-test-result: no path given
    exit /b 1
)
if not exist "%XML%" (
    echo [FAIL] result file was not produced: %XML%
    exit /b 1
)

set "LINE="
for /f "usebackq delims=" %%L in (`findstr /c:"<test-run " "%XML%"`) do (
    if not defined LINE set "LINE=%%L"
)
if not defined LINE (
    echo [FAIL] result file has no test-run element: %XML%
    exit /b 1
)

REM Walk the attributes as name, value, name, value. FOR splits at spaces and equal signs, so the
REM tokens come as: id  "2"  testcasecount  "1600" ...  The leading element name and the closing
REM angle bracket are removed first.
set "LINE=!LINE:*<test-run =!"
set "LINE=!LINE:>= !"

set "R_FAILED="
set "R_INCONC=0"
set "R_SKIPPED=0"
set "R_TOTAL="
set "R_PASSED="
set "R_RESULT="
set "PREV="
for %%T in (!LINE!) do (
    if defined PREV (
        if "!PREV!"=="failed" set "R_FAILED=%%~T"
        if "!PREV!"=="inconclusive" set "R_INCONC=%%~T"
        if "!PREV!"=="skipped" set "R_SKIPPED=%%~T"
        if "!PREV!"=="total" set "R_TOTAL=%%~T"
        if "!PREV!"=="passed" set "R_PASSED=%%~T"
        if "!PREV!"=="result" set "R_RESULT=%%~T"
        set "PREV="
    ) else (
        set "PREV=%%T"
    )
)

if not defined R_FAILED (
    echo [FAIL] could not read the failed attribute: %XML%
    exit /b 1
)
echo   result=!R_RESULT! total=!R_TOTAL! passed=!R_PASSED! failed=!R_FAILED! inconclusive=!R_INCONC! skipped=!R_SKIPPED!

if not "!R_FAILED!"=="0" exit /b 1
if "!R_TOTAL!"=="" exit /b 1
if "!R_TOTAL!"=="0" (
    echo [FAIL] no tests were run: %XML%
    exit /b 1
)
echo !R_RESULT!| findstr /i /c:"Failed" >nul
if "!ERRORLEVEL!"=="0" exit /b 1
if not "!R_INCONC!"=="0" exit /b 10
if not "!R_SKIPPED!"=="0" exit /b 10
exit /b 0
