// NetworkRigidbodyPlayer.cs
// - Owner reads local input, sends move + yaw to server.
// - Server applies rb.MoveRotation and velocity.
//
// Still includes the "HoldYawFor(blendTime)" fix so body doesn't chase blended yaw.
// Priority switching means PanTilt shouldn't reset anymore.

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

    // Keep last yaw we sent so server stays stable while yaw is disabled or held
    private float lastYawSent;

    // Camera yaw sampled in LateUpdate (Cinemachine updates late)
    private float cachedYawFromView;

    // Blend yaw hold
    private float yawHoldUntilTime;

    public NetworkVariable<Quaternion> NetAimRotation =
        new(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

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
            //input.Player.Jump.perofmed

            SpawnLocalCameraRigIfNeeded();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            foreach (var r in visualsToHideOwner)
                if (r) r.enabled = false;

            lastYawSent = transform.eulerAngles.y;
            cachedYawFromView = lastYawSent;
        }

        if (IsServer)
        {
            yawServer = transform.eulerAngles.y;
            yawServerSmooth = yawServer;
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

        // Priority default: walk
        localRig.SetModeSwing(false);
    }

    // Called by GolfStanceController
    public void SetMovementEnabled(bool enabled) => movementEnabled = enabled;

    // Called by GolfStanceController
    public void SetYawEnabled(bool enabled) => yawEnabled = enabled;

    // Called by GolfStanceController when switching cameras
    public void HoldYawFor(float seconds)
    {
        yawHoldUntilTime = Mathf.Max(yawHoldUntilTime, Time.time + seconds);
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;

        // Cinemachine applies camera motion in LateUpdate, so sample yaw here
        var view = ViewTransform;
       if (view != null)
        {
            cachedYawFromView = view.eulerAngles.y;

            Vector3 e = view.eulerAngles;
            float pitch = e.x;
            if (pitch > 180f) pitch -= 360f;
            pitch = Mathf.Clamp(pitch, -75f, 75f); // match your pitchClamp if you want

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
            // During blend window: DO NOT follow blended camera yaw
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

        SendMoveInputServerRpc(moveInputOwner);
        SendYawServerRpc(yawToSend);
    }

    [ServerRpc]
    private void SendMoveInputServerRpc(Vector2 inputValue)
    {
        moveInputServer = inputValue;
    }

    [ServerRpc]
    private void SendYawServerRpc(float yaw)
    {
        yawServer = Mathf.Repeat(yaw, 360f);
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

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

        float speed = movementEnabled ? maxSpeed : swingMaxSpeed;
        Vector3 targetFlatV = desiredDir * speed;
        Vector3 deltaV = targetFlatV - flatV;

        float maxDelta = accel * Time.fixedDeltaTime;
        deltaV = Vector3.ClampMagnitude(deltaV, maxDelta);

        Vector3 newFlatV = flatV + deltaV;
        rb.linearVelocity = new Vector3(newFlatV.x, v.y, newFlatV.z);
    }
}
