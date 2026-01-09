using System;

[Serializable]
public class ParticipantData
{
    public string ParticipantID;
    public string Handedness;
    public string Gender;
    public int Age;
    public string Ethnicity;
    public int YearsEducation;
    public string AlcoholFreq;
    public string CannabisFreq;

    // Condition Data
    public string ConditionName; // e.g., "Researcher Strokes"
}