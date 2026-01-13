==============================================================================
RUBBER HAND ILLUSION (VR) - EXPERIMENT INSTRUCTIONS
==============================================================================

1. INSTALLATION (CRITICAL)
------------------------------------------------------------------------------
Do NOT run the .exe directly from inside the ZIP file. 
Windows cannot load the data files if you do this.

   1. Right-click the downloaded ZIP file.
   2. Select "Extract All..." and choose a folder on your Desktop.
   3. Open the new unzipped folder.

2. HARDWARE SETUP
------------------------------------------------------------------------------
Ensure the following are connected BEFORE starting the application:

   * VR Headset: Quest 2 (via Link Cable or AirLink)
   * Webcam: Mounted to the front of the headset
   * Pulse Oximeter: Nonin (Ensure LSL Streamer is running if required)

3. RUNNING THE EXPERIMENT
------------------------------------------------------------------------------
   1. Put on the VR headset and enable Oculus Link / AirLink.
   2. On your PC, double-click "RubberHand_Exp.exe".
   3. The "Setup Dashboard" will appear on your monitor (not in VR).

4. EXPERIMENT SETUP DASHBOARD
------------------------------------------------------------------------------
   * Participant ID: Auto-generated (e.g., P001). You can edit this if needed.
   * Webcam: Select the correct camera from the dropdown. 
   * System Latency: Default is 0.134s. Adjust only if you have calibrated 
     a different intrinsic delay for this specific PC.
   * View Size: Drag the slider to scale the camera feed until it covers 
     the participant's full field of view.

   > Press "CONFIRM & START" when ready. 
   > The view inside the headset will activate.

5. RETRIEVING DATA
------------------------------------------------------------------------------
Your data (CSVs) are saved in a hidden Windows system folder. 
We have made a shortcut to help you find them.

   Method A (Easiest):
   Look in this folder (next to the .exe). You will see a file named:
   "OPEN_DATA_FOLDER.bat"
   Double-click it to instantly open your data folder.

   Method B (Manual):
   Navigate to: C:\Users\[YOUR_NAME]\AppData\LocalLow\DefaultCompany\RubberHand_Exp\Data

==============================================================================
TROUBLESHOOTING
==============================================================================
* Webcam is black? 
  Restart the app and ensure no other program (Zoom, Teams) is using the camera.

* VR is jittery?
  Click the game window to ensure it has "focus" (is the active window).

* Experiment paused?
  Ensure the game window is not minimized.