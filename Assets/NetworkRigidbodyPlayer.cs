using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class NetworkRigidbodyPlayer : NetworkBehaviour
{
    [Header("Movement")]
    public float maxSpeed = 5f;
    public float swingMaxSpeed = 1f;
    public float accel = 20f;
    public float yawLerp = 20f;

    [Header("Networking")]
    [Tooltip("How often to send input to server (Hz).")]
    [SerializeField] private float sendRateHz = 30f;

    [Tooltip("Deadzone for resending move input.")]
    [SerializeField] private float moveDeadzone = 0.01f;

    [Tooltip("Degrees threshold before resending yaw.")]
    [SerializeField] private float yawDeadzoneDeg = 0.25f;

    [Header("Camera Targets (on player prefab)")]
    public Transform cameraRoot;

    [Header("First Person")]
    [SerializeField] private Renderer[] visualsToHideOwner;

    [Header("Local Camera (Prefab)")]
    [SerializeField] private LocalCameraRig cameraRigPrefab;

    private LocalCameraRig localRig;
    public LocalCameraRig LocalRig => localRig;

    public Transform ViewTransform => localRig != null ? localRig.ViewTransform : null;

    private Rigidbody rb;

    // Input (owner)
    private PlayerInputActions input;
    private Vector2 moveInputOwner;

    // Networked input (server)
    private Vector2 moveInputServer;
    private float yawServer;
    private float yawServerSmooth;

    // Gating (controlled by stance controller)
    private bool movementEnabled = true;
    private bool yawEnabled = true;

    // SERVER gate: when false, this script does NOT drive the RB (SwingLockOrbit will)
    private bool serverLocomotionEnabled = true;

    // Keep last yaw we sent so server stays stable while yaw is disabled or held
    private float lastYawSent;

    // Camera yaw sampled in LateUpdate (Cinemachine updates late)
    private float cachedYawFromView;

    // Blend yaw hold
    private float yawHoldUntilTime;

    // Send throttling
    private float nextSendTime;
    private Vector2 lastMoveSent;
    private float lastYawSentToServer;

    public NetworkVariable<Quaternion> NetAimRotation =
        new(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);


    private bool serverMovementEnabled = true;
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            input = new PlayerInputActions();
            input.Enable();

            input.Player.Move.performed += ctx => moveInputOwner = ctx.ReadValue<Vector2>();
            input.Player.Move.canceled  += _   => moveInputOwner = Vector2.zero;

            SpawnLocalCameraRigIfNeeded();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            foreach (var r in visualsToHideOwner)
                if (r) r.enabled = false;

            lastYawSent = transform.eulerAngles.y;
            cachedYawFromView = lastYawSent;

            lastMoveSent = Vector2.zero;
            lastYawSentToServer = lastYawSent;
            nextSendTime = 0f;
        }

        if (IsServer)
        {
            yawServer = transform.eulerAngles.y;
            yawServerSmooth = yawServer;

            serverLocomotionEnabled = true;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            if (input != null) input.Disable();
            if (localRig != null) Destroy(localRig.gameObject);
        }

        base.OnNetworkDespawn();
    }

    private void SpawnLocalCameraRigIfNeeded()
    {
        if (!IsOwner) return;
        if (localRig != null) return;

        if (cameraRigPrefab == null)
        {
            Debug.LogError("[NetworkRigidbodyPlayer] cameraRigPrefab is null. Assign it in inspector.");
            return;
        }

        localRig = Instantiate(cameraRigPrefab);
        localRig.SetModeSwing(false);
    }

    // Called by GolfStanceController (local-only)
    public void SetMovementEnabled(bool enabled) => movementEnabled = enabled;

    // Called by GolfStanceController (local-only)
    public void SetYawEnabled(bool enabled) => yawEnabled = enabled;

    // Called by GolfStanceController when switching cameras
    public void HoldYawFor(float seconds)
    {
        yawHoldUntilTime = Mathf.Max(yawHoldUntilTime, Time.time + seconds);
    }

    // Called by stance controller to stop server locomotion during swing lock
    [ServerRpc(RequireOwnership = true)]
    public void SetServerLocomotionEnabledServerRpc(bool enabled, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        serverLocomotionEnabled = enabled;

        if (!enabled)
        {
            // zero velocity so we don't keep sliding while swing lock drives pose
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;

        var view = ViewTransform;
        if (view != null)
        {
            cachedYawFromView = view.eulerAngles.y;

            Vector3 e = view.eulerAngles;
            float pitch = e.x;
            if (pitch > 180f) pitch -= 360f;
            pitch = Mathf.Clamp(pitch, -75f, 75f);

            float yaw = e.y;
            NetAimRotation.Value = Quaternion.Euler(pitch, yaw, 0f);
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        float yawToSend = lastYawSent;

        if (yawEnabled)
        {
            if (Time.time < yawHoldUntilTime)
            {
                yawToSend = lastYawSent;
            }
            else
            {
                yawToSend = cachedYawFromView;
                lastYawSent = yawToSend;
            }
        }

        // Rate limit + deadzone resend
        float sendInterval = (sendRateHz <= 0f) ? 0.0333f : (1f / sendRateHz);
        float now = Time.time;

        bool moveChanged = (moveInputOwner - lastMoveSent).sqrMagnitude > (moveDeadzone * moveDeadzone);
        bool yawChanged = Mathf.Abs(Mathf.DeltaAngle(lastYawSentToServer, yawToSend)) > yawDeadzoneDeg;
        bool due = now >= nextSendTime;

        if (moveChanged || yawChanged || due)
        {
            SendInputServerRpc(moveInputOwner, yawToSend);

            lastMoveSent = moveInputOwner;
            lastYawSentToServer = yawToSend;
            nextSendTime = now + sendInterval;
        }
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable, RequireOwnership = true)]
    private void SendInputServerRpc(Vector2 inputValue, float yaw, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        moveInputServer = inputValue;
        yawServer = Mathf.Repeat(yaw, 360f);
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;
        if (!serverLocomotionEnabled) return;

        // Smooth yaw
        yawServerSmooth = Mathf.LerpAngle(
            yawServerSmooth,
            yawServer,
            yawLerp * Time.fixedDeltaTime
        );

        rb.MoveRotation(Quaternion.Euler(0f, yawServerSmooth, 0f));

        // Movement
        Vector3 inputDir = new Vector3(moveInputServer.x, 0f, moveInputServer.y);
        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

        if (inputDir.sqrMagnitude < 0.0001f)
            return;

        Quaternion yawRot = Quaternion.Euler(0f, yawServerSmooth, 0f);
        Vector3 desiredDir = yawRot * inputDir;

        Vector3 v = rb.linearVelocity;
        Vector3 flatV = new Vector3(v.x, 0f, v.z);

        float speed = serverMovementEnabled ? maxSpeed : swingMaxSpeed;
        Vector3 targetFlatV = desiredDir * speed;
        Vector3 deltaV = targetFlatV - flatV;

        float maxDelta = accel * Time.fixedDeltaTime;
        deltaV = Vector3.ClampMagnitude(deltaV, maxDelta);

        Vector3 newFlatV = flatV + deltaV;
        rb.linearVelocity = new Vector3(newFlatV.x, v.y, newFlatV.z);
    }

    [ServerRpc(RequireOwnership = true)]
    public void SetServerMovementEnabledServerRpc(bool enabled, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        serverMovementEnabled = enabled;
    }
}
