using System.Net.Sockets;
using UnityEditor.Rendering.LookDev;
using UnityEngine;

public class ClubBallContactLogger : MonoBehaviour
{
    [Header("Refs (optional; otherwise resolved via GolferContextLink / parents)")]
    [SerializeField] private ClubHeadVelocity vel;

    [Tooltip("Transform that defines the club face orientation. Clubface normal is local -X.")]
    [SerializeField] private Transform faceFrame;

    [Header("Ball")]
    [SerializeField] private LayerMask ballMask;

    [Header("Log Control")]
    [SerializeField] private float minSpeedToLog = 1.0f;
    [SerializeField] private float cooldown = 0.25f;

    [Header("Power")]
    [Tooltip("Speed (m/s) that maps to normalized=1.0 before the curve.")]
    [SerializeField] private float speedForFullPower = 14f;

    [Tooltip("Maps normalized speed (0..1) -> power01 (0..1). Make the start flatter to nerf tiny swings.")]
    [SerializeField] private AnimationCurve powerCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Curvature (debug only)")]
    [Tooltip("How many degrees of side curve you pretend per degree of (face-path).")]
    [SerializeField] private float curvePerDeg = 1.0f;
    [SerializeField] private float maxCurveDeg = 30f;

    [SerializeField] private float fpForFullCurve = 12f;

    [Header("Launch")]
    [Tooltip("launchDeg ≈ loft + attack. Clamped.")]
    [SerializeField] private float minLaunchDeg = 1f;
    [SerializeField] private float maxLaunchDeg = 65f;

    [Header("Debug draw")]
    [SerializeField] private bool drawRays = true;
    [SerializeField] private float raySeconds = 1.0f;
    [SerializeField] private float rayLength = 1.0f;

    float nextAllowedTime;

    [SerializeField] private GolferContextLink link;
    ClubFaceRollDriver face;

    GolfClub club;
    ClubData data;

    void Start()
    {
        if (!faceFrame) faceFrame = transform;
        if (!vel) vel = GetComponentInParent<ClubHeadVelocity>();

        club = GetComponentInParent<GolfClub>();
        if (club) data = club.data;
    }

    void OnTriggerEnter(Collider other)
    {
        if (link == null || link.golfer == null || !link.golfer.IsOwner)
            return;

        if (Time.time < nextAllowedTime) return;

        if (((1 << other.gameObject.layer) & ballMask.value) == 0)
            return;

        if (!vel) return;

        float speed = vel.Speed;
        if (speed < minSpeedToLog) return;

        Vector3 v = vel.VelocityWorld;

        // PATH from velocity (XZ)
        Vector3 vFlat = new Vector3(v.x, 0f, v.z);
        float flatSpeed = vFlat.magnitude;
        if (flatSpeed < 0.001f) return;

        Vector3 pathDir = vFlat / flatSpeed;
        float pathYaw = Mathf.Atan2(pathDir.x, pathDir.z) * Mathf.Rad2Deg;

        // Attack angle from velocity
        float attackDeg = Mathf.Atan2(v.y, flatSpeed) * Mathf.Rad2Deg;

        // FACE from real face normal (local -X), flattened on XZ
        Vector3 faceN = faceFrame.TransformDirection(Vector3.left); // -X
        Vector3 faceFlat = new Vector3(faceN.x, 0f, faceN.z);
        float faceFlatMag = faceFlat.magnitude;
        Vector3 faceDir = (faceFlatMag > 0.001f) ? (faceFlat / faceFlatMag) : pathDir;

        float faceYaw = Mathf.Atan2(faceDir.x, faceDir.z) * Mathf.Rad2Deg;
        float faceMinusPath = Mathf.DeltaAngle(pathYaw, faceYaw);

        float curve01 = Mathf.Clamp(faceMinusPath / fpForFullCurve, -1f, 1f);

        // Club data
        float loftDeg = data ? data.loftDeg : 0f;

        // Power (curve)
        float norm = Mathf.Clamp01(speed / Mathf.Max(0.001f, speedForFullPower));
        float power01 = Mathf.Clamp01(powerCurve.Evaluate(norm));

        // Launch angle
        float launchDeg = Mathf.Clamp(loftDeg + attackDeg, minLaunchDeg, maxLaunchDeg);

        // ---- BUILD A 3D LAUNCH DIRECTION THAT ALWAYS GOES UP ----
        Vector3 rightAxis = Vector3.Cross(pathDir, Vector3.up);
        if (rightAxis.sqrMagnitude < 0.0001f) rightAxis = transform.right;
        rightAxis.Normalize();

        Vector3 launchDir = Quaternion.AngleAxis(launchDeg, rightAxis) * pathDir;

        if (launchDir.y < 0f)
            launchDir = Quaternion.AngleAxis(launchDeg, -rightAxis) * pathDir;

        if (launchDir.y < 0.05f) launchDir.y = 0.05f;

        launchDir.Normalize();

        // Debug-only curvature preview
        float curveDeg = Mathf.Clamp(faceMinusPath * curvePerDeg, -maxCurveDeg, maxCurveDeg);

        float faceRoll = face ? face.CurrentFaceRoll : 0f;

        if (drawRays)
        {
            Debug.DrawRay(transform.position, pathDir * rayLength, Color.yellow, raySeconds); // path
            Debug.DrawRay(transform.position, faceDir * rayLength, Color.cyan, raySeconds);  // face
        }

        string clubName = (data && !string.IsNullOrEmpty(data.clubName)) ? data.clubName : "Club";
        string shape =
            Mathf.Abs(faceMinusPath) < 1.5f ? "straight" :
            (faceMinusPath < 0f ? "draw" : "fade");

        Debug.Log(
            $"[SWING] {clubName} " +
            $"spd={speed:F1}m/s norm={norm:F2} pow={power01:F2} atk={attackDeg:F1}° loft={loftDeg:F0}° launch≈{launchDeg:F1}° " +
            $"fp={faceMinusPath:F1}° ({shape}) curve≈{curveDeg:F1}° " +
            $"path={pathYaw:F1}° face={faceYaw:F1}° roll={faceRoll:F1}° " +
            $"launchDir={launchDir} ball={other.name}"
        );

        // 1) get the ball's NetworkObjectId
        var ballNO = other.GetComponentInParent<Unity.Netcode.NetworkObject>();
        if (!ballNO)
        {
            Debug.LogWarning("[SWING] Hit ball layer but no NetworkObject found on ball.");
            nextAllowedTime = Time.time + cooldown;
            return;
        }

        link.golfer.RequestBallHitFromClub(ballNO.NetworkObjectId, launchDir, power01, curve01);

        nextAllowedTime = Time.time + cooldown;
    }

    public void BindContext(GolferContextLink context)
    {
        link = context;
    }
}