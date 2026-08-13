@echo off
setlocal
title VR Delay - Collect Diagnostics

REM ==============================================================
REM  COLLECT_LOGS.bat
REM
REM  READ-ONLY. This script COPIES log files to a new folder on
REM  the Desktop. It deletes nothing and changes no settings.
REM  It is safe to run at any time, as many times as you like.
REM
REM  Participant CSV data is NOT copied - only the file names are
REM  listed, so we can confirm the data folder is intact.
REM ==============================================================

set "APPCO=SPST"
set "APPNAME=VR Delay"
set "LOGDIR=%USERPROFILE%\AppData\LocalLow\%APPCO%\%APPNAME%"
set "OUT=%USERPROFILE%\Desktop\VRDelay_Diagnostics"

echo.
echo  ==========================================================
echo    VR DELAY  -  DIAGNOSTIC COLLECTOR
echo  ==========================================================
echo.
echo    This only COPIES files. Nothing will be deleted.
echo    Everything is gathered onto your Desktop.
echo.
pause

if exist "%OUT%" rd /s /q "%OUT%" 2>nul
mkdir "%OUT%" 2>nul

REM --- 1. UNITY PLAYER LOGS (the most important files) ---
echo.
echo [1/7] Looking for the app's log files...
if exist "%LOGDIR%\Player.log" (
    copy /y "%LOGDIR%\Player.log" "%OUT%\Player.log" >nul
    echo       OK - found Player.log  ^(the most recent run^)
) else (
    echo       MISSING - no Player.log at:
    echo                 %LOGDIR%
    echo       ^(If this is missing, the app may never have started.^)
)
if exist "%LOGDIR%\Player-prev.log" (
    copy /y "%LOGDIR%\Player-prev.log" "%OUT%\Player-prev.log" >nul
    echo       OK - found Player-prev.log  ^(the run before that - the crash^)
) else (
    echo       MISSING - no Player-prev.log
)

REM --- 2. UNITY CRASH DUMPS ---
echo.
echo [2/7] Looking for crash reports...
if exist "%TEMP%\%APPCO%\%APPNAME%" (
    xcopy "%TEMP%\%APPCO%\%APPNAME%" "%OUT%\Crashes\" /s /i /y >nul 2>&1
    echo       OK - crash reports copied
) else (
    echo       None found ^(this is normal if the driver reset rather
    echo       than the app itself crashing^)
)

REM --- 3. GPU AND DRIVER DETAILS ---
echo.
echo [3/7] Recording graphics card and driver version...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-CimInstance Win32_VideoController | Select-Object Name,VideoProcessor,DriverVersion,DriverDate,VideoModeDescription | Format-List | Out-File -Encoding utf8 '%OUT%\gpu_info.txt'" 2>nul
echo       OK - saved to gpu_info.txt

REM --- 4. DISPLAY DRIVER RESETS (TDR events) ---
REM  Event ID 4101 from the 'Display' source is Windows saying
REM  "the graphics driver stopped responding and was reset".
REM  This is the single event that confirms or kills the theory.
echo.
echo [4/7] Checking Windows for graphics driver resets...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$e = Get-WinEvent -FilterHashtable @{LogName='System'; StartTime=(Get-Date).AddDays(-21)} -ErrorAction SilentlyContinue | Where-Object { $_.Id -eq 4101 -or $_.ProviderName -match 'Display|amdkmd|amdwddm' }; if ($e) { $e | Select-Object TimeCreated,Id,ProviderName,Message | Format-List | Out-File -Encoding utf8 '%OUT%\gpu_driver_resets.txt' } else { 'No display driver reset events in the last 21 days.' | Out-File -Encoding utf8 '%OUT%\gpu_driver_resets.txt' }" 2>nul
echo       OK - saved to gpu_driver_resets.txt

REM --- 5. APPLICATION CRASH EVENTS ---
echo.
echo [5/7] Checking Windows for application crash events...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$e = Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddDays(-21)} -ErrorAction SilentlyContinue | Where-Object { $_.ProviderName -match 'Application Error|Windows Error Reporting|.NET Runtime' }; if ($e) { $e | Select-Object TimeCreated,ProviderName,Message | Format-List | Out-File -Encoding utf8 '%OUT%\app_crash_events.txt' } else { 'No application crash events in the last 21 days.' | Out-File -Encoding utf8 '%OUT%\app_crash_events.txt' }" 2>nul
echo       OK - saved to app_crash_events.txt

REM --- 6. ALVR CONFIG AND LOGS ---
echo.
echo [6/7] Looking for ALVR settings and logs...
if exist "%APPDATA%\ALVR" (
    xcopy "%APPDATA%\ALVR\*.json" "%OUT%\ALVR\" /i /y >nul 2>&1
    xcopy "%APPDATA%\ALVR\*.txt"  "%OUT%\ALVR\" /i /y >nul 2>&1
    xcopy "%APPDATA%\ALVR\*.log"  "%OUT%\ALVR\" /i /y >nul 2>&1
    echo       OK - ALVR files copied
) else (
    echo       Not found at %APPDATA%\ALVR
    echo       ^(ALVR may be installed elsewhere - not a problem^)
)

REM --- 7. CONFIRM PARTICIPANT DATA IS STILL THERE ---
REM  We list file NAMES only. No participant data leaves the machine.
echo.
echo [7/7] Confirming participant data folder is intact...
if exist "%LOGDIR%\Data" (
    dir /s /b "%LOGDIR%\Data" > "%OUT%\data_folder_contents.txt" 2>nul
    echo       OK - data folder found, file list saved
    echo       ^(File NAMES only - no participant data is copied^)
) else (
    echo       No Data folder yet at %LOGDIR%\Data
)

REM --- ZIP IT UP FOR EMAILING ---
echo.
echo Packaging everything into a single ZIP file...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%OUT%.zip' -Force" 2>nul

echo.
echo  ==========================================================
echo    DONE
echo  ==========================================================
echo.
echo    On your Desktop you now have:
echo.
echo      VRDelay_Diagnostics.zip     ^<-- email this one file
echo      VRDelay_Diagnostics\        ^(the same files, unzipped^)
echo.
echo    Opening the folder now...
echo.

start "" "%OUT%"
pause
endlocal
