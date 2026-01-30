// NetworkGolferPlayer.cs
// Server-authoritative impulse mapping per club.
// Uses NetworkGolfBallState to support Teed/Held/Free and Practice vs Round rules.

using Unity.Netcode;
using UnityEngine;

public class NetworkGolferPlayer : NetworkBehaviour
{
    [Header("Hit Validation (server)")]
    [SerializeField] private bool mineOnly = true;

    [SerializeField] private ClubDatabase clubDb;
    [SerializeField] private NetworkClubEquipment equipment;

    // NOTE: 0 is fine for now, but later prefer ulong.MaxValue (host clientId is 0)
    private readonly NetworkVariable<ulong> myBallNetworkId =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkGolfBallState MyBall { get; private set; }

    private readonly NetworkVariable<ulong> myBagNetworkId =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkGolfBag MyBag { get; private set; }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        myBallNetworkId.OnValueChanged += OnMyBallIdChanged;

        // Register context (your existing pattern)
        if (PlayerRegistry.Instance != null)
        {
            var ctx = GetComponent<GolferContextLink>();
            if (ctx != null)
                PlayerRegistry.Instance.Register(ctx);
        }

        if (myBallNetworkId.Value != 0)
            TryResolveMyBall(myBallNetworkId.Value);

        myBagNetworkId.OnValueChanged += OnMyBagIdChanged;

        if (myBagNetworkId.Value != 0)
            TryResolveMyBag(myBagNetworkId.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (PlayerRegistry.Instance != null)
        {
            var ctx = GetComponent<GolferContextLink>();
            if (ctx != null)
                PlayerRegistry.Instance.Unregister(ctx);
        }

        myBallNetworkId.OnValueChanged -= OnMyBallIdChanged;
        myBagNetworkId.OnValueChanged -= OnMyBagIdChanged;

        base.OnNetworkDespawn();
    }

    // Called by server to set this player's ball id
    public void SetMyBallIdServer(ulong ballId)
    {
        if (!IsServer) return;
        myBallNetworkId.Value = ballId;
    }

    private void OnMyBallIdChanged(ulong oldValue, ulong newValue) => TryResolveMyBall(newValue);

    private void TryResolveMyBall(ulong netId)
    {
        if (netId == 0) { MyBall = null; return; }
        if (NetworkManager == null) return;

        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(netId, out var no) && no != null)
            MyBall = no.GetComponent<NetworkGolfBallState>();
        else
            MyBall = null;
    }

    // === Called by ClubBallContactLogger on OWNER ===
    public void RequestBallHitFromClub(ulong ballNetId, Vector3 dir, float power01, float curve01)
    {
        if (!IsOwner) return;
        if (dir.sqrMagnitude < 0.0001f) return;

        dir.Normalize();
        power01 = Mathf.Clamp01(power01);
        curve01 = Mathf.Clamp(curve01, -1f, 1f);

        RequestHitBallByIdServerRpc(ballNetId, dir, power01, curve01);
    }

    // === Server: hit a specific ball by NetworkObjectId ===
    [Rpc(SendTo.Server)]
    private void RequestHitBallByIdServerRpc(
        ulong ballNetId,
        Vector3 dir,
        float power01,
        float curve01,
        RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(ballNetId, out var no) || no == null)
            return;

        var ballState = no.GetComponent<NetworkGolfBallState>();
        if (ballState == null) return;

        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        power01 = Mathf.Clamp01(power01);
        curve01 = Mathf.Clamp(curve01, -1f, 1f);

        // Ownership rule:
        // mineOnly means "in Round mode, only the ball's logical owner can hit."
        if (mineOnly)
        {
            if (ballState.Mode.Value == NetworkGolfBallState.BallMode.Round &&
                ballState.LogicalOwnerClientId.Value != senderId)
                return;
        }

        // Can't hit while held
        if (ballState.State.Value == NetworkGolfBallState.BallState.Held)
            return;

        // Only enforce stopped when ball is Free (rolling). If Teed, allow.
        if (ballState.State.Value == NetworkGolfBallState.BallState.Free)
        {
            var phys = ballState.GetComponent<NetworkGolfBall>();
            if (phys == null || !phys.IsStoppedServer())
                return;
        }

        // === PER-CLUB authoritative impulse mapping ===
        int clubId = equipment != null ? equipment.equippedClubId.Value : 0;
        ClubData cd = (clubDb != null) ? clubDb.Get((int)clubId) : null;

        float minI = (cd != null) ? cd.minImpulse : 0f;
        float maxI = (cd != null) ? cd.maxImpulse : 0f;

        float impulse = Mathf.Lerp(minI, maxI, power01);

        // Stroke count (server)
        FindFirstObjectByType<GolfHoleManager>()?.AddStrokeServer(senderId);

        // IMPORTANT: go through state brain so teed balls release -> free -> physics hit
        ballState.TryHitServer(senderId, dir, impulse, curve01);

        BallHitClientRpc(senderId, dir, impulse);
    }

    // ---------------- Bag ----------------

    public void SetMyBagIdServer(ulong bagId)
    {
        if (!IsServer) return;
        myBagNetworkId.Value = bagId;
    }

    private void OnMyBagIdChanged(ulong oldValue, ulong newValue) => TryResolveMyBag(newValue);

    private void TryResolveMyBag(ulong netId)
    {
        if (netId == 0) { MyBag = null; return; }

        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(netId, out var no) && no != null)
            MyBag = no.GetComponent<NetworkGolfBag>();
        else
            MyBag = null;
    }

    [ClientRpc]
    private void BallHitClientRpc(ulong clientId, Vector3 dir, float impulse)
    {
        GameSignals.RaiseBallHit(clientId, dir, impulse);
    }
}
