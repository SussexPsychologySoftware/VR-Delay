import pyxdf
import numpy as np
import pandas as pd
import matplotlib.pyplot as plt

def load_rubber_hand_data(filename):
    print(f"Loading {filename}...")
    # Load the XDF file. 
    # streams is a list of dictionaries, one for each stream (Unity, Nonin, etc.)
    streams, header = pyxdf.load_xdf(filename)

    # 1. FIND STREAMS
    marker_stream = None
    data_stream = None

    for s in streams:
        name = s['info']['name'][0]
        type_ = s['info']['type'][0]
        
        # Look for our Unity stream
        if name == 'RubberHandEvents' or type_ == 'Markers':
            marker_stream = s
            print(f"Found Markers: {name}")
            
        # Look for the Nonin stream (Generic check for PPG or generic Data)
        # Adjust 'Nonin' if your specific device is named differently
        elif 'Nonin' in name or type_ in ['PPG', 'Data', 'EEG']:
            data_stream = s
            print(f"Found Data: {name} ({type_})")

    if not marker_stream or not data_stream:
        print("Error: Could not find both streams. Check stream names.")
        return

    # 2. EXTRACT DATA
    # LSL stores timestamps in 'time_stamps' and data in 'time_series'
    marker_stamps = marker_stream['time_stamps']
    marker_strings = [x[0] for x in marker_stream['time_series']] # Unpack list of lists

    data_stamps = data_stream['time_stamps']
    data_values = data_stream['time_series']

    # 3. SLICE TRIALS
    # We look for "Start" and "End" pairs in the markers
    results = []

    for i, marker in enumerate(marker_strings):
        if "Start" in marker:
            # Parse the marker string (e.g., "Threshold_Self_Sync_Start")
            # We split by '_'
            parts = marker.split('_')
            phase = parts[0]      # Threshold/Long
            condition = parts[1]  # Self/Other
            delay = parts[2]      # Sync/Async or ms
            
            start_time = marker_stamps[i]
            
            # Find the corresponding "End" marker (the next one)
            if i + 1 < len(marker_stamps):
                end_time = marker_stamps[i+1]
                end_marker = marker_strings[i+1]
                
                # Double check it is actually an end marker
                if "End" not in end_marker:
                    print(f"Warning: Marker at {start_time} has no immediate End marker.")
                    continue

                # 4. GET HEART RATE FOR THIS DURATION
                # Find indices in the data stream that fall between start and end
                # searchsorted is a fast way to find the index of a timestamp
                idx_start = np.searchsorted(data_stamps, start_time)
                idx_end = np.searchsorted(data_stamps, end_time)
                
                # Extract the chunk of data
                trial_data = data_values[idx_start:idx_end]
                
                # Calculate mean HR for this trial (assuming channel 0 is HR)
                if len(trial_data) > 0:
                    mean_hr = np.mean(trial_data) 
                    
                    results.append({
                        "Phase": phase,
                        "Condition": condition,
                        "Delay": delay,
                        "Mean_HR": mean_hr,
                        "Duration": end_time - start_time
                    })

    # 4. OUTPUT RESULTS
    df = pd.DataFrame(results)
    print("\n--- ANALYSIS RESULTS ---")
    print(df)
    
    # Save to CSV for SPSS/R
    df.to_csv("processed_results.csv", index=False)
    print("\nSaved to processed_results.csv")

    # Optional: Plot the last trial to sanity check
    if 'idx_start' in locals():
        plt.plot(data_stamps[idx_start:idx_end], data_values[idx_start:idx_end])
        plt.title(f"Raw Data for Last Trial ({marker})")
        plt.xlabel("Time (s)")
        plt.show()

# --- USAGE ---
# Replace with your actual file name
# load_rubber_hand_data("C:/Path/To/Data/P001_rubberhand.xdf")

if __name__ == "__main__":
    load_rubber_hand_data(r"C:\Users\Max\Documents\Code\VR Delay\Assets\StreamingAssets\Data\P020\sub-P001\ses-S001\eeg\sub-P001_ses-S001_task-Default_run-001_eeg.xdf")