using Unity.Netcode;
using UnityEngine;

public class NetworkGolfBag : NetworkBehaviour
{
    public NetworkVariable<ulong> LogicalOwnerClientId =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkList<int> ClubIds { get; private set; }

    private void Awake()
    {
        ClubIds = new NetworkList<int>();
    }

    public override void OnNetworkDespawn()
    {
        ClubIds?.Dispose();
    }

    public bool AddClubServer(int clubId, int capacity = 14)
    {
        if (!IsServer) return false;
        if (clubId <= 0) return false;
        if (ClubIds.Count >= capacity) return false;

        ClubIds.Add(clubId);
        //Debug.Log()
        return true;
    }
}
