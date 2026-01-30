using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class SwingLockOrbitNet : NetworkBehaviour
{
    [Header("References (same Player object)")]
    [SerializeField] private GolferContextLink ctx;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private NetworkRigidbodyPlayer move;
    [SerializeField] private PlayerInputGate gate;
    private Collider clubHeadColliderServer;
    [SerializeField] private float clubheadDisableGrace = 0.25f;
    private float reenableClubheadAtServer = -1f;

    private float backOffset;
    private float sideOffset;

    [Header("Validation")]
    [SerializeField] private float maxSnapDistance = 3.0f;

    [Header("Orbit")]
    [SerializeField] private float orbitSpeedDegPerSec = 90f;

    [Header("Facing")]
    [SerializeField] private bool faceBall = true;
    [SerializeField] private int tangentSign = +1;

    [Header("Networking")]
    [SerializeField] private float orbitSendRateHz = 30f;
    [SerializeField] private float orbitInputTimeout = 0.20f;

    // --- server state ---
    private bool lockedServer;
    private Vector3 centerServer;
    private Vector3 clubTargetOffset0XZ_Server;
    private float orbitYawDegServer;

    private Transform clubHeadServer;
    private Vector3 clubHeadLocalFromRootServer;

    private float orbitInputServer;
    private float lastOrbitInputRecvTimeServer;

    // --- client state ---
    private bool lockedClient;
    private float nextOrbitSendTime;
    private float lastOrbitSent;

    private void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!ctx) ctx = GetComponent<GolferContextLink>();
    }

    public void BeginSwing(Vector3 centerWorldPos, Vector3 playerForward)
    {
        if (!IsOwner) return;

        playerForward.y = 0f;
        if (playerForward.sqrMagnitude < 0.0001f) playerForward = Vector3.forward;
        playerForward.Normalize();

        lockedClient = false;

        lastOrbitSent = 999f;
        nextOrbitSendTime = 0f;

        BeginSwingServerRpc(centerWorldPos, playerForward);
    }

    public void EndSwing()
    {
        if (!IsOwner) return;

        lockedClient = false;
        orbitInputServer = 0f;

        if (clubHeadColliderServer != null)
            clubHeadColliderServer.enabled = true;
            
        EndSwingServerRpc();

        var move = GetComponent<NetworkRigidbodyPlayer>();
        
        if (move != null)
            move.SetServerLocomotionEnabledServerRpc(true);
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (!lockedClient) return;

        float input = move != null ? move.OrbitAxis : 0f;
        Debug.Log(input);

        if (gate != null && !gate.AllowOrbit) input = 0f;
        Debug.Log("gate passed");
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

    // ----- owner-only lock result -----

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

        // success => orbit drives RB => disable locomotion on server
        // fail    => keep locomotion enabled (slow-walk fallback remains)
        var move = GetComponent<NetworkRigidbodyPlayer>();
        if (move != null)
            move.SetServerLocomotionEnabledServerRpc(!success);
    }

    // ----- server rpc -----

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void BeginSwingServerRpc(Vector3 centerWorldPos, Vector3 playerForward, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        // validate proximity (XZ)
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

        if (!ctx) ctx = GetComponent<GolferContextLink>();
        clubHeadServer = (ctx != null) ? ctx.ClubHead : null;
        clubHeadColliderServer = clubHeadServer.GetComponent<Collider>();

        if (clubHeadServer == null)
        {
            Debug.LogError($"[{name}] SwingLockOrbitNet: ctx.ClubHead is NULL on server.");
            lockedServer = false;
            ReplyLockResultToOwner(false);
            return;
        }

        clubHeadLocalFromRootServer = transform.InverseTransformPoint(clubHeadServer.position);

        playerForward.y = 0f;
        if (playerForward.sqrMagnitude < 0.0001f) playerForward = Vector3.forward;
        playerForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, playerForward);
        if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
        right.Normalize();

        Vector3 clubTargetPos0 =
            centerServer
            - playerForward * backOffset
            + right * sideOffset;

        Vector3 off = clubTargetPos0 - centerServer;
        off.y = 0f;
        if (off.sqrMagnitude < 0.000001f)
            off = new Vector3(0f, 0f, 0.001f);

        clubTargetOffset0XZ_Server = off;

        orbitYawDegServer = 0f;
        orbitInputServer = 0f;
        lastOrbitInputRecvTimeServer = Time.time;

        lockedServer = true;

        clubHeadColliderServer.enabled = false;
        reenableClubheadAtServer = Time.time + clubheadDisableGrace;
        ApplyPoseServer();
        ReplyLockResultToOwner(true);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void EndSwingServerRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        lockedServer = false;
        orbitInputServer = 0f;

        if (clubHeadColliderServer != null)
            clubHeadColliderServer.enabled = true;

        ReplyLockResultToOwner(false);
    }

    [Rpc(SendTo.Server,Delivery = RpcDelivery.Unreliable,InvokePermission = RpcInvokePermission.Owner)]
    private void OrbitInputServerRpc(float input01, RpcParams rpcParams = default)
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

        if (clubHeadColliderServer != null && !clubHeadColliderServer.enabled && Time.time >= reenableClubheadAtServer)
            clubHeadColliderServer.enabled = true;

        if (!lockedServer) return;

        if (Time.time - lastOrbitInputRecvTimeServer > orbitInputTimeout)
            orbitInputServer = 0f;

        orbitYawDegServer += orbitInputServer * orbitSpeedDegPerSec * Time.fixedDeltaTime;

        if (orbitYawDegServer > 100000f || orbitYawDegServer < -100000f)
            orbitYawDegServer = Mathf.Repeat(orbitYawDegServer, 360f);

        ApplyPoseServer();
    }

    private void ApplyPoseServer()
    {
        Quaternion yaw = Quaternion.Euler(0f, orbitYawDegServer, 0f);
        Vector3 clubOffset = yaw * clubTargetOffset0XZ_Server;

        Vector3 clubTargetPos = centerServer + clubOffset;

        Quaternion rootRot1 = ComputeRootRot(clubTargetPos, clubOffset);
        Vector3 rootPos1 = SolveRootPosFromRot(clubTargetPos, rootRot1);

        Quaternion rootRot2 = rootRot1;
        if (faceBall)
        {
            Vector3 look = centerServer - rootPos1;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0001f)
                rootRot2 = Quaternion.LookRotation(look.normalized, Vector3.up);
        }

        Vector3 rootPos2 = SolveRootPosFromRot(clubTargetPos, rootRot2);
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
        // KEEP your scale math
        Matrix4x4 m = Matrix4x4.TRS(Vector3.zero, rootRot, transform.lossyScale);
        Vector3 clubWorldFromRoot = m.MultiplyVector(clubHeadLocalFromRootServer);
        return clubTargetPos - clubWorldFromRoot;
    }

    public void BeginSwing(Vector3 centerWorldPos, Vector3 playerForward, float back, float side)
    {
        backOffset = back;
        sideOffset = side;
        BeginSwing(centerWorldPos, playerForward);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void EnableClubheadColliderServerRpc()
    {
        if (!lockedServer) return;
        if (clubHeadColliderServer != null)
            clubHeadColliderServer.enabled = true;
    }
}
