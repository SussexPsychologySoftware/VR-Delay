using UnityEngine;
using System.Collections.Generic;

public class WebcamDelay : MonoBehaviour
{
    [Header("Device Selection")]
    [Tooltip("Leave empty to use default. Check Console for list of names.")]
    public string deviceName = ""; 
    
    [Header("Delay Settings")]
    public bool useDelay = false;
    [Range(0f, 5f)] public float delaySeconds = 1.0f;

    [Header("Performance")]
    public int targetFPS = 30; // 30 is standard for most webcams
    public Vector2Int resolution = new Vector2Int(1280, 720);

    // Internal
    private WebCamTexture webcam;
    private Renderer screenRenderer;
    private Texture2D displayTexture;
    private Queue<Color32[]> frameBuffer = new Queue<Color32[]>();

    void Start()
    {
        screenRenderer = GetComponent<Renderer>();
        ListDevices(); // Prints names to Console
        StartWebcam();
    }

    void ListDevices()
    {
        Debug.Log("--- Available Webcams ---");
        foreach (var device in WebCamTexture.devices)
        {
            Debug.Log($"Camera: {device.name}");
        }
        Debug.Log("-------------------------");
    }

    void StartWebcam()
    {
        // 1. Create the Webcam Texture
        // If specific name is given, use it. Otherwise use default.
        string nameToUse = string.IsNullOrEmpty(deviceName) ? WebCamTexture.devices[0].name : deviceName;
        
        webcam = new WebCamTexture(nameToUse, resolution.x, resolution.y, targetFPS);
        webcam.Play();

        // 2. Create the Display Texture (The canvas we paint on)
        displayTexture = new Texture2D(webcam.width, webcam.height);
        screenRenderer.material.mainTexture = displayTexture;
        
        Debug.Log($"Started Webcam: {nameToUse} at {webcam.width}x{webcam.height}");
    }

    void Update()
    {
        // Only process if the webcam has a new frame ready
        if (webcam.didUpdateThisFrame)
        {
            Color32[] currentPixels = webcam.GetPixels32();

            if (useDelay)
            {
                // A. Add current frame to the buffer (The "Waiting Room")
                frameBuffer.Enqueue(currentPixels);

                // B. Calculate how many frames correspond to the requested seconds
                // e.g. 1.0s * 30fps = 30 frames
                int requiredFrameCount = Mathf.RoundToInt(delaySeconds * targetFPS);

                // C. If the buffer is full enough, release the oldest frame
                if (frameBuffer.Count > requiredFrameCount)
                {
                    Color32[] oldPixels = frameBuffer.Dequeue();
                    displayTexture.SetPixels32(oldPixels);
                    displayTexture.Apply();
                }
            }
            else
            {
                // Synchronous Mode: Direct pass-through
                displayTexture.SetPixels32(currentPixels);
                displayTexture.Apply();

                // Clear the buffer so we don't have "stale" frames when we switch back
                if (frameBuffer.Count > 0) frameBuffer.Clear();
            }
        }
    }

    void OnDestroy()
    {
        if (webcam != null) webcam.Stop();
    }
}