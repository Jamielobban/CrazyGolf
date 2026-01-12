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

    private float nextAllowedTime;

    [SerializeField] private GolferContextLink link;

    private GolfClub club;
    private ClubData data;

    private void Awake()
    {
        if (!faceFrame) faceFrame = transform;
        if (!vel) vel = GetComponentInParent<ClubHeadVelocity>();

        club = GetComponentInParent<GolfClub>();
        if (club) data = club.data;
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
        float pathYaw = Mathf.Atan2(pathDir.x, pathDir.z) * Mathf.Rad2Deg;

        // Attack angle (up/down) from velocity
        float attackDeg = Mathf.Atan2(v.y, flatSpeed) * Mathf.Rad2Deg;

        // --- FACE from club face normal (local -X) flattened on XZ ---
        Vector3 faceN = faceFrame.TransformDirection(Vector3.left); // -X = face normal
        Vector3 faceFlat = new Vector3(faceN.x, 0f, faceN.z);
        float faceFlatMag = faceFlat.magnitude;

        Vector3 faceDir = (faceFlatMag > 0.001f) ? (faceFlat / faceFlatMag) : pathDir;
        float faceYaw = Mathf.Atan2(faceDir.x, faceDir.z) * Mathf.Rad2Deg;

        float faceMinusPath = Mathf.DeltaAngle(pathYaw, faceYaw);


        // Curve normalized (-1..1)
        float curve01 = Mathf.Clamp(faceMinusPath / Mathf.Max(0.001f, link.Data.fpForFullCurve), -1f, 1f);

        // Scale curve intent by speed (prevents tiny brushes making full hook/slice)
        float speed01ForCurve = Mathf.Clamp01(speed / Mathf.Max(0.001f, link.Data.speedForFullPower));
        curve01 *= speed01ForCurve;
        Debug.Log($"[HIT] speed={speed:F1} pathYaw={pathYaw:F1} faceYaw={faceYaw:F1} faceMinusPath={faceMinusPath:F1} curve01={curve01:F2} fp={link.Data.fpForFullCurve:F2}");

        // Loft
        float loftDeg = data ? data.loftDeg : 0f;

        // Power from speed curve
        float norm = Mathf.Clamp01(speed / Mathf.Max(0.001f, link.Data.speedForFullPower));
        float power01 = Mathf.Clamp01(link.Data.powerCurve.Evaluate(norm));

        // Launch angle
        float launchDeg = Mathf.Clamp(loftDeg + attackDeg, link.Data.minLaunchDeg, link.Data.maxLaunchDeg);

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

        // Request server hit (your existing method)
        link.golfer.RequestBallHitFromClub(ballNO.NetworkObjectId, launchDir, power01, curve01);

        nextAllowedTime = Time.time + cooldown;
    }
}
