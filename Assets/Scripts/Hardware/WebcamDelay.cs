using UnityEngine;
using System.Collections;

public class WebcamDelay : MonoBehaviour
{
    [Header("Configuration")]
    public int requestWidth = 1920; 
    public int requestHeight = 1080;
    public int requestFPS = 60; // Request 60 to force high-speed mode (low exposure/blur)

    [Header("Visual Quality")]
    [Tooltip("Adjust this slider in real-time to change screen size.")]
    [Range(0.1f, 5.0f)] public float viewSize = 0.8f;
    
    // Trilinear is much smoother for VR than Bilinear
    public FilterMode textureFilterMode = FilterMode.Trilinear; 

    [Header("Experimental Variables")]
    [Tooltip("Change this dynamically during the experiment (0 = real-time).")]
    [Range(0f, 3f)] public float currentDelaySeconds = 0.0f;

    [Tooltip("The maximum delay you will ever test. Memory is reserved for this amount.")]
    public float maxDelayCap = 3.0f; 

    // Private internals
    private WebCamTexture webcam;
    private Renderer screenRenderer;
    private RenderTexture[] frameBuffer;
    private int writeHead = 0;
    private int bufferSize = 0;
    private float actualFPS = 30f; 
    private bool isInitialized = false;

    // Call this from your ExperimentManager
    public void Initialize(string selectedDeviceName)
    {
        screenRenderer = GetComponent<Renderer>();
        StartCoroutine(StartWebcamRoutine(selectedDeviceName));
    }

    IEnumerator StartWebcamRoutine(string deviceName)
    {
        if (webcam != null) webcam.Stop();

        // 1. Start Camera
        webcam = new WebCamTexture(deviceName, requestWidth, requestHeight, requestFPS);
        webcam.Play();

        // 2. Wait for hardware initialization (Crucial for resolution)
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

        // 3. LOG ACTUAL RESOLUTION (Check your Console!)
        // If this says 640x480, your USB port is too slow or the camera is in USB 2.0 mode.
        Debug.Log($"<color=green>Webcam Active: {webcam.width}x{webcam.height} @ {webcam.requestedFPS} FPS</color>");
        
        // 4. Setup Ring Buffer
        actualFPS = webcam.requestedFPS > 0 ? webcam.requestedFPS : 30f;
        int safeFPS = Mathf.Max((int)actualFPS, 60); 
        
        bufferSize = Mathf.CeilToInt(maxDelayCap * safeFPS) + safeFPS; 
        frameBuffer = new RenderTexture[bufferSize];

        for (int i = 0; i < bufferSize; i++)
        {
            // ARGB32 = High quality, uncompressed.
            frameBuffer[i] = new RenderTexture(webcam.width, webcam.height, 0, RenderTextureFormat.ARGB32);
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
        if (webcam != null) webcam.Stop();
        if (frameBuffer != null)
        {
            foreach (var rt in frameBuffer) if (rt != null) rt.Release();
        }
    }
}