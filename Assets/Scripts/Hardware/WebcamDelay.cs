using UnityEngine;
using System.Collections.Generic;

public class WebcamDelay : MonoBehaviour
{
    [Header("Device Selection")]
    public string deviceName = ""; 
    
    [Header("Display Settings")]
    [Range(0.1f, 2.0f)] public float viewSize = 0.8f; 
    
    [Header("Delay Settings")]
    public bool useDelay = false;
    [Range(0f, 3f)] public float delaySeconds = 1.0f;

    [Header("Memory Optimization")]
    [Tooltip("Downscale the buffer to save VRAM. 0.5 = half resolution (25% memory use).")]
    [Range(0.1f, 1.0f)] public float bufferScale = 1f;
    public int targetFPS = 50;

    private WebCamTexture webcam;
    private Renderer screenRenderer;
    private RenderTexture[] frameBuffer;
    private int writeHead = 0;
    private int bufferSize = 0;

    public void Initialize()
    {
        screenRenderer = GetComponent<Renderer>();
        if (string.IsNullOrEmpty(deviceName) && WebCamTexture.devices.Length > 0)
            deviceName = WebCamTexture.devices[0].name;

        StartWebcam();
    }

    void StartWebcam()
    {
        // Request high res from hardware, but we will downscale for storage
        webcam = new WebCamTexture(deviceName, 1920, 1080, targetFPS);
        webcam.Play();
        StartCoroutine(SetupBuffer());
    }

    System.Collections.IEnumerator SetupBuffer()
    {
        while (webcam.width < 100) yield return null;

        // Calculate downscaled dimensions
        int bWidth = Mathf.RoundToInt(webcam.width * bufferScale);
        int bHeight = Mathf.RoundToInt(webcam.height * bufferScale);

        // Pre-allocate the GPU Ring Buffer
        bufferSize = targetFPS * 4; // Buffer for up to 4 seconds
        frameBuffer = new RenderTexture[bufferSize];

        for (int i = 0; i < bufferSize; i++)
        {
            // Create small, efficient textures
            frameBuffer[i] = new RenderTexture(bWidth, bHeight, 0);
            frameBuffer[i].filterMode = FilterMode.Bilinear; // Smooths out the downscaling
            frameBuffer[i].Create();
        }

        // Apply Aspect Ratio
        float aspect = (float)webcam.width / webcam.height;
        transform.localScale = new Vector3(viewSize * aspect, viewSize, 1f);
    }

    void Update()
    {
        if (webcam == null || !webcam.isPlaying) return;
        if (!webcam.didUpdateThisFrame) return;

        // DETERMINING THE BYPASS
        // If delay is off or set to 0, we show the raw webcam feed directly
        bool isBypassActive = !useDelay || delaySeconds <= 0.01f;

        if (isBypassActive)
        {
            // SPEED: Directly pipe the webcam texture to the material
            screenRenderer.material.mainTexture = webcam;
        
            // Optional: We stop writing to the buffer to save GPU cycles
            return; 
        }

        // DELAY LOGIC (Only runs if delay is active)
        if (frameBuffer == null) return;

        // 1. Write to buffer
        Graphics.Blit(webcam, frameBuffer[writeHead]);

        // 2. Calculate Read Head
        int framesToDelay = Mathf.RoundToInt(delaySeconds * targetFPS);
        framesToDelay = Mathf.Clamp(framesToDelay, 1, bufferSize - 1);
        int readHead = (writeHead - framesToDelay + bufferSize) % bufferSize;

        // 3. Display from buffer
        screenRenderer.material.mainTexture = frameBuffer[readHead];

        // 4. Advance Write Head
        writeHead = (writeHead + 1) % bufferSize;
    }

    void OnDestroy()
    {
        if (webcam != null) webcam.Stop();
        if (frameBuffer != null)
        {
            foreach (var rt in frameBuffer) if (rt != null) rt.Release();
        }
    }
}