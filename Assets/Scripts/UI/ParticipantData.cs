[System.Serializable]
public struct ParticipantData
{
    // We don't include ID here because the Manager generates it.
    public int Age;
    public int YearsEducation;
    public string Gender;
    public string Handedness;
    public string Ethnicity;
    public string AlcoholFreq;
    public string CannabisFreq;
}