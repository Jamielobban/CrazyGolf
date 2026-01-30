using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkGolfBall))]
public class NetworkGolfBallState : NetworkBehaviour
{
    public enum BallState : byte { Free = 0, Held = 1, Teed = 2 }
    public enum BallMode : byte { Practice = 0, Round = 1 }

    // ---- Static "who holds what" (local query on any peer) ----
    private static readonly Dictionary<ulong, NetworkGolfBallState> heldByClient = new();

    public static bool TryGetHeldBall(ulong clientId, out NetworkGolfBallState ball)
        => heldByClient.TryGetValue(clientId, out ball) && ball != null && ball.IsSpawned;

    [Header("Defaults")]
    [SerializeField] private BallMode defaultMode = BallMode.Practice;

    [Header("Practice pickup")]
    [SerializeField] private bool allowPracticePickupWhenStopped = true;

    [Header("Held follow (server)")]
    [SerializeField] private float heldFollowLerp = 30f;

    public NetworkVariable<BallState> State =
        new(BallState.Free, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<BallMode> Mode =
        new(BallMode.Practice, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Who is allowed to hit (Round mode). ulong.MaxValue = none/unassigned.
    public NetworkVariable<ulong> LogicalOwnerClientId =
        new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<ulong> HeldByClientId =
        new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<ulong> TeedSocketNetId =
        new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkGolfBall phys;
    private Rigidbody rb;

    private Transform heldAnchorServer;
    private Transform teeSocketServer;

    private void Awake()
    {
        phys = GetComponent<NetworkGolfBall>();
        rb = phys ? phys.RB : GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        State.OnValueChanged += (_, __) => OnStateChanged();
        HeldByClientId.OnValueChanged += (_, __) => UpdateHeldMapLocal();
        Mode.OnValueChanged += (_, __) => { /* optional */ };

        if (IsServer)
            Mode.Value = defaultMode;

        OnStateChanged();
        UpdateHeldMapLocal();
    }

    public override void OnNetworkDespawn()
    {
        State.OnValueChanged -= (_, __) => OnStateChanged();
        HeldByClientId.OnValueChanged -= (_, __) => UpdateHeldMapLocal();
        RemoveFromHeldMapLocal();
    }

    private void OnStateChanged()
    {
        UpdateHeldMapLocal();

        if (IsServer)
        {
            ApplyPhysicsServer();
            ResolveServerTargets();
        }
    }

    private void ApplyPhysicsServer()
    {
        if (!rb) return;

        if (State.Value == BallState.Free)
        {
            rb.isKinematic = false;
        }
        else
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void ResolveServerTargets()
    {
        heldAnchorServer = null;
        teeSocketServer = null;

        if (State.Value == BallState.Held)
        {
            heldAnchorServer = ResolvePlayerBallAnchor(HeldByClientId.Value);
        }
        else if (State.Value == BallState.Teed)
        {
            teeSocketServer = ResolveTeeSocket(TeedSocketNetId.Value);
        }
    }

    private Transform ResolvePlayerBallAnchor(ulong clientId)
    {
        if (!IsServer) return null;
        if (clientId == ulong.MaxValue) return null;

        var nm = NetworkManager.Singleton;
        if (!nm) return null;

        if (!nm.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
            return null;

        var anchor = client.PlayerObject.GetComponent<PlayerBallAnchor>();
        return anchor ? anchor.Anchor : null;
    }

    private Transform ResolveTeeSocket(ulong teeNetId)
    {
        if (!IsServer) return null;
        if (teeNetId == ulong.MaxValue) return null;

        var nm = NetworkManager.Singleton;
        if (!nm) return null;

        if (!nm.SpawnManager.SpawnedObjects.TryGetValue(teeNetId, out var teeNO))
            return null;

        var tee = teeNO.GetComponent<NetworkBallTeeSocket>();
        return tee ? tee.Socket : null;
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;
        if (!rb) return;

        if (State.Value == BallState.Held)
        {
            if (!heldAnchorServer)
            {
                heldAnchorServer = ResolvePlayerBallAnchor(HeldByClientId.Value);
                if (!heldAnchorServer) return;
            }

            float t = 1f - Mathf.Exp(-heldFollowLerp * Time.fixedDeltaTime);
            rb.MovePosition(Vector3.Lerp(rb.position, heldAnchorServer.position, t));
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, heldAnchorServer.rotation, t));
        }
        else if (State.Value == BallState.Teed)
        {
            if (!teeSocketServer)
            {
                teeSocketServer = ResolveTeeSocket(TeedSocketNetId.Value);
                if (!teeSocketServer) return;
            }

            // Snapped hard (stable)
            rb.MovePosition(teeSocketServer.position);
            rb.MoveRotation(teeSocketServer.rotation);
        }
    }

    // ----------------------------
    // Permission helpers (server)
    // ----------------------------
    private bool CanHit(ulong strikerClientId)
    {
        // Held: never hittable
        if (State.Value == BallState.Held) return false;

        // Practice: anyone can hit
        if (Mode.Value == BallMode.Practice) return true;

        // Round: only logical owner can hit
        return LogicalOwnerClientId.Value != ulong.MaxValue && LogicalOwnerClientId.Value == strikerClientId;
    }

    private bool CanPickup(ulong requesterClientId)
    {
        // In round: never allow pickup in play (you can loosen later for tee-only, etc.)
        if (Mode.Value == BallMode.Round) return false;

        // Practice:
        // - allow pickup when teed (optional)
        if (State.Value == BallState.Teed) return true;

        // - optionally allow pickup when stopped
        if (State.Value == BallState.Free && allowPracticePickupWhenStopped && phys != null)
            return phys.IsStoppedServer();

        return false;
    }

    // ----------------------------
    // RPCs
    // ----------------------------
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestPickupServerRpc(ulong playerNetId, RpcParams rpcParams = default)
    {
        if (!IsServer) return;

        ulong sender = rpcParams.Receive.SenderClientId;

        // Must be sender's player object
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerNetId, out var playerNO)) return;
        if (playerNO.OwnerClientId != sender) return;

        if (!CanPickup(sender)) return;

        // If teed, clear tee occupancy
        ClearTeeOccupancyIfNeeded();

        State.Value = BallState.Held;
        HeldByClientId.Value = sender;
        TeedSocketNetId.Value = ulong.MaxValue;

        // In practice, picking it up makes you the logical owner (useful for later)
        LogicalOwnerClientId.Value = sender;

        ApplyPhysicsServer();
        ResolveServerTargets();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestDropServerRpc(Vector3 worldPos, Quaternion worldRot, RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        ulong sender = rpcParams.Receive.SenderClientId;

        if (State.Value != BallState.Held) return;
        if (HeldByClientId.Value != sender) return;

        transform.SetPositionAndRotation(worldPos, worldRot);

        State.Value = BallState.Free;
        HeldByClientId.Value = ulong.MaxValue;
        TeedSocketNetId.Value = ulong.MaxValue;

        ApplyPhysicsServer();
        ResolveServerTargets();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestPlaceOnTeeServerRpc(ulong teeSocketNetId, ulong playerNetId, RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        ulong sender = rpcParams.Receive.SenderClientId;

        if (State.Value != BallState.Held) return;
        if (HeldByClientId.Value != sender) return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerNetId, out var playerNO)) return;
        if (playerNO.OwnerClientId != sender) return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(teeSocketNetId, out var teeNO)) return;
        var tee = teeNO.GetComponent<NetworkBallTeeSocket>();
        if (!tee) return;

        if (tee.IsOccupied) return;

        // Occupy tee
        tee.OccupiedBallNetId.Value = NetworkObjectId;

        // Snap
        Transform sock = tee.Socket;
        transform.SetPositionAndRotation(sock.position, sock.rotation);

        State.Value = BallState.Teed;
        TeedSocketNetId.Value = teeSocketNetId;
        HeldByClientId.Value = ulong.MaxValue;

        ApplyPhysicsServer();
        ResolveServerTargets();
    }

    // Optional: call this from your club hit code if you already hit on server.
    public void TryHitServer(ulong strikerClientId, Vector3 dir, float impulse, float curve01)
    {
        if (!IsServer) return;
        if (!CanHit(strikerClientId)) return;

        // If teed, release from tee and become free
        if (State.Value == BallState.Teed)
        {
            ClearTeeOccupancyIfNeeded();
            State.Value = BallState.Free;
            TeedSocketNetId.Value = ulong.MaxValue;
            ApplyPhysicsServer();
        }

        // Now dynamic, hit physics
        phys.HitServer(dir, impulse, curve01);
    }

    private void ClearTeeOccupancyIfNeeded()
    {
        if (!IsServer) return;
        if (TeedSocketNetId.Value == ulong.MaxValue) return;

        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(TeedSocketNetId.Value, out var teeNO))
        {
            var tee = teeNO.GetComponent<NetworkBallTeeSocket>();
            if (tee && tee.OccupiedBallNetId.Value == NetworkObjectId)
                tee.OccupiedBallNetId.Value = ulong.MaxValue;
        }
    }

    // ----------------------------
    // Static held map (local)
    // ----------------------------
    private void UpdateHeldMapLocal()
    {
        // Remove stale entries that point to this
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

        if (State.Value == BallState.Held && HeldByClientId.Value != ulong.MaxValue)
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

    // ----------------------------
    // For prompts/UI (client-friendly)
    // ----------------------------
    public string GetUsePrompt()
    {
        // You can tune this however you like
        if (State.Value == BallState.Held) return "";
        if (Mode.Value == BallMode.Round) return ""; // no pickup in round
        if (State.Value == BallState.Teed) return "Take ball";
        if (State.Value == BallState.Free) return "Pick up ball";
        return "";
    }
}
