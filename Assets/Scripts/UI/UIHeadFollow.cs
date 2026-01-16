using UnityEngine;

public class UIHeadFollow : MonoBehaviour
{
    [Header("Settings")]
    public float distance = 1.5f; // Distance from eyes
    public float smoothTime = 0.3f; // How long it takes to catch up
    public float heightOffset = -0.1f; // Slight drop so it's not blocking eyes directly

    private Transform cameraTransform;
    private Vector3 currentVelocity;

    void Start()
    {
        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
        else
        {
            Debug.LogError("Main Camera not found! Ensure your VR Camera is tagged 'MainCamera'.");
        }
    }

    void OnEnable()
    {
        // Snap immediately to position when turned on so it doesn't "fly in"
        if (cameraTransform != null)
        {
            UpdatePosition(true);
        }
    }

    void LateUpdate()
    {
        UpdatePosition(false);
    }

    void UpdatePosition(bool snap)
    {
        if (cameraTransform == null) return;

        // Calculate target position
        // We take the camera's forward direction, but flatten it on the Y axis 
        // so the UI doesn't clip into the floor if looking down.
        Vector3 forwardFlat = cameraTransform.forward;
        forwardFlat.y = 0;
        forwardFlat.Normalize();

        Vector3 targetPosition = cameraTransform.position + (forwardFlat * distance);
        targetPosition.y = cameraTransform.position.y + heightOffset;

        // Move
        if (snap)
        {
            transform.position = targetPosition;
        }
        else
        {
            transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref currentVelocity, smoothTime);
        }

        // Rotate to face the camera
        // LookAt makes the Z-axis point at target, usually UI needs to be rotated 180 or not.
        // Canvas World Space usually faces +Z. We want it to look at the camera.
        transform.LookAt(transform.position + cameraTransform.rotation * Vector3.forward, cameraTransform.rotation * Vector3.up);
    }
}