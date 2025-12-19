using UnityEngine;
using System.IO;
using System.Text;
using System;

public class ExperimentManager : MonoBehaviour
{
    [Header("Session Setup")]
    public string participantID = "test01";

    [Header("Experiment State")]
    public WebcamDelay webcamDisplay; // Drag your Webcam Quad here
    public bool isRecording = false;

    private string savePath;
    private StringBuilder csvData = new StringBuilder();
    private float startTime;

    void Start()
    {
        SetupDataFile();
    }

    void SetupDataFile()
    {
        // 1. Create Folder: StreamingAssets/Data/participantID/
        string folder = Path.Combine(Application.streamingAssetsPath, "Data", participantID);
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        // 2. Create File: Session_1_Date_Time.csv
        string fileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm}.csv";
        savePath = Path.Combine(folder, fileName);

        // 3. Write Headers
        csvData.AppendLine("Timestamp,Event,Value,Delay_State");
        File.WriteAllText(savePath, csvData.ToString());

        startTime = Time.time;
        LogEvent("System", "Experiment_Started");
        Debug.Log($"<color=green>Data logging to: {savePath}</color>");
    }

    public void LogEvent(string eventType, string value)
    {
        float timestamp = Time.time - startTime;
        string delayState = webcamDisplay.useDelay ? "Asynchronous" : "Synchronous";
        
        // Format: 12.435, Trigger, Start_Trial, Asynchronous
        string row = $"{timestamp:F3},{eventType},{value},{delayState}";
        
        // Save to memory and disk
        csvData.AppendLine(row);
        File.AppendAllText(savePath, row + "\n");
    }

    // --- EXPERIMENT CONTROL EXAMPLES ---

    void Update()
    {
        // Example: Press 'S' to start Synchronous Condition
        if (Input.GetKeyDown(KeyCode.S))
        {
            SetCondition(false); // No Delay
            LogEvent("Condition_Start", "Synchronous");
        }

        // Example: Press 'A' to start Asynchronous Condition
        if (Input.GetKeyDown(KeyCode.A))
        {
            SetCondition(true); // Delay
            LogEvent("Condition_Start", "Asynchronous");
        }
    }

    public void SetCondition(bool useDelay)
    {
        webcamDisplay.useDelay = useDelay;
        // If async, set 1.0s delay. If sync, set 0s.
        webcamDisplay.delaySeconds = useDelay ? 1.0f : 0f; 
    }
}