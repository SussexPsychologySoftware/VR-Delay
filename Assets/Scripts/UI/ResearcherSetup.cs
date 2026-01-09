using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;

public class ResearcherSetup : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject demographicsPanel; 
    public GameObject researcherIntsructions;
    
    [Header("Running Elements")]
    public TextMeshProUGUI researcherInstructionText;
    
    [Header("Demographic Inputs")]
    public TMP_InputField ageInput;
    public TMP_InputField educationInput;
    public TMP_Dropdown genderDropdown;
    public TMP_Dropdown handednessDropdown;
    public TMP_Dropdown ethnicityDropdown;
    public TMP_Dropdown alcoholDropdown;
    public TMP_Dropdown cannabisDropdown;

    [Header("Experiment Settings")]
    public TMP_Dropdown conditionDropdown; // e.g., "Researcher Strokes", "Participant Strokes"
    public Button startButton;

    private string saveDirectory;

    private void Start()
    {
        // Initial State: Show Setup, Hide Running
        demographicsPanel.SetActive(true);
        researcherIntsructions.SetActive(false);
    }

    private void PopulateDropdowns()
    {
        // Example: Clear and add options if not done in Editor
        // You can also just set these up in the Inspector and skip this code.
        conditionDropdown.ClearOptions();
        conditionDropdown.AddOptions(new System.Collections.Generic.List<string> { "Researcher Strokes", "Participant Strokes" });
    }

    private string GenerateParticipantID()
    {
        // SIMPLE METHOD: Count files and add 1.
        // File pattern: "Participant_X.csv"
        int fileCount = Directory.GetFiles(saveDirectory, "Participant_*.csv").Length;
        return "P" + (fileCount + 1).ToString("000"); // Returns P001, P002, etc.
    }

    private void OnStartClicked()
    {
        // Gather Data
        ParticipantData data = new ParticipantData();
        data.ParticipantID = GenerateParticipantID();
        data.Age = int.TryParse(ageInput.text, out int a) ? a : 0;
        data.YearsEducation = int.TryParse(educationInput.text, out int e) ? e : 0;
        
        // Get text from Dropdowns
        data.Gender = genderDropdown.options[genderDropdown.value].text;
        data.Handedness = handednessDropdown.options[handednessDropdown.value].text;
        data.Ethnicity = ethnicityDropdown.options[ethnicityDropdown.value].text;
        data.AlcoholFreq = alcoholDropdown.options[alcoholDropdown.value].text;
        data.CannabisFreq = cannabisDropdown.options[cannabisDropdown.value].text;
        data.ConditionName = conditionDropdown.options[conditionDropdown.value].text;

        // Set Researcher Instructions (red text)
        if (data.ConditionName == "Researcher Strokes")
            researcherInstructionText.text = "INSTRUCTION: STROKE THE HAND";
        else
            researcherInstructionText.text = "INSTRUCTION: OBSERVE THE PARTICIPANT";

        // SWAP PANELS
        demographicsPanel.SetActive(false);
        researcherIntsructions.SetActive(true);
        
        // 3. Pass to Manager and Start
        // Assuming ExperimentManager is a Singleton
        ExperimentManager.Instance.InitializeExperiment(data);

        // Hide this Setup Canvas 
        //gameObject.SetActive(false); 
    }

    private void UpdateResearcherInstructions(string condition)
    {
        if (researcherInstructionText != null)
        {
            researcherInstructionText.color = Color.red;
            
            if (condition == "Researcher Strokes")
            {
                researcherInstructionText.text = "INSTRUCTION: Researcher must stroke the rubber hand.";
            }
            else
            {
                researcherInstructionText.text = "INSTRUCTION: Participant must stroke the rubber hand.";
            }
        }
    }
}