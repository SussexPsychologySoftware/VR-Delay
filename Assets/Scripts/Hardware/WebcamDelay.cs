using UnityEngine;
using System.Collections.Generic;

public class WebcamDelay : MonoBehaviour
{
    [Header("Device Selection")]
    public string deviceName = ""; 
    
    [Header("Display Settings")]
    [Tooltip("Size multiplier. 1.0 = 1 meter tall. Adjust this to fill view.")]
    [Range(0.1f, 2.0f)] public float viewSize = 0.8f; 
    public bool fixAspectRatio = true;

    [Header("Delay Settings")]
    public bool useDelay = false;
    [Range(0f, 5f)] public float delaySeconds = 1.0f;

    [Header("Performance")]
    public int targetFPS = 50;

    // Internal
    private WebCamTexture webcam;
    private Renderer screenRenderer;
    private Texture2D displayTexture;
    private Queue<Color32[]> frameBuffer = new Queue<Color32[]>();
    private bool isRatioSet = false;

    public void Initialize()
    {
        screenRenderer = GetComponent<Renderer>();
    
        // Safety: If no name was provided, default to the first available
        if (string.IsNullOrEmpty(deviceName) && WebCamTexture.devices.Length > 0)
        {
            deviceName = WebCamTexture.devices[0].name;
        }

        StartWebcam();
    }

    void StartWebcam()
    {
        string nameToUse = string.IsNullOrEmpty(deviceName) ? WebCamTexture.devices[0].name : deviceName;
        // Request a high resolution, but the camera might give us something else
        webcam = new WebCamTexture(nameToUse, 1920, 1080, targetFPS);
        webcam.Play();

        displayTexture = new Texture2D(webcam.width, webcam.height);
        screenRenderer.material.mainTexture = displayTexture;
    }

    void Update()
    {
        if (webcam == null || !webcam.isPlaying) return;
        // Check if webcam has started and we haven't set the size yet
        if (webcam.didUpdateThisFrame) 
        {
            // Only adjust scale once the camera gives us valid dimensions ( > 100px)
            if (fixAspectRatio && !isRatioSet && webcam.width > 100)
            {
                UpdateAspectRatio();
            }
            
            ProcessFrames();
        }
    }

    // This function calculates the correct shape based on the camera hardware
    public void UpdateAspectRatio()
    {
        float aspectRatio = (float)webcam.width / (float)webcam.height;
        
        // Scale Y is controlled by viewSize
        // Scale X is calculated: Height * AspectRatio
        transform.localScale = new Vector3(viewSize * aspectRatio, viewSize, 1f);
        
        isRatioSet = true;
        //Debug.Log($"Camera Resolution: {webcam.width}x{webcam.height}. Aspect Ratio: {aspectRatio}");
    }

    // Called automatically when you change values in Inspector
    void OnValidate()
    {
        // Allows you to adjust the slider in real-time while playing
        if (webcam != null && webcam.isPlaying)
        {
            UpdateAspectRatio();
        }
    }

    void ProcessFrames()
    {
        Color32[] currentPixels = webcam.GetPixels32();

        if (useDelay)
        {
            frameBuffer.Enqueue(currentPixels);
            int requiredFrameCount = Mathf.RoundToInt(delaySeconds * targetFPS);

            if (frameBuffer.Count > requiredFrameCount)
            {
                displayTexture.SetPixels32(frameBuffer.Dequeue());
                displayTexture.Apply();
            }
        }
        else
        {
            displayTexture.SetPixels32(currentPixels);
            displayTexture.Apply();
            if (frameBuffer.Count > 0) frameBuffer.Clear();
        }
    }

    void OnDestroy()
    {
        if (webcam != null) webcam.Stop();
    }
}