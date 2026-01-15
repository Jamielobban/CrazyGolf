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

        BeginSwingServerRpc(centerWorldPos, playerForward);
        lockedClient = true;
    }

    // OWNER: call when swing ends
    public void EndSwing()
    {
        if (!IsOwner) return;

        lockedClient = false;
        EndSwingServerRpc();
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (!lockedClient) return;

        float input = 0f;
        if (Input.GetKey(leftKey)) input -= 1f;
        if (Input.GetKey(rightKey)) input += 1f;

        OrbitInputServerRpc(Mathf.Clamp(input, -1f, 1f));
    }

    [ServerRpc]
    private void BeginSwingServerRpc(Vector3 centerWorldPos, Vector3 playerForward, ServerRpcParams rpcParams = default)
    {
        // Validate player root is close enough (XZ)
        Vector3 p = rb.position;
        Vector3 toCenter = centerWorldPos - p;
        toCenter.y = 0f;
        if (toCenter.magnitude > maxSnapDistance)
        {
            lockedServer = false;
            return;
        }

        centerServer = centerWorldPos;

        // Grab clubhead on SERVER (must exist on server instance)
        if (!ctx) ctx = GetComponent<GolferContextLink>();
        clubHeadServer = (ctx != null) ? ctx.ClubHead : null;

        if (clubHeadServer == null)
        {
            Debug.LogError($"[{name}] SwingLockOrbit_ClubheadTarget_Net: ctx.ClubHead is NULL on server. " +
                           $"Make sure SetEquippedClub runs on the server too (or club is present on the server).");
            lockedServer = false;
            return;
        }

        // Capture clubhead local offset from the player root
        clubHeadLocalFromRootServer = transform.InverseTransformPoint(clubHeadServer.position);

        // Sanitize forward
        playerForward.y = 0f;
        if (playerForward.sqrMagnitude < 0.0001f) playerForward = Vector3.forward;
        playerForward.Normalize();

        // Compute the SAME snap target for the CLUBHEAD that you like
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

        lockedServer = true;

        ApplyPoseServer(); // yaw=0 => exact snap you wanted
    }

    [ServerRpc]
    private void EndSwingServerRpc(ServerRpcParams rpcParams = default)
    {
        lockedServer = false;
        orbitInputServer = 0f;
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    private void OrbitInputServerRpc(float input01, ServerRpcParams rpcParams = default)
    {
        if (!lockedServer) return;
        orbitInputServer = Mathf.Clamp(input01, -1f, 1f);
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;
        if (!lockedServer) return;

        orbitYawDegServer += orbitInputServer * orbitSpeedDegPerSec * Time.fixedDeltaTime;

        ApplyPoseServer();
    }

    private void ApplyPoseServer()
    {
        // 1) rotate the CLUBHEAD target offset around the center
        Quaternion yaw = Quaternion.Euler(0f, orbitYawDegServer, 0f);
        Vector3 clubOffset = yaw * clubTargetOffset0XZ_Server;

        Vector3 clubTargetPos = centerServer + clubOffset;

        // 2) decide player/root rotation
        Quaternion rootRot;

        if (faceBall)
        {
            // look at the ball from the club target position (stable)
            Vector3 look = centerServer - clubTargetPos;
            look.y = 0f;
            rootRot = (look.sqrMagnitude > 0.0001f)
                ? Quaternion.LookRotation(look.normalized, Vector3.up)
                : rb.rotation;
        }
        else
        {
            // side-on: face tangent to the orbit circle
            Vector3 tangent = Vector3.Cross(Vector3.up, clubOffset);
            if (tangentSign < 0) tangent = -tangent;
            tangent.y = 0f;

            rootRot = (tangent.sqrMagnitude > 0.0001f)
                ? Quaternion.LookRotation(tangent.normalized, Vector3.up)
                : rb.rotation;
        }

        // 3) solve root position so clubhead lands exactly on clubTargetPos
        // clubWorldFromRoot = rootRot * clubHeadLocalFromRoot
        Vector3 clubWorldFromRoot = rootRot * clubHeadLocalFromRootServer;
        Vector3 rootPos = clubTargetPos - clubWorldFromRoot;

        // keep existing height (matches your working snap behavior best)
        rootPos.y = rb.position.y;

        rb.MovePosition(rootPos);
        rb.MoveRotation(rootRot);
    }
}