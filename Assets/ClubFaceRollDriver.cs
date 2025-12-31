using UnityEngine;

public class ClubFaceRollDriver : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private SwingPivotMouseRotate swing;
    [SerializeField] private Transform handSlot; // where equipped club gets parented

    [Header("Face Roll (degrees)")]
    [SerializeField] private float rollFromYawVel = 0.015f; // 0.008–0.03
    [SerializeField] private float squareSpring = 18f;      // 10–30
    [SerializeField] private float squareDamping = 6f;      // 4–12
    [SerializeField] private float maxRoll = 35f;           // 20–45

    [Header("Sensitivity vs Pitch")]
    [SerializeField] private AnimationCurve rollGainByPitch =
        AnimationCurve.EaseInOut(-80f, 0.6f, 80f, 1.6f);

    private Transform facePivot; // visualRoot of equipped club

    private float faceRoll;
    private float faceRollVel;
    private float prevYaw;

    void Awake()
    {
        if (!swing) swing = GetComponent<SwingPivotMouseRotate>();
    }

    void OnEnable()
    {
        prevYaw = swing ? swing.CurrentYawOffset : 0f;
    }

    void LateUpdate()
    {
        if (!swing || !swing.isSwinging) return;

        // If club swapped, rebind automatically
        if (!facePivot) TryBindFromHandSlot();
        if (!facePivot) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float yaw = swing.CurrentYawOffset;
        float pitch = swing.CurrentPitchOffset;

        float yawVel = (yaw - prevYaw) / dt; // deg/sec
        prevYaw = yaw;

        float gain = rollGainByPitch.Evaluate(pitch);

        // Drive: fast yaw changes twist the face (open/close)
        float drive = yawVel * rollFromYawVel * gain;

        // Spring back to square (0) with damping
        float accel = (-squareSpring * faceRoll) - (squareDamping * faceRollVel) + drive;
        faceRollVel += accel * dt;
        faceRoll += faceRollVel * dt;

        faceRoll = Mathf.Clamp(faceRoll, -maxRoll, maxRoll);

        // Shaft axis is +Z -> roll around local Z
        facePivot.localRotation = Quaternion.Euler(0f, 0f, faceRoll);
    }

    public void OnEquippedClubChanged()
    {
        // Call this when you pick up / drop / swap club (best practice)
        facePivot = null;
        ResetFace();
        TryBindFromHandSlot();
    }

    private void TryBindFromHandSlot()
    {
        if (!handSlot) return;

        var refs = handSlot.GetComponentInChildren<EquippedClubRefs>(true);
        if (refs && refs.visualRoot)
        {
            facePivot = refs.visualRoot;
            ResetFace();
        }
    }

    public void ResetFace()
    {
        faceRoll = 0f;
        faceRollVel = 0f;
        if (facePivot) facePivot.localRotation = Quaternion.identity;
        if (swing) prevYaw = swing.CurrentYawOffset;
    }

    public float CurrentFaceRoll => faceRoll;
}
