using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Text;

public class QuestionnaireManager : MonoBehaviour
{
    [Header("UI Elements")]
    public GameObject questionnairePanel; // The entire Canvas/Panel
    public Button submitButton;
    public List<Slider> questionSliders;  // Drag sliders here
    
    // Callback to tell the main manager questionnaire is finished
    private System.Action<string> onCompleteCallback;

    void Start()
    {
        // Setup Submit Button
        submitButton.onClick.AddListener(SubmitAnswers);
        
        // Hide by default
        questionnairePanel.SetActive(false);
    }

    public void ShowQuestionnaire(System.Action<string> onComplete)
    {
        onCompleteCallback = onComplete;
        
        // Reset sliders to 0 (or 0.5 middle)
        foreach (var slider in questionSliders)
        {
            slider.value = 0;
        }

        questionnairePanel.SetActive(true);
    }

    void SubmitAnswers()
    {
        // Collect Data
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < questionSliders.Count; i++)
        {
            // Format: Q1:0.45;Q2:0.12;...
            sb.Append($"Q{i+1}:{questionSliders[i].value:F2};");
        }

        // Hide UI
        questionnairePanel.SetActive(false);

        // Send data back to Experiment Manager
        onCompleteCallback?.Invoke(sb.ToString());
    }
}