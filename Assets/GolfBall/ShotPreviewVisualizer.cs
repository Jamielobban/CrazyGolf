using UnityEngine;

/// <summary>
/// Local-only shot preview:
/// - Starts at the BALL position.
/// - Uses a STABLE aim transform for direction (assign swingPlane/swingPivot/your fake aim).
/// - Reads ClubData from GolferContextLink.
/// - Reads curve intent directly from ClubBallContactLogger on the current club head.
/// - Simulates only short flight (loft + curve shape). No landing/roll.
///
/// Not networked. Do NOT put this on a NetworkObject.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class ShotPreviewVisualizerShort : MonoBehaviour
{
    [Header("Refs (same object)")]
    [SerializeField] private GolferContextLink link;

    [Header("Aim (stable)")]
    [Tooltip("Use something stable like swingPlane forward, swingPivot forward, or a 'fake aim' transform you control.")]
    [SerializeField] private Transform aimTransform;

    [Header("Origin (ball)")]
    [SerializeField] private float originLift = 0.03f;
    [SerializeField] private float originForwardNudge = 0.02f;

    [Header("Preview length")]
    [SerializeField] private float previewTime = 1.0f; // seconds simulated
    [SerializeField] private int points = 26;
    [SerializeField] private float gravity = 9.81f;

    [Header("Assumed strike")]
    [Range(0f, 1f)]
    [SerializeField] private float previewPower01 = 1f; // 1 = maxImpulse
    [SerializeField] private float loftBiasDeg = 0f;

    [Header("Curve preview (match your ball feel)")]
    [SerializeField] private bool showCurve = true;
    [SerializeField] private float curveAccel = 18f;
    [SerializeField] private float minCurveFlatSpeed = 2f;
    [SerializeField] private float curveRefFlatSpeed = 18f;
    [SerializeField] private float maxCurveSpeedScale = 2f;
    [SerializeField] private float curveVisualScale = 1.0f;

    [Header("Show rules")]
    [SerializeField] private bool hideWhenBallHeld = true;
    [SerializeField] private bool hideWhenBallMoving = true;
    [SerializeField] private float localMovingSpeed = 0.12f;

    [Header("Controls")]
    [SerializeField] private KeyCode toggleKey = KeyCode.T;
    [SerializeField] private bool enabledByDefault = true;

    private LineRenderer lr;
    private bool isOn;

    // cached per-frame
    private ClubBallContactLogger loggerCached;

    private void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.positionCount = 0;

        if (!link) link = GetComponent<GolferContextLink>();

        isOn = enabledByDefault;
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            isOn = !isOn;

        if (!isOn)
        {
            SetVisible(false);
            return;
        }

        if (link == null || link.golfer == null)
        {
            SetVisible(false);
            return;
        }

        // Only local owner should render their preview
        if (!link.golfer.IsOwner)
        {
            SetVisible(false);
            return;
        }

        // Need ball state (your golfer.MyBall is NetworkGolfBallState)
        var ballState = link.golfer.MyBall;
        if (!ballState)
        {
            SetVisible(false);
            return;
        }

        if (hideWhenBallHeld && ballState.State.Value == NetworkGolfBallState.BallState.Held)
        {
            SetVisible(false);
            return;
        }

        if (hideWhenBallMoving)
        {
            var rbBall = ballState.GetComponent<Rigidbody>();
            if (rbBall)
            {
#if UNITY_6000_0_OR_NEWER
                Vector3 v = rbBall.linearVelocity;
#else
                Vector3 v = rbBall.velocity;
#endif
                v.y = 0f;

                if (v.magnitude > localMovingSpeed)
                {
                    SetVisible(false);
                    return;
                }
            }
        }

        // Club data
        ClubData cd = link.Data;
        if (cd == null)
        {
            SetVisible(false);
            return;
        }

        // Aim direction must be stable
        if (!aimTransform)
        {
            SetVisible(false);
            return;
        }

        // NOTE: Your original had rawAim = -aimTransform.right.
        // Keep it as-is, but if your aim looks backwards, swap to aimTransform.forward.
        Vector3 rawAim = -aimTransform.right;

        Vector3 pathDir = new Vector3(rawAim.x, 0f, rawAim.z);
        if (pathDir.sqrMagnitude < 0.0001f)
        {
            SetVisible(false);
            return;
        }
        pathDir.Normalize();

        // Origin on the ball
        Vector3 origin = ballState.transform.position + Vector3.up * originLift;
        origin += pathDir * originForwardNudge;

        // Launch direction from loft
        float loft = cd.loftDeg + loftBiasDeg;
        Vector3 launchDir = ApplyLoft(pathDir, loft).normalized;

        // Convert impulse -> initial velocity (dv = impulse / mass)
        var rb = ballState.GetComponent<Rigidbody>();
        if (!rb)
        {
            SetVisible(false);
            return;
        }

        float impulse = Mathf.Lerp(cd.minImpulse, cd.maxImpulse, Mathf.Clamp01(previewPower01));
        float mass = Mathf.Max(0.0001f, rb.mass);

        Vector3 v0 = launchDir * (impulse / mass);

        // Curve intent from logger on club head
        float curve01 = 0f;
        if (showCurve)
        {
            curve01 = ReadCurveIntent01() * curveVisualScale;
            curve01 = Mathf.Clamp(curve01, -1f, 1f);
        }

        // Draw
        PredictAndDraw(origin, v0, curve01);
        SetVisible(true);
    }

    private float ReadCurveIntent01()
    {
        // Find logger on the current club head (assigned by your binder)
        Transform head = link.ClubHead;
        if (!head) return 0f;

        // Cache if still valid
        if (loggerCached == null || loggerCached.transform != head)
            loggerCached = head.GetComponentInChildren<ClubBallContactLogger>(true);

        if (!loggerCached) return 0f;

        // Your logger needs a public getter like:
        // public float CurveIntent01 => curveIntent01;
        return loggerCached.CurveIntent01;
    }

    private void PredictAndDraw(Vector3 p0, Vector3 v0, float curve01)
    {
        int n = Mathf.Clamp(points, 6, 128);
        lr.positionCount = n;

        float dt = Mathf.Max(0.001f, previewTime / (n - 1));

        Vector3 p = p0;
        Vector3 v = v0;

        for (int i = 0; i < n; i++)
        {
            lr.SetPosition(i, p);

            // gravity
            v += Vector3.down * gravity * dt;

            // curve (air-only)
            if (showCurve && Mathf.Abs(curve01) > 0.0001f)
                ApplyCurveAcceleration(ref v, curve01, dt);

            p += v * dt;
        }
    }

    private void ApplyCurveAcceleration(ref Vector3 v, float curve01, float dt)
    {
        Vector3 vFlat = new Vector3(v.x, 0f, v.z);
        float flatSpd = vFlat.magnitude;
        if (flatSpd < minCurveFlatSpeed) return;

        Vector3 forwardFlat = vFlat / Mathf.Max(0.0001f, flatSpd);
        Vector3 side = Vector3.Cross(Vector3.up, forwardFlat);
        side.y = 0f;

        if (side.sqrMagnitude < 0.0001f) return;

        side.Normalize();
        side *= Mathf.Sign(curve01); // left/right

        float refSpd = Mathf.Max(0.001f, curveRefFlatSpeed);
        float speedScale = (flatSpd * flatSpd) / (refSpd * refSpd);
        speedScale = Mathf.Clamp(speedScale, 0f, maxCurveSpeedScale);

        Vector3 a = side * (curveAccel * speedScale * Mathf.Abs(curve01));
        v += a * dt;
    }

    private static Vector3 ApplyLoft(Vector3 flatDir, float loftDeg)
    {
        // Rotate around the "right" axis for that flat direction
        Vector3 right = Vector3.Cross(flatDir, Vector3.up);
        if (right.sqrMagnitude < 0.0001f) right = Vector3.right;

        right.Normalize();
        return Quaternion.AngleAxis(loftDeg, right) * flatDir;
    }

    private void SetVisible(bool v)
    {
        if (lr.enabled != v) lr.enabled = v;
        if (!v) lr.positionCount = 0;
    }
}
