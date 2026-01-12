using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class NetworkGolfBall : NetworkBehaviour
{
    [Header("Stopped check")]
    public float stoppedSpeed = 0.15f;

    public ulong LogicalOwnerClientId;

    [Header("Debug")]
    [SerializeField] private bool debugImpulse = true;

    [Header("Curve (server)")]
    [SerializeField] private float curveAccel = 18f;
    [SerializeField] private float minCurveFlatSpeed = 2.0f;

    [Tooltip("Reference flat speed used for speed² normalization.")]
    [SerializeField] private float curveRefFlatSpeed = 18f;

    [Tooltip("Safety clamp for curve strength.")]
    [SerializeField] private float maxCurveSpeedScale = 2.0f;

    [Tooltip("Stop curving after grounded for N frames.")]
    [SerializeField] private int groundedFramesToStopCurve = 3;

    [Header("Ground roll drag (server)")]
    [Tooltip("Constant horizontal deceleration while grounded (m/s^2). Bigger stops rolling faster.")]
    [SerializeField] private float groundRollDecel = 2.8f;

    [Tooltip("Extra proportional horizontal drag while grounded (m/s^2 per (m/s)).")]
    [SerializeField] private float groundVelDrag = 0.25f;

    [Tooltip("When grounded and below this flat speed, snap-stop to zero.")]
    [SerializeField] private float groundStopFlatSpeed = 0.12f;

    [Header("Ground check (server)")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundCheckRadius = 0.06f;
    [SerializeField] private float groundCheckDistance = 0.10f;
    [SerializeField] private float groundCheckUpOffset = 0.03f;

    [Header("Grounded State (read-only debug)")]
    [SerializeField] private bool groundedNow;
    [SerializeField] private int groundedFrames;

    private Rigidbody rb;

    // Position-based velocity estimate (debug)
    private Vector3 lastPos;
    private Vector3 lastVelFromPos;

    // Hit debug snapshot
    private bool pendingHitLog;
    private Vector3 hitDir;
    private float hitImpulse;
    private float hitMass;

    private Vector3 posAtHit;
    private Vector3 velFromPosAtHit;
    private float hitTime;

    // Curve state
    private float curveSigned; // -1..1

    // Stabilizes curve direction near apex (prevents corkscrew)
    private Vector3 lastForwardFlat = Vector3.forward;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        lastPos = transform.position;
        lastVelFromPos = Vector3.zero;
    }

    public bool IsStoppedServer()
    {
        if (!IsServer) return false;

        Vector3 v = rb.linearVelocity;
        v.y = 0f;
        return v.magnitude < stoppedSpeed;
    }

    public bool IsGroundedServer()
    {
        Vector3 origin = transform.position + Vector3.up * groundCheckUpOffset;

        bool grounded = Physics.SphereCast(
            origin,
            groundCheckRadius,
            Vector3.down,
            out _,
            groundCheckDistance,
            groundMask,
            QueryTriggerInteraction.Ignore
        );

        groundedNow = grounded;
        return grounded;
    }

    // Server authoritative hit
    public void HitServer(Vector3 dir, float impulse, float curve01)
    {
        if (!IsServer) return;

        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        rb.WakeUp();

        // For consistent testing (your original behavior)
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        curveSigned = Mathf.Clamp(curve01, -1f, 1f);
        groundedFrames = 0;

        // Initialize stable forwardFlat from launch direction
        Vector3 dFlat = new Vector3(dir.x, 0f, dir.z);
        if (dFlat.sqrMagnitude > 0.0001f)
            lastForwardFlat = dFlat.normalized;

        // Hit debug snapshot
        if (debugImpulse)
        {
            posAtHit = transform.position;
            velFromPosAtHit = lastVelFromPos;
            hitTime = Time.time;

            hitDir = dir;
            hitImpulse = impulse;
            hitMass = Mathf.Max(rb.mass, 0.0001f);

            pendingHitLog = true;
        }

        rb.AddForce(dir * impulse, ForceMode.Impulse);

        //Debug.Log($"[BALL][Server] HitServer impulse={impulse:F2} dir={dir} curve01={curveSigned:F2}");
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        // ---- Ground state ----
        bool grounded = IsGroundedServer();

        if (grounded)
            groundedFrames = Mathf.Min(groundedFrames + 1, groundedFramesToStopCurve);
        else
            groundedFrames = 0;

        if (groundedFrames >= groundedFramesToStopCurve)
            curveSigned = 0f;

        Vector3 v = rb.linearVelocity;
        Vector3 vFlat = new Vector3(v.x, 0f, v.z);
        float flatSpd = vFlat.magnitude;

        // ---- Ground-only roll drag (prevents rolling forever) ----
        if (grounded)
        {
            // Snap stop when slow
            if (flatSpd <= groundStopFlatSpeed)
            {
                rb.linearVelocity = new Vector3(0f, v.y, 0f);
            }
            else
            {
                // Oppose horizontal motion:
                // constant decel + proportional drag (feels good on slopes too)
                Vector3 flatDir = vFlat / flatSpd;

                float accelMag = groundRollDecel + groundVelDrag * flatSpd; // m/s^2
                Vector3 accel = -flatDir * accelMag;

                rb.AddForce(accel, ForceMode.Acceleration);
            }
        }

        // ---- Curve (airborne only) ----
        if (!grounded &&
            Mathf.Abs(curveSigned) > 0.001f)
        {
            // Update stable forward only when we have meaningful flat movement
            if (flatSpd > 0.25f)
                lastForwardFlat = vFlat / flatSpd;

            // Apply curve only above threshold (but do NOT zero curveSigned)
            if (flatSpd > minCurveFlatSpeed)
            {
                // Side direction (always horizontal, stable)
                Vector3 side = Vector3.Cross(Vector3.up, lastForwardFlat);
                side.y = 0f;
                float sideMag = side.magnitude;
                if (sideMag > 0.0001f)
                {
                    side /= sideMag;
                    side *= curveSigned;

                    // Speed² scaling
                    float refSpd = Mathf.Max(0.001f, curveRefFlatSpeed);
                    float speedScale = (flatSpd * flatSpd) / (refSpd * refSpd);
                    speedScale = Mathf.Clamp(speedScale, 0f, maxCurveSpeedScale);

                    rb.AddForce(side * (curveAccel * speedScale), ForceMode.Acceleration);
                }
            }
        }

        // ---- Debug / pos-velocity estimate (your original idea) ----
        float dt = Time.fixedDeltaTime;

        Vector3 posNow = transform.position;
        Vector3 velFromPosNow = (posNow - lastPos) / Mathf.Max(dt, 0.0001f);

        if (pendingHitLog)
        {
            pendingHitLog = false;

            Vector3 expectedDv = (hitImpulse / hitMass) * hitDir;
            Vector3 expectedVel1 = velFromPosAtHit + expectedDv;
            Vector3 expectedPos1 = posAtHit + expectedVel1 * dt;

            // Uncomment for spam when tuning:
            
            /*Debug.Log(
                $"[BALL][Server][Fixed HIT] tHit={hitTime:F3} dt={dt:F3} impulse={hitImpulse:F2} mass={hitMass:F2} dir={hitDir} " +
                $"posAtHit={posAtHit} velAtHit={velFromPosAtHit} " +
                $"posNow={posNow} velNow={velFromPosNow} " +
                $"expectedDv={expectedDv} expectedPos1={expectedPos1}"
            );
            */
        }

        lastVelFromPos = velFromPosNow;
        lastPos = posNow;
    }
}
