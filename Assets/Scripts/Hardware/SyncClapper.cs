// using UnityEngine;
// // Ensure OVR plugin is referenced
// public class SyncClapper : MonoBehaviour
// {
//     public ExperimentManager expManager; // Link to your main manager
//     public float impactThreshold = 2.5f; // Adjust based on testing
//     public float cooldown = 2.0f;
//     private float lastClap = 0;
//
//     void Update()
//     {
//         // Check Right Controller Acceleration
//         float gForce = OVRInput.GetLocalControllerAcceleration(OVRInput.Controller.RTouch).magnitude;
//
//         if (gForce > impactThreshold && Time.time > lastClap + cooldown)
//         {
//             lastClap = Time.time;
//             Debug.Log($"<color=yellow>CLAP DETECTED! Force: {gForce}</color>");
//             
//             // OPTIONAL: Haptic buzz to confirm the headset 'felt' it
//             OVRInput.SetControllerVibration(1, 1, OVRInput.Controller.RTouch);
//             Invoke("StopBuzz", 0.2f);
//
//             // Write directly to your existing data logging if possible
//             // Or just ensure this timestamp is saved in a distinct way
//             expManager.LogExternalEvent("SYNC_CLAP", gForce.ToString("F2"));
//         }
//     }
//     void StopBuzz() { OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch); }
// }