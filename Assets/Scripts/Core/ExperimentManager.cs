using UnityEngine;
using System.IO;
using System.Text;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class ExperimentManager : MonoBehaviour
{
    [Header("Components")]
    public WebcamDelay webcamScript;      // Drag the Quad/Script here
    public GameObject screenObject;       // Drag the Quad GameObject here (to turn it on/off)
    public AudioSource audioSource;       // For beeps (Start/Stop cues)
    public TMP_Text experimenterDisplay;
    
    [Header("UI & Input")]
    public QuestionnaireManager questionnaireScript; // Drag the new script here
    public GameObject leftHandController; // Drag XR Origin > LeftHand Controller
    public GameObject rightHandController; // Drag XR Origin > RightHand Controller
    
    [Header("ID")]
    public string participantID = "test01";
    
    [System.Serializable]
    public struct TrialType
    {
        public string id;           // e.g. "Sync_Passive"
        public bool isAsync;        // Controls the webcam delay
        public bool isActive;       // Controls the instruction (Who acts?)
        public string instructions; // Message for the Experimenter
    }
    
    private List<TrialType> trialStack = new List<TrialType>();
    
    [Header("Experiment Settings")]
    public float stimulationDuration = 60.0f; // Standard RHI time
    public float delay = 0.86f;
    public float ISI = 1.0f;
    public int blocks = 1; // How many times to do each of the 4 types?
    
    // Internal State
    private string savePath;
    private StringBuilder csvData = new StringBuilder();
    private float startTime;
    private bool isRunning = false;
    private TrialType currentTrial;
    
    void Start()
    {
        SetupDataFile();
        GenerateRandomBlocks();
        // Start with screen OFF so participant sees nothing
        screenObject.SetActive(false);
        UpdateExperimenterUI("READY. Press SPACE to start next trial.");
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
    
    void GenerateRandomBlocks()
    {
        // 1. Define the 4 Fundamental Conditions
        var conditions = new List<TrialType>
        {
            new TrialType { id="Sync_Passive", isAsync=false, isActive=false, instructions="RESEARCHER" },
            new TrialType { id="Async_Passive", isAsync=true, isActive=false, instructions="RESEARCHER" },
            new TrialType { id="Sync_Active", isAsync=false, isActive=true, instructions="PARTICIPANT" },
            new TrialType { id="Async_Active", isAsync=true, isActive=true, instructions="PARTICIPANT" }
        };

        // Multiply by repetitions
        for (int i = 0; i < blocks; i++)
        {
            trialStack.AddRange(conditions);
        }

        // Fisher-Yates Shuffle (Randomize)
        for (int i = 0; i < trialStack.Count; i++)
        {
            TrialType temp = trialStack[i];
            int randomIndex = UnityEngine.Random.Range(i, trialStack.Count);
            trialStack[i] = trialStack[randomIndex];
            trialStack[randomIndex] = temp;
        }

        Debug.Log($"Generated {trialStack.Count} trials in random order.");
    }
    
    // --- EXPERIMENT CONTROL EXAMPLES ---

    void Update()
    {
        // Single Key to advance everything
        if (!isRunning && trialStack.Count > 0)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                // Pop the next trial from the list
                currentTrial = trialStack[0];
                trialStack.RemoveAt(0);
                
                StartCoroutine(RunTrial(currentTrial));
            }
        }
        else if (!isRunning && trialStack.Count == 0)
        {
            UpdateExperimenterUI("SESSION COMPLETE.");
        }
        // if (!isRunning)
        // {
        //     if (Input.GetKeyDown(KeyCode.S)) StartCoroutine(RunTrial(false)); // Sync
        //     if (Input.GetKeyDown(KeyCode.A)) StartCoroutine(RunTrial(true));  // Async
        // }
    }
    
    // Timeline of trial
    IEnumerator RunTrial(TrialType trial)
    {
        // Setup Trial
        isRunning = true;
        // PREPARE (Experimenter sees instruction, Screen still OFF)
        string mode = trial.isAsync ? "ASYNC (Delay)" : "SYNC (Instant)";
        UpdateExperimenterUI($"NEXT: {mode} \n {trial.instructions} \n\n Press 'R' when ready to start.");
        
        // Wait for Experimenter to confirm they are ready (e.g. have picked up brush)
        yield return new WaitUntil(() => Input.GetKeyDown(KeyCode.R));
        
        // Setup Delay
        // Note: e.g. 0.86s + 0.14s intrinsic = 1.0s total delay
        float appliedDelay = trial.isAsync ? delay : 0f; 
        webcamScript.delaySeconds = appliedDelay;
        webcamScript.useDelay = trial.isAsync;

        LogData("Trial_Start", trial.id, trial.isActive ? "Active" : "Passive", "Intention");
        UpdateExperimenterUI("Running...");
        
        // Start trial
        // Inter-Stimulus-interval
        yield return new WaitForSeconds(ISI);

        // START STIMULATION
        PlayBeep(); // Audio cue for Researcher to start stroking
        screenObject.SetActive(true); // Screen ON
        LogData("Stimulation_Start", trial.id, trial.isActive ? "Active" : "Passive", "Visuals_On");
        
        // Wait for trial
        // We use a loop here so we can cancel it if something goes wrong
        float timer = 0;
        while (timer < stimulationDuration)
        {
            timer += Time.deltaTime;
            // Optional: Update UI countdown for experimenter
            UpdateExperimenterUI($"{trial.id}\nTime: {stimulationDuration - timer:F1}");
            yield return null;
        }

        // STOP Trial
        screenObject.SetActive(false); // Screen OFF
        PlayBeep(); // Audio cue for Researcher to stop
        LogData("Stimulation_End", trial.id, trial.isActive ? "Active" : "Passive", "Visuals_Off");
        
        // QUESTIONNAIRE PHASE
        UpdateExperimenterUI("Waiting for Participant Input...");

        // A. Turn on Lasers so they can click
        SetControllersActive(true);

        // B. Show UI and Wait
        bool answered = false;
        questionnaireScript.ShowQuestionnaire((resultString) => 
        {
            // This runs when they click "Submit"
            LogData("Questionnaire", trial.id, "Results", resultString);
            answered = true;
        });

        // C. Pause Coroutine until they answer
        yield return new WaitUntil(() => answered);

        // D. Turn off Lasers for the next trial
        SetControllersActive(false);

        UpdateExperimenterUI("Trial Complete. Press SPACE for next.");
        isRunning = false;

    }
    
    void UpdateExperimenterUI(string message)
    {
        if (experimenterDisplay != null) experimenterDisplay.text = message;
    }

    void LogData(string phase, string condition, string mode, string ev)
    {
        float t = Time.time - startTime;
        string row = $"{t:F3},{phase},{condition},{mode},{ev}";
        csvData.AppendLine(row);
        File.AppendAllText(savePath, row + "\n");
    }

    void PlayBeep()
    {
        if(audioSource) audioSource.Play();
    }
    
    // Toggles controller lazers
    void SetControllersActive(bool isActive)
    {
        leftHandController.SetActive(isActive);
        rightHandController.SetActive(isActive);
    }
}