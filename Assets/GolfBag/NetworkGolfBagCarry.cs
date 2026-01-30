using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class NetworkGolfBagCarry : NetworkBehaviour
{
    // -------------------------
    // STATIC: who is holding what (client + server can query locally)
    // -------------------------
    private static readonly Dictionary<ulong, NetworkGolfBagCarry> heldByClient = new();

    public static bool TryGetHeldBag(ulong clientId, out NetworkGolfBagCarry bag)
        => heldByClient.TryGetValue(clientId, out bag) && bag != null && bag.IsSpawned;

    // -------------------------
    [Header("Physics")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Collider[] cols;

    [Header("Held Offsets (local to anchor)")]
    [SerializeField] private Vector3 heldLocalPos = new Vector3(0.25f, -0.15f, -0.25f);
    [SerializeField] private Vector3 heldLocalEuler = new Vector3(0f, 180f, 0f);

    public NetworkVariable<bool> IsHeld =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<ulong> HeldByClientId =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Server-only follow target
    private Transform followAnchorServer;

    private void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        if (cols == null || cols.Length == 0) cols = GetComponentsInChildren<Collider>(true);
    }

    public override void OnNetworkSpawn()
    {
        IsHeld.OnValueChanged += OnHeldStateChanged;
        HeldByClientId.OnValueChanged += OnHeldByChanged;

        ApplyStateLocal();            // collider toggles + static map (all peers)
        ResolveServerAnchorIfNeeded(); // server follow target
        ApplyPhysicsServer();          // server kinematic state
    }

    public override void OnNetworkDespawn()
    {
        IsHeld.OnValueChanged -= OnHeldStateChanged;
        HeldByClientId.OnValueChanged -= OnHeldByChanged;

        // Remove from static map if it was registered
        RemoveFromHeldMapLocal();
    }

    private void OnHeldStateChanged(bool oldValue, bool newValue)
    {
        ApplyStateLocal();
        ResolveServerAnchorIfNeeded();
        ApplyPhysicsServer();
    }

    private void OnHeldByChanged(ulong oldValue, ulong newValue)
    {
        ApplyStateLocal();
        ResolveServerAnchorIfNeeded();
    }

    // -------------------------
    // LOCAL: colliders + static dictionary (runs on everyone)
    // -------------------------
    private void ApplyStateLocal()
    {
        // Disable colliders while held (prevents clipping/jitter + stops raycast hits if you want that)
        SetCollidersEnabled(!IsHeld.Value);

        // Update static held map
        UpdateHeldMapLocal();
    }

    private void SetCollidersEnabled(bool enabled)
    {
        if (cols == null) return;
        for (int i = 0; i < cols.Length; i++)
            if (cols[i]) cols[i].enabled = enabled;
    }

    private void UpdateHeldMapLocal()
    {
        // Remove any stale mapping to THIS bag under any client id
        // (cheap because map is tiny; 1 bag per player)
        ulong removeKey = ulong.MaxValue;
        foreach (var kv in heldByClient)
        {
            if (kv.Value == this)
            {
                removeKey = kv.Key;
                break;
            }
        }
        if (removeKey != ulong.MaxValue)
            heldByClient.Remove(removeKey);

        // Add current mapping if held
        if (IsHeld.Value)
            heldByClient[HeldByClientId.Value] = this;
    }

    private void RemoveFromHeldMapLocal()
    {
        ulong removeKey = ulong.MaxValue;
        foreach (var kv in heldByClient)
        {
            if (kv.Value == this)
            {
                removeKey = kv.Key;
                break;
            }
        }
        if (removeKey != ulong.MaxValue)
            heldByClient.Remove(removeKey);
    }

    // -------------------------
    // SERVER: follow + physics
    // -------------------------
    private void ApplyPhysicsServer()
    {
        if (!IsServer) return;

        if (IsHeld.Value)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        else
        {
            rb.isKinematic = false;
        }
    }

    private void ResolveServerAnchorIfNeeded()
    {
        if (!IsServer) return;

        if (!IsHeld.Value)
        {
            followAnchorServer = null;
            return;
        }

        followAnchorServer = ResolveAnchor(HeldByClientId.Value);
    }

    private Transform ResolveAnchor(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (!nm) return null;

        if (!nm.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
            return null;

        var anchor = client.PlayerObject.GetComponent<PlayerBagAnchor>();
        return anchor ? anchor.Anchor : null;
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;
        if (!IsHeld.Value) return;

        if (followAnchorServer == null)
        {
            followAnchorServer = ResolveAnchor(HeldByClientId.Value);
            if (followAnchorServer == null) return;
        }

        Quaternion localRot = Quaternion.Euler(heldLocalEuler);
        Quaternion targetRot = followAnchorServer.rotation * localRot;
        Vector3 targetPos = followAnchorServer.TransformPoint(heldLocalPos);

        rb.MovePosition(targetPos);
        rb.MoveRotation(targetRot);
    }

    // -------------------------
    // RPCs
    // -------------------------
    [Rpc(SendTo.Server,InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestHoldServerRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        IsHeld.Value = true;
        HeldByClientId.Value = sender;

        ResolveServerAnchorIfNeeded();
        ApplyPhysicsServer();
    }

    [Rpc(SendTo.Server,InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestDropServerRpc(Vector3 dropVel, Vector3 dropAngVel, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        // only holder can drop
        if (!IsHeld.Value || HeldByClientId.Value != sender) return;

        IsHeld.Value = false;
        HeldByClientId.Value = 0;
        followAnchorServer = null;

        rb.isKinematic = false;
        rb.linearVelocity = dropVel;
        rb.angularVelocity = dropAngVel;
    }
}
