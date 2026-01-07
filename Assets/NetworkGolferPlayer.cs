// NetworkGolferPlayer.cs
// Owner requests hits, server validates and applies.
// IMPORTANT CHANGE:
// - Client does NOT send impulse.
// - Client sends power01 (0..1). Server maps it to impulse using authoritative min/max.

using Unity.Netcode;
using UnityEngine;

public class NetworkGolferPlayer : NetworkBehaviour
{
    [Header("Hit Validation (server)")]
    [SerializeField] private Transform hitOrigin;
    [SerializeField] private float hitRange = 2.0f;
    [SerializeField] private LayerMask ballMask;
    [SerializeField] private bool mineOnly = true;

    [Header("Impulse (authoritative on server)")]
    [SerializeField] private float minImpulse = 3f;
    [SerializeField] private float maxImpulse = 16f;

    private readonly NetworkVariable<ulong> myBallNetworkId =
        new NetworkVariable<ulong>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public NetworkGolfBall MyBall { get; private set; }

    // Optional input fallback (button-hit), you can remove if you want
    private PlayerInputActions input;
    private bool hitPressed;

    public NetworkVariable<int> EquippedClubId =
        new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        myBallNetworkId.OnValueChanged += OnMyBallIdChanged;

        if (IsOwner)
        {
            input = new PlayerInputActions();
            input.Enable();

            // Optional fallback: press hit to do a simple forward hit at full power
            input.Player.Hit.performed += _ => hitPressed = true;
        }

        if (myBallNetworkId.Value != 0)
            TryResolveMyBall(myBallNetworkId.Value);
    }

    public override void OnNetworkDespawn()
    {
        myBallNetworkId.OnValueChanged -= OnMyBallIdChanged;

        if (IsOwner && input != null)
            input.Disable();

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsOwner) return;

        // OPTIONAL fallback while you're prototyping
        if (hitPressed)
        {
            hitPressed = false;

            Vector3 aim = (Camera.main != null) ? Camera.main.transform.forward : transform.forward;
            aim.y = 0f;
            if (aim.sqrMagnitude < 0.0001f) return;

            // Full power
            //RequestHitClosestBallServerRpc(aim.normalized, 1f);
        }
    }

    // Called by server (manager) to set this player's ball id
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
        curve01 = Mathf.Clamp(curve01,-1f,1f);

        RequestHitBallByIdServerRpc(ballNetId, dir.normalized, power01, curve01);
    }

    // === Server: hit a specific ball by NetworkObjectId ===
    [ServerRpc]
    private void RequestHitBallByIdServerRpc(ulong ballNetId, Vector3 dir, float power01, float curve01, ServerRpcParams rpcParams = default)
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

        // Authoritative impulse mapping
        float impulse = Mathf.Lerp(minImpulse, maxImpulse, power01);

        // Debug (optional)
        //Debug.Log($"[HIT][Server] sender={senderId} ball={ballNetId} power01={power01:F2} impulse={impulse:F2} dir={dir}");

        ball.HitServer(dir, impulse,curve01);
    }
}
