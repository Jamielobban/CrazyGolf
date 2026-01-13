using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class SwingLockOrbitNet : NetworkBehaviour
{
    [Header("Lock rules (server)")]
    [SerializeField] private float lockRadius = 2.0f;

    [Header("Orbit controls (server)")]
    [SerializeField] private float orbitSpeedDegPerSec = 90f;

    [Header("Address placement (server snap)")]
    [Tooltip("Meters behind the ball along -aim.")]
    [SerializeField] private float backOffset = 1.15f;

    [Tooltip("Meters to the RIGHT of the ball along +right. (negative = left)")]
    [SerializeField] private float sideOffset = 0.35f;

    [Tooltip("Extra lift above ground for the player's root (usually 0).")]
    [SerializeField] private float upOffset = 0.0f;

    [Tooltip("Clamp how close/far we can end up from the center.")]
    [SerializeField] private float minOrbitRadius = 0.9f;
    [SerializeField] private float maxOrbitRadius = 1.8f;

    [Header("Grounding (server)")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundRayUp = 1.0f;
    [SerializeField] private float groundRayDown = 3.0f;

    [Header("Input (client)")]
    [SerializeField] private KeyCode leftKey = KeyCode.A;
    [SerializeField] private KeyCode rightKey = KeyCode.D;

    [Header("Debug")]
    [SerializeField] private bool lockedServer;
    [SerializeField] private float orbitRadiusServer;
    [SerializeField] private float orbitYawDegServer;
    [SerializeField] private Vector3 centerPosServer;
    [SerializeField] private Vector3 addressPointServer;

    private Rigidbody rb;

    // owner-only
    private bool lockedClient;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    // OWNER: called by stance controller
    public void BeginSwingLock(Vector3 centerWorldPos, Vector3 referenceForward)
    {
        if (!IsOwner) return;

        referenceForward.y = 0f;
        if (referenceForward.sqrMagnitude < 0.0001f) referenceForward = Vector3.forward;
        referenceForward.Normalize();

        BeginLockServerRpc(centerWorldPos, referenceForward);
        lockedClient = true;
    }

    public void EndSwingLock()
    {
        if (!IsOwner) return;
        lockedClient = false;
        EndLockServerRpc();
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (!lockedClient) return;

        float input = 0f;
        if (Input.GetKey(leftKey)) input -= 1f;
        if (Input.GetKey(rightKey)) input += 1f;

        if (Mathf.Abs(input) > 0.001f)
            OrbitInputServerRpc(input);
    }

    [ServerRpc]
    private void BeginLockServerRpc(Vector3 centerWorldPos, Vector3 refForward, ServerRpcParams rpcParams = default)
    {
        centerPosServer = centerWorldPos;

        // Validate player is close enough to lock
        Vector3 p = rb.position;
        Vector3 toPlayer = p - centerPosServer;
        toPlayer.y = 0f;

        if (toPlayer.magnitude > lockRadius)
        {
            lockedServer = false;
            return;
        }

        lockedServer = true;

        // Flatten/sanitize aim
        refForward.y = 0f;
        if (refForward.sqrMagnitude < 0.0001f) refForward = Vector3.forward;
        refForward.Normalize();

        // Find ground point under/near the ball
        Vector3 groundPoint = centerPosServer;
        Vector3 groundNormal = Vector3.up;

        Vector3 rayOrigin = centerPosServer + Vector3.up * groundRayUp;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, groundRayUp + groundRayDown, groundMask, QueryTriggerInteraction.Ignore))
        {
            groundPoint = hit.point;
            groundNormal = hit.normal;
        }

        // Build local basis from aim
        Vector3 up = Vector3.up; // keep it simple; can use groundNormal if you want slopes later
        Vector3 right = Vector3.Cross(up, refForward);
        if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
        right.Normalize();

        // Desired "address" point (where the player root should be relative to ball)
        // +right = stand to the right, -forward = stand behind
        addressPointServer =
            groundPoint
            + right * sideOffset
            - refForward * backOffset
            + up * upOffset;

        // Derive orbit yaw/radius from center -> address point
        Vector3 fromCenter = addressPointServer - centerPosServer;
        fromCenter.y = 0f;

        float r = fromCenter.magnitude;
        orbitRadiusServer = Mathf.Clamp(r, minOrbitRadius, maxOrbitRadius);

        if (fromCenter.sqrMagnitude < 0.0001f)
            orbitYawDegServer = 0f;
        else
            orbitYawDegServer = Mathf.Atan2(fromCenter.x, fromCenter.z) * Mathf.Rad2Deg;

        ApplyOrbitPoseServer();
    }

    [ServerRpc]
    private void EndLockServerRpc(ServerRpcParams rpcParams = default)
    {
        lockedServer = false;
    }

    [ServerRpc]
    private void OrbitInputServerRpc(float input01, ServerRpcParams rpcParams = default)
    {
        if (!lockedServer) return;

        input01 = Mathf.Clamp(input01, -1f, 1f);

        float dt = Time.deltaTime;
        if (dt <= 0f) dt = 1f / 60f;

        orbitYawDegServer += input01 * orbitSpeedDegPerSec * dt;

        ApplyOrbitPoseServer();
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;
        if (!lockedServer) return;

        ApplyOrbitPoseServer();
    }

    private void ApplyOrbitPoseServer()
    {
        Quaternion yawRot = Quaternion.Euler(0f, orbitYawDegServer, 0f);
        Vector3 offset = yawRot * new Vector3(0f, 0f, orbitRadiusServer);

        Vector3 desiredPos = centerPosServer + offset;
        desiredPos.y = rb.position.y;

        Vector3 look = centerPosServer - desiredPos;
        look.y = 0f;

        Quaternion desiredRot =
            (look.sqrMagnitude > 0.0001f)
                ? Quaternion.LookRotation(look.normalized, Vector3.up)
                : rb.rotation;

        rb.MovePosition(desiredPos);
        rb.MoveRotation(desiredRot);
    }
}
