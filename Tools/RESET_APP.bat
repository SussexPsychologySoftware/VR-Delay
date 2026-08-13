@echo off
setlocal
title VR Delay - Reset After Crash

REM ==============================================================
REM  RESET_APP.bat
REM
REM  Recovers the machine after a graphics driver crash, when
REM  "VR Delay.exe" will no longer open.
REM
REM  It does THREE things, asking before each one:
REM    1. Closes leftover processes stuck from the crash
REM    2. Clears graphics shader caches (Windows rebuilds these)
REM    3. Optionally resets the app's saved window/settings
REM
REM  IT NEVER TOUCHES PARTICIPANT DATA.
REM  The Data folder is not referenced anywhere in this script.
REM
REM  RUN COLLECT_LOGS.bat FIRST if you have not already - once
REM  these caches are cleared, some crash evidence is gone.
REM ==============================================================

set "APPCO=SPST"
set "APPNAME=VR Delay"
set "BACKUP=%USERPROFILE%\Desktop\VRDelay_SettingsBackup.reg"

REM Start every prompt answer empty, so that simply pressing Enter
REM always means "skip" rather than leaving a stale value behind.
set "GO="
set "S1="
set "S2="
set "S3="

echo.
echo  ==========================================================
echo    VR DELAY  -  RESET AFTER CRASH
echo  ==========================================================
echo.
echo    Your participant data is NOT affected by this script.
echo.
echo    IMPORTANT: If you have not yet run COLLECT_LOGS.bat,
echo    close this window and run that one first.
echo.
set /p GO="Continue? Type Y then press Enter: "
if /i not "%GO%"=="Y" goto :cancelled

REM ==============================================================
REM  STEP 1 - CLOSE STUCK PROCESSES
REM ==============================================================
REM  After a driver reset, the app and SteamVR often survive as
REM  invisible processes that still hold the graphics card, the
REM  webcam and the network port. A new copy of the app then
REM  silently loses the fight and never opens a window.
echo.
echo  ----------------------------------------------------------
echo   STEP 1 of 3: Closing leftover processes
echo  ----------------------------------------------------------
echo.
echo   This closes: the experiment app, SteamVR and ALVR.
echo   Nothing else is touched. Steam itself stays open.
echo.
set /p S1="Do this? Type Y then press Enter: "
if /i not "%S1%"=="Y" goto :step2

echo.
taskkill /f /im "VR Delay.exe"          2>nul && echo   Closed: VR Delay.exe
taskkill /f /im "UnityCrashHandler64.exe" 2>nul && echo   Closed: UnityCrashHandler64.exe
taskkill /f /im "vrmonitor.exe"         2>nul && echo   Closed: vrmonitor.exe
taskkill /f /im "vrcompositor.exe"      2>nul && echo   Closed: vrcompositor.exe
taskkill /f /im "vrserver.exe"          2>nul && echo   Closed: vrserver.exe
taskkill /f /im "vrdashboard.exe"       2>nul && echo   Closed: vrdashboard.exe
taskkill /f /im "vrwebhelper.exe"       2>nul && echo   Closed: vrwebhelper.exe
taskkill /f /im "vrstartup.exe"         2>nul && echo   Closed: vrstartup.exe
taskkill /f /im "ALVR Dashboard.exe"    2>nul && echo   Closed: ALVR Dashboard.exe
taskkill /f /im "alvr_dashboard.exe"    2>nul && echo   Closed: alvr_dashboard.exe
echo.
echo   Done. ^(Messages about processes "not found" are normal -
echo   it just means that one was not running.^)

:step2
REM ==============================================================
REM  STEP 2 - CLEAR SHADER CACHES
REM ==============================================================
REM  These are auto-generated files that Windows and the AMD
REM  driver rebuild on next launch. A crash mid-write leaves them
REM  corrupt, which causes launches to fail instantly with no
REM  error message. Deleting them is always safe.
echo.
echo  ----------------------------------------------------------
echo   STEP 2 of 3: Clearing graphics shader caches
echo  ----------------------------------------------------------
echo.
echo   These rebuild themselves automatically. Nothing is lost.
echo   The first launch afterwards may be a little slower.
echo.
set /p S2="Do this? Type Y then press Enter: "
if /i not "%S2%"=="Y" goto :step3

echo.
if exist "%LOCALAPPDATA%\D3DSCache"  ( rd /s /q "%LOCALAPPDATA%\D3DSCache"  2>nul & echo   Cleared: D3DSCache  ^(Windows DirectX cache^) ) else ( echo   Not present: D3DSCache )
if exist "%LOCALAPPDATA%\AMD\DxCache" ( rd /s /q "%LOCALAPPDATA%\AMD\DxCache" 2>nul & echo   Cleared: AMD DxCache ^(DirectX^) ) else ( echo   Not present: AMD DxCache )
if exist "%LOCALAPPDATA%\AMD\GLCache" ( rd /s /q "%LOCALAPPDATA%\AMD\GLCache" 2>nul & echo   Cleared: AMD GLCache ^(OpenGL^) ) else ( echo   Not present: AMD GLCache )
if exist "%LOCALAPPDATA%\AMD\VkCache" ( rd /s /q "%LOCALAPPDATA%\AMD\VkCache" 2>nul & echo   Cleared: AMD VkCache ^(Vulkan^) ) else ( echo   Not present: AMD VkCache )
if exist "%TEMP%\%APPCO%\%APPNAME%"   ( rd /s /q "%TEMP%\%APPCO%\%APPNAME%"   2>nul & echo   Cleared: old crash dumps ) else ( echo   Not present: crash dumps )
echo.
echo   Done.

:step3
REM ==============================================================
REM  STEP 3 - RESET SAVED WINDOW STATE AND PREFERENCES
REM ==============================================================
REM  Unity saves the app's window size/position here alongside the
REM  two dashboard settings. If it crashed while the display was
REM  in a bad state, it can reopen off-screen or zero-sized -
REM  which looks exactly like "the app won't open".
REM
REM  ONLY DO THIS if the app still refuses to open after a reboot.
echo.
echo  ----------------------------------------------------------
echo   STEP 3 of 3: Reset saved window position and settings
echo  ----------------------------------------------------------
echo.
echo   ONLY do this if the app STILL will not open after you have
echo   restarted the computer and tried again.
echo.
echo   This clears the app's saved window position, and also the
echo   two dashboard values:
echo        - System Latency  ^(default is 0.134^)
echo        - Webcam Size / View Size
echo   You would need to type those two numbers in again.
echo.
echo   A backup is saved to your Desktop first, so this is
echo   reversible - just double-click the backup file to restore.
echo.
set /p S3="Do this? Type Y then press Enter, or just press Enter to skip: "
if /i not "%S3%"=="Y" goto :finish

echo.
reg query "HKCU\SOFTWARE\%APPCO%\%APPNAME%" >nul 2>&1
if errorlevel 1 (
    echo   Nothing saved yet - no reset needed.
    goto :finish
)

reg export "HKCU\SOFTWARE\%APPCO%\%APPNAME%" "%BACKUP%" /y >nul 2>&1
if exist "%BACKUP%" (
    echo   Backup saved to your Desktop: VRDelay_SettingsBackup.reg
) else (
    echo   WARNING: backup could not be written. Stopping to be safe.
    goto :finish
)

reg delete "HKCU\SOFTWARE\%APPCO%\%APPNAME%" /f >nul 2>&1
if errorlevel 1 (
    echo   Could not reset - try right-clicking this file and
    echo   choosing "Run as administrator".
) else (
    echo   Done - saved window position and settings cleared.
)

:finish
echo.
echo  ==========================================================
echo    FINISHED
echo  ==========================================================
echo.
echo    NOW PLEASE RESTART THE COMPUTER before trying the
echo    experiment again. A graphics card that has been reset
echo    is often not fully recovered until a restart.
echo.
echo    After restarting, start things in this order:
echo       1. SteamVR / ALVR - wait until the headset shows a view
echo       2. Then "VR Delay.exe"
echo.
goto :end

:cancelled
echo.
echo  Cancelled. Nothing was changed.

:end
echo.
pause
endlocal
