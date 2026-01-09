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
    public static ExperimentManager Instance { get; private set; }
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
    
    [Header("UI & Input")]
    public QuestionnaireManager questionnaireScript; // Drag the new script here
    public GameObject thresholdUI;
    public GameObject leftHandController; // Drag XR Origin > LeftHand Controller
    public GameObject rightHandController; // Drag XR Origin > RightHand Controller
    
    [Header("Participant Settings")]
    public string participantID = "test01";
    public bool startWithSelf = true;     // Toggle this to counterbalance order

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
    
    // Internal State
    private List<TrialData> trialStack = new List<TrialData>();
    private string eventLogPath;
    private string thresholdDataPath;
    private string longDataPath;
    
    private int globalTrialCounter = 0;
    private float startTime;
    private bool isRunning = false;
    private TrialData currentTrial;
    
    void Start()
    {
        SetupDataFile();
        GenerateAllTrials(startWithSelf);
        
        screenObject.SetActive(false);
        if(thresholdUI) thresholdUI.SetActive(false);
        
        startTime = Time.time;
        UpdateExperimenterUI($"Press SPACE to begin.");
    }
    
    public void InitExperiment(string id, bool isSelfFirst, int age, string gender, string hand)
    {
        // 1. Accept settings from the Setup UI
        participantID = id;
        startWithSelf = isSelfFirst;
    
        // 2. Log Demographics immediately (Optional: or save to a separate file)
        SaveDemographics(id, age, gender, hand, isSelfFirst);

        // 3. Now run the setup logic that used to be in Start()
        SetupDataFile();
        GenerateAllTrials(startWithSelf);
    
        // 4. Update UI
        screenObject.SetActive(false);
        if(thresholdUI) thresholdUI.SetActive(false);
        UpdateExperimenterUI($"ID: {participantID}\nOrder: {(startWithSelf ? "Self-First" : "Other-First")}\nPress SPACE to begin.");
    }

    // Helper to save demographics to a separate single file
    void SaveDemographics(string id, int age, string sex, string hand, bool selfFirst)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Data", "Master_Demographics.csv");
        string header = "ParticipantID,Timestamp,Age,Sex,Handedness,ConditionOrder\n";
    
        if (!File.Exists(path)) 
        {
            // Create folder if needed
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, header);
        }
    
        string row = $"{id},{DateTime.Now},{age},{sex},{hand},{(selfFirst ? "Self-First" : "Other-First")}\n";
        File.AppendAllText(path, row);
    }

    void SetupDataFile()
    {
        string folder = Path.Combine(Application.streamingAssetsPath, "Data", participantID);
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");

        // 1. EVENT LOG (Technical Debugging)
        eventLogPath = Path.Combine(folder, $"{participantID}_{timestamp}_Events.csv");
        File.WriteAllText(eventLogPath, "Timestamp,Phase,TrialID,Event,Data\n");

        // 2. THRESHOLD DATA (Analysis Ready)
        // Columns: ID, TrialOrder, TrialID, Condition(Self/Other), Delay(ms), Q_Sync(Binary), Q_Ownership, Q_Pleasantness
        thresholdDataPath = Path.Combine(folder, $"{participantID}_{timestamp}_Threshold.csv");
        File.WriteAllText(thresholdDataPath, "ParticipantID,TrialOrder,TrialID,OwnerCondition,DelayMS,Q_SyncBinary,Q_Ownership,Q_Pleasantness\n");

        // 3. LONG DATA (Analysis Ready)
        // Columns: ID, TrialOrder, TrialID, Condition(Self/Other), DelayType(Sync/Async), Q1_Alienation, Q2_Ownership, ..., Q9_Pleasantness
        longDataPath = Path.Combine(folder, $"{participantID}_{timestamp}_Long.csv");
        string qHeaders = "Q1_Alienation,Q2_BodyNotMine,Q3_Numb,Q4_LessVivid,Q5_BodyOwn,Q6_MoveSeen,Q7_NotReal,Q8_Detached,Q9_Pleasant";
        File.WriteAllText(longDataPath, $"ParticipantID,TrialOrder,TrialID,OwnerCondition,DelayType,{qHeaders}\n");
    }
    
    void SaveThresholdRow(TrialData t, string questionnaireData, float appliedDelay)
    {
        // questionnaireData format: "Yes,0.55,0.82"
        globalTrialCounter++;
        string owner = t.isSelf ? "Self" : "Other";
    
        // Use t.delayMs (int)
        string row = $"{participantID},{globalTrialCounter},{t.id},{owner},{t.delay},{questionnaireData}";
    
        File.AppendAllText(thresholdDataPath, row + "\n");
    
        // Pass 't' and 'appliedDelay' to match new LogEvent signature
        LogEvent(t, appliedDelay, "Data_Saved", "Threshold"); 
    }

    void SaveLongRow(TrialData t, string questionnaireData, float appliedDelay)
    {
        // questionnaireData format: "0.1,0.2,..."
        globalTrialCounter++;
        string owner = t.isSelf ? "Self" : "Other";
    
        // Use t.delayMs check
        string delayType = (t.delay > 0) ? "Asynchronous" : "Synchronous";
    
        string row = $"{participantID},{globalTrialCounter},{t.id},{owner},{delayType},{questionnaireData}";
    
        File.AppendAllText(longDataPath, row + "\n");
    
        // Pass 't' and 'appliedDelay'
        LogEvent(t, appliedDelay, "Data_Saved", "Long");
    }

    // Simplified Event Logger for technical events (Start/Stop/Beep)
    void LogEvent(TrialData t, float appliedDelay, string eventName, string eventValue)
    {
        // Format: Timestamp, Phase, TrialID, Event, Value, AppliedWebcamDelay
        string row = $"{Time.time - startTime:F3},{t.phase},{t.id},{eventName},{eventValue},{appliedDelay:F3}";
    
        // Write to the technical event log ONLY
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