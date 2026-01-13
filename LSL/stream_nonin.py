import time
import sys
from pylsl import StreamInfo, StreamOutlet
import nonin

# --- SETTINGS ---
# Set your COM port here (e.g., 'COM3', 'COM4'). 
# Check Device Manager > Ports (COM & LPT) to find it.
COM_PORT = 'COM3' 

def main():
    print(f"Attempting to connect to Nonin on {COM_PORT}...")

    # 1. SETUP LSL
    # We create a stream for Heart Rate.
    # Name: 'Nonin_Stream', Type: 'HeartRate', Channels: 1, Rate: 0 (Irregular), Format: float32
    info = StreamInfo('Nonin_Stream', 'HeartRate', 1, 0, 'float32', 'nonin_dev_01')
    outlet = StreamOutlet(info)
    
    print(">> LSL Stream 'Nonin_Stream' created.")
    print(">> Waiting for device connection...")

    try:
        # 2. CONNECT TO DEVICE
        # The 'nonin' library manages the serial connection
        with nonin.Nonin(COM_PORT) as device:
            print(f">> Connected to {COM_PORT}. Reading data...")
            
            # 3. STREAMING LOOP
            while True:
                # Read specific packet from device
                data = device.read()
                
                if data:
                    # --- DATA EXTRACTION LOGIC ---
                    # The 'nonin' package on PyPI typically returns an object with attributes.
                    # We check for 'pulse' (common) or 'heart_rate'.
                    
                    current_hr = 0.0
                    
                    if hasattr(data, 'pulse'):
                        current_hr = float(data.pulse)
                    elif hasattr(data, 'heart_rate'):
                        current_hr = float(data.heart_rate)
                    elif hasattr(data, 'hr'):
                        current_hr = float(data.hr)
                    else:
                        # Fallback: Print structure if we can't find the name
                        print(f"Unknown data format. Attributes: {dir(data)}")
                        continue

                    # Filter out bad reads (sometimes devices return 511 or 0 for invalid)
                    if current_hr > 0 and current_hr < 300:
                        # --- SEND TO LSL ---
                        outlet.push_sample([current_hr])
                        
                        # Console feedback (overwrites line to keep it clean)
                        print(f"HR: {current_hr:.1f} BPM", end='\r')
                    
    except KeyboardInterrupt:
        print("\n>> Stopping stream per user request.")
    except Exception as e:
        print(f"\n>> ERROR: {e}")
        print(">> Tips: Check correct COM Port in script. Unplug/replug USB.")

if __name__ == "__main__":
    main()