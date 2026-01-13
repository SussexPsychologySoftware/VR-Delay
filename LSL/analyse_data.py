import pyxdf
import numpy as np
import pandas as pd
import matplotlib.pyplot as plt
import tkinter as tk
from tkinter import filedialog
import os

def load_and_analyze():
    # --- 1. OPEN FILE PICKER ---
    # This hides the main empty tkinter window
    root = tk.Tk()
    root.withdraw()

    print("Please select your .xdf data file...")
    filename = filedialog.askopenfilename(
        title="Select XDF Data File",
        filetypes=[("XDF files", "*.xdf"), ("All files", "*.*")]
    )

    if not filename:
        print("No file selected. Exiting.")
        return

    print(f"\nLoading: {os.path.basename(filename)}...")

    # --- 2. LOAD XDF ---
    try:
        streams, header = pyxdf.load_xdf(filename)
    except Exception as e:
        print(f"Error loading XDF: {e}")
        return

    # --- 3. IDENTIFY STREAMS ---
    marker_stream = None
    data_stream = None

    print("\n--- STREAMS FOUND ---")
    for s in streams:
        name = s['info']['name'][0]
        type_ = s['info']['type'][0]
        count = int(s['info']['channel_count'][0])
        print(f"Name: {name}, Type: {type_}, Channels: {count}")

        if 'RubberHand' in name or type_ == 'Markers':
            marker_stream = s
        elif 'Nonin' in name or 'HeartRate' in type_:
            data_stream = s

    if not marker_stream:
        print("ERROR: Could not find 'RubberHandEvents' marker stream.")
        return
    if not data_stream:
        print("ERROR: Could not find Heart Rate data stream.")
        return

    # --- 4. EXTRACT DATA ---
    marker_stamps = marker_stream['time_stamps']
    marker_strings = [x[0] for x in marker_stream['time_series']]

    data_stamps = data_stream['time_stamps']
    data_values = data_stream['time_series']

    # --- 5. SLICE TRIALS ---
    results = []

    print("\n--- PROCESSING TRIALS ---")
    for i, marker in enumerate(marker_strings):
        if "Start" in marker:
            # Parse marker: "Phase_Owner_Condition_Start"
            # Example: "Threshold_Self_Sync_Start"
            try:
                parts = marker.split('_')
                phase = parts[0]
                condition = parts[1]
                delay_type = parts[2]
            except:
                print(f"Skipping malformed marker: {marker}")
                continue

            start_time = marker_stamps[i]

            # Find matching End marker
            if i + 1 < len(marker_stamps):
                end_marker = marker_strings[i+1]
                end_time = marker_stamps[i+1]

                if "End" not in end_marker:
                    print(f"Warning: Trial started at {start_time:.1f} but next marker is {end_marker}")
                    continue

                # SLICE DATA
                # Find indices in the HR stream corresponding to start/end times
                idx_start = np.searchsorted(data_stamps, start_time)
                idx_end = np.searchsorted(data_stamps, end_time)

                trial_data = data_values[idx_start:idx_end]

                # Flatten the list of lists (if necessary) and calculate mean
                if len(trial_data) > 0:
                    # Convert to flat numpy array for math
                    flat_data = np.array(trial_data).flatten()
                    mean_hr = np.mean(flat_data)

                    results.append({
                        "Phase": phase,
                        "Owner": condition,
                        "Delay": delay_type,
                        "Mean_HR": round(mean_hr, 2),
                        "Duration_s": round(end_time - start_time, 2)
                    })
                    print(f"Processed: {marker} -> HR: {mean_hr:.1f} bpm")
                else:
                    print(f"No data points found for {marker}")

    # --- 6. SAVE RESULTS ---
    if len(results) > 0:
        df = pd.DataFrame(results)

        # Create output filename based on input filename
        input_dir = os.path.dirname(filename)
        base_name = os.path.splitext(os.path.basename(filename))[0]
        output_path = os.path.join(input_dir, f"{base_name}_RESULTS.csv")

        df.to_csv(output_path, index=False)
        print(f"\nSUCCESS! Results saved to:\n{output_path}")
        print("\nPreview:")
        print(df.head())
    else:
        print("\nNo valid trials found. Check your marker names.")

if __name__ == "__main__":
    load_and_analyze()