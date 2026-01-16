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

    [Header("Launch Ramp (server)")]
    [Tooltip("If true, we apply the SAME total impulse as a time-shaped dv ramp, so it starts slower then speeds up.")]
    [SerializeField] private bool useLaunchRamp = true;

    [Tooltip("Seconds over which we distribute the SAME total impulse.")]
    [SerializeField] private float launchRampDuration = 0.15f;

    [Tooltip("0..1 -> 0..1. Shape of how quickly we spend the impulse (area-normalized).")]
    [SerializeField] private AnimationCurve launchRampCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Curve sampling resolution for area normalization. Higher = closer to exact.")]
    [SerializeField] private int launchCurveSamples = 32;

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

    // Launch ramp state (applies same total dv as impulse, but time-shaped)
    private bool launchRampActive;
    private float launchRampTime;
    private Vector3 launchDeltaVTotal; // total dv to apply over the ramp
    private float launchCurveArea = 1f; // area under curve over [0..1]
    private float launchPrevCDF;        // cumulative fraction applied last frame [0..1]

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        lastPos = transform.position;
        lastVelFromPos = Vector3.zero;
        RecomputeLaunchCurveArea();
    }

    private void OnValidate()
    {
        // keep area valid in-editor
        launchCurveSamples = Mathf.Max(4, launchCurveSamples);
        if (Application.isPlaying)
            RecomputeLaunchCurveArea();
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

        // ---- Launch: same total "impulse" but time-shaped so it starts slower then speeds up ----
        if (!useLaunchRamp || launchRampDuration <= 0.0001f)
        {
            rb.AddForce(dir * impulse, ForceMode.Impulse);
        }
        else
        {
            // Impulse = m * dv  => dv = impulse / m
            float m = Mathf.Max(0.0001f, rb.mass);
            launchDeltaVTotal = dir * (impulse / m);

            launchRampActive = true;
            launchRampTime = 0f;
            launchPrevCDF = 0f;
        }

        Debug.Log($"[BALL][Server] HitServer impulse={impulse:F2} dir={dir} curve01={curveSigned:F2} ramp={(useLaunchRamp ? launchRampDuration.ToString("F2") : "off")}");
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

        // ---- Launch ramp (same total dv as impulse) ----
        if (launchRampActive)
        {
            float dt = Time.fixedDeltaTime;
            launchRampTime += dt;

            float t01 = Mathf.Clamp01(launchRampTime / Mathf.Max(0.0001f, launchRampDuration));

            // desired cumulative fraction of total dv at this t
            float cdf = CurveCDF01(t01);

            // apply incremental fraction since last frame
            float deltaFrac = Mathf.Clamp01(cdf - launchPrevCDF);

            if (deltaFrac > 0f)
            {
                Vector3 dvStep = launchDeltaVTotal * deltaFrac;

                // Mass-independent dv application; sum(dvStep) == launchDeltaVTotal
                rb.AddForce(dvStep, ForceMode.VelocityChange);
            }

            launchPrevCDF = cdf;

            if (t01 >= 1f)
                launchRampActive = false;
        }

        // refresh velocity after ramp step
        v = rb.linearVelocity;
        vFlat = new Vector3(v.x, 0f, v.z);
        flatSpd = vFlat.magnitude;

        // ---- Ground-only roll drag (prevents rolling forever) ----
        if (grounded)
        {
            if (flatSpd <= groundStopFlatSpeed)
            {
                rb.linearVelocity = new Vector3(0f, v.y, 0f);
            }
            else
            {
                Vector3 flatDir = vFlat / flatSpd;

                float accelMag = groundRollDecel + groundVelDrag * flatSpd; // m/s^2
                Vector3 accel = -flatDir * accelMag;

                rb.AddForce(accel, ForceMode.Acceleration);
            }
        }

        // ---- Curve (airborne only) ----
        if (!grounded && Mathf.Abs(curveSigned) > 0.001f)
        {
            if (flatSpd > 0.25f)
                lastForwardFlat = vFlat / flatSpd;

            if (flatSpd > minCurveFlatSpeed)
            {
                Vector3 side = Vector3.Cross(Vector3.up, lastForwardFlat);
                side.y = 0f;
                float sideMag = side.magnitude;
                if (sideMag > 0.0001f)
                {
                    side /= sideMag;
                    side *= curveSigned;

                    float refSpd = Mathf.Max(0.001f, curveRefFlatSpeed);
                    float speedScale = (flatSpd * flatSpd) / (refSpd * refSpd);
                    speedScale = Mathf.Clamp(speedScale, 0f, maxCurveSpeedScale);

                    rb.AddForce(side * (curveAccel * speedScale), ForceMode.Acceleration);
                }
            }
        }

        // ---- Debug / pos-velocity estimate ----
        float dtPos = Time.fixedDeltaTime;

        Vector3 posNow = transform.position;
        Vector3 velFromPosNow = (posNow - lastPos) / Mathf.Max(dtPos, 0.0001f);

        if (pendingHitLog)
        {
            pendingHitLog = false;

            Vector3 expectedDv = (hitImpulse / hitMass) * hitDir;
            Vector3 expectedVel1 = velFromPosAtHit + expectedDv;
            Vector3 expectedPos1 = posAtHit + expectedVel1 * dtPos;

            /*
            Debug.Log(
                $"[BALL][Server][Fixed HIT] tHit={hitTime:F3} dt={dtPos:F3} impulse={hitImpulse:F2} mass={hitMass:F2} dir={hitDir} " +
                $"posAtHit={posAtHit} velAtHit={velFromPosAtHit} " +
                $"posNow={posNow} velNow={velFromPosNow} " +
                $"expectedDv={expectedDv} expectedPos1={expectedPos1}"
            );
            */
        }

        lastVelFromPos = velFromPosNow;
        lastPos = posNow;
    }

    // ---- Launch curve normalization ----

    private void RecomputeLaunchCurveArea()
    {
        int n = Mathf.Max(4, launchCurveSamples);

        float sum = 0f;
        float prevT = 0f;
        float prevV = Mathf.Max(0f, launchRampCurve != null ? launchRampCurve.Evaluate(0f) : 0f);

        for (int i = 1; i <= n; i++)
        {
            float t = (float)i / n;
            float v = Mathf.Max(0f, launchRampCurve != null ? launchRampCurve.Evaluate(t) : t);

            sum += 0.5f * (prevV + v) * (t - prevT);

            prevT = t;
            prevV = v;
        }

        launchCurveArea = Mathf.Max(0.0001f, sum);
    }

    // Normalized cumulative fraction (0..1) of curve integral up to t01
    private float CurveCDF01(float t01)
    {
        t01 = Mathf.Clamp01(t01);

        int n = Mathf.Max(4, launchCurveSamples);

        float sum = 0f;
        float prevT = 0f;
        float prevV = Mathf.Max(0f, launchRampCurve != null ? launchRampCurve.Evaluate(0f) : 0f);

        int steps = Mathf.Max(1, Mathf.RoundToInt(n * t01));
        for (int i = 1; i <= steps; i++)
        {
            float t = Mathf.Min(t01, (float)i / n);
            float v = Mathf.Max(0f, launchRampCurve != null ? launchRampCurve.Evaluate(t) : t);

            sum += 0.5f * (prevV + v) * (t - prevT);

            prevT = t;
            prevV = v;

            if (t >= t01) break;
        }

        return Mathf.Clamp01(sum / launchCurveArea);
    }
}
