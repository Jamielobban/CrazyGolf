using Unity.Netcode;
using UnityEngine;

public class ClubHitbox : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private NetworkGolferPlayer golfer;
    [SerializeField] private Transform clubHead;

    [Header("Detection")]
    [SerializeField] private LayerMask ballMask;
    [SerializeField] private float radius = 0.07f;
    [SerializeField] private float hitCooldown = 0.25f;

    [Header("Power from club-head speed (client-side feel)")]
    [SerializeField] private float minSwingSpeed = 0.5f;   // m/s -> power01=0
    [SerializeField] private float maxSwingSpeed = 10.0f;  // m/s -> power01=1

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool verboseLogs = false;
    [SerializeField] private float logEverySeconds = 0.25f;

    private Vector3 prevPos;
    private Vector3 lastFrom;
    private Vector3 lastTo;
    private bool lastHit;

    private float lastHitTime;
    private float nextLogTime;

    private void Awake()
    {
        if (clubHead == null) clubHead = transform;
        if (golfer == null) golfer = GetComponentInParent<NetworkGolferPlayer>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) enabled = false;
        base.OnNetworkSpawn();
    }

    private void Start()
    {
        prevPos = clubHead.position;
        lastFrom = prevPos;
        lastTo = prevPos;
    }

    private void LateUpdate()
    {
        Vector3 currPos = clubHead.position;

        lastFrom = prevPos;
        lastTo = currPos;
        lastHit = false;

        if (Time.time - lastHitTime < hitCooldown)
        {
            prevPos = currPos;
            return;
        }

        Vector3 delta = currPos - prevPos;

        // Stationary-ish: still allow overlap contact (very gentle tap)
        if (delta.sqrMagnitude < 1e-8f)
        {
            TouchCheck(currPos, dt: Mathf.Max(Time.deltaTime, 0.0001f), distMoved: 0f, moveDir: Vector3.zero);
            prevPos = currPos;
            return;
        }

        float dist = delta.magnitude;
        Vector3 moveDir = delta / dist;

        // Sweep
        if (Physics.SphereCast(prevPos, radius, moveDir, out RaycastHit hit, dist, ballMask, QueryTriggerInteraction.Ignore))
        {
            lastHit = true;
            OnBallContact(hit.collider, moveDir, dist);
        }
        else
        {
            // Fallback overlap
            TouchCheck(currPos, dt: Mathf.Max(Time.deltaTime, 0.0001f), distMoved: dist, moveDir: moveDir);
        }

        prevPos = currPos;
    }

    private void TouchCheck(Vector3 pos, float dt, float distMoved, Vector3 moveDir)
    {
        Collider[] cols = Physics.OverlapSphere(pos, radius, ballMask, QueryTriggerInteraction.Ignore);
        if (cols == null || cols.Length == 0) return;

        // If we’re not moving, choose a reasonable direction
        Vector3 dir = moveDir;
        if (dir.sqrMagnitude < 1e-6f)
        {
            dir = clubHead.forward;
            dir.y = 0f;
        }

        if (dir.sqrMagnitude < 1e-6f) return;
        dir.Normalize();

        OnBallContact(cols[0], dir, distMoved, dtOverride: dt);
    }

    private void OnBallContact(Collider col, Vector3 dir, float distMoved, float dtOverride = -1f)
    {
        var ball = col.GetComponentInParent<NetworkGolfBall>();
        if (ball == null) return;

        float dt = (dtOverride > 0f) ? dtOverride : Mathf.Max(Time.deltaTime, 0.0001f);
        float speed = distMoved / dt;

        float power01 = Mathf.InverseLerp(minSwingSpeed, maxSwingSpeed, speed);
        power01 = Mathf.Clamp01(power01);

        // Flatten direction
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        if (verboseLogs && Time.time >= nextLogTime)
        {
            nextLogTime = Time.time + logEverySeconds;
            Debug.Log($"[CLUB][Owner] dist={distMoved:F4} dt={dt:F4} speed={speed:F2} " +
                      $"power01={power01:F2} dir={dir} ballNetId={ball.NetworkObjectId} " +
                      $"minSpeed={minSwingSpeed:F2} maxSpeed={maxSwingSpeed:F2}");
        }

        //golfer.RequestBallHitFromClub(ball.NetworkObjectId, dir, power01);

        lastHitTime = Time.time;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Gizmos.DrawWireSphere(lastFrom, radius);
        Gizmos.DrawWireSphere(lastTo, radius);
        Gizmos.DrawLine(lastFrom, lastTo);

        if (lastHit)
            Gizmos.DrawWireSphere((lastFrom + lastTo) * 0.5f, radius * 1.25f);
    }
}