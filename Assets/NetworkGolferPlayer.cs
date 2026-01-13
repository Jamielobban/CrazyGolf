// NetworkGolferPlayer.cs
// Per-club authoritative impulse mapping on the server using EquippedClubId.
// Keeps curve01 plumbing unchanged for now.

using Unity.Netcode;
using UnityEngine;

public class NetworkGolferPlayer : NetworkBehaviour
{
    [Header("Hit Validation (server)")]
    [SerializeField] private Transform hitOrigin;
    [SerializeField] private float hitRange = 2.0f;
    [SerializeField] private LayerMask ballMask;
    [SerializeField] private bool mineOnly = true;

    [Header("Impulse (fallback if no club data)")]
    [SerializeField] private float minImpulse = 3f;
    [SerializeField] private float maxImpulse = 16f;

    [SerializeField] private ClubDatabase clubDb;

    private readonly NetworkVariable<ulong> myBallNetworkId =
        new NetworkVariable<ulong>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public NetworkGolfBall MyBall { get; private set; }

    public NetworkVariable<int> EquippedClubId =
        new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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
            MyBall = no.GetComponent<NetworkGolfBall>();
    }

    // === Called by ClubBallContact on OWNER ===
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
    [ServerRpc]
    private void RequestHitBallByIdServerRpc(
        ulong ballNetId,
        Vector3 dir,
        float power01,
        float curve01,
        ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(ballNetId, out var no) || no == null)
            return;

        var ball = no.GetComponent<NetworkGolfBall>();
        if (ball == null) return;

        // Validate ownership / stopped / distance
        if (mineOnly && ball.LogicalOwnerClientId != senderId) return;
        if (!ball.IsStoppedServer()) return;

        if (hitOrigin != null)
        {
            float dist = Vector3.Distance(hitOrigin.position, ball.transform.position);
            if (dist > hitRange) return;
        }

        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        power01 = Mathf.Clamp01(power01);
        curve01 = Mathf.Clamp(curve01, -1f, 1f);

        // === PER-CLUB authoritative impulse mapping ===
        int clubId = EquippedClubId.Value;
        ClubData cd = (clubDb != null) ? clubDb.Get(clubId) : null;

        float minI = (cd != null) ? cd.minImpulse : minImpulse;
        float maxI = (cd != null) ? cd.maxImpulse : maxImpulse;

        float impulse = Mathf.Lerp(minI, maxI, Mathf.Clamp01(power01));

        //Debug.Log(cd.name + " -> " + cd.maxImpulse);
        ball.HitServer(dir, impulse, Mathf.Clamp(curve01, -1f, 1f));
    }
}
