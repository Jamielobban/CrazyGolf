using Unity.Netcode;
using UnityEngine;

public class NetworkClubSwingVisual : NetworkBehaviour
{
    [Header("Swing Plane")]
    [SerializeField] private Transform swingPivot; // PLAYER swing plane (rotates on Y)

    [Header("Mouse → Swing")]
    [SerializeField] private float swingSpeed = 0.35f; // deg per pixel
    [SerializeField] private float maxBackswing = 110f;
    [SerializeField] private float maxFollowThrough = 70f;

    [Header("Smoothing")]
    [SerializeField] private float swingLerp = 25f;
    [SerializeField] private float returnLerp = 18f;

    private bool swingHeld;

    private float swingTarget;
    private float swingCurrent;

    private Quaternion restLocalRot;
    private bool cachedRest;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        CacheRest();
    }

    void CacheRest()
    {
        if (cachedRest) return;
        restLocalRot = swingPivot.localRotation;
        cachedRest = true;
    }

    public void BeginSwing()
    {
        CacheRest();
        swingHeld = true;
        swingTarget = 0f;
        swingCurrent = 0f;
    }

    public void EndSwing()
    {
        swingHeld = false;
    }

    // CALLED BY GolfStanceController
    public void AddLookDelta(Vector2 d)
    {
        if (!swingHeld) return;

        swingTarget += d.x * swingSpeed;
        swingTarget = Mathf.Clamp(
            swingTarget,
            -maxFollowThrough,
            maxBackswing
        );
    }

    void LateUpdate()
    {
        if (!IsOwner || !swingPivot) return;

        float dt = Time.deltaTime;

        if (!swingHeld)
            swingTarget = Mathf.Lerp(swingTarget, 0f, returnLerp * dt);

        swingCurrent = Mathf.Lerp(swingCurrent, swingTarget, swingLerp * dt);

        swingPivot.localRotation =
            restLocalRot * Quaternion.AngleAxis(swingCurrent, Vector3.up);
    }
}
