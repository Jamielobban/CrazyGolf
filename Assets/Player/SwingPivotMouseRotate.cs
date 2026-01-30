using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class SwingPivotMouseRotate : NetworkBehaviour
{
    [Header("Assign")]
    [SerializeField] private Transform swingPivot;

    [Header("Live Club Data (debug)")]
    [SerializeField] private ClubData currentData;

    public bool isSwinging;

    private Quaternion baseLocalRotation;
    private float yawOffset;
    private float pitchOffset;

    // ---------------------------
    // DEBUG: detectors (no gameplay changes)
    // ---------------------------
    [Header("Debug: Flip/Jumps")]
    [SerializeField] private bool debugFlip = true;
    [SerializeField] private float logCooldown = 0.25f;

    private Quaternion lastTarget = Quaternion.identity;
    private Vector2 lastMouse;
    private float lastDt;
    private float lastYaw;
    private float lastPitch;
    private float nextLogTime;

    [SerializeField] private bool debugRotJumps = true;
    [SerializeField] private float jumpDegrees = 45f;
    [SerializeField] private float jumpCooldown = 0.15f;

    private Quaternion prevApplied = Quaternion.identity;
    private float nextJumpLogTime;

    [SerializeField] private float parentJumpDegrees = 45f;
    private Quaternion prevParent = Quaternion.identity;

    [SerializeField] private float baseDriftDegrees = 10f;
    private Quaternion baseLocalAtBegin = Quaternion.identity;
    private Quaternion baseWorldAtBegin = Quaternion.identity;
    private Quaternion parentWorldAtBegin = Quaternion.identity;

    private int lastWriteFrame = -1;
    private Quaternion rotationWrittenThisFrame = Quaternion.identity;

    // Tunables (pulled from ClubData)
    private float yawSpeed;
    private float pitchSpeed;
    private float pitchMin;
    private float pitchMax;
    private float rotFollow;

    private bool enableCatchup;
    private float catchupBoost;
    private float catchupTime;
    private float reversalThreshold;

    private bool enableAntiParking;
    private float yawClamp;
    private float yawDecay;
    private float pitchDecay;
    private AnimationCurve yawGainByPitch;

    private bool invertY;

    // reversal detection
    private float prevPitchOffset;
    private float prevPitchVel;
    private float catchupTimer;

    public float CurrentYawOffset => yawOffset;
    public float CurrentPitchOffset => pitchOffset;

    private GolferContextLink link;
    private bool baseRotationProvided;

    void Awake()
    {
        link = GetComponentInParent<GolferContextLink>();
    }

    public void BeginSwing()
    {
        isSwinging = true;
        if (!swingPivot) return;

        ResolveAndApplyClubData();

        if (!baseRotationProvided)
        {
            baseLocalRotation = swingPivot.localRotation;
            Debug.Log($"[SwingPivot] fallback base={baseLocalRotation.eulerAngles}");
        }
        else
        {
            Debug.Log($"[SwingPivot] provided base={baseLocalRotation.eulerAngles}");
        }

        yawOffset = 0f;
        pitchOffset = 0f;

        prevPitchOffset = pitchOffset;
        prevPitchVel = 0f;
        catchupTimer = 0f;

        // Debug init
        prevApplied = swingPivot.localRotation;
        nextJumpLogTime = 0f;

        prevParent = swingPivot.parent ? swingPivot.parent.rotation : Quaternion.identity;

        baseLocalAtBegin = baseLocalRotation;
        baseWorldAtBegin = swingPivot.rotation;
        parentWorldAtBegin = swingPivot.parent ? swingPivot.parent.rotation : Quaternion.identity;

        lastWriteFrame = -1;
        rotationWrittenThisFrame = Quaternion.identity;
    }

    public void EndSwing()
    {
        isSwinging = false;
        if (!swingPivot) return;

        swingPivot.localRotation = baseLocalRotation;
        catchupTimer = 0f;
        baseRotationProvided = false;
    }

    private void ResolveAndApplyClubData()
    {
        if (!link) link = GetComponentInParent<GolferContextLink>();

        currentData = link ? link.Data : null;
        if (currentData == null) return;

        var d = currentData;

        yawSpeed = d.yawSpeed;
        pitchSpeed = d.pitchSpeed;
        pitchMin = d.pitchMin;
        pitchMax = d.pitchMax;
        rotFollow = d.rotFollow;

        enableCatchup = d.enableCatchup;
        catchupBoost = d.catchupBoost;
        catchupTime = d.catchupTime;
        reversalThreshold = d.reversalThreshold;

        enableAntiParking = d.enableAntiParking;
        yawClamp = d.yawClamp;
        yawDecay = d.yawDecay;
        pitchDecay = d.pitchDecay;
        yawGainByPitch = d.yawGainByPitch;

        invertY = d.invertY;
    }

    void Update()
    {
        if (!IsOwner) return;
        if (!isSwinging || !swingPivot) return;
        if (!currentData) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // avoid giant spikes on hitch / alt-tab
        dt = Mathf.Min(dt, 0.05f);

        Vector2 m = ReadMouseDelta();
        float mx = m.x;
        float my = m.y * (invertY ? -1f : 1f);

        // ---- pitch ----
        pitchOffset += my * pitchSpeed;
        pitchOffset = Mathf.Clamp(pitchOffset, pitchMin, pitchMax);

        // ---- yaw gain by pitch (safer) ----
        float yawGain = 1f;
        if (enableAntiParking && yawGainByPitch != null)
        {
            float pitch01 = Mathf.InverseLerp(pitchMin, pitchMax, pitchOffset); // 0..1 across full range
            yawGain = yawGainByPitch.Evaluate(pitch01);
        }

        // ---- yaw ----
        yawOffset += mx * yawSpeed * yawGain;

        // ---- clamp + decay only when not actively inputting ----
        bool hasInput = Mathf.Abs(mx) > 0.0001f || Mathf.Abs(my) > 0.0001f;

        if (enableAntiParking)
        {
            yawOffset = Mathf.Clamp(yawOffset, -yawClamp, yawClamp);

            if (!hasInput)
            {
                float yawDecayT = 1f - Mathf.Exp(-yawDecay * dt);
                float pitchDecayT = 1f - Mathf.Exp(-pitchDecay * dt);

                yawOffset = Mathf.Lerp(yawOffset, 0f, yawDecayT);
                pitchOffset = Mathf.Lerp(pitchOffset, 0f, pitchDecayT);
            }
        }

        // ---- detect reversal and trigger catchup ----
        float pitchVel = (pitchOffset - prevPitchOffset) / dt;
        bool signFlip = (prevPitchVel > 0f && pitchVel < 0f) || (prevPitchVel < 0f && pitchVel > 0f);
        bool strong = Mathf.Abs(prevPitchVel) > reversalThreshold && Mathf.Abs(pitchVel) > reversalThreshold;

        if (enableCatchup && signFlip && strong)
            catchupTimer = catchupTime;

        prevPitchOffset = pitchOffset;
        prevPitchVel = pitchVel;

        // ---- rotation ----
        Quaternion offsetRot = Quaternion.Euler(pitchOffset, yawOffset, 0f);
        Quaternion target = baseLocalRotation * offsetRot;

        // DEBUG: target/arc/mouse/dt detectors (no gameplay changes)
        DebugDetectWeirdness(dt, new Vector2(mx, my), target);

        float follow = rotFollow;
        if (catchupTimer > 0f)
        {
            follow *= catchupBoost;
            catchupTimer -= dt;
        }

        float followT = 1f - Mathf.Exp(-follow * dt);
        swingPivot.localRotation = Quaternion.Slerp(swingPivot.localRotation, target, followT);

        // DEBUG: overwritten detection snapshot
        rotationWrittenThisFrame = swingPivot.localRotation;
        lastWriteFrame = Time.frameCount;

        if (debugRotJumps)
        {
            float delta = Quaternion.Angle(prevApplied, swingPivot.localRotation);

            if (delta > jumpDegrees && Time.time >= nextJumpLogTime)
            {
                nextJumpLogTime = Time.time + jumpCooldown;

                Debug.Log(
                    $"[ROT JUMP] Δ={delta:0.0}deg dt={dt:0.000} " +
                    $"mouse=({mx:0.00},{my:0.00}) yawOff={yawOffset:0.0} pitchOff={pitchOffset:0.0} " +
                    $"curEuler={swingPivot.localEulerAngles}"
                );
            }

            prevApplied = swingPivot.localRotation;
        }
    }

    private static Vector2 ReadMouseDelta()
    {
        // fallback (old input manager)
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
    }

    public void SetBaseLocalRotation(Quaternion baseRot)
    {
        baseLocalRotation = baseRot;
        baseRotationProvided = true;
    }

    // ---------------------------
    // DEBUG helpers (no gameplay changes)
    // ---------------------------
    private void DebugDetectWeirdness(float dt, Vector2 mouse, Quaternion target)
    {
        if (!debugFlip) return;
        if (Time.time < nextLogTime) return;

        // Make thresholds sensitive enough to catch your 47–80 deg snaps
        bool dtSpike = dt > 0.030f;

        float mouseMag = mouse.magnitude;
        bool mouseSpike = mouseMag > 12f;

        float targetJump = Quaternion.Angle(lastTarget, target);
        bool targetDiscontinuity = targetJump > 25f;

        float toCurrent = Quaternion.Angle(swingPivot.localRotation, target);
        bool hugeSlerpArc = toCurrent > 140f;

        if (dtSpike || mouseSpike || targetDiscontinuity || hugeSlerpArc)
        {
            nextLogTime = Time.time + logCooldown;

            Debug.Log(
                $"[SwingFlip?] dt={dt:0.000} mouse=({mouse.x:0.00},{mouse.y:0.00}) mag={mouseMag:0.00} " +
                $"yaw={yawOffset:0.0} pitch={pitchOffset:0.0} " +
                $"targetJump={targetJump:0.0} toCurrent={toCurrent:0.0} " +
                $"flags: dtSpike={dtSpike} mouseSpike={mouseSpike} targetJumpBig={targetDiscontinuity} hugeArc={hugeSlerpArc}"
            );
        }

        lastTarget = target;
        lastMouse = mouse;
        lastDt = dt;
        lastYaw = yawOffset;
        lastPitch = pitchOffset;
    }

    private void LateUpdate()
    {
        if (!isSwinging || !swingPivot) return;

        // 1) Parent sudden jump (frame-to-frame)
        if (debugRotJumps && swingPivot.parent)
        {
            float pDelta = Quaternion.Angle(prevParent, swingPivot.parent.rotation);
            if (pDelta > parentJumpDegrees && Time.time >= nextJumpLogTime)
            {
                nextJumpLogTime = Time.time + jumpCooldown;
                Debug.Log($"[PARENT JUMP] Δ={pDelta:0.0}deg parent={swingPivot.parent.name}");
            }
            prevParent = swingPivot.parent.rotation;
        }

        // 2) Parent drift since BeginSwing (once-per-swing snap root cause)
        if (debugRotJumps && swingPivot.parent)
        {
            float parentSinceBegin = Quaternion.Angle(parentWorldAtBegin, swingPivot.parent.rotation);
            if (parentSinceBegin > 10f && Time.time >= nextJumpLogTime)
            {
                nextJumpLogTime = Time.time + jumpCooldown;
                Debug.Log($"[PARENT SINCE BEGIN] Δ={parentSinceBegin:0.0}deg");
            }
        }

        // 3) Base changed mid-swing (someone called SetBaseLocalRotation)
        if (debugRotJumps)
        {
            float baseChanged = Quaternion.Angle(baseLocalAtBegin, baseLocalRotation);
            if (baseChanged > baseDriftDegrees && Time.time >= nextJumpLogTime)
            {
                nextJumpLogTime = Time.time + jumpCooldown;
                Debug.Log($"[BASE CHANGED] Δ={baseChanged:0.0}deg (baseLocalRotation modified mid-swing)");
            }
        }

        // 4) Overwritten after Update (another script fights swingPivot)
        if (debugRotJumps && Time.frameCount == lastWriteFrame)
        {
            float overwritten = Quaternion.Angle(rotationWrittenThisFrame, swingPivot.localRotation);
            if (overwritten > 1f && Time.time >= nextJumpLogTime)
            {
                nextJumpLogTime = Time.time + jumpCooldown;
                Debug.Log($"[OVERWRITTEN] Someone changed swingPivot after our Update. Δ={overwritten:0.0}deg");
            }
        }
    }
}
