using UnityEngine;
using UnityEngine.UI;
using System;
using System.Text;
using System.Collections.Generic;
using TMPro;

public class QuestionnaireManager : MonoBehaviour
{
    [Header("UI Containers")]
    public GameObject fullPanel;
    public GameObject thresholdPanel;
    
    [Header("Full Questionnaire Elements")]
    public Slider[] fullSliders;
    
    [Header("Threshold Questionnaire Elements")]
    public Slider[] thresholdSliders;  // Drag the 2 sliders here (Ownership, Pleasantness)
    public Button yesButton;           // Drag the 'YES' button
    public Button noButton;            // Drag the 'NO' button
    
    // Internal State
    private Action<string> onCompleteCallback;
    private string binaryAnswer = "NA"; // Stores "Yes" or "No"

    void Start()
    {
        // Ensure everything is hidden at start
        if(fullPanel) fullPanel.SetActive(false);
        if(thresholdPanel) thresholdPanel.SetActive(false);

        // Setup Binary Button Listeners
        if (yesButton) yesButton.onClick.AddListener(() => SetBinaryChoice("Yes"));
        if (noButton)  noButton.onClick.AddListener(() => SetBinaryChoice("No"));
    }
    
    // --- PUBLIC METHODS CALLED BY EXPERIMENT MANAGER ---
    public void ShowFullQuestionnaire(Action<string> callback)
    {
        onCompleteCallback = callback;
        
        ResetSliders(fullSliders);

        fullPanel.SetActive(true);
        thresholdPanel.SetActive(false);
    }
    
    public void ShowThresholdQuestionnaire(Action<string> callback)
    {
        onCompleteCallback = callback;

        // Reset UI
        ResetSliders(thresholdSliders);
        binaryAnswer = "NA"; 
        ResetButtonColors();

        // Show Panel
        thresholdPanel.SetActive(true);
        fullPanel.SetActive(false);
    }
    
    // --- HELPER METHODS ---

    public void SubmitFull()
    {
        // 1. Collect Data (9 columns)
        StringBuilder sb = new StringBuilder();
        foreach (Slider s in fullSliders)
        {
            sb.Append(s.value.ToString("F3") + ",");
        }
        
        // Remove trailing comma
        if(sb.Length > 0) sb.Length--;

        // Hide & Callback
        fullPanel.SetActive(false);
        onCompleteCallback?.Invoke(sb.ToString());
    }
    
    public void SubmitThreshold()
    {
        // Validation: Did they select Yes/No?
        if (binaryAnswer == "NA")
        {
            Debug.LogWarning("Participant must select Yes or No first!");
            return; // Don't submit yet
        }

        // 1. Collect Data: "Yes,0.54,0.88"
        string slider1 = thresholdSliders[0].value.ToString("F3");
        string slider2 = thresholdSliders[1].value.ToString("F3");
        string result = $"{binaryAnswer},{slider1},{slider2}";

        // 2. Hide & Callback
        thresholdPanel.SetActive(false);
        onCompleteCallback?.Invoke(result);
    }

    // --- UI LOGIC ---
    void SetBinaryChoice(string choice)
    {
        binaryAnswer = choice;
        
        // Visual Feedback (Highlight selected)
        Color selectedColor = Color.green;
        Color normalColor = Color.white;

        var yesImg = yesButton.GetComponent<Image>();
        var noImg = noButton.GetComponent<Image>();

        if (yesImg) yesImg.color = (choice == "Yes") ? selectedColor : normalColor;
        if (noImg)  noImg.color = (choice == "No")  ? selectedColor : normalColor;
    }
    
    void ResetSliders(Slider[] sliders)
    {
        foreach (var s in sliders) s.value = 0.5f; // Or 0 if you prefer
    }
    
    void ResetButtonColors()
    {
        if (yesButton) yesButton.GetComponent<Image>().color = Color.white;
        if (noButton)  noButton.GetComponent<Image>().color = Color.white;
    }
}