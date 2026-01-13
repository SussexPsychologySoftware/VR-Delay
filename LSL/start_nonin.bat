@echo off
cd /d "%~dp0"

REM --- SECTION 1: ENVIRONMENT SETUP ---
if not exist "venv" (
    echo [SETUP] Creating virtual environment...
    python -m venv venv
    call venv\Scripts\activate
    
    echo [SETUP] Installing libraries...
    REM We install nonin (for device), pylsl (for streaming), and pyserial (for USB connection)
    pip install nonin pylsl pyserial
    
    echo [SETUP] Done.
) else (
    call venv\Scripts\activate
)

REM --- SECTION 2: RUN ---
echo.
echo [RUN] Starting Nonin Stream...
echo [INFO] Connect your Nonin device now.
echo [INFO] Press Ctrl+C to stop.
echo.

python stream_nonin.py

pause