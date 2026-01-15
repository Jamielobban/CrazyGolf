using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class SwingLockOrbitNet : NetworkBehaviour
{
    [Header("References (on the same Player object)")]
    [SerializeField] private GolferContextLink ctx;   // holds ClubHead
    [SerializeField] private Rigidbody rb;            // player root rigidbody (this object)

    [Header("Snap Offsets (meters) - FOR CLUBHEAD TARGET")]
    [SerializeField] private float backOffset = 1.2f;
    [SerializeField] private float sideOffset = 0.35f;

    [Header("Validation")]
    [SerializeField] private float maxSnapDistance = 3.0f; // player root must be this close to center (XZ)

    [Header("Orbit")]
    [SerializeField] private float orbitSpeedDegPerSec = 90f;

    [Header("Input (client)")]
    [SerializeField] private KeyCode leftKey = KeyCode.A;
    [SerializeField] private KeyCode rightKey = KeyCode.D;

    [Header("Facing")]
    [Tooltip("If true, player faces the ball while orbiting. If false, faces 'tangent' (side-on).")]
    [SerializeField] private bool faceBall = true;

    [Tooltip("Only used when faceBall=false. +1 or -1 to flip which tangent direction the player faces.")]
    [SerializeField] private int tangentSign = +1;

    [Header("Networking")]
    [Tooltip("How often the owner sends orbit input while locked (Hz).")]
    [SerializeField] private float orbitSendRateHz = 30f;

    [Tooltip("If server hasn't received orbit input for this long, it decays to 0.")]
    [SerializeField] private float orbitInputTimeout = 0.20f;

    [Header("Debug")]
    [SerializeField] private bool lockedServer;
    [SerializeField] private Vector3 centerServer;
    [SerializeField] private Vector3 clubTargetOffset0XZ_Server; // initial club target offset around center (XZ)
    [SerializeField] private float orbitYawDegServer;

    // clubhead solving
    private Transform clubHeadServer;
    private Vector3 clubHeadLocalFromRootServer; // clubhead position in PLAYER ROOT local space (captured at BeginSwing)

    // client
    private bool lockedClient;

    // server held input
    private float orbitInputServer;
    private float lastOrbitInputRecvTimeServer;

    // client send throttling
    private float nextOrbitSendTime;
    private float lastOrbitSent;

    private void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!ctx) ctx = GetComponent<GolferContextLink>();
    }

    // OWNER: call when swing begins
    public void BeginSwing(Vector3 centerWorldPos, Vector3 playerForward)
    {
        if (!IsOwner) return;

        playerForward.y = 0f;
        if (playerForward.sqrMagnitude < 0.0001f) playerForward = Vector3.forward;
        playerForward.Normalize();

        // Do NOT assume lock is valid until server approves
        lockedClient = false;

        BeginSwingServerRpc(centerWorldPos, playerForward);

        // reset client send state
        lastOrbitSent = 999f;
        nextOrbitSendTime = 0f;
    }

    // OWNER: call when swing ends
    public void EndSwing()
    {
        if (!IsOwner) return;

        lockedClient = false;
        EndSwingServerRpc();

        // also ensure server locomotion is allowed again (slow-walk fallback etc.)
        var move = GetComponent<NetworkRigidbodyPlayer>();
        if (move != null)
            move.SetServerLocomotionEnabledServerRpc(true);
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (!lockedClient) return;

        float input = 0f;
        if (Input.GetKey(leftKey)) input -= 1f;
        if (Input.GetKey(rightKey)) input += 1f;
        input = Mathf.Clamp(input, -1f, 1f);

        float now = Time.time;
        float sendInterval = (orbitSendRateHz <= 0f) ? 0.0333f : (1f / orbitSendRateHz);

        bool changed = Mathf.Abs(input - lastOrbitSent) > 0.001f;
        bool due = now >= nextOrbitSendTime;

        if (changed || due)
        {
            OrbitInputServerRpc(input);
            lastOrbitSent = input;
            nextOrbitSendTime = now + sendInterval;
        }
    }

    // -------------------------
    // Lock success/fail -> owner
    // -------------------------

    private void ReplyLockResultToOwner(bool success)
    {
        var p = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };

        LockResultClientRpc(success, p);
    }

    [ClientRpc]
    private void LockResultClientRpc(bool success, ClientRpcParams _ = default)
    {
        if (!IsOwner) return;

        lockedClient = success;

        // success => SwingLock drives RB on server, so disable server locomotion (prevents RB fighting)
        // fail    => keep server locomotion enabled (slow-walk fallback)
        var move = GetComponent<NetworkRigidbodyPlayer>();
        if (move != null)
            move.SetServerLocomotionEnabledServerRpc(!success);
    }

    // -------------------------
    // Server RPCs
    // -------------------------

    [ServerRpc(RequireOwnership = true)]
    private void BeginSwingServerRpc(Vector3 centerWorldPos, Vector3 playerForward, ServerRpcParams rpcParams = default)
    {
        // extra paranoia: ensure sender is the owner
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        // Validate player root is close enough (XZ)
        Vector3 p = rb.position;
        Vector3 toCenter = centerWorldPos - p;
        toCenter.y = 0f;
        if (toCenter.magnitude > maxSnapDistance)
        {
            lockedServer = false;
            ReplyLockResultToOwner(false);
            return;
        }

        centerServer = centerWorldPos;

        // Grab clubhead on SERVER (must exist on server instance)
        if (!ctx) ctx = GetComponent<GolferContextLink>();
        clubHeadServer = (ctx != null) ? ctx.ClubHead : null;

        if (clubHeadServer == null)
        {
            Debug.LogError($"[{name}] SwingLockOrbitNet: ctx.ClubHead is NULL on server. " +
                           $"Make sure SetEquippedClub runs on the server too (or club exists on server).");
            lockedServer = false;
            ReplyLockResultToOwner(false);
            return;
        }

        // Capture clubhead local offset from the player root
        clubHeadLocalFromRootServer = transform.InverseTransformPoint(clubHeadServer.position);

        // Sanitize forward
        playerForward.y = 0f;
        if (playerForward.sqrMagnitude < 0.0001f) playerForward = Vector3.forward;
        playerForward.Normalize();

        // Compute snap target for the CLUBHEAD
        Vector3 right = Vector3.Cross(Vector3.up, playerForward);
        if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
        right.Normalize();

        Vector3 clubTargetPos0 =
            centerServer
            - playerForward * backOffset
            + right * sideOffset;

        // Store initial orbit offset in XZ (around the ball)
        Vector3 off = clubTargetPos0 - centerServer;
        off.y = 0f;

        // safety: avoid zero offset
        if (off.sqrMagnitude < 0.000001f)
            off = new Vector3(0f, 0f, 0.001f);

        clubTargetOffset0XZ_Server = off;

        orbitYawDegServer = 0f;
        orbitInputServer = 0f;
        lastOrbitInputRecvTimeServer = Time.time;

        lockedServer = true;

        ApplyPoseServer(); // yaw=0 => exact snap you wanted
        ReplyLockResultToOwner(true);
    }

    [ServerRpc(RequireOwnership = true)]
    private void EndSwingServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        lockedServer = false;
        orbitInputServer = 0f;

        // ensure owner stays in "slow-walk allowed" mode once swing ends
        ReplyLockResultToOwner(false);
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable, RequireOwnership = true)]
    private void OrbitInputServerRpc(float input01, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!lockedServer) return;

        orbitInputServer = Mathf.Clamp(input01, -1f, 1f);
        lastOrbitInputRecvTimeServer = Time.time;
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;
        if (!lockedServer) return;

        // if input hasn't been received recently (unreliable), decay to 0 to avoid stuck orbit
        if (Time.time - lastOrbitInputRecvTimeServer > orbitInputTimeout)
            orbitInputServer = 0f;

        orbitYawDegServer += orbitInputServer * orbitSpeedDegPerSec * Time.fixedDeltaTime;

        // prevent orbitYaw from growing forever
        if (orbitYawDegServer > 100000f || orbitYawDegServer < -100000f)
            orbitYawDegServer = Mathf.Repeat(orbitYawDegServer, 360f);

        ApplyPoseServer();
    }

    private void ApplyPoseServer()
    {
        // 1) rotate the CLUBHEAD target offset around the center
        Quaternion yaw = Quaternion.Euler(0f, orbitYawDegServer, 0f);
        Vector3 clubOffset = yaw * clubTargetOffset0XZ_Server;

        Vector3 clubTargetPos = centerServer + clubOffset;

        // --- Solve in 2 passes (tiny improvement) ---
        // Pass 1: compute rotation from "target geometry"
        Quaternion rootRot1 = ComputeRootRot(clubTargetPos, clubOffset);

        // Pass 1: compute root position from that rotation (KEEP SCALE MATH as-is)
        Vector3 rootPos1 = SolveRootPosFromRot(clubTargetPos, rootRot1);

        // Pass 2: if faceBall, recompute rotation from actual rootPos (more stable)
        Quaternion rootRot2 = rootRot1;
        if (faceBall)
        {
            Vector3 look = centerServer - rootPos1;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0001f)
                rootRot2 = Quaternion.LookRotation(look.normalized, Vector3.up);
        }

        // Pass 2: solve position again with improved rotation
        Vector3 rootPos2 = SolveRootPosFromRot(clubTargetPos, rootRot2);

        // keep existing height (matches your working snap behavior best)
        rootPos2.y = rb.position.y;

        rb.MovePosition(rootPos2);
        rb.MoveRotation(rootRot2);
    }

    private Quaternion ComputeRootRot(Vector3 clubTargetPos, Vector3 clubOffset)
    {
        if (faceBall)
        {
            Vector3 look = centerServer - clubTargetPos;
            look.y = 0f;
            return (look.sqrMagnitude > 0.0001f)
                ? Quaternion.LookRotation(look.normalized, Vector3.up)
                : rb.rotation;
        }
        else
        {
            Vector3 tangent = Vector3.Cross(Vector3.up, clubOffset);
            if (tangentSign < 0) tangent = -tangent;
            tangent.y = 0f;

            return (tangent.sqrMagnitude > 0.0001f)
                ? Quaternion.LookRotation(tangent.normalized, Vector3.up)
                : rb.rotation;
        }
    }

    private Vector3 SolveRootPosFromRot(Vector3 clubTargetPos, Quaternion rootRot)
    {
        // KEEP YOUR SCALE MATH (requested)
        Matrix4x4 m = Matrix4x4.TRS(Vector3.zero, rootRot, transform.lossyScale);
        Vector3 clubWorldFromRoot = m.MultiplyVector(clubHeadLocalFromRootServer);

        Vector3 rootPos = clubTargetPos - clubWorldFromRoot;
        return rootPos;
    }
}
