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
    private float[] frameTimes;    // Capture time (Time.time) per slot, parallel to frameBuffer.
                                   // Lets us select the delayed frame by ELAPSED TIME rather than
                                   // by counting frames, so the delay is correct regardless of the
                                   // camera's actual/variable capture rate.
    private int writeHead = 0;
    private int bufferSize = 0;
    private float firstRealFrameTime = -1f; // Time.time of the first genuinely-captured frame
                                            // after init (-1 = none yet). Drives the readiness
                                            // gate: we can only honour a delay of D once at
                                            // least D seconds of real frames have accumulated.
    private float actualFPS = 30f; // Only used for buffer SIZING now, not for the delay itself.
    private bool isInitialized = false;
    private Coroutine activeRoutine; // Track so we can cancel on re-init
    private Coroutine fpsRoutine;    // Periodic actual-FPS logger

    // Optional UI hook: invoked with a human-readable status only on connection state
    // changes (never per-frame). Assign directly, e.g. cam.OnStatusChanged = lbl.SetText.
    public System.Action<string> OnStatusChanged;

    // True once the camera is streaming and the ring buffer is allocated. Lets callers
    // avoid restarting a camera that's already running (a restart triggers the cold-start
    // enumeration race) and gate trial start on a live feed.
    // Getter shorthand - read only outside the class
    public bool IsInitialized => isInitialized;

    // True once enough genuinely-captured frames have accumulated to honour a given delay — i.e.
    // the camera has been streaming for at least `delaySeconds`. Before that the feed shows black
    // rather than a stale primed frame, so callers can gate a trial's measurement window on this
    // to guarantee every displayed frame is a real, correctly-delayed frame.
    public bool IsReadyForDelay(float delaySeconds) =>
        isInitialized && firstRealFrameTime >= 0f && (Time.time - firstRealFrameTime) >= delaySeconds;

    // Readiness for the delay currently configured.
    public bool IsReady => IsReadyForDelay(currentDelaySeconds);

    private void SetStatus(string msg)
    {
        Debug.Log($"[Webcam] {msg}");
        OnStatusChanged?.Invoke(msg);
    }

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
        firstRealFrameTime = -1f;
        
        if (fpsRoutine != null) { StopCoroutine(fpsRoutine); fpsRoutine = null; }

        if (webcam != null) { webcam.Stop(); webcam = null; }
        
        if (frameBuffer != null)
        {
            foreach (var rt in frameBuffer)
                if (rt != null) rt.Release();
            frameBuffer = null;
        }

        frameTimes = null;
    }

    IEnumerator StartWebcamRoutine(string deviceName)
    {
        // CleanupResources() already stopped old webcam and released old buffers

        // The camera/driver often loses an enumeration race on a cold start (especially
        // with ALVR + SteamVR also spinning up): the stream opens then immediately dies
        // (green LED flashes then goes dark). Rather than give up — which leaves a white
        // screen and forces an app restart — retry a few times, letting the bus settle
        // between attempts. We always request the SAME format (60 FPS) so capture rate is
        // identical for every participant; we never silently fall back to a lower rate.
        const int maxAttempts = 5;
        const float timeoutPerAttempt = 8.0f; // cold-start enumeration can exceed 5s

        bool connected = false;
        for (int attempt = 1; attempt <= maxAttempts && !connected; attempt++)
        {
            SetStatus($"Connecting to camera... (attempt {attempt}/{maxAttempts})");

            webcam = new WebCamTexture(deviceName, requestWidth, requestHeight, requestFPS);
            webcam.Play();

            // Success = resolution populated AND a real frame actually arrived.
            // (Checking width alone can pass for a stream that opened then died.)
            float timeout = timeoutPerAttempt;
            while ((webcam.width < 100 || !webcam.didUpdateThisFrame) && timeout > 0)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (webcam.width >= 100 && webcam.didUpdateThisFrame)
            {
                connected = true;
                break;
            }

            // This attempt failed — tear down and let the driver/USB settle before retrying.
            Debug.LogWarning($"Webcam attempt {attempt} failed (stream didn't start). Retrying...");
            webcam.Stop();
            webcam = null;
            yield return new WaitForSeconds(1.0f);
        }

        if (!connected)
        {
            Debug.LogError($"Failed to start webcam after {maxAttempts} attempts: {deviceName}");
            SetStatus("Camera failed to start. Press Reconnect to try again.");
            yield break;
        }

        // LOG ACTUAL RESOLUTION (Check console!)
        // If this says 640x480, USB port is too slow or the camera is in USB 2.0 mode.
        Debug.Log($"<color=green>Webcam Active: {webcam.width}x{webcam.height} @ {webcam.requestedFPS} FPS (requested)</color>");
        SetStatus($"Connected: {webcam.width}x{webcam.height}");
        
        // Setup Ring Buffer
        actualFPS = webcam.requestedFPS > 0 ? webcam.requestedFPS : 30f;
        int safeFPS = Mathf.Max((int)actualFPS, 60); 
        
        bufferSize = Mathf.CeilToInt(maxDelayCap * safeFPS) + safeFPS;
        frameBuffer = new RenderTexture[bufferSize];
        frameTimes = new float[bufferSize];

        for (int i = 0; i < bufferSize; i++)
        {
            // RGB565 = 2 bytes/pixel (vs 4 for ARGB32). No alpha needed for webcam.
            // Halves VRAM usage per texture. Visually imperceptible for a live hand feed.
            frameBuffer[i] = new RenderTexture(webcam.width, webcam.height, 0, RenderTextureFormat.RGB565);
            frameBuffer[i].filterMode = textureFilterMode;
            frameBuffer[i].Create();
            // Mark every slot as "no real frame yet" with a sentinel timestamp that can never
            // satisfy the delay read (PositiveInfinity is never <= targetTime). The read never
            // selects a slot until a genuinely-captured frame has been written into it, so we
            // don't pre-fill slot contents — uninitialised GPU memory is never displayed. Until
            // enough real history exists to honour the requested delay, the feed shows black
            // rather than a stale/wrong-moment frame.
            frameTimes[i] = float.PositiveInfinity;
        }

        isInitialized = true;

        // requestedFPS above is only what we asked for. Log what the camera actually
        // delivers so we can confirm every participant is captured at the same rate (60).
        fpsRoutine = StartCoroutine(ReportActualFPS());
    }

    // One-shot check at startup: counts how many frames the webcam genuinely refreshed
    // over a short window and logs the real capture FPS once, so you can confirm 60 at
    // connect time. Deliberately does NOT run during the experiment — no periodic logging
    // that could cause a frame hitch and disturb the delay timing.
    IEnumerator ReportActualFPS()
    {
        yield return new WaitForSeconds(1.0f); // let the stream stabilise first

        int frames = 0;
        float elapsed = 0f;
        while (elapsed < 2.0f && isInitialized && webcam != null)
        {
            if (webcam.didUpdateThisFrame) frames++;
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (elapsed > 0f)
            Debug.Log($"Webcam actual capture rate: {(frames / elapsed):F1} FPS (measured at startup)");
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
        // A. Write current frame to buffer, timestamped with when we received it.
        Graphics.Blit(webcam, frameBuffer[writeHead]);
        frameTimes[writeHead] = Time.time;
        if (firstRealFrameTime < 0f) firstRealFrameTime = Time.time; // arms the readiness gate

        // B. Read from Buffer (Delay Logic)
        if (currentDelaySeconds <= 0.02f)
        {
            screenRenderer.material.mainTexture = frameBuffer[writeHead];
        }
        else
        {
            // Time-based selection: show the NEWEST frame that is at least currentDelaySeconds
            // old. Walking back by real timestamps (rather than delay * fps) makes the applied
            // delay correct no matter the camera's actual or varying capture rate.
            float targetTime = Time.time - currentDelaySeconds;
            int readHead = -1;
            for (int step = 0; step < bufferSize; step++)
            {
                int idx = (writeHead - step + bufferSize) % bufferSize;
                if (frameTimes[idx] <= targetTime) { readHead = idx; break; }
            }

            if (readHead >= 0)
            {
                screenRenderer.material.mainTexture = frameBuffer[readHead];
            }
            else
            {
                // No genuinely-captured frame is old enough yet (within the first
                // currentDelaySeconds after the camera starts). Show black rather than a
                // frozen/wrong-moment frame: a plausible-but-stale image would silently
                // contaminate a delay-perception measurement, whereas black is unambiguous.
                screenRenderer.material.mainTexture = Texture2D.blackTexture;
            }
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