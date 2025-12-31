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
    [Tooltip("Side acceleration applied while curving. Bigger = more bend.")]
    [SerializeField] private float curveAccel = 18f;

    [Tooltip("Curve only applies while flat speed is above this.")]
    [SerializeField] private float minCurveFlatSpeed = 2.0f;

    [Tooltip("Curve applies only for this many seconds after impact (prevents late weird bending).")]
    [SerializeField] private float curveMaxSeconds = 1.2f;

    [Tooltip("Stop curving after we've been grounded for this many consecutive physics frames.")]
    [SerializeField] private int groundedFramesToStopCurve = 3;

    [Header("Ground check (server)")]
    [SerializeField] private LayerMask groundMask = ~0; // set to your ground layers
    [SerializeField] private float groundCheckRadius = 0.06f;
    [SerializeField] private float groundCheckDistance = 0.10f;
    [SerializeField] private float groundCheckUpOffset = 0.03f;

    [Header("Grounded State (read-only debug)")]
    [SerializeField] private bool groundedNow;          // visible in inspector
    [SerializeField] private int groundedFrames;        // visible in inspector

    private Rigidbody rb;

    // For position-based velocity estimate
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
    private float curveSigned;     // -1..1
    private float curveStopTime;   // Time.time when curve window ends

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

        Vector3 v = rb.linearVelocity; // if your project uses rb.linearVelocity, swap it
        v.y = 0f;
        return v.magnitude < stoppedSpeed;
    }

    // PUBLIC so you can watch it / call it (server truth). Shows groundedNow in inspector too.
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

        // Optional: for consistent testing
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Curve setup
        curveSigned = Mathf.Clamp(curve01, -1f, 1f);
        curveStopTime = Time.time + curveMaxSeconds;
        groundedFrames = 0;

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

        Debug.Log($"[BALL][Server] HitServer impulse={impulse:F2} dir={dir} curve01={curveSigned:F2}");
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        // ---- Ground state (with anti-bounce counter) ----
        bool grounded = IsGroundedServer();

       if (grounded)
            groundedFrames = Mathf.Min(groundedFrames + 1, groundedFramesToStopCurve);
        else
            groundedFrames = 0;

        // If we’ve been grounded for a few frames, stop curving completely
        if (groundedFrames >= groundedFramesToStopCurve)
            curveSigned = 0f;

        // ---- Curve while airborne, fast enough, and within the hit window ----
        if (!grounded &&
            Time.time < curveStopTime &&
            Mathf.Abs(curveSigned) > 0.001f)
        {
            Vector3 v = rb.linearVelocity; // or rb.linearVelocity
            Vector3 vFlat = new Vector3(v.x, 0f, v.z);

            float flatSpd = vFlat.magnitude;
            if (flatSpd > minCurveFlatSpeed)
            {
                Vector3 forward = vFlat / flatSpd;
                Vector3 side = Vector3.Cross(Vector3.up, forward) * curveSigned;

                rb.AddForce(side * curveAccel, ForceMode.Acceleration);
            }
        }

        // ---- Your existing debug / pos-velocity estimate ----
        float dt = Time.fixedDeltaTime;

        Vector3 posNow = transform.position;
        Vector3 velFromPosNow = (posNow - lastPos) / Mathf.Max(dt, 0.0001f);

        if (pendingHitLog)
        {
            pendingHitLog = false;

            Vector3 expectedDv = (hitImpulse / hitMass) * hitDir;
            Vector3 expectedVel1 = velFromPosAtHit + expectedDv;
            Vector3 expectedPos1 = posAtHit + expectedVel1 * dt;

            // Uncomment if you want the spam:
            /*
            Debug.Log(
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
