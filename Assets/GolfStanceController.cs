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
        rig = movement ? movement.LocalRig : null;

        input = new PlayerInputActions();
        input.Enable();

        input.Player.Swing.performed += _ => EnterSwing();
        input.Player.Swing.canceled  += _ => ExitSwing();

        // NEW: resolve NetworkHandRig by LogicalOwnerClientId
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
                    Debug.LogError(
                        "[GolfStanceController] NetworkHandRig found, but GripInertiaFollower missing."
                    );
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
    }

    private void ExitSwing()
    {
        stance = Stance.Walk;

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
