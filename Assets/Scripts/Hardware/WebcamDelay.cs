using UnityEngine;
using System.Collections;

public class WebcamDelay : MonoBehaviour
{
    [Header("Configuration")]
    public int requestWidth = 1280; 
    public int requestHeight = 720;
    public int requestFPS = 60; // Request 60 to force high-speed mode (low exposure/blur)

    [Header("Visual Quality")]
    [Tooltip("Adjust this slider in real-time to change screen size.")]
    [Range(0.1f, 5.0f)] public float viewSize = 0.8f;
    
    // Trilinear is much smoother for VR than Bilinear
    public FilterMode textureFilterMode = FilterMode.Trilinear; 

    [Header("Experimental Variables")]
    [Tooltip("Change this dynamically during the experiment (0 = real-time).")]
    [Range(0f, 1.5f)] public float currentDelaySeconds = 0.0f;

    [Tooltip("The maximum delay you will ever test. Memory is reserved for this amount.")]
    public float maxDelayCap = 1.5f; 

    // Private internals
    private WebCamTexture webcam;
    private Renderer screenRenderer;
    private RenderTexture[] frameBuffer;
    private int writeHead = 0;
    private int bufferSize = 0;
    private float actualFPS = 30f; 
    private bool isInitialized = false;
    private Coroutine activeRoutine; // Track so we can cancel on re-init

    // Call this from your ExperimentManager
    public void Initialize(string selectedDeviceName)
    {
        screenRenderer = GetComponent<Renderer>();
        
        // Stop any in-progress startup coroutine so we don't get two running
        if (activeRoutine != null) StopCoroutine(activeRoutine);
        
        // Release old resources BEFORE allocating new ones
        CleanupResources();
        
        activeRoutine = StartCoroutine(StartWebcamRoutine(selectedDeviceName));
    }
    
    // Centralised cleanup — called on re-init AND on destroy
    private void CleanupResources()
    {
        isInitialized = false;
        writeHead = 0;
        
        if (webcam != null) { webcam.Stop(); webcam = null; }
        
        if (frameBuffer != null)
        {
            foreach (var rt in frameBuffer)
                if (rt != null) rt.Release();
            frameBuffer = null;
        }
    }

    IEnumerator StartWebcamRoutine(string deviceName)
    {
        // CleanupResources() already stopped old webcam and released old buffers

        // Start Camera
        webcam = new WebCamTexture(deviceName, requestWidth, requestHeight, requestFPS);
        webcam.Play();

        // Wait for hardware initialization (Crucial for resolution)
        float timeout = 5.0f;
        while (webcam.width < 100 && timeout > 0) 
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (webcam.width < 100)
        {
            Debug.LogError($"Failed to start webcam: {deviceName}");
            yield break;
        }

        // LOG ACTUAL RESOLUTION (Check console!)
        // If this says 640x480, USB port is too slow or the camera is in USB 2.0 mode.
        Debug.Log($"<color=green>Webcam Active: {webcam.width}x{webcam.height} @ {webcam.requestedFPS} FPS</color>");
        
        // Setup Ring Buffer
        actualFPS = webcam.requestedFPS > 0 ? webcam.requestedFPS : 30f;
        int safeFPS = Mathf.Max((int)actualFPS, 60); 
        
        bufferSize = Mathf.CeilToInt(maxDelayCap * safeFPS) + safeFPS; 
        frameBuffer = new RenderTexture[bufferSize];

        for (int i = 0; i < bufferSize; i++)
        {
            // RGB565 = 2 bytes/pixel (vs 4 for ARGB32). No alpha needed for webcam.
            // Halves VRAM usage per texture. Visually imperceptible for a live hand feed.
            frameBuffer[i] = new RenderTexture(webcam.width, webcam.height, 0, RenderTextureFormat.RGB565);
            frameBuffer[i].filterMode = textureFilterMode;
            frameBuffer[i].Create();
        }
        
        isInitialized = true;
    }

    void Update()
    {
        if (!isInitialized || !webcam.didUpdateThisFrame) return;

        // --- UPDATE VIEW SIZE REALTIME ---
        // We calculate this every frame so you can adjust the slider in the Inspector
        if (webcam.height > 0)
        {
            float aspect = (float)webcam.width / webcam.height;
            transform.localScale = new Vector3(viewSize * aspect, viewSize, 1f);
        }

        // --- BUFFER LOGIC ---
        // A. Write to Buffer 
        Graphics.Blit(webcam, frameBuffer[writeHead]);

        // B. Read from Buffer (Delay Logic)
        if (currentDelaySeconds <= 0.02f) 
        {
            screenRenderer.material.mainTexture = frameBuffer[writeHead];
        }
        else
        {
            int framesToDelay = Mathf.RoundToInt(currentDelaySeconds * actualFPS);
            framesToDelay = Mathf.Clamp(framesToDelay, 0, bufferSize - 1);
            
            int readHead = (writeHead - framesToDelay + bufferSize) % bufferSize;
            screenRenderer.material.mainTexture = frameBuffer[readHead];
        }

        // C. Advance Write Head
        writeHead = (writeHead + 1) % bufferSize;
    }

    void OnDestroy()
    {
        CleanupResources();
    }
    
    public void SetVisuals(bool isVisible)
    {
        if (screenRenderer != null)
        {
            screenRenderer.enabled = isVisible;
        }
    }
    
    public bool IsVisualsEnabled()
    {
        return screenRenderer != null && screenRenderer.enabled;
    }
}