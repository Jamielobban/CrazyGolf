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
}
