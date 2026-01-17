using Unity.Netcode;
using UnityEngine;

public class NetworkClubEquipment : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private GolferContextLink link;
    [SerializeField] private ClubVisualBinder binder;

    // For now, treat this as a CLUB ID (not NetworkObjectId)
    public readonly NetworkVariable<int> equippedClubId =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        if (!link) link = GetComponent<GolferContextLink>();
        if (!binder) binder = GetComponent<ClubVisualBinder>();

        equippedClubId.OnValueChanged += OnEquippedChanged;

        // apply initial
        OnEquippedChanged(-999, equippedClubId.Value);
    }

    public override void OnNetworkDespawn()
    {
        equippedClubId.OnValueChanged -= OnEquippedChanged;
    }

    private void OnEquippedChanged(int oldId, int newId)
    {
        // equipment does NOT set visuals directly; binder listens too, but this is fine if you want single path:
        if (!binder) binder = GetComponent<ClubVisualBinder>();
        if (binder) binder.OnClubChanged(oldId, newId);
    }

    // OWNER calls this (for debug / testing)
    public void DebugEquip(int clubId)
    {
        if (!IsOwner) return;
        DebugEquipServerRpc(clubId);
    }

    [ServerRpc(RequireOwnership = true)]
    private void DebugEquipServerRpc(int clubId, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;

        equippedClubId.Value = clubId;
    }

        public void TryPickupWorldClub(WorldClub club)
    {
        if (!IsOwner || club == null) return;

        var no = club.GetComponent<NetworkObject>();
        if (!no || !no.IsSpawned) return;

        RequestPickupWorldClubServerRpc(no.NetworkObjectId);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestPickupWorldClubServerRpc(ulong clubNetObjectId, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(clubNetObjectId, out var clubNO) || clubNO == null)
            return;

        var worldClub = clubNO.GetComponent<WorldClub>();
        if (worldClub == null) return;

        // Optional: distance validation (recommended)
        float dist = Vector3.Distance(transform.position, clubNO.transform.position);
        if (dist > 3.0f) return;

        // Equip by ID (visual binder will update everywhere)
        equippedClubId.Value = worldClub.ClubId;

        // Remove world object (so it can't be picked by others)
        clubNO.Despawn(true);
    }
    }
