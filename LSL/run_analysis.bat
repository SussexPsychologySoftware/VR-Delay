@echo off
cd /d "%~dp0"

REM --- 1. CHECK FOR VIRTUAL ENVIRONMENT ---
if not exist "venv" (
    echo [ERROR] No virtual environment found!
    echo Please run 'start_nonin.bat' at least once to create the environment.
    pause
    exit
)

REM --- 2. ACTIVATE VENV ---
call venv\Scripts\activate

REM --- 3. INSTALL ANALYSIS TOOLS ---
REM We install pandas, matplotlib, and pyxdf into the venv.
REM This only takes time on the first run; subsequent runs are instant.
echo [SETUP] Checking analysis libraries...
pip install pandas matplotlib pyxdf

REM --- 4. RUN ANALYSIS SCRIPT ---
echo.
echo [RUN] Launching Analysis Script...
python analyse_data.py

pause