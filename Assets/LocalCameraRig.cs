// LocalCameraRig.cs
// LOCAL-ONLY prefab (NOT a NetworkObject).
// Uses PRIORITY switching (NO SetActive), so PanTilt won't reset/recenter on enable.
// Prefab contains:
// - MainCamera (Camera + AudioListener + CinemachineBrain)
// - WalkCam (CinemachineCamera) with PanTilt + InputAxisController (optional)
// - SwingCam (CinemachineCamera) with RotationComposer etc.

using UnityEngine;
using Unity.Cinemachine;

public class LocalCameraRig : MonoBehaviour
{
    [Header("Cinemachine Cameras")]
    public CinemachineCamera walkCam;
    public CinemachineCamera swingCam;

    [Header("Priority (only one is “top” at a time)")]
    public int walkPriority = 20;
    public int swingPriority = 30;

    [Header("Optional input controller (usually on WalkCam)")]
    public CinemachineInputAxisController inputAxis;

    public Camera UnityCamera { get; private set; }
    public Transform ViewTransform => UnityCamera != null ? UnityCamera.transform : null;

    private void Awake()
    {
        UnityCamera = GetComponentInChildren<Camera>(true);
        if (UnityCamera == null)
            Debug.LogWarning("[LocalCameraRig] No Unity Camera found in children.");
    }

    private void OnEnable()
    {
        // Default to walk on spawn so there is never ambiguity.
        SetModeSwing(false);
    }

    public void SetModeSwing(bool swing)
    {
        // Keep BOTH cams enabled; only priorities decide which is live.
        if (walkCam != null) walkCam.Priority = swing ? 0 : walkPriority;
        if (swingCam != null) swingCam.Priority = swing ? swingPriority : 0;

        // Common pattern: only allow free-look input while walking
        if (inputAxis != null) inputAxis.enabled = !swing;
    }

    public void BindWalk(Transform follow, Transform lookAt)
    {
        if (walkCam == null) return;
        SetTargets(walkCam, follow, lookAt);
    }

    public void BindSwing(Transform follow, Transform lookAt)
    {
        if (swingCam == null) return;
        SetTargets(swingCam, follow, lookAt);
    }

    private static void SetTargets(CinemachineCamera cam, Transform tracking, Transform lookAt)
    {
        if (cam == null) return;
        var t = cam.Target;           // struct copy
        t.TrackingTarget = tracking;
        t.LookAtTarget = lookAt;
        cam.Target = t;               // write back
    }
}
