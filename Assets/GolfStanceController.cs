// GolfStanceController.cs
// - Owner-only.
// - Finds the LOCAL HandRig (spawned separately) and wires:
//    grip (GripInertiaFollower) + swingPivotDriver (SwingPivotMouseRotate)
// - Uses a small coroutine retry in case the HandRig spawns a few frames later.

using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class GolfStanceController : NetworkBehaviour
{
    public enum Stance { Walk, Swing }

    [Header("Refs")]
    [SerializeField] private NetworkRigidbodyPlayer movement;

    // These come from the HandRig (spawned separately)
    [SerializeField] private GripInertiaFollower grip;
    [SerializeField] private SwingPivotMouseRotate swingPivotDriver;

    [Header("Targets (player)")]
    [SerializeField] private Transform fpsFollow;
    [SerializeField] private Transform swingFollow;
    [SerializeField] private Transform walkLookPoint; // can be null
    [SerializeField] private Transform hitPoint;

    [Header("Blend Times (match CinemachineBrain custom blends)")]
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

        if (movement == null) movement = GetComponent<NetworkRigidbodyPlayer>();
        rig = movement != null ? movement.LocalRig : null;

        input = new PlayerInputActions();
        input.Enable();

        input.Player.Swing.performed += _ => EnterSwing();
        input.Player.Swing.canceled  += _ => ExitSwing();

        // HandRig may spawn a frame or two later, so resolve it async.
        waitHandRigCo = StartCoroutine(WaitForLocalHandRigAndBind());

        ApplyWalk();
    }

    private IEnumerator WaitForLocalHandRigAndBind()
    {
        // Try for a short while; increase if needed.
        const float timeoutSeconds = 2.0f;
        float t0 = Time.time;

        while (IsOwner && Time.time - t0 < timeoutSeconds)
        {
            var rigId = HandRigIdentity.FindLocal();
            if (rigId != null)
            {
                // 1) Grip follower lives on the HandRig root
                if (!grip)
                    grip = rigId.GetComponent<GripInertiaFollower>();

                // Helpful debug
                // Debug.Log($"[GolfStanceController] Bound HandRig: grip={(grip ? "OK" : "NULL")} swing={(swingPivotDriver ? "OK" : "NULL")}");

                yield break;
            }

            yield return null; // wait a frame
        }

        Debug.LogWarning("[GolfStanceController] Timed out waiting for local HandRig. " +
                         "Make sure HandRigSpawner is spawning it and HandRigIdentity tag/ownership is correct.");
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && input != null)
            input.Disable();

        if (waitHandRigCo != null)
            StopCoroutine(waitHandRigCo);

        base.OnNetworkDespawn();
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

        // If hand rig hasn’t bound yet, avoid null spam
        if (grip) grip.SetFollowBodyAnchor();

        movement?.HoldYawFor(walkToSwingBlend);

        if (movement != null)
        {
            movement.SetMovementEnabled(false);
            movement.SetYawEnabled(false);
        }

        swingPivotDriver?.BeginSwing();

        if (rig != null)
        {
            Transform follow = swingFollow != null ? swingFollow : fpsFollow;
            rig.BindSwing(follow, hitPoint);
            rig.SetModeSwing(true);
        }
    }

    private void ExitSwing()
    {
        stance = Stance.Walk;

        swingPivotDriver?.EndSwing();

        if (grip) grip.SetFollowCamera();

        ApplyWalk();

        movement?.HoldYawFor(swingToWalkBlend);
    }

    private void ApplyWalk()
    {
        if (movement != null)
        {
            movement.SetMovementEnabled(true);
            movement.SetYawEnabled(true);
        }

        if (rig != null)
        {
            rig.BindWalk(fpsFollow, walkLookPoint);
            rig.SetModeSwing(false);
        }
    }
}
