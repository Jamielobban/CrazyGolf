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

    [Header("First Person")]
    [SerializeField] private Renderer[] visualsToHideOwner;

    [Header("Local Camera (Prefab)")]
    [SerializeField] private LocalCameraRig cameraRigPrefab;

    private LocalCameraRig localRig;
    public LocalCameraRig LocalRig => localRig;
    public Transform ViewTransform => localRig != null ? localRig.ViewTransform : null;

    private Rigidbody rb;
    private Interactor interactor;

    // Input (owner)
    private PlayerInputActions input;
    private Vector2 moveInputOwner;

    // Input (server)
    private Vector2 moveInputServer;
    private float yawServer;
    private float yawServerSmooth;

    // Local gating (owner)
    private bool yawEnabled = true;

    // Server gates
    private bool serverLocomotionEnabled = true; // false => SwingLockOrbit drives rb pose
    private bool serverMovementEnabled = true;   // true => maxSpeed, false => swingMaxSpeed

    // Yaw sampling (owner)
    private float lastYawSent;
    private float cachedYawFromView;

    // Blend yaw hold (owner)
    private float yawHoldUntilTime;

    // Send throttling (owner)
    private float nextSendTime;
    private Vector2 lastMoveSent;
    private float lastYawSentToServer;

    private PlayerHeldController held;

    public NetworkVariable<Quaternion> NetAimRotation =
        new(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    //INPUT GATES
    private float peekAxis;
    private float orbitAxis;
    private PlayerInputGate gate;

    public float PeekAxis => peekAxis;
    public float OrbitAxis => orbitAxis;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            interactor = GetComponent<Interactor>();
            held = GetComponent<PlayerHeldController>();
            gate = GetComponent<PlayerInputGate>();

            input = new PlayerInputActions();
            input.Enable();

            input.Player.Move.performed += ctx => moveInputOwner = ctx.ReadValue<Vector2>();
            input.Player.Move.canceled  += _   => moveInputOwner = Vector2.zero;
            
            input.Player.Interact.performed += _ =>
            {
                interactor.TryTapUse();
            };

            input.Player.HoldInteract.performed += _ =>
            {
                interactor.TryHoldInteract();
            };
            
            input.Player.Drop.performed += _ =>
            {
                if (gate != null && !gate.AllowDrop) return;
                held.Drop();
            };

            input.Player.HoldDrop.performed += _ =>
            {
                if (gate != null && !gate.AllowDrop) return;
                held.Throw(1f);
            };

            input.Player.DropBag.performed += _ =>
            {
                //if (gate != null && !gate.AllowDrop) return;
                held.DropBagOnly();
            };

            input.Player.PeekAxis.performed += ctx => peekAxis = ctx.ReadValue<float>();
            input.Player.PeekAxis.canceled  += _   => peekAxis = 0f;

            input.Player.OrbitAxis.performed += ctx => orbitAxis = ctx.ReadValue<float>();
            input.Player.OrbitAxis.canceled  += _   => orbitAxis = 0f;
            
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
            serverMovementEnabled = true;
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

    // Called by stance controller (owner local)
    public void SetYawEnabled(bool enabled) => yawEnabled = enabled;

    // Called by stance controller (owner local)
    public void HoldYawFor(float seconds)
    {
        yawHoldUntilTime = Mathf.Max(yawHoldUntilTime, Time.time + seconds);
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;

        var view = ViewTransform;
        if (view == null) return;

        cachedYawFromView = view.eulerAngles.y;

        Vector3 e = view.eulerAngles;
        float pitch = e.x;
        if (pitch > 180f) pitch -= 360f;
        pitch = Mathf.Clamp(pitch, -75f, 75f);

        float yaw = e.y;
        NetAimRotation.Value = Quaternion.Euler(pitch, yaw, 0f);
       // Debug.Log(OrbitAxis + " , " + orbitAxis);
    }

    private void Update()
    {
        if (!IsOwner) return;

        // ✅ Only send yaw when it's valid to drive the body
        bool hasYaw = yawEnabled && Time.time >= yawHoldUntilTime;

        float yawToSend = lastYawSent;
        if (hasYaw)
        {
            yawToSend = cachedYawFromView;
            lastYawSent = yawToSend;
        }

        float sendInterval = (sendRateHz <= 0f) ? 0.0333f : (1f / sendRateHz);
        float now = Time.time;

        bool moveChanged = (moveInputOwner - lastMoveSent).sqrMagnitude > (moveDeadzone * moveDeadzone);
        bool yawChanged  = hasYaw && Mathf.Abs(Mathf.DeltaAngle(lastYawSentToServer, yawToSend)) > yawDeadzoneDeg;
        bool due         = now >= nextSendTime;

        if (moveChanged || yawChanged || due)
        {
            SendInputServerRpc(moveInputOwner, yawToSend, hasYaw);

            lastMoveSent = moveInputOwner;
            if (hasYaw) lastYawSentToServer = yawToSend;
            nextSendTime = now + sendInterval;
        }

        if (Input.GetKeyDown(KeyCode.J))
            interactor.DebugBagDeposit();

        if (Input.GetKeyDown(KeyCode.K))
            interactor.DebugBagEquip();
    }

    [Rpc(SendTo.Server,Delivery = RpcDelivery.Unreliable,InvokePermission = RpcInvokePermission.Owner)]
    private void SendInputServerRpc(Vector2 inputValue, float yaw, bool hasYaw, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        moveInputServer = inputValue;

        // ✅ Prevent stale pre-swing yaw from overwriting orbit yaw
        if (hasYaw)
            yawServer = Mathf.Repeat(yaw, 360f);
    }

    // ---- Server control from stance/orbit ----

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void SetServerMovementEnabledServerRpc(bool enabled, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        serverMovementEnabled = enabled;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void SetServerLocomotionEnabledServerRpc(bool enabled, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        serverLocomotionEnabled = enabled;

        if (!enabled)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return;
        }

        // When locomotion re-enables, adopt the current body yaw
        float y = rb.rotation.eulerAngles.y;
        yawServer = Mathf.Repeat(y, 360f);
        yawServerSmooth = yawServer;
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;
        if (!serverLocomotionEnabled) return;

        yawServerSmooth = Mathf.LerpAngle(
            yawServerSmooth,
            yawServer,
            yawLerp * Time.fixedDeltaTime
        );

        rb.MoveRotation(Quaternion.Euler(0f, yawServerSmooth, 0f));

        Vector3 inputDir = new Vector3(moveInputServer.x, 0f, moveInputServer.y);
        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();
        if (inputDir.sqrMagnitude < 0.0001f) return;

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
}
