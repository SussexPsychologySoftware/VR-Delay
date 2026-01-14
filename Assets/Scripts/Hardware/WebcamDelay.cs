using UnityEngine;
using UnityEngine.UI; 
using System.Collections;
using Serenegiant; // Required for AndroidUtils

public class WebcamDelay : MonoBehaviour
{
    [Header("UVC Connection")]
    [Tooltip("Drag the RawImage from your Canvas here. The UVC plugin draws to this, and we will steal the texture from it.")]
    public RawImage uvcRawImage;

    [Header("Configuration")]
    public int requestFPS = 60; 

    [Header("Visual Quality")]
    [Range(0.1f, 5.0f)] public float viewSize = 0.8f;
    public FilterMode textureFilterMode = FilterMode.Trilinear; 

    [Header("Delay Settings")]
    [Range(0f, 3f)] public float currentDelaySeconds = 0.0f;
    public float maxDelayCap = 3.0f; 

    // Internal Variables
    private Renderer screenRenderer;
    private RenderTexture[] frameBuffer;
    private int writeHead = 0;
    private int bufferSize = 0;
    private float actualFPS = 60f; 
    private bool isInitialized = false;

    // --- INITIALIZATION ---
    // This is called by ExperimentManager.cs
    // We keep the 'deviceName' argument to prevent errors, but we ignore it 
    // because we are forcing the UVC connection on Quest.
    public void Initialize(string deviceNameIgnored = "")
    {
        // Prevent double-initialization
        StopAllCoroutines();
        isInitialized = false;

        screenRenderer = GetComponent<Renderer>();
        
        // Start the Quest-specific startup sequence
        StartCoroutine(StartupRoutine());
    }

    IEnumerator StartupRoutine()
    {
        Debug.Log("WebcamDelay: Requesting Android Permissions...");

        // 1. Ask for Android Permissions
        // We use a flag to wait for the callback
        bool permissionCheckDone = false;
        bool permissionGranted = false;

        yield return AndroidUtils.GrantCameraPermission((permission, result) => 
        {
            if (result == AndroidUtils.PermissionGrantResult.PERMISSION_GRANT)
                permissionGranted = true;
            
            permissionCheckDone = true;
        });

        // Wait for callback to finish (just in case)
        while (!permissionCheckDone) yield return null;

        if (!permissionGranted)
        {
            Debug.LogError("WebcamDelay: Camera Permission Denied! Script stopped.");
            yield break;
        }

        // 2. Permission granted, now wait for the hardware to start
        Debug.Log("WebcamDelay: Waiting for UVC Texture...");
        yield return WaitForUVCTexture();
    }

    IEnumerator WaitForUVCTexture()
    {
        // 3. Wait until the UVC plugin has actually created a valid texture
        while (uvcRawImage == null || uvcRawImage.texture == null || uvcRawImage.texture.width < 100)
        {
            yield return null; 
        }

        Debug.Log($"<color=green>UVC Camera Found: {uvcRawImage.texture.width}x{uvcRawImage.texture.height}</color>");

        // 4. Initialize the Buffer
        SetupBuffer(uvcRawImage.texture.width, uvcRawImage.texture.height);
        isInitialized = true;
    }

    void SetupBuffer(int width, int height)
    {
        actualFPS = requestFPS > 0 ? requestFPS : 60f;
        int safeFPS = Mathf.Max((int)actualFPS, 60); 
        
        bufferSize = Mathf.CeilToInt(maxDelayCap * safeFPS) + safeFPS; 
        frameBuffer = new RenderTexture[bufferSize];

        for (int i = 0; i < bufferSize; i++)
        {
            frameBuffer[i] = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            frameBuffer[i].filterMode = textureFilterMode;
            frameBuffer[i].Create();
        }
    }

    // --- MAIN LOOP ---
    void Update()
    {
        // Stop if not ready or if the texture vanished (unplugged)
        if (!isInitialized || uvcRawImage.texture == null) return;

        Texture source = uvcRawImage.texture;

        // --- UPDATE VIEW SIZE ---
        if (source.height > 0)
        {
            float aspect = (float)source.width / source.height;
            transform.localScale = new Vector3(viewSize * aspect, viewSize, 1f);
        }

        // --- BUFFER LOGIC ---
        // A. Copy current Webcam frame to Ring Buffer
        Graphics.Blit(source, frameBuffer[writeHead]);

        // B. Read from Ring Buffer (Delay Logic)
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

        // C. Advance Head
        writeHead = (writeHead + 1) % bufferSize;
    }

    void OnDestroy()
    {
        if (frameBuffer != null)
        {
            foreach (var rt in frameBuffer) if (rt != null) rt.Release();
        }
    }
}