using UnityEngine;
using System.IO;
using System.Text;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit;

public class ExperimentManager : MonoBehaviour
{
    // SINGLETON
    public static ExperimentManager instance { get; private set; }
    
    // PUBLIC DATA PATH ACCESSORS
    public string participantID;      
    public string participantFolder; 
    public string eventLogPath;
    public string thresholdDataPath;
    public string longDataPath;
    public string demographicsPath;
    
    // Internal
    private string rootSaveDirectory;
    private int participantNum;
    
    private List<TrialData> trialStack = new List<TrialData>();
    private int globalTrialCounter = 0;
    private float startTime;
    private bool isRunning = false;
    private TrialData currentTrial;

    public enum ExperimentPhase { Threshold, Long }
    
    [System.Serializable]
    public class TrialData
    {
        public string id;             // e.g. "Threshold_66ms"
        public ExperimentPhase phase; // Threshold or Long
        public bool isSelf;           // True = Participant strokes, False = Researcher strokes
        public int delay;             // In milliseconds
        public float duration;        // 7s or 60s
    }
    
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
        rootSaveDirectory = Path.Combine(Application.streamingAssetsPath, "Data");
    }
    
    public string PreviewNextParticipantID()
    {
        if (!Directory.Exists(rootSaveDirectory)) Directory.CreateDirectory(rootSaveDirectory);
        string[] directories = Directory.GetDirectories(rootSaveDirectory, "P*");
        return "P" + (directories.Length + 1).ToString("000");
    }
    
    public void InitExperiment(ParticipantData demographics)
    {
        SetupParticipantFiles();

        // Save Demographics to CSV
        SaveDemographicsFile(demographics);

        // Run the setup logic
        bool startWithSelf = (participantNum % 2 != 0);
        GenerateAllTrials(startWithSelf);
    
        // Update UI
        screenObject.SetActive(false);
        if(thresholdUI) thresholdUI.SetActive(false);
        UpdateExperimenterUI($"ID: {participantID}\nOrder: {(startWithSelf ? "Self-First" : "Other-First")}\nPress SPACE to begin.");
        Debug.Log($"<color=green>Experiment Started. ID: {participantID}. Demographics Saved.</color>");
        
        // Record start time
        startTime = Time.time;
    }

    // Helper to save demographics to a separate single file
    private void SaveDemographicsFile(ParticipantData d)
    {
        // Simple CSV format
        string header = "ParticipantID,Age,Gender,Handedness,Ethnicity,Alcohol,Cannabis,StartCondition\n";
        
        // Determine start condition string for the record
        string startCond = (participantNum % 2 != 0) ? "Self-First" : "Other-First";
        
        string data = $"{participantID},{d.Age},{d.Gender},{d.Handedness},{d.Ethnicity},{d.AlcoholFreq},{d.CannabisFreq},{startCond}\n";
        
        File.WriteAllText(demographicsPath, header + data);
    }

    private void SetupParticipantFiles()
    {
        // Ensure Root Exists
        if (!Directory.Exists(rootSaveDirectory)) Directory.CreateDirectory(rootSaveDirectory);

        // Generate ID
        string[] directories = Directory.GetDirectories(rootSaveDirectory, "P*");
        participantNum = directories.Length + 1;
        participantID = "P" + participantNum.ToString("000");

        // Create Folder
        participantFolder = Path.Combine(rootSaveDirectory, participantID);
        if (!Directory.Exists(participantFolder)) Directory.CreateDirectory(participantFolder);

        // Define Paths
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
        
        eventLogPath = Path.Combine(participantFolder, $"{participantID}_{timestamp}_Events.csv");
        thresholdDataPath = Path.Combine(participantFolder, $"{participantID}_{timestamp}_Threshold.csv");
        longDataPath = Path.Combine(participantFolder, $"{participantID}_{timestamp}_Long.csv");
        demographicsPath = Path.Combine(participantFolder, $"{participantID}_{timestamp}_Demographics.csv");

        // --- WRITE HEADERS (Matches your original format) ---
        
        // Fixed: Added ",AppliedDelay" to match the 6 columns in LogEvent
        File.WriteAllText(eventLogPath, "Timestamp,Phase,TrialID,Event,Data,AppliedDelay\n");

        File.WriteAllText(thresholdDataPath, "ParticipantID,TrialOrder,TrialID,OwnerCondition,DelayMS,Q_SyncBinary,Q_Ownership,Q_Pleasantness\n");
        
        string qHeaders = "Q1_Alienation,Q2_BodyNotMine,Q3_Numb,Q4_LessVivid,Q5_BodyOwn,Q6_MoveSeen,Q7_NotReal,Q8_Detached,Q9_Pleasant";
        File.WriteAllText(longDataPath, $"ParticipantID,TrialOrder,TrialID,OwnerCondition,DelayType,{qHeaders}\n");
    }

    // 2. SAVE THRESHOLD (Restored Logic)
    void SaveThresholdRow(TrialData t, string questionnaireData, float appliedDelay)
    {
        // questionnaireData format: "Yes,0.55,0.82"
        globalTrialCounter++;
        string owner = t.isSelf ? "Self" : "Other";

        // Matches your original format exactly
        string row = $"{participantID},{globalTrialCounter},{t.id},{owner},{t.delay},{questionnaireData}";

        File.AppendAllText(thresholdDataPath, row + "\n");

        LogEvent(t, appliedDelay, "Data_Saved", "Threshold"); 
    }

    // 3. SAVE LONG (Restored Logic)
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

    // 4. LOG EVENT (Restored Logic)
    void LogEvent(TrialData t, float appliedDelay, string eventName, string eventValue)
    {
        // Format: Timestamp, Phase, TrialID, Event, Value, AppliedWebcamDelay
        string row = $"{Time.time - startTime:F3},{t.phase},{t.id},{eventName},{eventValue},{appliedDelay:F3}";

        File.AppendAllText(eventLogPath, row + "\n");
    }
    
    void GenerateAllTrials(bool selfFirst)
    {
        if (selfFirst)
        {
            AddThresholdBlock(true);  
            AddThresholdBlock(false); 
            AddLongBlock(true);       
            AddLongBlock(false);      
        }
        else
        {
            AddThresholdBlock(false); 
            AddThresholdBlock(true);  
            AddLongBlock(false);      
            AddLongBlock(true);       
        }

        Debug.Log($"Generated Total: {trialStack.Count} trials.");
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
                    duration = thresholdDuration
                });
            }
        }
        Shuffle(blockTrials);
        trialStack.AddRange(blockTrials);
    }
    
    void AddLongBlock(bool isSelf)
    {
        List<TrialData> blockTrials = new List<TrialData>();
        string ownerLabel = isSelf ? "Self" : "Other";
        
        // Sync Condition (instant)
        blockTrials.Add(new TrialData { 
            id=$"Long_Sync_{ownerLabel}", 
            phase=ExperimentPhase.Long, 
            isSelf=isSelf, 
            delay=0, 
            duration=longDuration 
        });

        // Async Condition:
        // Target is 1.0 second TOTAL (System + Artificial).
        blockTrials.Add(new TrialData { 
            id=$"Long_Async_{ownerLabel}", 
            phase=ExperimentPhase.Long, 
            isSelf=isSelf, 
            delay=longAsyncTargetDelay, 
            duration=longDuration 
        });

        Shuffle(blockTrials);
        trialStack.AddRange(blockTrials);
    }
    
    void Update()
    {
        if (!isRunning && trialStack.Count > 0)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                currentTrial = trialStack[0];
                trialStack.RemoveAt(0);
                StartCoroutine(RunTrial(currentTrial));
            }
        }
        else if (!isRunning && trialStack.Count == 0)
        {
            UpdateExperimenterUI("EXPERIMENT COMPLETE.");
        }
    }
    
    // Timeline of trial
    IEnumerator RunTrial(TrialData trial)
    {
        isRunning = true;
        float appliedDelay = 0f;
        float targetDelaySeconds = trial.delay / 1000f;
        
        if (trial.phase == ExperimentPhase.Threshold)
        {
            appliedDelay = targetDelaySeconds;
        }
        else
        {
            // LONG: We want a specific TOTAL experience (Target).
            // Applied = Target - System
            appliedDelay = Mathf.Max(0f, targetDelaySeconds - estimatedSystemLatency);
        }
        
        webcamScript.delaySeconds = appliedDelay;
        webcamScript.useDelay = (appliedDelay > 0.001f); 

        // UI Updates
        string actor = trial.isSelf ? "PARTICIPANT" : "RESEARCHER";
        string phase = trial.phase == ExperimentPhase.Threshold ? "THRESHOLD" : "LONG";
        
        UpdateExperimenterUI($"Phase: {phase}\n\nNext actor: {actor}\n\nPress 'R' when ready.");
        yield return new WaitUntil(() => Input.GetKeyDown(KeyCode.R));  //NOTE uncomment to run trials automatically

        LogEvent(trial, appliedDelay, "Trial_Start", "Intention");
        UpdateExperimenterUI("Running...");
        
        yield return new WaitForSeconds(ISI);
        
        PlayBeep(); 
        screenObject.SetActive(true);
        LogEvent(trial, appliedDelay, "Stimulation_Start", "Visuals_On");
        
        float timer = 0;
        while (timer < trial.duration)
        {
            timer += Time.deltaTime;
            UpdateExperimenterUI($"{trial.id}\n{trial.duration - timer:F1}s");
            if(Input.GetKeyDown(KeyCode.Escape)) { screenObject.SetActive(false); isRunning=false; yield break; }
            yield return null;
        }

        screenObject.SetActive(false);
        PlayBeep();
        LogEvent(trial, appliedDelay, "Stimulation_End", "Visuals_Off");
        
        // QUESTIONNAIRE LOGIC
        UpdateExperimenterUI("Waiting for Response...");
        SetControllersActive(true);

        bool qAnswered = false;

        // CHECK PHASE AND SHOW CORRECT UI
        if (trial.phase == ExperimentPhase.Threshold)
        {
            questionnaireScript.ShowThresholdQuestionnaire((resultString) => 
            {
                // resultString example: "Yes,0.45,0.12"
                SaveThresholdRow(trial, resultString, appliedDelay);
                qAnswered = true;
            });
        }
        else
        {
            questionnaireScript.ShowLongQuestionnaire((resultString) => 
            {
                // resultString example: "0.1,0.2,0.3,..."
                SaveLongRow(trial, resultString, appliedDelay);
                qAnswered = true;
            });
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
        if(leftHandController) leftHandController.SetActive(isActive);
        if(rightHandController) rightHandController.SetActive(isActive);
    }
    void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++) { T temp = list[i]; int r = UnityEngine.Random.Range(i, list.Count); list[i] = list[r]; list[r] = temp; }
    }
}