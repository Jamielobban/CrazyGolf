using UnityEngine;

/// <summary>
/// Local-only shot preview visualizer.
/// - Spawns (renders) from the BALL position (not camera).
/// - Aims using a provided aim transform (ex: swingPlane/swingPivot) OR clubhead velocity.
/// - Uses ClubData to pick loft + impulse (ideal strike).
/// - Simulates ballistic arc + simple ground roll + optional ground snapping.
/// 
/// IMPORTANT: This is NOT networked and should not be a NetworkObject.
/// Put it on the local player (or a local-only helper object).
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class ShotPreviewVisualizer : MonoBehaviour
{
    public enum AimSource
    {
        AimTransformForward,
        ClubHeadVelocity
    }

    [Header("Refs (assign on Player)")]
    [SerializeField] private NetworkGolferPlayer golfer;          // your player script
    [SerializeField] private NetworkClubEquipment equipment;      // has equippedClubId
    [SerializeField] private ClubDatabase clubDb;                 // your DB asset
    [SerializeField] private Transform aimTransform;              // swingPlane or swingPivot (recommended)
    [SerializeField] private GolferContextLink link;   // optional, only if using ClubHeadVelocity aim
    [SerializeField] private ClubHeadVelocity clubHeadVelocity;   // optional, only if using ClubHeadVelocity aim

    [Header("Show rules")]
    [SerializeField] private bool showOnlyForOwner = true;
    [SerializeField] private bool hideWhenBallMoving = true;
    [SerializeField] private float localMovingSpeed = 0.12f; // m/s (client-side check)
    [SerializeField] private bool hideWhenBallHeld = true;

    [Header("Aim")]
    [SerializeField] private AimSource aimSource = AimSource.AimTransformForward;
    [SerializeField] private float aimFallbackYawDeg = 0f; // if aim direction degenerates

    [Header("Origin")]
    [SerializeField] private float originLift = 0.03f; // lifts the first point above grass
    [SerializeField] private float startForwardNudge = 0.02f; // tiny forward offset to avoid line clipping into ball

    [Header("Perfect strike assumptions")]
    [Range(0f, 1f)]
    [SerializeField] private float previewPower01 = 1f; // 1 = maxImpulse
    [SerializeField] private float loftBiasDeg = 0f;    // extra degrees for “nice strike” or attack angle
    [SerializeField] private bool ignoreCurve = true;   // v1: straight preview

    [Header("Prediction quality")]
    [SerializeField] private int maxPoints = 80;
    [SerializeField] private float simDt = 0.04f;
    [SerializeField] private float maxSimTime = 4.0f;

    [Header("Approx physics")]
    [SerializeField] private float gravity = 9.81f;
    [SerializeField] private float groundRollDecel = 3.0f; // m/s^2
    [SerializeField] private float groundStopSpeed = 0.25f;

    [Header("Ground snapping")]
    [SerializeField] private bool snapToGround = true;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundRayUp = 1.5f;
    [SerializeField] private float groundRayDown = 6.0f;
    [SerializeField] private float groundClearance = 0.02f; // keep slightly above surface

    [Header("Controls")]
    [SerializeField] private KeyCode toggleKey = KeyCode.T;
    [SerializeField] private bool enabledByDefault = true;

    private LineRenderer lr;
    private bool isOn;

    private void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.positionCount = 0;

        if (!golfer) golfer = GetComponentInParent<NetworkGolferPlayer>();
        if (!equipment) equipment = GetComponentInParent<NetworkClubEquipment>();

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

        if (!golfer || !equipment || !clubDb)
        {
            SetVisible(false);
            return;
        }

       // if (showOnlyForOwner && golfer is NetworkBehaviour nb && !nb.IsOwner)
        //{
           // SetVisible(false);
           // return;
        //}

        // Resolve ball (you changed golfer.MyBall to NetworkGolfBallState)
        NetworkGolfBallState ballState = golfer.MyBall;
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

        // If ball is moving, hide preview (client-side RB check)
        if (hideWhenBallMoving)
        {
            var rbBall = ballState.GetComponent<Rigidbody>();
            if (rbBall)
            {
                Vector3 v = rbBall.linearVelocity;
                v.y = 0f;
                if (v.magnitude > localMovingSpeed)
                {
                    SetVisible(false);
                    return;
                }
            }
        }

        // Resolve club data
        int clubId = equipment.equippedClubId.Value;
        ClubData cd = clubDb.Get(clubId);
        if (!cd)
        {
            SetVisible(false);
            return;
        }

        // Origin at ball (not camera)
        Vector3 origin = ballState.transform.position + Vector3.up * originLift;

        // Aim direction (flat XZ)
        if (!TryGetPathDir(out Vector3 pathDir))
        {
            SetVisible(false);
            return;
        }

        // Build launch direction using loft
        float loft = cd.loftDeg + loftBiasDeg;
        Vector3 launchDir = ApplyLoft(pathDir, loft).normalized;

        // Convert impulse -> initial velocity
        Rigidbody rb = ballState.GetComponent<Rigidbody>();
        if (!rb)
        {
            SetVisible(false);
            return;
        }

        float impulse = Mathf.Lerp(cd.minImpulse, cd.maxImpulse, Mathf.Clamp01(previewPower01));
        Vector3 v0 = launchDir * (impulse / Mathf.Max(0.0001f, rb.mass));

        // Tiny nudge forward so the line doesn’t visually start inside the ball mesh
        origin += pathDir * startForwardNudge;

        // Predict and draw
        PredictAndDraw(origin, v0);

        SetVisible(true);
    }

    private void SetVisible(bool v)
    {
        if (lr.enabled != v)
            lr.enabled = v;

        if (!v)
            lr.positionCount = 0;
    }

    private bool TryGetPathDir(out Vector3 pathDir)
    {
        pathDir = Vector3.forward;

        Vector3 raw;

        if (aimSource == AimSource.ClubHeadVelocity)
        {
            if (!clubHeadVelocity)
                clubHeadVelocity = link.ClubHead.GetComponent<ClubHeadVelocity>();

            if (!clubHeadVelocity)
                return false;

            raw = clubHeadVelocity.VelocityWorld;
        }
        else
        {
            if (!aimTransform)
                return false;

            raw = aimTransform.forward;
        }

        Vector3 flat = new Vector3(raw.x, 0f, raw.z);

        if (flat.sqrMagnitude < 0.0001f)
        {
            // Fallback: world yaw
            pathDir = Quaternion.Euler(0f, aimFallbackYawDeg, 0f) * Vector3.forward;
            return true;
        }

        pathDir = flat.normalized;
        return true;
    }

    private static Vector3 ApplyLoft(Vector3 flatDir, float loftDeg)
    {
        Vector3 right = Vector3.Cross(flatDir, Vector3.up);
        if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
        right.Normalize();

        return Quaternion.AngleAxis(loftDeg, right) * flatDir;
    }

    private void PredictAndDraw(Vector3 p0, Vector3 v0)
    {
        int pts = Mathf.Clamp(maxPoints, 8, 256);
        lr.positionCount = pts;

        Vector3 p = p0;
        Vector3 v = v0;

        float t = 0f;
        bool grounded = false;

        for (int i = 0; i < pts; i++)
        {
            if (snapToGround)
                p = SnapPointToGround(p, grounded);

            lr.SetPosition(i, p);

            // End early if too long
            t += simDt;
            if (t > maxSimTime)
            {
                FillRest(i + 1, pts, p);
                return;
            }

            if (!grounded)
            {
                // Airborne ballistic
                v += Vector3.down * gravity * simDt;
                Vector3 pNext = p + v * simDt;

                if (snapToGround && WouldHitGround(p, pNext))
                {
                    grounded = true;
                    // Land: clamp to ground and remove vertical velocity
                    pNext = SnapPointToGround(pNext, true);
                    v.y = 0f;
                }

                p = pNext;
            }
            else
            {
                // Rolling
                Vector3 vFlat = new Vector3(v.x, 0f, v.z);
                float spd = vFlat.magnitude;

                if (spd <= groundStopSpeed)
                {
                    v = Vector3.zero;
                    FillRest(i + 1, pts, p);
                    return;
                }

                Vector3 dir = vFlat / spd;
                float newSpd = Mathf.Max(0f, spd - groundRollDecel * simDt);
                Vector3 vFlatNew = dir * newSpd;

                v = new Vector3(vFlatNew.x, 0f, vFlatNew.z);
                p += v * simDt;
            }
        }
    }

    private void FillRest(int start, int count, Vector3 p)
    {
        for (int k = start; k < count; k++)
            lr.SetPosition(k, p);
    }

    private Vector3 SnapPointToGround(Vector3 p, bool alreadyGrounded)
    {
        if (!snapToGround) return p;

        Vector3 start = p + Vector3.up * groundRayUp;
        float dist = groundRayUp + groundRayDown;

        if (Physics.Raycast(start, Vector3.down, out var hit, dist, groundMask, QueryTriggerInteraction.Ignore))
        {
            float y = hit.point.y + groundClearance;

            // If airborne, don’t “snap up” to cliffs above us; only clamp downwards
            if (!alreadyGrounded && y > p.y)
                return p;

            p.y = y;
        }

        return p;
    }

    private bool WouldHitGround(Vector3 pA, Vector3 pB)
    {
        // If we’re going downward and the ground under B is above B.y, treat as landing
        Vector3 start = pB + Vector3.up * groundRayUp;
        float dist = groundRayUp + groundRayDown;

        if (Physics.Raycast(start, Vector3.down, out var hit, dist, groundMask, QueryTriggerInteraction.Ignore))
        {
            float groundY = hit.point.y + groundClearance;
            return pB.y <= groundY + 0.01f;
        }

        return false;
    }
}
