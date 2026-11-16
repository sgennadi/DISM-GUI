@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Four-stage Windows field acceptance. PROBE/PLAN by default.
rem It never edits the user's original WIM, the WIMMount registry or mount folders.
rem Usage:
rem   Invoke-FourStageAcceptance.cmd "C:\OEMTests\matrix.json" "D:\WIMTests"
rem   Invoke-FourStageAcceptance.cmd "C:\OEMTests\matrix.json" "D:\WIMTests" TEST_COPY_ONLY

if "%~1"=="" goto usage
if "%~2"=="" goto usage

set "MATRIX=%~f1"
set "WORK=%~f2"
set "RUNNER=%~dp0RealOemIntegration.exe"
set "RUNID=%RANDOM%%RANDOM%"
set "REPORT=%WORK%\FIELD-%RUNID%"
if not exist "%MATRIX%" (
  echo ERROR: Matrix manifest not found: "%MATRIX%"
  exit /b 2
)
if not exist "%WORK%\" (
  echo ERROR: Create a local test root before running: "%WORK%"
  exit /b 2
)
if not exist "%RUNNER%" (
  echo ERROR: Put RealOemIntegration.exe in the same folder as this CMD.
  exit /b 2
)
if not "%~4"=="" goto usage
if not "%~3"=="" if /i not "%~3"=="TEST_COPY_ONLY" goto usage
if exist "%REPORT%\" (
  echo ERROR: The report destination already exists.
  exit /b 2
)
mkdir "%REPORT%" || exit /b 2

echo DISM GUI FOUR-STAGE FIELD ACCEPTANCE > "%REPORT%\summary.txt"
echo Local test root: %WORK% >> "%REPORT%\summary.txt"
echo Timestamp: %DATE% %TIME% >> "%REPORT%\summary.txt"
echo Only approved disposable-copy OEM servicing; no changes to source images. >> "%REPORT%\summary.txt"
echo. >> "%REPORT%\summary.txt"

echo [1/4] Read-only mounted-image inventory
dism /Get-MountedImageInfo > "%REPORT%\mounted-before.txt" 2>&1
set "MOUNT_RC=%ERRORLEVEL%"
reg query "HKLM\SOFTWARE\Microsoft\WIMMount\Mounted Images" /s > "%REPORT%\wimmount-registry-readonly.txt" 2>&1
sc query WIMMount > "%REPORT%\wimmount-driver-readonly.txt" 2>&1

echo [2/4] Verify all real WIM/INF source files can be read
"%RUNNER%" --probe --matrix "%MATRIX%" > "%REPORT%\media-probe.txt" 2>&1
set "PROBE_RC=%ERRORLEVEL%"
if not "%PROBE_RC%"=="0" goto verify_failure

echo [3/4] Full real-image read-only WIM/CAT preflight
"%RUNNER%" --plan --matrix "%MATRIX%" --work-root "%WORK%" > "%REPORT%\matrix-plan.txt" 2>&1
set "PLAN_RC=%ERRORLEVEL%"
if not "%PLAN_RC%"=="0" goto verify_failure

if /i not "%~3"=="TEST_COPY_ONLY" goto no_execute
echo [4/4] Explicit, approved DISM servicing of isolated test WIM copies
"%RUNNER%" --execute --confirm TEST_COPY_ONLY --matrix "%MATRIX%" --work-root "%WORK%" > "%REPORT%\matrix-execute.txt" 2>&1
set "EXECUTE_RC=%ERRORLEVEL%"
if not "%EXECUTE_RC%"=="0" goto verify_failure
goto verify_ok

:no_execute
echo [4/4] COPY-ONLY execution NOT REQUESTED
echo --execute was NOT started. >> "%REPORT%\summary.txt"
goto verify_ok

:verify_failure
echo FAIL: One of the mounted-status, media probe, plan or copy-only tests failed.
echo Review "%REPORT%\summary.txt" and the individual saved logs.
echo FAIL or INCOMPLETE: review probe/plan/execute logs. >> "%REPORT%\summary.txt"
dism /Get-MountedImageInfo > "%REPORT%\mounted-after.txt" 2>&1
echo. >> "%REPORT%\summary.txt"
echo DPI 200 physical display verification: NOT VERIFIED. >> "%REPORT%\summary.txt"
echo Manual 200%% check: Windows display scaling must be set to 200%%; inspect all dialogs, buttons, tabs and long paths. >> "%REPORT%\summary.txt"
exit /b 1

:verify_ok
dism /Get-MountedImageInfo > "%REPORT%\mounted-after.txt" 2>&1
echo Command-line acceptance steps completed successfully. >> "%REPORT%\summary.txt"
echo. >> "%REPORT%\summary.txt"
echo Physical DPI verification: NOT VERIFIED by this script. >> "%REPORT%\summary.txt"
echo Set Windows Display Scale to 200%%, launch DISM-GUI, inspect Mount Health, Mount Recovery, Operation Center, Compare Indexes and Driver Studio. >> "%REPORT%\summary.txt"
echo Compare recorded mount states before/after; do NOT auto-clean INVALID registrations. >> "%REPORT%\summary.txt"
echo.
echo Reports available in "%REPORT%"
echo This does not prove DPI 200 visual acceptance or the contents of your original WIM without separately reviewing results.
exit /b 0

:usage
echo Usage:
echo   %~nx0 "C:\OEMTests\matrix.json" "D:\WIMTests" [TEST_COPY_ONLY]
echo Without TEST_COPY_ONLY, only read-only probe and plan run.
echo With TEST_COPY_ONLY, signed OEM drivers are staged and committed to disposable COPY images only.
exit /b 2
