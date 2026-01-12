using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class ResearcherSetup : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject setupPanel; // Assign the Canvas/Panel holding this form
    public TextMeshProUGUI statusText; // Assign the red instruction text at the top

    [Header("Demographic Inputs")]
    public TMP_InputField ageInput;
    public TMP_InputField educationInput;
    public TMP_Dropdown genderDropdown;
    public TMP_Dropdown handednessDropdown;
    public TMP_Dropdown ethnicityDropdown;
    public TMP_Dropdown alcoholDropdown;
    public TMP_Dropdown cannabisDropdown;

    // NOTE: Removed Condition Dropdown (Handled automatically by Manager now)

    private void Start()
    {
        // 1. Preview the Next ID
        // We ask the Manager what the next ID will be so we can write it in our notebook if needed.
        if (ExperimentManager.instance != null)
        {
            string nextID = ExperimentManager.instance.PreviewNextParticipantID();
            UpdateStatus($"Ready. Next Participant: {nextID}");
        }
    }

    public void OnStartClicked()
    {
        // 1. Basic Validation
        if (string.IsNullOrEmpty(ageInput.text))
        {
            UpdateStatus("Error: Please enter Age.");
            return;
        }

        // 2. Package the Data
        ParticipantData data = new ParticipantData();
        
        int.TryParse(ageInput.text, out data.Age);
        int.TryParse(educationInput.text, out data.YearsEducation);
        
        data.Gender = GetDropdownValue(genderDropdown);
        data.Handedness = GetDropdownValue(handednessDropdown);
        data.Ethnicity = GetDropdownValue(ethnicityDropdown);
        data.AlcoholFreq = GetDropdownValue(alcoholDropdown);
        data.CannabisFreq = GetDropdownValue(cannabisDropdown);

        // 3. Hand off to Manager
        // This starts the experiment, creates folders, and generates the ID.
        if (ExperimentManager.instance != null)
        {
            ExperimentManager.instance.StartExperiment(data);
        }
        else
        {
            Debug.LogError("ExperimentManager not found!");
            return;
        }

        // 4. Hide Setup UI
        // The ExperimentManager will now display its own instructions on the 'experimenterDisplay'.
        setupPanel.SetActive(false); 
    }

    // Helper to get text safely
    private string GetDropdownValue(TMP_Dropdown dropdown)
    {
        if (dropdown == null || dropdown.options.Count == 0) return "NA";
        return dropdown.options[dropdown.value].text;
    }

    private void UpdateStatus(string msg)
    {
        if (statusText) statusText.text = msg;
    }
}