using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class NetworkGolfBag : NetworkBehaviour
{
    [Header("Ownership")]
    [SerializeField] private bool ownerOnlyAccess = true;

    [Header("Capacity")]
    [SerializeField] private int maxClubs = 14;

    [Header("Database")]
    [SerializeField] private ClubDatabase clubDb;

    public NetworkVariable<ulong> LogicalOwnerClientId =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Authoritative
    public NetworkList<int> Clubs { get; private set; }

    // Inspector-only
    [Header("Debug (Inspector Only)")]
    [SerializeField] private List<string> debugClubNames = new();

    private void Awake()
    {
        Clubs = new NetworkList<int>();
    }

    public override void OnNetworkSpawn()
    {
        Clubs.OnListChanged += _ => SyncDebugList();
        SyncDebugList();
    }

    public override void OnNetworkDespawn()
    {
        Clubs.OnListChanged -= _ => SyncDebugList();
    }

    private void SyncDebugList()
    {
        if (debugClubNames == null)
            debugClubNames = new List<string>();

        debugClubNames.Clear();

        foreach (var id in Clubs)
        {
            if (clubDb == null)
            {
                debugClubNames.Add($"id {id} (no db)");
                continue;
            }

            var cd = clubDb.Get(id);
            debugClubNames.Add(cd != null ? cd.clubName : $"<unknown id {id}>");
        }
    }

    private bool CanAccess(ulong clientId)
    {
        return !ownerOnlyAccess || clientId == LogicalOwnerClientId.Value;
    }

    [Rpc(SendTo.Server,InvokePermission = RpcInvokePermission.Everyone)]
    public void DepositEquippedToBagServerRpc(ulong playerNetId, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!CanAccess(sender)) return;
        if (Clubs.Count >= maxClubs) return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerNetId, out var playerNO))
            return;

        // Must be sender's player (prevents depositing someone else’s equipment)
        if (playerNO.OwnerClientId != sender) return;

        var equip = playerNO.GetComponent<NetworkClubEquipment>();
        if (!equip) return;

        int clubId = equip.equippedClubId.Value;
        if (clubId <= 0) return;

        Clubs.Add(clubId);
        equip.equippedClubId.Value = 0;
    }

    [Rpc(SendTo.Server,InvokePermission = RpcInvokePermission.Everyone)]
    public void EquipFromBagServerRpc(ulong playerNetId, int index, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!CanAccess(sender)) return;
        if (index < 0 || index >= Clubs.Count) return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerNetId, out var playerNO))
            return;

        // Must be sender's player
        if (playerNO.OwnerClientId != sender) return;

        var equip = playerNO.GetComponent<NetworkClubEquipment>();
        if (!equip) return;

        if (equip.equippedClubId.Value != 0) return;

        int clubId = Clubs[index];
        if (clubId <= 0) return;

        Clubs.RemoveAt(index);
        equip.equippedClubId.Value = clubId;
    }
    
}
