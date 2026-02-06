using UnityEngine;
using System.IO;
using System.Text;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit;
using LSL;
using UnityEngine.Serialization;

public class ExperimentManager : MonoBehaviour
{
    // SINGLETON
    public static ExperimentManager instance { get; private set; }
    
    // PUBLIC DATA PATH ACCESSORS
    public string participantID;      
    private string participantFolder; 
    private string eventLogPath;
    private string thresholdDataPath;
    private string longDataPath;
    
    // Internal
    private string rootSaveDirectory;
    private int participantNum;
    private bool hasSetupFinished = false;
    private List<TrialData> trialStack = new List<TrialData>();
    private int globalTrialCounter = 0;
    private float startTime;
    private bool isRunning = false;
    private TrialData currentTrial;

    public enum ExperimentPhase { Practice, Threshold, Long }
    
    [System.Serializable]
    public class TrialData
    {
        public string id;             // e.g. "Threshold_66ms"
        public ExperimentPhase phase; // Practice, Threshold or Long
        public bool isSelf;           // True = Participant strokes, False = Researcher strokes
        public int delay;             // In milliseconds
        public float duration;        // 7s or 60s
        public bool isPractice;       // Helper flag to skip data saving
    }
    
    [Header("Researcher Dashboard")]
    public GameObject setupCanvas;
    public TMP_Dropdown webcamDropdown;
    public TMP_Dropdown thresholdConditionDropdown;
    public TMP_Dropdown longConditionDropdown;
    public TMP_InputField idInput;
    public UnityEngine.UI.Button confirmButton;
    public TMP_InputField latencyInput;
    public TMP_InputField sizeInput;
    public TMP_Text viewSizeLabel;
    
    [Header("Components")]
    public WebcamDelay webcamScript;      // Drag the Quad/Script here
    public GameObject screenObject;       // Drag the Quad GameObject here (to turn it on/off)
    public AudioSource audioSource;       // For beeps (Start/Stop cues)
    public TMP_Text experimenterDisplay;  // Provide notes to researchers on pc screen
    public QuestionnaireManager questionnaireScript; // Drag the new script here
    public GameObject thresholdUI;
    public GameObject leftHandController; // Drag XR Origin > LeftHand Controller
    public GameObject rightHandController; // Drag XR Origin > RightHand Controller
    
    [Header("Delay Settings (int milliseconds)")] 
    public int maxThresholdDelay = 594;
    public int longAsyncTargetDelay = 1000; // Target for Long Asynchronous trials (e.g. 1.0s)
    
    [Header("Threshold Trial Settings")] 
    public int nThresholdSteps = 10; // Includes 0! i.e. 594/10 = stepSize=66ms
    public int thresholdRepetitions = 4;
    
    [Header("Other Experiment Settings (float seconds)")] 
    public float thresholdDuration = 7.0f;
    public float longDuration = 6.0f;
    public float ISI = 1.0f;
    public float estimatedSystemLatency = 0.134f;
    public float practiceDuration = 7.0f; // New Setting
     
    [Header("LSL Settings")]
    public string lslStreamName = "RubberHandEvents";
    public string lslStreamType = "Markers";
    private StreamOutlet lslOutlet;
    private string[] lslSample = new string[1]; // Buffer for sending data
    
    [System.Serializable]
    public struct TrialType
    {
        public string id;           // e.g. "Sync_Passive"
        public bool isAsync;        // Controls the webcam delay
        public bool isActive;       // Controls the instruction (Who acts?)
        public string instructions; // Message for the Experimenter
    }
        
    private void Awake()
    {
        // Ensure only one instance exists
        if (instance != null && instance != this)
        {
            Destroy(this.gameObject);
        }
        else
        {
            instance = this;
            DontDestroyOnLoad(this.gameObject); // Optional: Keep across scenes
        }
        
        // Initialize Root Path
        // NOTE: StreamingAssets is read-only folder, works for editor/desktop builds.
        // TODO: consider persistentDataPath is often safer for writing save data, 
        rootSaveDirectory = Path.Combine(Application.persistentDataPath, "Data");
    }
    
    private void Start()
    {
        SetControllersActive(false);
        InitializeLSL();
        InitializeSetupUI();
    }
    
    void InitializeLSL()
    {
        // Define the stream info: Name, Type, Channel Count (1), Rate (0=Irregular/Event), Format (String)
        StreamInfo streamInfo = new StreamInfo(lslStreamName, lslStreamType, 1, 0, channel_format_t.cf_string);
        lslOutlet = new StreamOutlet(streamInfo);
        Debug.Log($"LSL Stream '{lslStreamName}' created.");
    }
    
    // Generates a unique marker string based on the current trial config
    // Format: Phase_Owner_Condition_State
    // Examples: "Long_Self_Async_Start", "Threshold_Other_Sync_End"
    void SendLSLMarker(TrialData t, string state)
    {
        if (lslOutlet == null || t.phase == ExperimentPhase.Practice) return;
        
        string phaseStr = t.phase == ExperimentPhase.Threshold ? "Threshold" : "Long";
        string ownerStr = t.isSelf ? "Self" : "Other";
        string conditionStr = "Sync"; // Default for Threshold

        if (t.phase == ExperimentPhase.Long)
        {
            // If delay > 0 it is Async
            conditionStr = (t.delay > 0) ? "Async" : "Sync";
        }
        else
        {
            conditionStr = $"{t.delay}ms"; 
        }

        // Construct the marker string
        string marker = $"{phaseStr}_{ownerStr}_{conditionStr}_{state}";

        // Send to LSL
        lslSample[0] = marker;
        lslOutlet.push_sample(lslSample);
        
        Debug.Log($"<color=cyan>[LSL] Sent Marker: {marker}</color>");
    }
    
    void InitializeSetupUI()
    {
        setupCanvas.SetActive(true);

        // 1. CALCULATE PARTICIPANT NUMBER
        // Check the Data folder to count existing participants
        if (!Directory.Exists(rootSaveDirectory)) Directory.CreateDirectory(rootSaveDirectory);
        string[] directories = Directory.GetDirectories(rootSaveDirectory);
        
        int nextParticipantNum = directories.Length + 1; 

        // GENERATE ID STRING
        idInput.text = "P" + nextParticipantNum.ToString("000");

        // POPULATE HARDWARE LISTS (Must be dynamic)
        webcamDropdown.ClearOptions();
        foreach (var d in WebCamTexture.devices) webcamDropdown.options.Add(new TMP_Dropdown.OptionData(d.name));
        webcamDropdown.RefreshShownValue();

        // comPortDropdown.ClearOptions();
        // string[] ports = System.IO.Ports.SerialPort.GetPortNames();
        // foreach (var p in ports) comPortDropdown.options.Add(new TMP_Dropdown.OptionData(p));
        // comPortDropdown.RefreshShownValue();
        if (latencyInput != null)
        {
            // 1. LOAD SAVED LATENCY (Default to your script's default 0.134f if nothing saved)
            float savedLatency = PlayerPrefs.GetFloat("SystemLatency", estimatedSystemLatency);
    
            // 2. Update the variable immediately so the experiment is ready
            estimatedSystemLatency = savedLatency;

            // 3. Update the UI text
            latencyInput.text = savedLatency.ToString(); 
        }
        // 4. SET DEFAULT INDICES (Based on Participant Number)
        // A. Threshold: Odd = Self-First (Index 0), Even = Other-First (Index 1)
        bool isOdd = (nextParticipantNum % 2 != 0);
        thresholdConditionDropdown.value = isOdd ? 0 : 1;
        thresholdConditionDropdown.RefreshShownValue();

        // B. Long: Cycle 0-3 based on participant number
        // P1=0, P2=1, P3=2, P4=3, P5=0...
        longConditionDropdown.value = (nextParticipantNum - 1) % 4;
        longConditionDropdown.RefreshShownValue();
        
        if (sizeInput != null)
        {
            // 1. LOAD SAVED VALUE (Default to 1.0 if nothing saved)
            float savedSize = PlayerPrefs.GetFloat("WebcamScale", 1.0f);

            // 2. Apply to UI and Script immediately
            sizeInput.text = savedSize.ToString("F2");
            if (webcamScript != null) webcamScript.viewSize = savedSize;
            if (viewSizeLabel != null) viewSizeLabel.text = "Webcam Size: " + savedSize.ToString("F2");

            // 3. Listener: Update Script live while typing
            sizeInput.onValueChanged.RemoveAllListeners();
            sizeInput.onValueChanged.AddListener((valStr) => 
            {
                if (float.TryParse(valStr, out float newVal))
                {
                    if (webcamScript != null) webcamScript.viewSize = newVal;
                    if (viewSizeLabel != null) viewSizeLabel.text = "Webcam Size: " + newVal.ToString("F2");
                }
            });
        }

        // 6. AUTO-START PREVIEW (The "By Default" logic)
        screenObject.SetActive(true); // Turn on the Quad
        
        // Start the camera immediately with the default option
        if (webcamDropdown.options.Count > 0)
        {
            string defaultCam = webcamDropdown.options[0].text;
            webcamScript.Initialize(defaultCam);
        }

        // 7. HANDLE WEBCAM CHANGES
        // If the researcher picks a different camera, restart immediately
        webcamDropdown.onValueChanged.RemoveAllListeners();
        webcamDropdown.onValueChanged.AddListener((index) => 
        {
            string newCam = webcamDropdown.options[index].text;
            webcamScript.Initialize(newCam);
        });

        // 5. BIND CONFIRM BUTTON
        confirmButton.onClick.RemoveAllListeners();
        confirmButton.onClick.AddListener(OnConfirmSettings);
    }
    
    public void OnConfirmSettings()
    {
        // Capture Final Decisions
        // The researcher might have changed the ID or the dropdowns, 
        // so we trust the UI values over the calculated ones at this point.
        participantID = idInput.text;
        
        // Initialize Hardware
        string selectedCamera = webcamDropdown.options[webcamDropdown.value].text;
        webcamScript.Initialize(selectedCamera);
        
        // Read Condition Indices
        bool selfFirst = (thresholdConditionDropdown.value == 0);
        int latinGroupIndex = longConditionDropdown.value;
        
        // Latency
        if (latencyInput != null)
        {
            if (float.TryParse(latencyInput.text, out float parsedLatency))
            {
                // Update the runtime variable
                estimatedSystemLatency = parsedLatency;
        
                // SAVE TO PLAYER PREFS
                PlayerPrefs.SetFloat("SystemLatency", parsedLatency);
                PlayerPrefs.Save(); // Force write to disk

                Debug.Log($"System Latency updated and saved: {estimatedSystemLatency}s");
            }
            else
            {
                Debug.LogWarning($"Invalid Latency format '{latencyInput.text}'. Keeping previous: {estimatedSystemLatency}s");
            }
        }
        
        if (sizeInput != null && float.TryParse(sizeInput.text, out float finalSize))
        {
            // Clamp to prevent invisible or massive screens (0.1x to 5.0x)
            finalSize = Mathf.Clamp(finalSize, 0.1f, 5.0f); 
    
            PlayerPrefs.SetFloat("WebcamScale", finalSize);
            PlayerPrefs.Save(); // Force write to disk

            // Ensure script has final value
            webcamScript.viewSize = finalSize;
        }
        // Hide UI
        setupCanvas.SetActive(false);

        // Launch Experiment
        StartExperiment(selfFirst, latinGroupIndex);
    }
    
    public string PreviewNextParticipantID()
    {
        if (!Directory.Exists(rootSaveDirectory)) Directory.CreateDirectory(rootSaveDirectory);
        string[] directories = Directory.GetDirectories(rootSaveDirectory, "P*");
        return "P" + (directories.Length + 1).ToString("000");
    }
    
    public void StartExperiment(bool startWithSelf, int latinGroupIndex)
    {
        // 1. Create Folders & Files
        // (This still calculates participantNum for folder naming, but won't dictate logic)
        SetupParticipantFiles();

        // 2. Generate the Trials using the PASSED arguments
        // REMOVED: bool startWithSelf = (participantNum % 2 != 0); 
        GenerateAllTrials(startWithSelf, latinGroupIndex);

        // 3. Final UI & Timer Setup
        UpdateExperimenterUI($"ID: {participantID}\nStarting with PRACTICE.\n\nPress SPACE to begin.");
        
        startTime = Time.time;
        hasSetupFinished = true;
        screenObject.SetActive(true);
        Debug.Log($"<color=green>Experiment Started. ID: {participantID}. Group: {latinGroupIndex+1}. Data saved to: {participantFolder}</color>");
    }

    private void SetupParticipantFiles()
    {
        // Create Folder
        participantFolder = Path.Combine(rootSaveDirectory, participantID);
        if (!Directory.Exists(participantFolder)) Directory.CreateDirectory(participantFolder);

        // Define Paths
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
        
        eventLogPath = Path.Combine(participantFolder, $"{participantID}_{timestamp}_Events.csv");
        thresholdDataPath = Path.Combine(participantFolder, $"{participantID}_{timestamp}_Threshold.csv");
        longDataPath = Path.Combine(participantFolder, $"{participantID}_{timestamp}_Long.csv");

        // --- WRITE HEADERS ---
        File.WriteAllText(eventLogPath, "Timestamp,Phase,TrialID,Event,Data,AppliedDelay\n");
        File.WriteAllText(thresholdDataPath, "ParticipantID,TrialOrder,TrialID,OwnerCondition,DelayMS,Q_SyncBinary,Q_Ownership,Q_Pleasantness\n");
        string qHeaders = "Q1_Alienation,Q2_BodyNotMine,Q3_Numb,Q4_LessVivid,Q5_BodyOwn,Q6_MoveSeen,Q7_NotReal,Q8_Detached,Q9_Pleasant";
        File.WriteAllText(longDataPath, $"ParticipantID,TrialOrder,TrialID,OwnerCondition,DelayType,{qHeaders}\n");
    }

    // SAVE DATA ROWS
    void SaveThresholdRow(TrialData t, string questionnaireData, float appliedDelay)
    {
        if (t.isPractice) return; // skip if practice trial
        // questionnaireData format: "Yes,0.55,0.82"
        globalTrialCounter++;
        string owner = t.isSelf ? "Self" : "Other";
        string row = $"{participantID},{globalTrialCounter},{t.id},{owner},{t.delay},{questionnaireData}";

        File.AppendAllText(thresholdDataPath, row + "\n");
        LogEvent(t, appliedDelay, "Data_Saved", "Threshold"); 
    }
    
    void SaveLongRow(TrialData t, string questionnaireData, float appliedDelay)
    {
        // questionnaireData format: "0.1,0.2,..."
        globalTrialCounter++;
        string owner = t.isSelf ? "Self" : "Other";

        // Logic: If delay > 0, it is Async
        string delayType = (t.delay > 0) ? "Asynchronous" : "Synchronous";
        string row = $"{participantID},{globalTrialCounter},{t.id},{owner},{delayType},{questionnaireData}";
        File.AppendAllText(longDataPath, row + "\n");

        LogEvent(t, appliedDelay, "Data_Saved", "Long");
    }

    // LOG EVENT
    void LogEvent(TrialData t, float appliedDelay, string eventName, string eventValue)
    {
        // Format: Timestamp, Phase, TrialID, Event, Value, AppliedWebcamDelay
        string row = $"{Time.time - startTime:F3},{t.phase},{t.id},{eventName},{eventValue},{appliedDelay:F3}";
        File.AppendAllText(eventLogPath, row + "\n");
    }
    
    private void GenerateAllTrials(bool selfFirst, int latinGroupIndex)
    {
        AddPracticeBlock();
        
        // 1. Threshold Generation
        if (selfFirst)
        {
            AddThresholdBlock(true);  
            AddThresholdBlock(false); 
        }
        else
        {
            AddThresholdBlock(false); 
            AddThresholdBlock(true);  
        }

        // 2. Long Generation (Using the UI selection)
        int[][] latinSquare = new int[][]
        {
            new int[] { 0, 1, 3, 2 }, // Group 1
            new int[] { 1, 2, 0, 3 }, // Group 2
            new int[] { 2, 3, 1, 0 }, // Group 3
            new int[] { 3, 0, 2, 1 }  // Group 4
        };

        // Safety check
        latinGroupIndex = Mathf.Clamp(latinGroupIndex, 0, 3);
    
        int[] selectedSequence = latinSquare[latinGroupIndex];

        foreach (int conditionIndex in selectedSequence)
        {
            AddLongTrialByIndex(conditionIndex);
        }
    }
    
    // Add practice
    void AddPracticeBlock()
    {
        for (int i = 1; i <= 3; i++)
        {
            trialStack.Add(new TrialData
            {
                id = $"Practice_{i}",
                phase = ExperimentPhase.Practice,
                isSelf = true, // Default to Self for practice
                delay = 0,     // No delay
                duration = practiceDuration,
                isPractice = true
            });
        }
    }
    
    // Helper to translate the Latin Square Index (0-3) into actual Trial Data
    private void AddLongTrialByIndex(int index)
    {
        bool isSelf = (index == 0 || index == 1); // 0 and 1 are Self
        bool isSync = (index == 0 || index == 2); // 0 and 2 are Sync
    
        string ownerLabel = isSelf ? "Self" : "Other";
        string delayLabel = isSync ? "Sync" : "Async";
    
        // Determine Delay (0 for Sync, target for Async)
        int delay = isSync ? 0 : longAsyncTargetDelay;

        // Create the single trial
        trialStack.Add(new TrialData 
        { 
            id = $"Long_{delayLabel}_{ownerLabel}", 
            phase = ExperimentPhase.Long, 
            isSelf = isSelf, 
            delay = delay, 
            duration = longDuration,
            isPractice = false
        });
    }
    
    void AddThresholdBlock(bool isSelf)
    {
        List<TrialData> blockTrials = new List<TrialData>();
        string ownerLabel = isSelf ? "Self" : "Other";

        int stepSize = maxThresholdDelay / (nThresholdSteps-1);
        
        for (int i = 0; i < nThresholdSteps; i++)
        {
            // NOTE: haven't added system delay to threshold delay
            int trialDelay = i * stepSize;

            for (int r = 0; r < thresholdRepetitions; r++)
            {
                blockTrials.Add(new TrialData
                {
                    id = $"Threshold_{ownerLabel}_{trialDelay}ms_{(r+1)}",
                    phase = ExperimentPhase.Threshold,
                    isSelf = isSelf,
                    delay = trialDelay, 
                    duration = thresholdDuration,
                    isPractice = false
                });
            }
        }
        Shuffle(blockTrials);
        trialStack.AddRange(blockTrials);
    }
    
    void Update()
    {
        if (!hasSetupFinished) return;
        
        if (!isRunning && trialStack.Count > 0)
        {
                currentTrial = trialStack[0];
                trialStack.RemoveAt(0);
                StartCoroutine(RunTrial(currentTrial));
        }
        else if (!isRunning && trialStack.Count == 0)
        {
            UpdateExperimenterUI($"EXPERIMENT COMPLETE.\nID: {participantID}");
        }
    }
    
    // Timeline of trial
    IEnumerator RunTrial(TrialData trial)
    {
        isRunning = true;
        if (!screenObject.activeSelf) screenObject.SetActive(true); // Ensure script is running
        webcamScript.SetVisuals(false);
        float appliedDelay = 0f;
        float targetDelaySeconds = trial.delay / 1000f;
        
        if (trial.phase == ExperimentPhase.Threshold)
        {
            appliedDelay = targetDelaySeconds;
        }
        else if (trial.phase == ExperimentPhase.Long)
        {
            // LONG: We want a specific TOTAL experience (Target).
            // Applied = Target - System
            appliedDelay = Mathf.Max(0f, targetDelaySeconds - estimatedSystemLatency);
        }
        else if (trial.phase == ExperimentPhase.Practice)
        {
            appliedDelay = Mathf.Max(0f, targetDelaySeconds - estimatedSystemLatency);
        }
        
        Debug.Log($"<color=yellow>[TRIAL SETUP] ID: {trial.id} | Target: {targetDelaySeconds:F3}s | System Latency: {estimatedSystemLatency:F3}s | <b>APPLIED DELAY: {appliedDelay:F3}s</b></color>");
        
        // UI Updates
        string actor = trial.isSelf ? "PARTICIPANT" : "RESEARCHER";
        string phase = trial.phase == ExperimentPhase.Threshold ? "THRESHOLD" : "LONG";
        
        UpdateExperimenterUI($"Phase: {phase}\n\nNext actor: {actor}\n\nPress 'Space' when ready.");
        while (!Input.GetKeyDown(KeyCode.Space))
        {
            // Allow Researcher to toggle camera to check setup
            if (Input.GetKeyDown(KeyCode.C))
            {
				webcamScript.currentDelaySeconds = 0.0f; // remove delay for testing
                bool current = webcamScript.IsVisualsEnabled();
                webcamScript.SetVisuals(!current);
            }
            yield return null;
        }

        webcamScript.currentDelaySeconds = appliedDelay;
        webcamScript.SetVisuals(false);
        LogEvent(trial, appliedDelay, "Trial_Start", "Intention");
        UpdateExperimenterUI("Running...");
        
        yield return new WaitForSeconds(ISI);
        
        PlayBeep(); 
        webcamScript.SetVisuals(true);
        LogEvent(trial, appliedDelay, "Stimulation_Start", "Visuals_On");
        SendLSLMarker(trial, "Start");
        
        float timer = 0;
        while (timer < trial.duration)
        {
            timer += Time.deltaTime;
            UpdateExperimenterUI($"{phase}\n{actor}\n{trial.duration - timer:F1}s");
            if(Input.GetKeyDown(KeyCode.Escape)) 
            { 
                // --- CHANGE 4: EMERGENCY EXIT ---
                // OLD: screenObject.SetActive(false); 
                // NEW:
                webcamScript.SetVisuals(false);
                isRunning=false; 
                yield break; 
            }
            yield return null;
        }

        webcamScript.SetVisuals(false);       
        SendLSLMarker(trial, "End");
        PlayBeep();
        LogEvent(trial, appliedDelay, "Stimulation_End", "Visuals_Off");
        
        // QUESTIONNAIRE LOGIC
        UpdateExperimenterUI("Waiting for Response...");
        SetControllersActive(true);

        bool qAnswered = false;

        // CHECK PHASE AND SHOW CORRECT UI
        if (trial.phase == ExperimentPhase.Threshold || trial.phase == ExperimentPhase.Practice)
        {
            questionnaireScript.ShowThresholdQuestionnaire((resultString) => 
            {
                // resultString example: "Yes,0.45,0.12"
                if (!trial.isPractice) SaveThresholdRow(trial, resultString, appliedDelay);
                qAnswered = true;
            });
        }
        else if (trial.phase == ExperimentPhase.Long)
        {
            questionnaireScript.ShowLongQuestionnaire((resultString) => 
            {
                // resultString example: "0.1,0.2,0.3,..."
                if (!trial.isPractice) SaveLongRow(trial, resultString, appliedDelay);
                qAnswered = true;
            });
        }
        else
        {
            Debug.Log($"INCORRECT TRIAL PHASE! {trial}");
            UpdateExperimenterUI($"INCORRECT TRIAL PHASE! {trial}");
        }

        // Wait here until the callback above sets qAnswered = true
        yield return new WaitUntil(() => qAnswered);

        SetControllersActive(false); // Disable lasers
        UpdateExperimenterUI("Trial Done. Press SPACE.");
        isRunning = false;
    }  

    void UpdateExperimenterUI(string message) { if (experimenterDisplay != null) experimenterDisplay.text = message; }
    void PlayBeep() { if(audioSource) audioSource.Play(); }
    
    void SetControllersActive(bool isActive)
    {
        GameObject[] controllers = new GameObject[] { leftHandController, rightHandController };

        foreach (var controllerGO in controllers)
        {
            if (controllerGO == null) continue;
            // Always on - so 'XR Controller (Action-based)' script keeps tracking position.
            controllerGO.SetActive(true);
            // Toggle the Ray Interactor - stops controller from interacting
            var rayInteractor = controllerGO.GetComponent<XRRayInteractor>();
            if (rayInteractor != null) rayInteractor.enabled = isActive;
            // Toggle Laser
            var lineVisual = controllerGO.GetComponent<XRInteractorLineVisual>();
            if (lineVisual != null) lineVisual.enabled = isActive;
            var lineRenderer = controllerGO.GetComponent<LineRenderer>();
            if (lineRenderer != null) lineRenderer.enabled = isActive;
            // Toggle Controller 3D Model
            var renderers = controllerGO.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r != lineRenderer) // So we don't double set it
                {
                    r.enabled = isActive;
                }
            }
        }
    }
    
    void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++) { T temp = list[i]; int r = UnityEngine.Random.Range(i, list.Count); list[i] = list[r]; list[r] = temp; }
    }
}