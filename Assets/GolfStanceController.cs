using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class GolfStanceController : NetworkBehaviour
{
    public enum Stance { Walk, Swing }

    [Header("Refs")]
    [SerializeField] private NetworkRigidbodyPlayer movement;

    // Comes from the NetworkHandRig (spawned by server)
    [SerializeField] private GripInertiaFollower grip;
    [SerializeField] private SwingPivotMouseRotate swingPivotDriver;

    [Header("Targets (player)")]
    [SerializeField] private Transform fpsFollow;
    [SerializeField] private Transform swingFollow;
    [SerializeField] private Transform walkLookPoint;
    [SerializeField] private Transform hitPoint;

    [Header("Blend Times")]
    [SerializeField] private float walkToSwingBlend = 0.25f;
    [SerializeField] private float swingToWalkBlend = 0.15f;

    [Header("Debug")]
    [SerializeField] private bool debugToggleT = false;

    [Header("Swing Lock/Orbit")]
    [SerializeField] private SwingLockOrbitNet swingLockOrbit;

    private PlayerInputActions input;
    public Stance stance = Stance.Walk;

    private LocalCameraRig rig;
    private Coroutine waitHandRigCo;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        if (!movement) movement = GetComponent<NetworkRigidbodyPlayer>();
        if (!swingLockOrbit) swingLockOrbit = GetComponent<SwingLockOrbitNet>();

        rig = movement ? movement.LocalRig : null;

        input = new PlayerInputActions();
        input.Enable();

        input.Player.Swing.performed += _ => EnterSwing();
        input.Player.Swing.canceled  += _ => ExitSwing();

        waitHandRigCo = StartCoroutine(WaitForMyHandRig());

        ApplyWalk();
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && input != null)
            input.Disable();

        if (waitHandRigCo != null)
            StopCoroutine(waitHandRigCo);
    }

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

                if (!grip)
                {
                    Debug.LogError("[GolfStanceController] NetworkHandRig found, but GripInertiaFollower missing.");
                    yield break;
                }

                Debug.Log("[GolfStanceController] Bound local NetworkHandRig.");
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning(
            "[GolfStanceController] Timed out waiting for local NetworkHandRig. " +
            "Ensure GolfGameManager spawns it and sets LogicalOwnerClientId."
        );
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (debugToggleT && Input.GetKeyDown(KeyCode.T))
        {
            if (stance == Stance.Walk) EnterSwing();
            else ExitSwing();
        }

        // Swing camera peek: Q/E changes TargetOffset.x
        if (stance == Stance.Swing && rig != null)
        {
            float input01 = 0f;
            if (Input.GetKey(KeyCode.Q)) input01 -= 1f;
            if (Input.GetKey(KeyCode.E)) input01 += 1f;

            rig.UpdateSwingPeek(input01, Time.deltaTime);
        }
    }

    private Vector3 GetSwingCenterWorld()
    {
        // Prefer the actual ball if resolved
        var golfer = GetComponent<NetworkGolferPlayer>();
        if (golfer != null && golfer.MyBall != null)
            return golfer.MyBall.transform.position;

        // fallback
        if (hitPoint != null)
            return hitPoint.position;

        return transform.position + transform.forward * 1.2f;
    }

    private Vector3 GetSwingReferenceForward()
    {
        // Prefer camera forward so "behind ball" matches what you're looking at
        var view = movement != null ? movement.ViewTransform : null;
        if (view != null)
        {
            Vector3 f = view.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 0.0001f) return f.normalized;
        }

        // fallback: player forward
        Vector3 pf = transform.forward;
        pf.y = 0f;
        return (pf.sqrMagnitude > 0.0001f) ? pf.normalized : Vector3.forward;
    }

    private void EnterSwing()
    {
        stance = Stance.Swing;

        if (grip)
            grip.SetFollowBodyAnchor();

        movement?.HoldYawFor(walkToSwingBlend);

        if (movement)
        {
            movement.SetMovementEnabled(false);
            movement.SetYawEnabled(false);
        }

        swingPivotDriver?.BeginSwing();

        if (rig)
        {
            Transform follow = swingFollow ? swingFollow : fpsFollow;
            rig.BindSwing(follow, hitPoint);
            rig.SetModeSwing(true);
        }

        // === NEW: lock/orbit around ball center (snap) ===
        if (swingLockOrbit != null)
        {
            Vector3 centerWorld = GetSwingCenterWorld();
            Vector3 refForward = GetSwingReferenceForward();
            swingLockOrbit.BeginSwing(centerWorld, refForward);
        }
    }

    private void ExitSwing()
    {
        stance = Stance.Walk;

        // stop lock/orbit
        swingLockOrbit?.EndSwing();

        swingPivotDriver?.EndSwing();

        if (grip)
            grip.SetFollowCamera();

        ApplyWalk();
        movement?.HoldYawFor(swingToWalkBlend);
    }

    private void ApplyWalk()
    {
        if (movement)
        {
            movement.SetMovementEnabled(true);
            movement.SetYawEnabled(true);
        }

        if (rig)
        {
            rig.BindWalk(fpsFollow, walkLookPoint);
            rig.SetModeSwing(false);
        }
    }
}
