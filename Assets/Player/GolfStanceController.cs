// GolfStanceController.cs (only the parts that matter changed)
// - Calls rig.SyncYawAxes(bodyYaw) every frame during swing
// - Also keeps syncing for the swingToWalkBlend window after exit, to prevent “weird half move”

using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class GolfStanceController : NetworkBehaviour
{
    public enum Stance { Walk, Swing }

    [Header("Refs")]
    [SerializeField] private NetworkRigidbodyPlayer movement;
    [SerializeField] private GripInertiaFollower grip;
    [SerializeField] private SwingPivotMouseRotate swingPivotDriver;
    [SerializeField] private GolferContextLink context;
    [SerializeField] private PlayerInputGate gate;

    [Header("Targets (player)")]
    [SerializeField] private Transform fpsFollow;
    [SerializeField] private Transform swingFollow;
    [SerializeField] private Transform walkLookPoint;
    [SerializeField] private Transform hitPoint;
    [SerializeField] private Transform swingPlane;

    [Header("Blend Times")]
    [SerializeField] private float walkToSwingBlend = 0.25f;
    [SerializeField] private float swingToWalkBlend = 0.15f;

    [Header("Swing Lock/Orbit")]
    [SerializeField] private SwingLockOrbitNet swingLockOrbit;

    private PlayerInputActions input;
    public Stance stance = Stance.Walk;

    private LocalCameraRig rig;
    private Coroutine waitHandRigCo;

    // keep axis syncing alive briefly after exit
    private float yawAxisSyncUntilTime;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        if (!movement) movement = GetComponent<NetworkRigidbodyPlayer>();
        if (!swingLockOrbit) swingLockOrbit = GetComponent<SwingLockOrbitNet>();
        if (!context) context = GetComponent<GolferContextLink>();
        gate = GetComponent<PlayerInputGate>();

        rig = movement ? movement.LocalRig : null;

        input = new PlayerInputActions();
        input.Enable();

        input.Player.Swing.performed += _ => EnterSwing();
        input.Player.Swing.canceled  += _ => ExitSwing();

        waitHandRigCo = StartCoroutine(WaitForMyHandRig());

        ApplyWalk();
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;

        // Keep Cinemachine internal yaw axes synced to body yaw
        if (rig != null && (stance == Stance.Swing || Time.time < yawAxisSyncUntilTime))
        {
            rig.SyncYawAxes(transform.eulerAngles.y);
        }

        if (stance == Stance.Swing && rig != null)
        {
            float input01 = 0f;

            if (movement != null)
                input01 = movement.PeekAxis;

            if (gate != null && !gate.AllowPeek)
                input01 = 0f;

            rig.UpdateSwingPeek(input01, Time.deltaTime);
        }
    }

    private void EnterSwing()
        {
        stance = Stance.Swing;
        if (gate != null) gate.inSwing = true;

        if (grip)
            grip.SetFollowBodyAnchor();

        movement?.HoldYawFor(walkToSwingBlend);

        if (movement)
        {
            movement.SetYawEnabled(false);                
            movement.SetServerMovementEnabledServerRpc(false); // slow-walk
            movement.SetServerLocomotionEnabledServerRpc(true); // keep locomotion unless lock succeeds
        }
        var cd = GetEquippedClubData();

        if(cd != null && swingPivotDriver != null)
        {
            swingPivotDriver.SetBaseLocalRotation(cd.swingPlaneRotation);
        }

        swingPivotDriver?.BeginSwing();

        if (cd != null)
        {
            if (hitPoint)   hitPoint.localPosition   = cd.swingHitPointLocal;
            if (swingFollow) swingFollow.localPosition = cd.swingFollowLocal;
        }

        if (rig)
        {
            Transform follow = swingFollow ? swingFollow : fpsFollow;
            rig.BindSwing(follow, hitPoint);
            rig.SetModeSwing(true);
        }

        if (swingLockOrbit != null)
        {
            Vector3 centerWorld = GetSwingCenterWorld();
            Vector3 refForward = GetSwingReferenceForward();
            float back = cd ? cd.stanceBackOffset : 1.2f;
            float side = cd ? cd.stanceSideOffset : 0.35f;

            swingLockOrbit.BeginSwing(centerWorld, refForward, back, side);
        }
    }

private void ExitSwing()
{
    stance = Stance.Walk;
    if (gate != null) gate.inSwing = false;
    // stop orbit first (it will re-enable server locomotion internally if you kept that)
    swingLockOrbit?.EndSwing();

    swingPivotDriver?.EndSwing();

    if (grip)
        grip.SetFollowCamera();

    ApplyWalk();

    // keep body from chasing blended camera yaw
    movement?.HoldYawFor(swingToWalkBlend);

    if (movement)
    {
        movement.SetServerMovementEnabledServerRpc(true);
        movement.SetServerLocomotionEnabledServerRpc(true);
    }
}

private void ApplyWalk()
{
    if (movement)
    {
        movement.SetYawEnabled(true);                 // ✅ yaw sending resumes only after hold expires
        movement.SetServerMovementEnabledServerRpc(true);
    }

    if (rig)
    {
        rig.BindWalk(fpsFollow, walkLookPoint);
        rig.SetModeSwing(false);
    }
}

    // --- your existing helpers unchanged ---
    private IEnumerator WaitForMyHandRig()
    {
        const float timeoutSeconds = 3f;
        float t0 = Time.time;

        var nm = NetworkManager.Singleton;
        ulong localClientId = nm.LocalClientId;

        while (IsOwner && Time.time - t0 < timeoutSeconds)
        {
            foreach (var no in nm.SpawnManager.SpawnedObjectsList)
            {
                var handRig = no.GetComponent<NetworkHandRig>();
                if (!handRig) continue;

                if (handRig.LogicalOwnerClientId.Value != localClientId)
                    continue;

                grip = handRig.GetComponent<GripInertiaFollower>();
                yield break;
            }

            yield return null;
        }
    }

    private Vector3 GetSwingCenterWorld()
    {
        var golfer = GetComponent<NetworkGolferPlayer>();
        if (golfer != null && golfer.MyBall != null)
            return golfer.MyBall.transform.position;

        if (hitPoint != null)
            return hitPoint.position;

        return transform.position + transform.forward * 1.2f;
    }

    private Vector3 GetSwingReferenceForward()
    {
        var view = movement != null ? movement.ViewTransform : null;
        if (view != null)
        {
            Vector3 f = view.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 0.0001f) return f.normalized;
        }

        Vector3 pf = transform.forward;
        pf.y = 0f;
        return (pf.sqrMagnitude > 0.0001f) ? pf.normalized : Vector3.forward;
    }
    private ClubData GetEquippedClubData()
    {
        if (context != null && context.Data != null) return context.Data;
        // or: derive from equipment.equippedClubId + clubDb (if you prefer)
        return null;
    }
}
