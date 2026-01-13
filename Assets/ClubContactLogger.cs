// ClubBallContactLogger.cs
using UnityEngine;

public class ClubBallContactLogger : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private ClubHeadVelocity vel;
    [SerializeField] private Transform faceFrame;

    [Header("Ball")]
    [SerializeField] private LayerMask ballMask;

    [Header("Log Control")]
    [SerializeField] private float minSpeedToHit = 1.0f;
    [SerializeField] private float cooldown = 0.25f;

    [Header("Power")]
    [SerializeField] private float speedForFullPower = 14f;
    [SerializeField] private AnimationCurve powerCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Curve (legacy tuning kept but not used for intent)")]
    [SerializeField] private float fpForFullCurve = 12f; // kept so you can revert easily

    [Header("TEMP Curve Intent (debug)")]
    [SerializeField] private float debugCurveStrength = 0.35f; // hold 4/5 to apply this (scaled by speed)

    [Header("Launch")]
    [SerializeField] private float minLaunchDeg = 1f;
    [SerializeField] private float maxLaunchDeg = 65f;

    private float nextAllowedTime;

    [SerializeField] private GolferContextLink link;

    private GolfClub club;
    private ClubData data;

    // live intent (-debugCurveStrength..+debugCurveStrength)
    public float curveIntent01;

    private void Awake()
    {
        if (!faceFrame) faceFrame = transform;
        if (!vel) vel = GetComponentInParent<ClubHeadVelocity>();

        club = GetComponentInParent<GolfClub>();
        if (club) data = club.data;
    }

    // TEMP: hold 4 for left, 5 for right
    private void Update()
    {
        curveIntent01 = 0f;

        if (Input.GetKey(KeyCode.Alpha4))
            curveIntent01 = -debugCurveStrength;

        if (Input.GetKey(KeyCode.Alpha5))
            curveIntent01 = debugCurveStrength;
    }

    public void BindContext(GolferContextLink context) => link = context;

    private void OnTriggerEnter(Collider other)
    {
        // Owner-only (prevents double hits)
        if (link == null || link.golfer == null || !link.golfer.IsOwner)
            return;

        if (Time.time < nextAllowedTime)
            return;

        if (((1 << other.gameObject.layer) & ballMask.value) == 0)
            return;

        if (vel == null)
            return;

        // Use RAW speed for power (captures peak better)
        float speed = vel.RawSpeed;
        if (speed < minSpeedToHit)
            return;

        // Use smoothed velocity for direction stability
        Vector3 v = vel.VelocityWorld;

        // --- PATH from velocity on XZ ---
        Vector3 vFlat = new Vector3(v.x, 0f, v.z);
        float flatSpeed = vFlat.magnitude;
        if (flatSpeed < 0.001f)
            return;

        Vector3 pathDir = vFlat / flatSpeed;

        // Attack angle (up/down) from velocity
        float attackDeg = Mathf.Atan2(v.y, flatSpeed) * Mathf.Rad2Deg;

        // --- FACE (still computed, but NOT used for curve intent anymore; kept for future) ---
        Vector3 faceN = faceFrame.TransformDirection(Vector3.left); // -X = face normal
        Vector3 faceFlat = new Vector3(faceN.x, 0f, faceN.z);
        float faceFlatMag = faceFlat.magnitude;
        Vector3 faceDir = (faceFlatMag > 0.001f) ? (faceFlat / faceFlatMag) : pathDir;

        // (legacy values if you want to log them)
        float pathYaw = Mathf.Atan2(pathDir.x, pathDir.z) * Mathf.Rad2Deg;
        float faceYaw = Mathf.Atan2(faceDir.x, faceDir.z) * Mathf.Rad2Deg;
        float faceMinusPath = Mathf.DeltaAngle(pathYaw, faceYaw);

        // --- CURVE FROM INTENT (scaled by speed) ---
        float speed01ForCurve = Mathf.Clamp01(speed / Mathf.Max(0.001f, speedForFullPower));
        float curve01 = curveIntent01 * speed01ForCurve;

        // Optional safety clamp while testing:
        // curve01 = Mathf.Clamp(curve01, -0.6f, 0.6f);

        // Loft
        float loftDeg = data ? data.loftDeg : 0f;

        // Power from speed curve
        float norm = Mathf.Clamp01(speed / Mathf.Max(0.001f, speedForFullPower));
        float power01 = Mathf.Clamp01(powerCurve.Evaluate(norm));

        // Launch angle
        float launchDeg = Mathf.Clamp(loftDeg + attackDeg, minLaunchDeg, maxLaunchDeg);

        // Build a launch direction that always goes upward-ish
        Vector3 rightAxis = Vector3.Cross(pathDir, Vector3.up);
        if (rightAxis.sqrMagnitude < 0.0001f) rightAxis = transform.right;
        rightAxis.Normalize();

        Vector3 launchDir = Quaternion.AngleAxis(launchDeg, rightAxis) * pathDir;

        // If we accidentally aimed downward, flip the axis
        if (launchDir.y < 0.05f)
            launchDir = Quaternion.AngleAxis(launchDeg, -rightAxis) * pathDir;

        if (launchDir.y < 0.05f) launchDir.y = 0.05f;
        launchDir.Normalize();

        // Get ball NetworkObjectId
        var ballNO = other.GetComponentInParent<Unity.Netcode.NetworkObject>();
        if (!ballNO)
        {
            nextAllowedTime = Time.time + cooldown;
            return;
        }

        // Request server hit (curve01 now comes from intent)
        link.golfer.RequestBallHitFromClub(ballNO.NetworkObjectId, launchDir, power01, curve01);

        nextAllowedTime = Time.time + cooldown;

        // Optional debug:
        // Debug.Log($"[HIT] speed={speed:F1} power01={power01:F2} curveIntent={curveIntent01:F2} curve01={curve01:F2} f-p={faceMinusPath:F1}");
    }
}