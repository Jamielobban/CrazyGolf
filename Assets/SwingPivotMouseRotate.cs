using Unity.Netcode;
using UnityEngine;

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

    void Awake()
    {
        link = GetComponentInParent<GolferContextLink>();
    }

    public void BeginSwing()
    {
        isSwinging = true;
        if (!swingPivot) return;

        // Must have club data
        ResolveAndApplyClubData();

        baseLocalRotation = swingPivot.localRotation;
        yawOffset = 0f;
        pitchOffset = 0f;

        prevPitchOffset = pitchOffset;
        prevPitchVel = 0f;
        catchupTimer = 0f;
    }

    public void EndSwing()
    {
        isSwinging = false;
        if (!swingPivot) return;

        swingPivot.localRotation = baseLocalRotation;
        catchupTimer = 0f;
    }

    private void ResolveAndApplyClubData()
    {
        if (!link) link = GetComponent<GolferContextLink>();

        currentData = link ? link.Data : null;

        if (!currentData)
        {
            Debug.LogError("[SwingPivotMouseRotate] No ClubData found. Make sure GolferContextLink.equippedClub is set and has data.");
            enabled = false;
            return;
        }

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

        float mx = Input.GetAxisRaw("Mouse X");
        float my = Input.GetAxisRaw("Mouse Y") * (invertY ? -1f : 1f);

        // ---- pitch ----
        pitchOffset += my * pitchSpeed;
        pitchOffset = Mathf.Clamp(pitchOffset, pitchMin, pitchMax);

        // ---- yaw ----
        float yawGain = enableAntiParking ? yawGainByPitch.Evaluate(pitchOffset) : 1f;
        yawOffset += mx * yawSpeed * yawGain;

        if (enableAntiParking)
        {
            yawOffset = Mathf.Clamp(yawOffset, -yawClamp, yawClamp);

            float yawDecayT = 1f - Mathf.Exp(-yawDecay * dt);
            float pitchDecayT = 1f - Mathf.Exp(-pitchDecay * dt);

            yawOffset = Mathf.Lerp(yawOffset, 0f, yawDecayT);
            pitchOffset = Mathf.Lerp(pitchOffset, 0f, pitchDecayT);
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

        float follow = rotFollow;
        if (catchupTimer > 0f)
        {
            follow *= catchupBoost;
            catchupTimer -= dt;
        }

        float followT = 1f - Mathf.Exp(-follow * dt);
        swingPivot.localRotation = Quaternion.Slerp(swingPivot.localRotation, target, followT);
    }
}
