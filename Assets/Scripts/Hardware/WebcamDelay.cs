using UnityEngine;
using System.Collections;

public class WebcamDelay : MonoBehaviour
{
    [Header("Configuration")]
    public int requestWidth = 1280; 
    public int requestHeight = 720;
    public int requestFPS = 30;

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

    [Tooltip("Store frames as RGB565 (half the VRAM per frame) instead of ARGB32. " +
             "Render-target support for RGB565 is an OPTIONAL driver capability, not a " +
             "guarantee — it can vary between GPUs and between driver versions on the same " +
             "GPU. Leave this off unless you have confirmed it works on this exact GPU and " +
             "driver AND you need the memory back.")]
    public bool useCompactFormat = false;

    // Private internals
    private WebCamTexture webcam;
    private Renderer screenRenderer;
    private Material screenMaterial;   // Our instanced copy of the quad's material — see ScreenMaterial
    private string currentDeviceName;  // Device the live stream belongs to. Lets us ignore a
                                       // redundant re-Initialize for the camera already running.
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

    // Renderer.material instantiates a private copy of the shared material on FIRST access and
    // caches it on the renderer; that copy belongs to us and must be destroyed by us. Cached here
    // so cleanup can clear the texture reference without a bare `.material` access accidentally
    // instantiating a fresh copy during teardown.
    private Material ScreenMaterial
    {
        get
        {
            if (screenMaterial == null && screenRenderer != null)
                screenMaterial = screenRenderer.material;
            return screenMaterial;
        }
    }

    // Call this from your ExperimentManager
    public void Initialize(string selectedDeviceName)
    {
        // Re-opening the camera that is already streaming buys nothing and costs plenty: it
        // closes and re-opens the USB capture device (the enumeration race this class retries
        // around) and rebuilds the entire delay buffer. The dashboard drives this from a dropdown
        // callback that fires on every value change, so the redundant case is the common one.
        if (isInitialized && selectedDeviceName == currentDeviceName)
        {
            SetStatus($"Already connected: {webcam.width}x{webcam.height}");
            return;
        }

        screenRenderer = GetComponent<Renderer>();
        currentDeviceName = selectedDeviceName;

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
        bufferSize = 0;
        firstRealFrameTime = -1f;

        if (fpsRoutine != null) { StopCoroutine(fpsRoutine); fpsRoutine = null; }

        if (webcam != null)
        {
            webcam.Stop();
            Destroy(webcam);
            webcam = null;
        }

        // Drop the quad's reference to the buffer BEFORE releasing it. A material still pointing
        // at a released RenderTexture counts as "using" it, and Unity re-creates a released
        // target automatically on next use — so the surface comes straight back, except now we've
        // dropped frameBuffer and nothing can ever free it again. One stranded full-resolution
        // surface per reconnect, and it does not show up as a leaked object anywhere.
        // Deliberately the cached field, not the ScreenMaterial property: if we never displayed a
        // frame there is no instanced material yet, and going through the property would create
        // one purely to blank it.
        if (screenMaterial != null) screenMaterial.mainTexture = null;

        if (frameBuffer != null)
        {
            foreach (var rt in frameBuffer)
            {
                if (rt == null) continue;
                rt.Release(); // frees the GPU surface now
                Destroy(rt);  // frees the object — Release() alone leaves it alive and re-creatable
            }
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
        const int maxAttempts = 2;
        const float timeoutPerAttempt = 8.0f; // cold-start enumeration can exceed 5s

        bool connected = false;
        for (int attempt = 1; attempt <= maxAttempts && !connected; attempt++)
        {
            SetStatus($"Connecting to camera... (attempt {attempt}/{maxAttempts})");

            yield return null;

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
            Destroy(webcam); // Stop() alone orphans the capture handle — see CleanupResources
            webcam = null;
            yield return new WaitForSeconds(1.0f); // spans frames, so the Destroy above completes
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
        int safeFPS = Mathf.Max(Mathf.CeilToInt(actualFPS * 1.5f), 30);
        
        bufferSize = Mathf.CeilToInt(maxDelayCap * safeFPS) + Mathf.CeilToInt(0.25f * safeFPS);
        frameBuffer = new RenderTexture[bufferSize];
        frameTimes = new float[bufferSize];

        RenderTextureFormat format = RenderTextureFormat.ARGB32;
        if (useCompactFormat)
        {
            if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB565))
                format = RenderTextureFormat.RGB565;
            else
                Debug.LogWarning("RGB565 render targets are not supported by this GPU/driver — falling back to ARGB32.");
        }

        float mbPerSlot = (webcam.width * webcam.height * (format == RenderTextureFormat.RGB565 ? 2 : 4)) / 1048576f;
        Debug.Log($"Allocating delay buffer: {bufferSize} x {webcam.width}x{webcam.height} {format} " +
                  $"= {(bufferSize * mbPerSlot):F0} MB VRAM");

        for (int i = 0; i < bufferSize; i++)
        {
            frameBuffer[i] = new RenderTexture(webcam.width, webcam.height, 0, format);
            frameBuffer[i].filterMode = textureFilterMode;

            // Create() returns false when the driver refuses the allocation — out of VRAM, or
            // a format it won't render to. Discarding that result leaves an invalid target
            // that we then blit into every frame for the rest of the session: a silently
            // broken stimulus, with no error anywhere. Fail loudly and stop instead.
            if (!frameBuffer[i].Create())
            {
                Debug.LogError($"Failed to allocate delay buffer slot {i + 1}/{bufferSize} ({format}). " +
                               "Lower maxDelayCap, or reduce the capture resolution.");
                SetStatus($"GPU memory exhausted at {i + 1}/{bufferSize} frames. Lower the delay cap.");
                CleanupResources();
                yield break;
            }
            // Mark every slot as "no real frame yet" with a sentinel timestamp that can never
            // satisfy the delay read (PositiveInfinity is never <= targetTime). The read never
            // selects a slot until a genuinely-captured frame has been written into it, so we
            // don't pre-fill slot contents — uninitialised GPU memory is never displayed. Until
            // enough real history exists to honour the requested delay, the feed shows black
            // rather than a stale/wrong-moment frame.
            frameTimes[i] = float.PositiveInfinity;

            // Yield periodically. Allocating the whole buffer in one frame is a long
            // synchronous burst of driver work, and Windows resets a display driver that stops
            // responding for ~2s (a TDR) — which hangs the app in a way Task Manager cannot
            // kill. Spreading the cost keeps every frame well inside that budget, and turns an
            // apparent freeze into visible progress that names the slot it stopped on.
            if ((i + 1) % 16 == 0)
            {
                SetStatus($"Preparing delay buffer... {i + 1}/{bufferSize}");
                yield return null;
            }
        }

        SetStatus($"Ready: {webcam.width}x{webcam.height}");
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

        if (elapsed <= 0f) yield break;

        float measured = frames / elapsed;
        Debug.Log($"Webcam actual capture rate: {measured:F1} FPS (measured at startup)");
        float secondsCovered = measured > 0f ? bufferSize / measured : float.PositiveInfinity;
        if (secondsCovered < maxDelayCap)
        {
            Debug.LogError($"Delay buffer spans only {secondsCovered:F2}s at the measured {measured:F1} FPS, " +
                           $"but maxDelayCap is {maxDelayCap:F2}s. Delays longer than {secondsCovered:F2}s " +
                           "will display black. Raise requestFPS and reconnect the camera.");
            SetStatus($"WARNING: buffer covers {secondsCovered:F2}s, need {maxDelayCap:F2}s.");
        }
    }

    void Update()
    {
        if (!isInitialized || !webcam.didUpdateThisFrame) return;

        // --- UPDATE VIEW SIZE REALTIME ---
        // We calculate this every frame so you can adjust the slider in the Inspector
        if (webcam.height > 0)
        {
            float aspect = (float)webcam.width / webcam.height;
            Vector3 targetScale = new Vector3(viewSize * aspect, viewSize, 1f);

            // This quad also carries a MeshCollider (for the XR ray interactors). Every write to
            // localScale dirties the transform and forces PhysX to re-derive the scaled collision
            // shape, whether or not the value actually changed. viewSize only moves when someone
            // drags the inspector slider, so write only on a real change.
            if (transform.localScale != targetScale) transform.localScale = targetScale;
        }

        // --- BUFFER LOGIC ---
        // A. Write current frame to buffer, timestamped with when we received it.
        Graphics.Blit(webcam, frameBuffer[writeHead]);
        frameTimes[writeHead] = Time.time;
        if (firstRealFrameTime < 0f) firstRealFrameTime = Time.time; // arms the readiness gate

        // B. Read from Buffer (Delay Logic)
        if (currentDelaySeconds <= 0.02f)
        {
            ScreenMaterial.mainTexture = frameBuffer[writeHead];
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
                ScreenMaterial.mainTexture = frameBuffer[readHead];
            }
            else
            {
                // No genuinely-captured frame is old enough yet (within the first
                // currentDelaySeconds after the camera starts). Show black rather than a
                // frozen/wrong-moment frame: a plausible-but-stale image would silently
                // contaminate a delay-perception measurement, whereas black is unambiguous.
                ScreenMaterial.mainTexture = Texture2D.blackTexture;
            }
        }

        // C. Advance Write Head
        writeHead = (writeHead + 1) % bufferSize;
    }

    void OnDestroy()
    {
        CleanupResources();

        // Renderer.material handed us a private clone of the shared material. Unity does not
        // reclaim it with the renderer — the docs put it on us — so it outlives the scene unless
        // we destroy it here. One material is small, but it also keeps its texture references
        // alive, which is how a "small" leak ends up pinning a full-resolution surface.
        if (screenMaterial != null)
        {
            Destroy(screenMaterial);
            screenMaterial = null;
        }
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