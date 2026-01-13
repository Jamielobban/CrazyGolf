using System.Data.Common;
using UnityEngine;

public class ClubBallContactLogger : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private ClubHeadVelocity vel;

    [Header("Ball")]
    [SerializeField] private LayerMask ballMask;

    [Header("Log Control")]
    [SerializeField] private float minSpeedToHit = 1.0f;
    [SerializeField] private float cooldown = 0.25f;


    [Header("Intent Input (set-before-hit)")]
    [Tooltip("Curve intent ramp speed (units/sec) while holding 4/5.")]
    [SerializeField] private float curveRampSpeed = 1.5f;

    [Tooltip("Launch bias ramp speed (deg/sec) while holding 6/7.")]
    [SerializeField] private float launchBiasRampSpeedDeg = 18f;

    [Tooltip("Optional reset key 0 resets curve+launch bias.")]
    [SerializeField] private bool enableResetKey = true;

    [Header("Runtime Intent (debug)")]
    [Range(-1f, 1f)] [SerializeField] private float curveIntent01 = 0f;
    [SerializeField] private float launchBiasDeg = 0f;

    [SerializeField] private GolferContextLink link;

    private float nextAllowedTime;

    private void Awake()
    {
        if (!vel) vel = GetComponentInParent<ClubHeadVelocity>();
    }

    public void BindContext(GolferContextLink context) => link = context;

    private void Update()
    {
        if (link == null || link.golfer == null || !link.golfer.IsOwner)
            return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Curve intent: hold-to-ramp, value persists (set-before-hit)
        if (Input.GetKey(KeyCode.Alpha4))
            curveIntent01 -= curveRampSpeed * dt;
        if (Input.GetKey(KeyCode.Alpha5))
            curveIntent01 += curveRampSpeed * dt;

        curveIntent01 = Mathf.Clamp(curveIntent01, -1f, 1f);

        // Launch bias intent: hold-to-ramp degrees, persists (set-before-hit)
        if (Input.GetKey(KeyCode.Alpha6))
            launchBiasDeg -= launchBiasRampSpeedDeg * dt;
        if (Input.GetKey(KeyCode.Alpha7))
            launchBiasDeg += launchBiasRampSpeedDeg * dt;

        // Clamp bias by club (if available)
        var d = (link != null) ? link.Data : null;
        float biasMax = (d != null) ? d.launchBiasMaxAbsDeg : 10f;
        launchBiasDeg = Mathf.Clamp(launchBiasDeg, -biasMax, biasMax);

        if (enableResetKey && Input.GetKeyDown(KeyCode.Alpha0))
        {
            curveIntent01 = 0f;
            launchBiasDeg = 0f;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
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

        // Use equipped club data if present
        ClubData d = link.Data;

        float speedForFull = Mathf.Max(0.001f, d.speedForFullPower);

        float norm = Mathf.Clamp01(speed / speedForFull);
        float power01 = Mathf.Clamp01(d.powerCurve.Evaluate(norm));

        // --- LAUNCH (base + player bias, then clamp) ---
        float baseLaunchDeg = d.loftDeg + attackDeg;
        float launchDeg = Mathf.Clamp(baseLaunchDeg + launchBiasDeg, d.minLaunchDeg, d.maxLaunchDeg);

        // Build a launch direction that always goes upward-ish
        Vector3 rightAxis = Vector3.Cross(pathDir, Vector3.up);
        if (rightAxis.sqrMagnitude < 0.0001f) rightAxis = transform.right;
        rightAxis.Normalize();

        Vector3 launchDir = Quaternion.AngleAxis(launchDeg, rightAxis) * pathDir;

        if (launchDir.y < 0.05f)
            launchDir = Quaternion.AngleAxis(launchDeg, -rightAxis) * pathDir;

        if (launchDir.y < 0.05f) launchDir.y = 0.05f;
        launchDir.Normalize();

        // --- CURVE (intent * speed scaling * club effectiveness, capped per club) ---
        float curveEff  = (d != null) ? d.curveEffectiveness : 1f;
        float curveCap  = (d != null) ? d.curveMaxAbs : 0.5f;

        float curve01 = curveIntent01 * norm * curveEff;
        curve01 = Mathf.Clamp(curve01, -curveCap, curveCap);

        // Get ball NetworkObjectId
        var ballNO = other.GetComponentInParent<Unity.Netcode.NetworkObject>();
        if (!ballNO)
        {
            nextAllowedTime = Time.time + cooldown;
            return;
        }

        // Request server hit
        link.golfer.RequestBallHitFromClub(ballNO.NetworkObjectId, launchDir, power01, curve01);

        nextAllowedTime = Time.time + cooldown;
    }
}
