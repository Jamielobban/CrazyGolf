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

    [Header("Swing Peek (Rotation Composer Target Offset X)")]
    [SerializeField] private float peekSpeed = 1.2f;     // units/sec
    [SerializeField] private float peekMaxAbs = 0.8f;    // clamp around base
    [SerializeField] private float peekRecenterSpeed = 2.0f; // 0 = don't recenter
    [SerializeField] private AnimationCurve peekSpeedByHold01 =
    AnimationCurve.EaseInOut(0f, 0.15f, 1f, 1f);

    [SerializeField] private float peekRampTime = 0.35f; // seconds to reach full
    private float peekHold01;

    public Camera UnityCamera { get; private set; }
    public Transform ViewTransform => UnityCamera != null ? UnityCamera.transform : null;

    private CinemachineRotationComposer swingComposer;
    private float swingBaseOffsetX;
    private bool swingMode;

    private void Awake()
    {
        UnityCamera = GetComponentInChildren<Camera>(true);
        if (UnityCamera == null)
            Debug.LogWarning("[LocalCameraRig] No Unity Camera found in children.");

        CacheSwingComposer();
    }

    private void OnEnable()
    {
        SetModeSwing(false);
    }

    private void CacheSwingComposer()
    {
        swingComposer = null;
        if (swingCam == null) return;

        swingComposer = swingCam.GetComponent<CinemachineRotationComposer>();
        if (swingComposer != null)
            swingBaseOffsetX = swingComposer.TargetOffset.x;
    }

    public void SetModeSwing(bool swing)
    {
        swingMode = swing;

        if (walkCam != null) walkCam.Priority = swing ? 0 : walkPriority;
        if (swingCam != null) swingCam.Priority = swing ? swingPriority : 0;

        if (inputAxis != null) inputAxis.enabled = !swing;

        // If swing cam got assigned late, grab composer now
        if (swing && swingComposer == null)
            CacheSwingComposer();
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

        // Composer might exist but wasn't cached yet
        if (swingComposer == null)
            CacheSwingComposer();
    }

    // Call this every frame while in swing mode (or let another script call it)
    public void UpdateSwingPeek(float input01, float dt)
    {
        if (!swingMode) return;
        if (swingComposer == null) return;
        if (dt <= 0f) return;

        // build a 0..1 hold amount that ramps up while input is held, ramps down when released
        if (Mathf.Abs(input01) > 0.001f)
            peekHold01 = Mathf.MoveTowards(peekHold01, 1f, dt / Mathf.Max(0.001f, peekRampTime));
        else
            peekHold01 = Mathf.MoveTowards(peekHold01, 0f, dt / Mathf.Max(0.001f, peekRampTime));

        float speedMul = peekSpeedByHold01 != null ? peekSpeedByHold01.Evaluate(peekHold01) : 1f;

        Vector3 off = swingComposer.TargetOffset;

        if (Mathf.Abs(input01) > 0.001f)
        {
            off.x += input01 * (peekSpeed * speedMul) * dt;
            off.x = Mathf.Clamp(off.x, swingBaseOffsetX - peekMaxAbs, swingBaseOffsetX + peekMaxAbs);
        }
        else if (peekRecenterSpeed > 0f)
        {
            // optional: also shape recenter if you want (keep linear for now)
            off.x = Mathf.MoveTowards(off.x, swingBaseOffsetX, peekRecenterSpeed * dt);
        }

        swingComposer.TargetOffset = off;
    }


    private static void SetTargets(CinemachineCamera cam, Transform tracking, Transform lookAt)
    {
        if (cam == null) return;
        var t = cam.Target;
        t.TrackingTarget = tracking;
        t.LookAtTarget = lookAt;
        cam.Target = t;
    }
}
