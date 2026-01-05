using UnityEngine;

public class WebcamDiagnostics : MonoBehaviour
{
    public WebCamTexture webcam; // Drag your WebCamTexture here in Inspector if possible, or we find it
    private float deltaTime = 0.0f;

    void Update()
    {
        // 1. Calculate FPS
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        float fps = 1.0f / deltaTime;

        // 2. Check actual webcam update rate
        // (Note: This is rough; Unity doesn't give a "Camera FPS" variable, 
        // so we infer it from how often the texture actually updates).
        string camStats = "Waiting for camera...";
        if (webcam != null)
        {
            camStats = $"Req: {webcam.requestedWidth}x{webcam.requestedHeight}@{webcam.requestedFPS}\n" +
                       $"Actual Size: {webcam.width}x{webcam.height}\n" +
                       $"Did Update: {webcam.didUpdateThisFrame}";
        }
    }

    void OnGUI()
    {
        int w = Screen.width, h = Screen.height;
        GUIStyle style = new GUIStyle();
        style.alignment = TextAnchor.UpperLeft;
        style.fontSize = h * 4 / 100;
        style.normal.textColor = Color.red;

        // Show Unity Game FPS
        float msec = deltaTime * 1000.0f;
        float fps = 1.0f / deltaTime;
        string text = string.Format("{0:0.0} ms ({1:0.} FPS)", msec, fps);
        
        // Show Webcam Stats if available
        if (webcam != null)
        {
            // We can't query "webcam.currentFPS" directly, but we can see resolution
            text += "\n" + $"Cam: {webcam.width}x{webcam.height}";
        }

        GUI.Label(new Rect(0, 0, w, h * 2 / 100), text, style);
    }
}