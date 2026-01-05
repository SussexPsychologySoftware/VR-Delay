using UnityEngine;
using System.IO;
using System.Text;
using System;
using System.Collections;

public class ExperimentManager : MonoBehaviour
{
    [Header("Components")]
    public WebcamDelay webcamScript;      // Drag the Quad/Script here
    public GameObject screenObject;       // Drag the Quad GameObject here (to turn it on/off)
    public AudioSource audioSource;       // For beeps (Start/Stop cues)

    [Header("ID")]
    public string participantID = "test01";
    
    [Header("Experiment Settings")]
    public float stimulationDuration = 60.0f; // Standard RHI time
    public float delay = 0.86f;
    public float ISI = 1.0f;
    
    // Internal State
    private string savePath;
    private StringBuilder csvData = new StringBuilder();
    private float startTime;
    private bool isRunning = false;

    void Start()
    {
        SetupDataFile();
        // Start with screen OFF so participant sees nothing
        screenObject.SetActive(false);
    }

    void SetupDataFile()
    {
        // Create Folder: StreamingAssets/Data/participantID/
        string folder = Path.Combine(Application.streamingAssetsPath, "Data", participantID);
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        // Create file
        string fileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm}.csv";
        savePath = Path.Combine(folder, fileName);

        // Write Headers
        csvData.AppendLine("Timestamp,Event,Value,Delay_State");
        File.WriteAllText(savePath, csvData.ToString());

        startTime = Time.time;
        //LogEvent("System", "Experiment_Started");
        
        Debug.Log($"<color=green>Ready. Press 'S' for Sync, 'A' for Async.</color>");    
    }
    
    // --- EXPERIMENT CONTROL EXAMPLES ---

    void Update()
    {
        if (!isRunning)
        {
            if (Input.GetKeyDown(KeyCode.S)) StartCoroutine(RunTrial(false)); // Sync
            if (Input.GetKeyDown(KeyCode.A)) StartCoroutine(RunTrial(true));  // Async
        }
    }
    
    // Timeline of trial
    IEnumerator RunTrial(bool isAsync)
    {
        // Setup Trial
        isRunning = true;
        string conditionName = isAsync ? "Asynchronous" : "Synchronous";
        
        // Setup Delay
        // Note: e.g. 0.86s + 0.14s intrinsic = 1.0s total delay
        float delayVal = isAsync ? delay : 0f; 
        webcamScript.delaySeconds = delayVal;
        webcamScript.useDelay = isAsync;

        LogData("Trial_Start", conditionName, "Intention");

        // Start trial
        // Inter-Stimulus-interval
        yield return new WaitForSeconds(ISI);

        // START STIMULATION
        PlayBeep(); // Audio cue for Researcher to start stroking
        screenObject.SetActive(true); // Screen ON
        LogData("Stimulation", conditionName, "Visuals_On");

        // Wait for trial
        // We use a loop here so we can cancel it if something goes wrong
        float timer = 0;
        while (timer < stimulationDuration)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        // STOP Trial
        screenObject.SetActive(false); // Screen OFF
        PlayBeep(); // Audio cue for Researcher to stop
        LogData("Stimulation_End", conditionName, "Visuals_Off");

        // QUESTIONNAIRE PHASE (Placeholder for now)
        //Debug.Log("Show Questionnaire Now...");
        
        isRunning = false;
    }

    void LogData(string phase, string condition, string ev)
    {
        float t = Time.time - startTime;
        string row = $"{t:F3},{phase},{condition},{ev}";
        
        csvData.AppendLine(row);
        File.AppendAllText(savePath, row + "\n");
    }

    void PlayBeep()
    {
        if(audioSource) audioSource.Play();
    }
}