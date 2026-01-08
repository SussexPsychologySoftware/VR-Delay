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
    public enum ExperimentPhase { Threshold, Long }
    
    [System.Serializable]
    public class TrialData
    {
        public string id;             // e.g. "Threshold_66ms"
        public ExperimentPhase phase; // Threshold or Long
        public bool isSelf;           // True = Participant strokes, False = Researcher strokes
        public float delay;      // In seconds (e.g. 0.066)
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

    [Header("Experiment Settings")] 
    public float maxThresholdDelay = 0.594f;
    public int nThresholdSteps = 10; // Includes 0! i.e. 594/10 = stepSize=66ms
    public float estimatedSystemLatency = 0.1f;
    public float longAsyncTargetDelay = 1.0f; // Target for Long Asynchronous trials (e.g. 1.0s)
    public float thresholdDuration = 7.0f;
    public float longDuration = 60.0f;
    public float ISI = 1.0f;
    public int thresholdRepetitions = 4;
    
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
    private string savePath;
    private StringBuilder csvData = new StringBuilder();
    private float startTime;
    private bool isRunning = false;
    private TrialData currentTrial;
    
    void Start()
    {
        SetupDataFile();
        GenerateAllTrials(startWithSelf);
        
        screenObject.SetActive(false);
        if(thresholdUI) thresholdUI.SetActive(false);
        
        UpdateExperimenterUI($"Press SPACE to begin.");
    }

    void SetupDataFile()
    {
        string folder = Path.Combine(Application.streamingAssetsPath, "Data", participantID);
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        string fileName = $"{participantID}_{DateTime.Now:yyyy-MM-dd_HH-mm}.csv";
        savePath = Path.Combine(folder, fileName);

        csvData.AppendLine("Timestamp,Phase,Condition,TargetDelay,AppliedDelay,Event,Value");
        File.WriteAllText(savePath, csvData.ToString());

        startTime = Time.time;
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

        float stepSize = maxThresholdDelay / (nThresholdSteps-1);
        
        for (int i = 0; i < nThresholdSteps; i++)
        {
            // NOTE: haven't added system delay to threshold delay
            float rawAddedDelay = i * stepSize;

            for (int r = 0; r < thresholdRepetitions; r++)
            {
                blockTrials.Add(new TrialData
                {
                    id = $"Threshold_+{Mathf.RoundToInt(rawAddedDelay * 1000)}ms",
                    phase = ExperimentPhase.Threshold,
                    isSelf = isSelf,
                    delay = rawAddedDelay, 
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

        // Sync Condition (instant)
        blockTrials.Add(new TrialData { 
            id="Long_Sync", 
            phase=ExperimentPhase.Long, 
            isSelf=isSelf, 
            delay=0.0f, 
            duration=longDuration 
        });

        // Async Condition:
        // Target is 1.0 second TOTAL (System + Artificial).
        blockTrials.Add(new TrialData { 
            id="Long_Async", 
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

        if (trial.phase == ExperimentPhase.Threshold)
        {
            appliedDelay = trial.delay;
        }
        else
        {
            // LONG: We want a specific TOTAL experience (Target).
            // Applied = Target - System
            appliedDelay = Mathf.Max(0f, trial.delay - estimatedSystemLatency);
        }
        
        webcamScript.delaySeconds = appliedDelay;
        webcamScript.useDelay = (appliedDelay > 0.001f); 

        // UI Updates
        string actor = trial.isSelf ? "PARTICIPANT" : "RESEARCHER";
        string phase = trial.phase == ExperimentPhase.Threshold ? "THRESHOLD" : "LONG";
        
        UpdateExperimenterUI($"Phase: {phase}\nNext actor: {actor}\n\nPress 'R' when ready.");
        yield return new WaitUntil(() => Input.GetKeyDown(KeyCode.R));  //NOTE uncomment to run trials automatically

        LogData(trial, appliedDelay, "Trial_Start", "Intention");
        UpdateExperimenterUI("Running...");
        
        yield return new WaitForSeconds(ISI);
        
        PlayBeep(); 
        screenObject.SetActive(true);
        LogData(trial, appliedDelay, "Stimulation_Start", "Visuals_On");

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
        LogData(trial, appliedDelay, "Stimulation_End", "Visuals_Off");

        // QUESTIONNAIRE LOGIC
        UpdateExperimenterUI("Waiting for Response...");
        SetControllersActive(true);

        bool qAnswered = false;

        // CHECK PHASE AND SHOW CORRECT UI
        if (trial.phase == ExperimentPhase.Threshold)
        {
            // Show the new Threshold Panel (Binary + 2 Sliders)
            questionnaireScript.ShowThresholdQuestionnaire((resultString) => 
            {
                // Result format: "Yes,0.45,0.12"
                LogData(trial, appliedDelay, "Threshold_Data", resultString);
                qAnswered = true;
            });
        }
        else
        {
            // Show the Full Scroll View (9 Sliders)
            questionnaireScript.ShowFullQuestionnaire((resultString) => 
            {
                // Result format: "0.1,0.2,0.3,..."
                LogData(trial, appliedDelay, "Full_Data", resultString);
                qAnswered = true;
            });
        }

        // Wait here until the callback above sets qAnswered = true
        yield return new WaitUntil(() => qAnswered);

        SetControllersActive(false); // Disable lasers
        UpdateExperimenterUI("Trial Done. Press SPACE.");
        isRunning = false;
    }  
    
    void LogData(TrialData t, float applied, string ev, string val)
    {
        string row = $"{Time.time - startTime:F3},{t.phase},{t.isSelf},{t.delay:F3},{applied:F3},{ev},{val}";
        csvData.AppendLine(row);
        File.AppendAllText(savePath, row + "\n");
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